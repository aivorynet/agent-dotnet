# AIVory Monitor .NET Agent

Production debugging agent for .NET applications that captures exceptions and context without stopping execution.

## Requirements

- .NET 6.0, 7.0, or 8.0
- Windows, Linux, or macOS
- Network connectivity to AIVory backend

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package AIVory.Monitor
```

Or add to your `.csproj`:

```xml
<PackageReference Include="AIVory.Monitor" Version="1.0.0" />
```

## Usage

### Basic Initialization

```csharp
using AIVory.Monitor;
using Microsoft.Extensions.Logging;

// Initialize with configuration from environment variables
var config = AgentConfig.FromEnvironment();
var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AIVoryMonitorAgent>();

AIVoryMonitorAgent.Init(config, logger);

// Agent will now automatically capture unhandled exceptions
// and gracefully shutdown on process exit
```

### ASP.NET Core Integration

Add the middleware to your ASP.NET Core application:

```csharp
using AIVory.Monitor;

var builder = WebApplication.CreateBuilder(args);

// Initialize agent
var config = AgentConfig.FromEnvironment();
AIVoryMonitorAgent.Init(config, builder.Logging.CreateLogger<AIVoryMonitorAgent>());

var app = builder.Build();

// Add AIVory monitoring middleware (captures request context)
app.UseAIVoryMonitor();

app.MapGet("/", () => "Hello World!");

app.Run();
```

The middleware automatically captures:
- HTTP request details (method, path, query, headers)
- User identity (if authenticated)
- Request timing
- Exception context correlation

### Manual Exception Capture

Capture exceptions explicitly without rethrowing:

```csharp
try
{
    // Your code
}
catch (Exception ex)
{
    var context = new Dictionary<string, object>
    {
        { "userId", currentUser.Id },
        { "operation", "ProcessOrder" }
    };

    AIVoryMonitorAgent.Instance.CaptureException(ex, context);

    // Handle exception gracefully
}
```

### Capture and Rethrow

Capture exception details and rethrow:

```csharp
try
{
    // Your code
}
catch (Exception ex)
{
    var context = new Dictionary<string, object>
    {
        { "orderId", order.Id }
    };

    AIVoryMonitorAgent.Instance.CaptureAndRethrow(ex, context);
}
```

### Extension Method

Use the fluent extension method on exceptions:

```csharp
try
{
    // Your code
}
catch (Exception ex)
{
    var context = new Dictionary<string, object>
    {
        { "userId", userId },
        { "timestamp", DateTime.UtcNow }
    };

    ex.CaptureWithAIVory(context);
    throw;
}
```

## Configuration

Configure the agent via environment variables or programmatically.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `AIVORY_API_KEY` | Authentication key (required) | - |
| `AIVORY_BACKEND_URL` | Backend WebSocket URL | `wss://api.aivory.net` |
| `AIVORY_ENVIRONMENT` | Environment name (dev, staging, prod) | `production` |
| `AIVORY_SAMPLING_RATE` | Exception sampling rate (0.0-1.0) | `1.0` |
| `AIVORY_MAX_DEPTH` | Variable capture depth | `3` |
| `AIVORY_DEV_MODE` | Enable debug logging | `false` |

### Programmatic Configuration

```csharp
var config = new AgentConfig
{
    ApiKey = "your-api-key",
    BackendUrl = "wss://api.aivory.net",
    Environment = "production",
    SamplingRate = 1.0,
    MaxDepth = 3,
    DevMode = false
};

AIVoryMonitorAgent.Init(config, logger);
```

## ASP.NET Core Integration

### Dependency Injection

Register the agent in your DI container:

```csharp
builder.Services.AddSingleton(sp =>
{
    var config = AgentConfig.FromEnvironment();
    var logger = sp.GetRequiredService<ILogger<AIVoryMonitorAgent>>();
    AIVoryMonitorAgent.Init(config, logger);
    return AIVoryMonitorAgent.Instance;
});
```

### Middleware Pipeline

Add the middleware early in the pipeline to capture all exceptions:

```csharp
app.UseAIVoryMonitor();  // Add this early
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

### Controller Integration

Capture exceptions in controllers:

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(Order order)
    {
        try
        {
            var result = await _orderService.CreateAsync(order);
            return Ok(result);
        }
        catch (Exception ex)
        {
            var context = new Dictionary<string, object>
            {
                { "orderId", order.Id },
                { "userId", User.Identity?.Name }
            };

            ex.CaptureWithAIVory(context);
            throw;
        }
    }
}
```

## Building from Source

Clone the repository and build:

```bash
git clone https://github.com/aivory/aivory-monitor.git
cd aivory-monitor/monitor-agents/agent-dotnet
dotnet restore
dotnet build
dotnet test
```

Create NuGet package:

```bash
dotnet pack -c Release
```

## How It Works

### Exception Capture

The agent uses multiple mechanisms to capture exceptions:

1. **AppDomain.UnhandledException**: Captures unhandled exceptions on any thread
2. **Manual Capture**: Explicit `CaptureException()` calls in try-catch blocks
3. **Middleware**: Automatic capture of ASP.NET Core request exceptions

### Context Collection

For each exception, the agent captures:

- Full stack trace with line numbers
- Local variables at each stack frame (up to configured depth)
- Exception type and message
- Request context (HTTP method, path, headers, user)
- Environment metadata (hostname, runtime version, timestamp)
- Custom context provided by application code

### Data Transport

- WebSocket connection to AIVory backend
- Automatic reconnection on connection loss
- Message queuing during disconnection
- Graceful shutdown on process exit

### Performance Impact

- Minimal overhead when no exceptions occur
- Asynchronous exception processing
- Configurable sampling to reduce load
- Automatic throttling under high exception rates

## Troubleshooting

### Agent Not Connecting

Check that `AIVORY_API_KEY` is set and valid:

```bash
echo $AIVORY_API_KEY  # Linux/macOS
echo %AIVORY_API_KEY%  # Windows
```

Enable debug logging:

```csharp
var config = AgentConfig.FromEnvironment();
config.DevMode = true;
AIVoryMonitorAgent.Init(config, logger);
```

### Exceptions Not Captured

Verify the agent is initialized before exceptions occur:

```csharp
// Initialize in Program.cs or Startup.cs
AIVoryMonitorAgent.Init(config, logger);

// Then start application
app.Run();
```

### High Memory Usage

Reduce variable capture depth:

```bash
export AIVORY_MAX_DEPTH=1
```

Enable sampling to capture only a percentage of exceptions:

```bash
export AIVORY_SAMPLING_RATE=0.1  # Capture 10%
```

### WebSocket Connection Issues

Check firewall rules allow WebSocket connections:

```bash
# Test connectivity
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" \
  https://api.aivory.net/ws/monitor/agent
```

Use HTTP backend URL for environments that block WebSockets:

```bash
export AIVORY_BACKEND_URL=https://api.aivory.net
```

### Integration with Application Insights / Sentry

The agent can coexist with other monitoring tools:

```csharp
try
{
    // Your code
}
catch (Exception ex)
{
    // Capture in AIVory
    ex.CaptureWithAIVory(context);

    // Also send to other tools
    _telemetryClient.TrackException(ex);

    throw;
}
```

## License

Copyright (c) AIVory. All rights reserved.
