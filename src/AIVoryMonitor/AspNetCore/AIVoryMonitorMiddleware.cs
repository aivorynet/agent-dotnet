using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIVory.Monitor.AspNetCore;

/// <summary>
/// ASP.NET Core middleware for capturing request context with exceptions.
/// </summary>
public class AIVoryMonitorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AIVoryMonitorMiddleware>? _logger;

    public AIVoryMonitorMiddleware(RequestDelegate next, ILogger<AIVoryMonitorMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Capture exception with request context
            var requestContext = BuildRequestContext(context);
            AIVoryMonitorAgent.Instance?.CaptureException(ex, requestContext);

            // Re-throw to let other middleware handle it
            throw;
        }
    }

    private Dictionary<string, object?> BuildRequestContext(HttpContext context)
    {
        var request = context.Request;

        return new Dictionary<string, object?>
        {
            ["http_method"] = request.Method,
            ["http_path"] = request.Path.Value,
            ["http_query"] = request.QueryString.Value,
            ["http_host"] = request.Host.Value,
            ["http_scheme"] = request.Scheme,
            ["user_agent"] = request.Headers.UserAgent.ToString(),
            ["content_type"] = request.ContentType,
            ["remote_ip"] = context.Connection.RemoteIpAddress?.ToString(),
            ["user_id"] = context.User?.Identity?.Name,
            ["request_id"] = context.TraceIdentifier
        };
    }
}

/// <summary>
/// Extension methods for adding AIVory Monitor to ASP.NET Core applications.
/// </summary>
public static class AIVoryMonitorMiddlewareExtensions
{
    /// <summary>
    /// Adds AIVory Monitor middleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseAIVoryMonitor(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AIVoryMonitorMiddleware>();
    }

    /// <summary>
    /// Adds AIVory Monitor services to the service collection.
    /// </summary>
    public static IServiceCollection AddAIVoryMonitor(this IServiceCollection services, Action<AgentConfig>? configure = null)
    {
        var config = AgentConfig.FromEnvironment();
        configure?.Invoke(config);

        services.AddSingleton(config);
        services.AddSingleton(sp =>
        {
            var logger = sp.GetService<ILogger<AIVoryMonitorAgent>>();
            return new AIVoryMonitorAgent(config, logger);
        });

        return services;
    }
}

/// <summary>
/// IApplicationBuilder interface for compatibility.
/// </summary>
public interface IApplicationBuilder
{
    IApplicationBuilder UseMiddleware<T>() where T : class;
}
