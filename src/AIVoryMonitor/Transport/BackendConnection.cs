using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AIVory.Monitor.Transport;

/// <summary>
/// Manages WebSocket connection to the AIVory backend.
/// </summary>
public class BackendConnection : IDisposable
{
    private readonly AgentConfig _config;
    private readonly ILogger? _logger;
    private readonly ConcurrentQueue<string> _messageQueue = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private Task? _sendTask;

    private int _reconnectAttempts = 0;
    private bool _isConnected = false;
    private bool _isDisposed = false;
    private string? _agentId;

    public event Action<JsonElement>? OnMessage;
    public event Action<string>? OnBreakpointSet;
    public event Action<string>? OnBreakpointRemove;

    public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;
    public string? AgentId => _agentId;

    public BackendConnection(AgentConfig config, ILogger? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the backend WebSocket server.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(BackendConnection));

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("X-Agent-Key", _config.ApiKey);

            Log($"Connecting to {_config.BackendUrl}...");
            await _webSocket.ConnectAsync(new Uri(_config.BackendUrl), _cts.Token);

            _isConnected = true;
            _reconnectAttempts = 0;
            Log("Connected to backend");

            // Start background tasks
            _receiveTask = ReceiveLoopAsync(_cts.Token);
            _heartbeatTask = HeartbeatLoopAsync(_cts.Token);
            _sendTask = SendLoopAsync(_cts.Token);

            // Register with backend
            await RegisterAsync();
        }
        catch (Exception ex)
        {
            Log($"Connection failed: {ex.Message}", LogLevel.Error);
            _isConnected = false;
            await ScheduleReconnectAsync();
        }
    }

    /// <summary>
    /// Disconnects from the backend.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _isConnected = false;
        _cts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
            catch { }
        }

        _webSocket?.Dispose();
        _webSocket = null;

        Log("Disconnected from backend");
    }

    /// <summary>
    /// Sends an exception to the backend.
    /// </summary>
    public void SendException(ExceptionData exception)
    {
        var message = new
        {
            type = "exception",
            payload = exception,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        QueueMessage(message);
    }

    /// <summary>
    /// Sends a snapshot to the backend.
    /// </summary>
    public void SendSnapshot(SnapshotData snapshot)
    {
        var message = new
        {
            type = "snapshot",
            payload = snapshot,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        QueueMessage(message);
    }

    private async Task RegisterAsync()
    {
        var message = new
        {
            type = "register",
            payload = new
            {
                agent_key = _config.ApiKey,
                runtime = "dotnet",
                runtime_version = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                hostname = System.Environment.MachineName,
                environment = _config.Environment,
                application_name = _config.ApplicationName ?? AppDomain.CurrentDomain.FriendlyName
            }
        };

        await SendMessageAsync(message);
    }

    private void QueueMessage(object message)
    {
        var json = JsonSerializer.Serialize(message);
        _messageQueue.Enqueue(json);
    }

    private async Task SendMessageAsync(object message)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                if (_messageQueue.TryDequeue(out var json))
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_webSocket?.State == WebSocketState.Open)
                        {
                            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
                        }
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                else
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"Send error: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                var result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log("Server closed connection");
                    _isConnected = false;
                    await ScheduleReconnectAsync();
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleMessage(json);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException ex)
            {
                Log($"WebSocket error: {ex.Message}", LogLevel.Error);
                _isConnected = false;
                await ScheduleReconnectAsync();
                break;
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                await Task.Delay(_config.HeartbeatIntervalMs, cancellationToken);

                var heartbeat = new
                {
                    type = "heartbeat",
                    payload = new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                };

                await SendMessageAsync(heartbeat);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"Heartbeat error: {ex.Message}", LogLevel.Warning);
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString();

            Log($"Received message: {type}");

            switch (type)
            {
                case "registered":
                    if (root.TryGetProperty("payload", out var payload) &&
                        payload.TryGetProperty("agent_id", out var agentIdElement))
                    {
                        _agentId = agentIdElement.GetString();
                        Log($"Registered with agent ID: {_agentId}");
                    }
                    break;

                case "set_breakpoint":
                    if (root.TryGetProperty("payload", out var bpPayload))
                    {
                        var bpJson = bpPayload.GetRawText();
                        OnBreakpointSet?.Invoke(bpJson);
                    }
                    break;

                case "remove_breakpoint":
                    if (root.TryGetProperty("payload", out var rmPayload) &&
                        rmPayload.TryGetProperty("id", out var bpId))
                    {
                        OnBreakpointRemove?.Invoke(bpId.GetString()!);
                    }
                    break;

                case "error":
                    if (root.TryGetProperty("payload", out var errPayload) &&
                        errPayload.TryGetProperty("message", out var errMsg))
                    {
                        Log($"Backend error: {errMsg.GetString()}", LogLevel.Error);
                    }
                    break;
            }

            OnMessage?.Invoke(root);
        }
        catch (Exception ex)
        {
            Log($"Error handling message: {ex.Message}", LogLevel.Error);
        }
    }

    private async Task ScheduleReconnectAsync()
    {
        if (_reconnectAttempts >= _config.MaxReconnectAttempts)
        {
            Log("Max reconnection attempts reached", LogLevel.Error);
            return;
        }

        _reconnectAttempts++;
        var delay = Math.Min(1000 * (1 << _reconnectAttempts), 60000);

        Log($"Reconnecting in {delay}ms (attempt {_reconnectAttempts})...");
        await Task.Delay(delay);

        if (!_isDisposed)
        {
            await ConnectAsync();
        }
    }

    private void Log(string message, LogLevel level = LogLevel.Information)
    {
        if (_config.Debug || level >= LogLevel.Warning)
        {
            _logger?.Log(level, "[AIVory Monitor] {Message}", message);
            if (_config.Debug)
            {
                Console.WriteLine($"[AIVory Monitor] {message}");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _webSocket?.Dispose();
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}
