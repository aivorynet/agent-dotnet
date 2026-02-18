using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AIVory.Monitor.Transport;

namespace AIVory.Monitor.Breakpoint;

/// <summary>
/// Manages non-breaking breakpoints for the .NET agent.
/// Provides a manual API: developers place AIVoryMonitorAgent.Breakpoint("id") calls
/// at locations of interest, and the backend enables/disables them remotely.
/// </summary>
public class BreakpointManager
{
    private readonly AgentConfig _config;
    private readonly BackendConnection _connection;
    private readonly ConcurrentDictionary<string, BreakpointInfo> _breakpoints = new();

    // Rate limiting
    private const int MaxCapturesPerSecond = 50;
    private int _captureCount;
    private long _captureWindowStart;

    public BreakpointManager(AgentConfig config, BackendConnection connection)
    {
        _config = config;
        _connection = connection;
        _captureWindowStart = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Handles a set_breakpoint message from the backend.
    /// </summary>
    public void HandleSetBreakpoint(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var id = root.GetProperty("id").GetString() ?? "";
            var filePath = "";
            if (root.TryGetProperty("file_path", out var fp))
                filePath = fp.GetString() ?? "";
            else if (root.TryGetProperty("file", out var f))
                filePath = f.GetString() ?? "";

            var lineNumber = 0;
            if (root.TryGetProperty("line_number", out var ln))
                lineNumber = ln.GetInt32();
            else if (root.TryGetProperty("line", out var l))
                lineNumber = l.GetInt32();

            string? condition = null;
            if (root.TryGetProperty("condition", out var cond))
                condition = cond.GetString();

            var maxHits = 1;
            if (root.TryGetProperty("max_hits", out var mh))
                maxHits = mh.GetInt32();

            SetBreakpoint(id, filePath, lineNumber, condition, maxHits);
        }
        catch (Exception ex)
        {
            if (_config.Debug)
                Console.WriteLine($"[AIVory Monitor] Error parsing set_breakpoint: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a breakpoint.
    /// </summary>
    public void SetBreakpoint(string id, string filePath, int lineNumber, string? condition = null, int maxHits = 1)
    {
        maxHits = Math.Clamp(maxHits, 1, 50);
        var bp = new BreakpointInfo
        {
            Id = id,
            FilePath = filePath,
            LineNumber = lineNumber,
            Condition = condition,
            MaxHits = maxHits,
            CreatedAt = DateTime.UtcNow
        };

        _breakpoints[id] = bp;

        if (_config.Debug)
            Console.WriteLine($"[AIVory Monitor] Breakpoint set: {id} at {filePath}:{lineNumber}");
    }

    /// <summary>
    /// Removes a breakpoint.
    /// </summary>
    public void RemoveBreakpoint(string id)
    {
        _breakpoints.TryRemove(id, out _);

        if (_config.Debug)
            Console.WriteLine($"[AIVory Monitor] Breakpoint removed: {id}");
    }

    /// <summary>
    /// Called from user code to trigger a breakpoint capture.
    /// Only captures if the breakpoint ID is registered and active.
    /// </summary>
    public void Hit(string id)
    {
        if (!_breakpoints.TryGetValue(id, out var bp))
            return;

        if (bp.HitCount >= bp.MaxHits)
            return;

        if (!RateLimitOk())
            return;

        Interlocked.Increment(ref bp.HitCount);

        if (_config.Debug)
            Console.WriteLine($"[AIVory Monitor] Breakpoint hit: {id}");

        var stackTrace = new StackTrace(1, true);
        var frames = BuildStackTrace(stackTrace);

        var payload = new
        {
            breakpoint_id = bp.Id,
            captured_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            file_path = bp.FilePath,
            line_number = bp.LineNumber,
            stack_trace = frames,
            hit_count = bp.HitCount
        };

        _connection.SendBreakpointHit(bp.Id, payload);
    }

    private bool RateLimitOk()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _captureWindowStart) / (double)Stopwatch.Frequency;

        if (elapsed >= 1.0)
        {
            Interlocked.Exchange(ref _captureCount, 0);
            Interlocked.Exchange(ref _captureWindowStart, now);
        }

        if (_captureCount >= MaxCapturesPerSecond)
        {
            if (_config.Debug)
                Console.WriteLine("[AIVory Monitor] Rate limit reached, skipping capture");
            return false;
        }

        Interlocked.Increment(ref _captureCount);
        return true;
    }

    private List<object> BuildStackTrace(StackTrace stackTrace)
    {
        var frames = new List<object>();

        foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
        {
            var method = frame.GetMethod();
            var fileName = frame.GetFileName();

            frames.Add(new
            {
                method_name = method?.Name,
                class_name = method?.DeclaringType?.FullName,
                file_path = fileName,
                file_name = fileName != null ? Path.GetFileName(fileName) : null,
                line_number = frame.GetFileLineNumber(),
                column_number = frame.GetFileColumnNumber(),
                is_native = fileName == null
            });
        }

        return frames;
    }
}

/// <summary>
/// Represents a registered breakpoint.
/// </summary>
public class BreakpointInfo
{
    public string Id { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string? Condition { get; set; }
    public int MaxHits { get; set; } = 1;
    public int HitCount;
    public DateTime CreatedAt { get; set; }
}
