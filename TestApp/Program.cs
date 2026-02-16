// AIVory .NET Agent Test Application
//
// Generates various exception types to test exception capture and local variable extraction.
//
// Usage:
//   cd monitor-agents/agent-dotnet/TestApp
//   dotnet run

using AIVory.Monitor;

Console.WriteLine("===========================================");
Console.WriteLine("AIVory .NET Agent Test Application");
Console.WriteLine("===========================================");

// Initialize the agent (reads from environment variables)
var config = new AgentConfig
{
    ApiKey = Environment.GetEnvironmentVariable("AIVORY_API_KEY") ?? "test-key-123",
    BackendUrl = Environment.GetEnvironmentVariable("AIVORY_BACKEND_URL") ?? "ws://localhost:19999/api/monitor/agent/v1",
    Environment = Environment.GetEnvironmentVariable("AIVORY_ENVIRONMENT") ?? "development",
    Debug = Environment.GetEnvironmentVariable("AIVORY_DEBUG")?.ToLower() == "true"
};

using var agent = AIVoryMonitorAgent.Init(config);

// Wait for agent to connect
Console.WriteLine("Waiting for agent to connect...");
await Task.Delay(3000);
Console.WriteLine("Starting exception tests...\n");

// Generate test exceptions
for (int i = 0; i < 3; i++)
{
    Console.WriteLine($"--- Test {i + 1} ---");
    try
    {
        TriggerException(i);
    }
    catch (Exception e)
    {
        Console.WriteLine($"Caught: {e.GetType().Name} - {e.Message}");
        // Also manually capture for testing
        agent.CaptureException(e, new Dictionary<string, object?>
        {
            ["test_iteration"] = i
        });
    }
    Console.WriteLine();
    await Task.Delay(3000);
}

Console.WriteLine("===========================================");
Console.WriteLine("Test complete. Check database for exceptions.");
Console.WriteLine("===========================================");

// Keep running briefly to allow final messages to send
await Task.Delay(2000);

void TriggerException(int iteration)
{
    // Create some local variables to capture
    string testVar = $"test-value-{iteration}";
    int count = iteration * 10;
    var items = new List<string> { "apple", "banana", "cherry" };
    var metadata = new Dictionary<string, object>
    {
        ["iteration"] = iteration,
        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["nested"] = new Dictionary<string, object> { ["key"] = "value", ["count"] = count }
    };
    var user = new UserContext($"user-{iteration}", "test@example.com");

    switch (iteration)
    {
        case 0:
            // NullReferenceException
            Console.WriteLine("Triggering NullReferenceException...");
            string? nullStr = null;
            _ = nullStr!.Length; // NullReferenceException here
            break;

        case 1:
            // ArgumentException
            Console.WriteLine("Triggering ArgumentException...");
            throw new ArgumentException($"Invalid argument: testVar={testVar}");

        case 2:
            // IndexOutOfRangeException
            Console.WriteLine("Triggering IndexOutOfRangeException...");
            var arr = new int[3];
            arr[10] = 1; // IndexOutOfRangeException here
            break;

        default:
            throw new InvalidOperationException($"Unknown iteration: {iteration}");
    }
}

record UserContext(string UserId, string Email, bool Active = true);
