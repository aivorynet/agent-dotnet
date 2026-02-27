using System;

namespace AIVory.Monitor;

/// <summary>
/// Configuration for the AIVory Monitor agent.
/// </summary>
public class AgentConfig
{
    /// <summary>
    /// API key for authentication with the backend.
    /// Required. Set via AIVORY_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// WebSocket URL for the backend server.
    /// Default: wss://api.aivory.net/monitor/agent
    /// </summary>
    public string BackendUrl { get; set; } = "wss://api.aivory.net/monitor/agent";

    /// <summary>
    /// Environment name (production, staging, development).
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Application name for identification.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Sampling rate for exceptions (0.0 to 1.0).
    /// 1.0 = capture all exceptions.
    /// </summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Maximum depth for variable capture in stack frames.
    /// </summary>
    public int MaxVariableDepth { get; set; } = 10;

    /// <summary>
    /// Maximum string length for captured variables.
    /// </summary>
    public int MaxStringLength { get; set; } = 1000;

    /// <summary>
    /// Maximum collection size for captured variables.
    /// </summary>
    public int MaxCollectionSize { get; set; } = 100;

    /// <summary>
    /// Enable debug logging.
    /// </summary>
    public bool Debug { get; set; } = false;

    /// <summary>
    /// Enable breakpoint support.
    /// </summary>
    public bool EnableBreakpoints { get; set; } = true;

    /// <summary>
    /// Heartbeat interval in milliseconds.
    /// </summary>
    public int HeartbeatIntervalMs { get; set; } = 30000;

    /// <summary>
    /// Maximum reconnection attempts before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// Creates configuration from environment variables.
    /// </summary>
    public static AgentConfig FromEnvironment()
    {
        var config = new AgentConfig
        {
            ApiKey = GetEnvOrDefault("AIVORY_API_KEY", string.Empty),
            BackendUrl = GetEnvOrDefault("AIVORY_BACKEND_URL", "wss://api.aivory.net/monitor/agent"),
            Environment = GetEnvOrDefault("AIVORY_ENVIRONMENT", "production"),
            ApplicationName = System.Environment.GetEnvironmentVariable("AIVORY_APP_NAME"),
            SamplingRate = double.TryParse(System.Environment.GetEnvironmentVariable("AIVORY_SAMPLING_RATE"), out var rate) ? rate : 1.0,
            MaxVariableDepth = int.TryParse(System.Environment.GetEnvironmentVariable("AIVORY_MAX_DEPTH"), out var depth) ? depth : 10,
            Debug = GetEnvOrDefault("AIVORY_DEBUG", "false").ToLower() == "true",
            EnableBreakpoints = GetEnvOrDefault("AIVORY_ENABLE_BREAKPOINTS", "true").ToLower() == "true"
        };

        return config;
    }

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            throw new InvalidOperationException("AIVORY_API_KEY environment variable is required");
        }

        if (SamplingRate < 0 || SamplingRate > 1)
        {
            throw new InvalidOperationException("Sampling rate must be between 0.0 and 1.0");
        }

        if (MaxVariableDepth < 0 || MaxVariableDepth > 10)
        {
            throw new InvalidOperationException("Max variable depth must be between 0 and 10");
        }
    }

    private static string GetEnvOrDefault(string key, string defaultValue)
    {
        return System.Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }
}
