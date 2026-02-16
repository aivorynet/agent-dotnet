using System;
using System.Threading;
using System.Threading.Tasks;
using AIVory.Monitor.Capture;
using AIVory.Monitor.Transport;
using Microsoft.Extensions.Logging;

namespace AIVory.Monitor;

/// <summary>
/// Main AIVory Monitor agent for .NET applications.
/// </summary>
public class AIVoryMonitorAgent : IDisposable
{
    private static AIVoryMonitorAgent? _instance;
    private static readonly object _lock = new();

    private readonly AgentConfig _config;
    private readonly BackendConnection _connection;
    private readonly ExceptionCapture _exceptionCapture;
    private readonly ILogger? _logger;

    private bool _started = false;
    private bool _disposed = false;

    /// <summary>
    /// Gets the singleton instance of the agent.
    /// </summary>
    public static AIVoryMonitorAgent? Instance => _instance;

    /// <summary>
    /// Creates a new agent instance.
    /// </summary>
    public AIVoryMonitorAgent(AgentConfig? config = null, ILogger? logger = null)
    {
        _config = config ?? AgentConfig.FromEnvironment();
        _config.Validate();

        _logger = logger;
        _connection = new BackendConnection(_config, logger);
        _exceptionCapture = new ExceptionCapture(_config, _connection);
    }

    /// <summary>
    /// Initializes and starts the global agent instance.
    /// </summary>
    public static AIVoryMonitorAgent Init(AgentConfig? config = null, ILogger? logger = null)
    {
        lock (_lock)
        {
            if (_instance != null)
            {
                return _instance;
            }

            _instance = new AIVoryMonitorAgent(config, logger);
            _instance.Start();
            return _instance;
        }
    }

    /// <summary>
    /// Starts the agent.
    /// </summary>
    public void Start()
    {
        if (_started) return;

        // Connect to backend (fire and forget)
        _ = _connection.ConnectAsync();

        // Install exception handlers
        _exceptionCapture.Install();

        // Register shutdown handler
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _started = true;

        if (_config.Debug)
        {
            Console.WriteLine("[AIVory Monitor] Agent started");
        }
    }

    /// <summary>
    /// Stops the agent.
    /// </summary>
    public void Stop()
    {
        if (!_started) return;

        _exceptionCapture.Uninstall();
        _ = _connection.DisconnectAsync();

        _started = false;

        if (_config.Debug)
        {
            Console.WriteLine("[AIVory Monitor] Agent stopped");
        }
    }

    /// <summary>
    /// Manually captures an exception.
    /// </summary>
    public void CaptureException(Exception exception, Dictionary<string, object?>? context = null)
    {
        if (!_started) return;
        _exceptionCapture.Capture(exception, context);
    }

    /// <summary>
    /// Captures an exception and returns it for re-throwing.
    /// </summary>
    public T CaptureAndRethrow<T>(T exception, Dictionary<string, object?>? context = null) where T : Exception
    {
        CaptureException(exception, context);
        return exception;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _connection.Dispose();

        lock (_lock)
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

/// <summary>
/// Extension methods for AIVory Monitor integration.
/// </summary>
public static class AIVoryMonitorExtensions
{
    /// <summary>
    /// Captures the exception using the global agent instance.
    /// </summary>
    public static T CaptureWithAIVory<T>(this T exception, Dictionary<string, object?>? context = null) where T : Exception
    {
        AIVoryMonitorAgent.Instance?.CaptureException(exception, context);
        return exception;
    }
}
