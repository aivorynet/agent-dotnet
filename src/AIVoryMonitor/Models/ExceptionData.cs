using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIVory.Monitor;

/// <summary>
/// Data model for captured exceptions.
/// </summary>
public class ExceptionData
{
    [JsonPropertyName("exception_type")]
    public string ExceptionType { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; set; }

    [JsonPropertyName("method_name")]
    public string? MethodName { get; set; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "error";

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "dotnet";

    [JsonPropertyName("runtime_version")]
    public string? RuntimeVersion { get; set; }

    [JsonPropertyName("stack_trace")]
    public List<StackFrameData> StackTrace { get; set; } = new();

    [JsonPropertyName("local_variables")]
    public Dictionary<string, object?>? LocalVariables { get; set; }

    [JsonPropertyName("request_context")]
    public Dictionary<string, object?>? RequestContext { get; set; }
}

/// <summary>
/// Data model for a stack frame.
/// </summary>
public class StackFrameData
{
    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    [JsonPropertyName("method_name")]
    public string? MethodName { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; set; }

    [JsonPropertyName("column_number")]
    public int ColumnNumber { get; set; }

    [JsonPropertyName("is_native")]
    public bool IsNative { get; set; }

    [JsonPropertyName("local_variables")]
    public Dictionary<string, VariableData>? LocalVariables { get; set; }
}

/// <summary>
/// Data model for a captured variable.
/// </summary>
public class VariableData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("is_null")]
    public bool IsNull { get; set; }

    [JsonPropertyName("is_truncated")]
    public bool IsTruncated { get; set; }

    [JsonPropertyName("children")]
    public Dictionary<string, VariableData>? Children { get; set; }
}

/// <summary>
/// Data model for captured snapshots.
/// </summary>
public class SnapshotData
{
    [JsonPropertyName("breakpoint_id")]
    public string? BreakpointId { get; set; }

    [JsonPropertyName("exception_id")]
    public string? ExceptionId { get; set; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; set; }

    [JsonPropertyName("method_name")]
    public string? MethodName { get; set; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; set; }

    [JsonPropertyName("stack_trace")]
    public List<StackFrameData> StackTrace { get; set; } = new();

    [JsonPropertyName("local_variables")]
    public Dictionary<string, VariableData>? LocalVariables { get; set; }

    [JsonPropertyName("request_context")]
    public Dictionary<string, object?>? RequestContext { get; set; }
}
