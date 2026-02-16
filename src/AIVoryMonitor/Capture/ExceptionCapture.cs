using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AIVory.Monitor.Transport;

namespace AIVory.Monitor.Capture;

/// <summary>
/// Captures exceptions and their context.
/// </summary>
public class ExceptionCapture
{
    private readonly AgentConfig _config;
    private readonly BackendConnection _connection;
    private readonly Random _random = new();
    private readonly HashSet<string> _capturedFingerprints = new();
    private readonly object _lock = new();

    public ExceptionCapture(AgentConfig config, BackendConnection connection)
    {
        _config = config;
        _connection = connection;
    }

    /// <summary>
    /// Installs exception handlers for the current AppDomain.
    /// </summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (_config.Debug)
        {
            Console.WriteLine("[AIVory Monitor] Exception handlers installed");
        }
    }

    /// <summary>
    /// Uninstalls exception handlers.
    /// </summary>
    public void Uninstall()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_config.Debug)
        {
            Console.WriteLine("[AIVory Monitor] Exception handlers uninstalled");
        }
    }

    /// <summary>
    /// Manually captures an exception.
    /// </summary>
    public void Capture(Exception exception, Dictionary<string, object?>? context = null)
    {
        CaptureException(exception, "error", context);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CaptureException(ex, "critical");
        }
    }

    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        // Only capture if sampling allows
        if (_config.SamplingRate < 1.0 && _random.NextDouble() > _config.SamplingRate)
        {
            return;
        }

        // Skip common framework exceptions that are caught and handled
        if (ShouldSkipException(e.Exception))
        {
            return;
        }

        CaptureException(e.Exception, "error");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (e.Exception != null)
        {
            CaptureException(e.Exception, "error");
        }
    }

    private bool ShouldSkipException(Exception ex)
    {
        var typeName = ex.GetType().FullName ?? "";

        // Skip common handled exceptions
        if (typeName.StartsWith("System.IO.FileNotFoundException") ||
            typeName.StartsWith("System.Net.Sockets.SocketException") ||
            typeName.StartsWith("System.TimeoutException") ||
            typeName.StartsWith("System.OperationCanceledException") ||
            typeName.StartsWith("System.Threading.Tasks.TaskCanceledException"))
        {
            return true;
        }

        return false;
    }

    private void CaptureException(Exception exception, string severity, Dictionary<string, object?>? context = null)
    {
        try
        {
            // Compute fingerprint for deduplication
            var fingerprint = ComputeFingerprint(exception);

            // Skip if we've already captured this exact exception recently
            lock (_lock)
            {
                if (_capturedFingerprints.Contains(fingerprint))
                {
                    return;
                }
                _capturedFingerprints.Add(fingerprint);

                // Keep set from growing too large
                if (_capturedFingerprints.Count > 1000)
                {
                    _capturedFingerprints.Clear();
                }
            }

            var exceptionData = BuildExceptionData(exception, severity, context);
            _connection.SendException(exceptionData);

            if (_config.Debug)
            {
                Console.WriteLine($"[AIVory Monitor] Captured exception: {exception.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            if (_config.Debug)
            {
                Console.WriteLine($"[AIVory Monitor] Error capturing exception: {ex.Message}");
            }
        }
    }

    private ExceptionData BuildExceptionData(Exception exception, string severity, Dictionary<string, object?>? context)
    {
        var stackTrace = new StackTrace(exception, true);
        var frames = BuildStackFrames(stackTrace);

        var topFrame = frames.FirstOrDefault();

        // Capture exception properties as local variables
        var localVariables = new Dictionary<string, object?>();
        var exceptionVars = CaptureExceptionAsVariables(exception);
        foreach (var kvp in exceptionVars)
        {
            localVariables[kvp.Key] = kvp.Value;
        }

        return new ExceptionData
        {
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
            Severity = severity,
            FilePath = topFrame?.FilePath,
            LineNumber = topFrame?.LineNumber ?? 0,
            MethodName = topFrame?.MethodName,
            ClassName = topFrame?.ClassName,
            Runtime = "dotnet",
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            StackTrace = frames,
            LocalVariables = localVariables,
            RequestContext = context
        };
    }

    private List<StackFrameData> BuildStackFrames(StackTrace stackTrace)
    {
        var frames = new List<StackFrameData>();

        foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
        {
            var method = frame.GetMethod();
            if (method == null) continue;

            var declaringType = method.DeclaringType;
            var fileName = frame.GetFileName();

            frames.Add(new StackFrameData
            {
                ClassName = declaringType?.FullName ?? declaringType?.Name,
                MethodName = method.Name,
                FilePath = fileName,
                FileName = fileName != null ? Path.GetFileName(fileName) : null,
                LineNumber = frame.GetFileLineNumber(),
                ColumnNumber = frame.GetFileColumnNumber(),
                IsNative = fileName == null,
                LocalVariables = CaptureLocalVariables(method)
            });
        }

        return frames;
    }

    private Dictionary<string, VariableData>? CaptureLocalVariables(MethodBase method)
    {
        if (_config.MaxVariableDepth <= 0) return null;

        try
        {
            var variables = new Dictionary<string, VariableData>();

            // Capture method parameters info
            foreach (var param in method.GetParameters())
            {
                variables[$"param:{param.Name}"] = new VariableData
                {
                    Name = param.Name ?? "unknown",
                    Type = param.ParameterType.Name,
                    Value = "[parameter]",
                    IsNull = false
                };
            }

            return variables.Count > 0 ? variables : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Captures exception properties and inner exceptions as "local variables".
    /// Since .NET doesn't provide runtime access to actual locals without debugging APIs,
    /// we extract all useful information from the exception object itself.
    /// </summary>
    private Dictionary<string, VariableData> CaptureExceptionAsVariables(Exception exception, int depth = 0)
    {
        var variables = new Dictionary<string, VariableData>();

        if (depth > _config.MaxVariableDepth) return variables;

        // Capture exception properties
        var exType = exception.GetType();

        // Add the exception message
        variables["message"] = new VariableData
        {
            Name = "message",
            Type = "string",
            Value = TruncateString(exception.Message),
            IsNull = string.IsNullOrEmpty(exception.Message),
            IsTruncated = exception.Message?.Length > _config.MaxStringLength
        };

        // Add source info
        if (!string.IsNullOrEmpty(exception.Source))
        {
            variables["source"] = new VariableData
            {
                Name = "source",
                Type = "string",
                Value = exception.Source,
                IsNull = false
            };
        }

        // Add HResult
        variables["hResult"] = new VariableData
        {
            Name = "hResult",
            Type = "int",
            Value = exception.HResult.ToString(),
            IsNull = false
        };

        // Add help link if present
        if (!string.IsNullOrEmpty(exception.HelpLink))
        {
            variables["helpLink"] = new VariableData
            {
                Name = "helpLink",
                Type = "string",
                Value = exception.HelpLink,
                IsNull = false
            };
        }

        // Capture custom exception properties via reflection
        foreach (var prop in exType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip base Exception properties we've already captured
            if (prop.DeclaringType == typeof(Exception) || prop.DeclaringType == typeof(SystemException))
                continue;

            // Skip stack trace (we capture it separately)
            if (prop.Name == "StackTrace" || prop.Name == "TargetSite")
                continue;

            try
            {
                var value = prop.GetValue(exception);
                variables[$"prop:{prop.Name}"] = CaptureValue(prop.Name, value, prop.PropertyType, depth + 1);
            }
            catch
            {
                // Skip properties that throw when accessed
            }
        }

        // Capture Data dictionary
        if (exception.Data?.Count > 0)
        {
            var dataChildren = new Dictionary<string, VariableData>();
            var count = 0;
            foreach (var key in exception.Data.Keys)
            {
                if (count >= _config.MaxCollectionSize) break;
                try
                {
                    var keyStr = key?.ToString() ?? "null";
                    var value = exception.Data[key];
                    dataChildren[keyStr] = CaptureValue(keyStr, value, value?.GetType() ?? typeof(object), depth + 1);
                    count++;
                }
                catch { }
            }

            if (dataChildren.Count > 0)
            {
                variables["Data"] = new VariableData
                {
                    Name = "Data",
                    Type = "IDictionary",
                    Value = $"[{dataChildren.Count} entries]",
                    IsNull = false,
                    Children = dataChildren
                };
            }
        }

        // Capture inner exception
        if (exception.InnerException != null)
        {
            var innerVars = CaptureExceptionAsVariables(exception.InnerException, depth + 1);
            variables["InnerException"] = new VariableData
            {
                Name = "InnerException",
                Type = exception.InnerException.GetType().Name,
                Value = TruncateString(exception.InnerException.Message),
                IsNull = false,
                Children = innerVars
            };
        }

        // For AggregateException, capture inner exceptions
        if (exception is AggregateException aggEx && aggEx.InnerExceptions?.Count > 0)
        {
            var innerChildren = new Dictionary<string, VariableData>();
            for (int i = 0; i < Math.Min(aggEx.InnerExceptions.Count, _config.MaxCollectionSize); i++)
            {
                var inner = aggEx.InnerExceptions[i];
                innerChildren[$"[{i}]"] = new VariableData
                {
                    Name = $"[{i}]",
                    Type = inner.GetType().Name,
                    Value = TruncateString(inner.Message),
                    IsNull = false,
                    Children = CaptureExceptionAsVariables(inner, depth + 1)
                };
            }

            variables["InnerExceptions"] = new VariableData
            {
                Name = "InnerExceptions",
                Type = "AggregateException.InnerExceptions",
                Value = $"[{aggEx.InnerExceptions.Count} exceptions]",
                IsNull = false,
                Children = innerChildren
            };
        }

        return variables;
    }

    private VariableData CaptureValue(string name, object? value, Type type, int depth)
    {
        if (value == null)
        {
            return new VariableData
            {
                Name = name,
                Type = type.Name,
                Value = "null",
                IsNull = true
            };
        }

        if (depth > _config.MaxVariableDepth)
        {
            return new VariableData
            {
                Name = name,
                Type = type.Name,
                Value = "<max depth>",
                IsTruncated = true
            };
        }

        // Primitives
        if (type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
        {
            return new VariableData
            {
                Name = name,
                Type = type.Name,
                Value = value.ToString() ?? "",
                IsNull = false
            };
        }

        // String
        if (type == typeof(string))
        {
            var str = (string)value;
            return new VariableData
            {
                Name = name,
                Type = "string",
                Value = TruncateString(str),
                IsNull = false,
                IsTruncated = str.Length > _config.MaxStringLength
            };
        }

        // Arrays and collections
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var collection = value as System.Collections.IEnumerable;
            var count = 0;
            if (collection != null)
            {
                foreach (var _ in collection) count++;
            }
            return new VariableData
            {
                Name = name,
                Type = type.Name,
                Value = $"[{count} items]",
                IsNull = false
            };
        }

        // Other objects - just show type name
        return new VariableData
        {
            Name = name,
            Type = type.Name,
            Value = $"<{type.Name}>",
            IsNull = false
        };
    }

    private string TruncateString(string? str)
    {
        if (str == null) return "";
        if (str.Length <= _config.MaxStringLength) return str;
        return str.Substring(0, _config.MaxStringLength);
    }

    private string ComputeFingerprint(Exception exception)
    {
        var stackTrace = new StackTrace(exception, false);
        var topFrames = stackTrace.GetFrames()?.Take(3) ?? Array.Empty<StackFrame>();

        var sb = new StringBuilder();
        sb.Append(exception.GetType().FullName);
        sb.Append(':');

        foreach (var frame in topFrames)
        {
            var method = frame.GetMethod();
            if (method != null)
            {
                sb.Append(method.DeclaringType?.FullName);
                sb.Append('.');
                sb.Append(method.Name);
                sb.Append(':');
            }
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLower();
    }
}
