using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PxMCP.Services;

/// <summary>
/// MCP request filter that catches parameter binding errors during tool invocation
/// and returns a user-friendly error result instead of a generic server error.
/// </summary>
public static class ToolErrorFilter
{
    /// <summary>
    /// The names of tools that accept a <c>filters</c> parameter keyed by dimension code.
    /// </summary>
    private static readonly HashSet<string> FilterTools = ["get_data", "validate_query"];

    /// <summary>
    /// Creates a <see cref="McpRequestFilter{TParams, TResult}"/> for <c>tools/call</c> that
    /// logs JSON binding failures, including missing polymorphic discriminators, and returns
    /// a <see cref="CallToolResult"/> with <c>IsError = true</c> and actionable guidance.
    /// For <c>get_data</c> and <c>validate_query</c>, the message describes the expected filter
    /// structure. For other tools, it returns a generic invalid-arguments message.
    /// Explicit nulls for required parameters with a non-nullable single-type schema are
    /// rejected before invoking the tool. Logs retain the exception category and stack trace,
    /// but omit exception messages, inner exceptions and JSON paths that may contain caller input.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (request, cancellationToken) =>
        {
            try
            {
                if (request.MatchedPrimitive is McpServerTool tool &&
                    request.Params?.Arguments is { } arguments &&
                    tool.ProtocolTool.InputSchema.TryGetProperty("required", out JsonElement required) &&
                    tool.ProtocolTool.InputSchema.TryGetProperty("properties", out JsonElement properties))
                {
                    foreach (JsonElement parameter in required.EnumerateArray())
                    {
                        string name = parameter.GetString()!;
                        if (arguments.TryGetValue(name, out JsonElement value) &&
                            value.ValueKind == JsonValueKind.Null &&
                            properties.GetProperty(name).TryGetProperty("type", out JsonElement type) &&
                            type.ValueKind == JsonValueKind.String &&
                            type.GetString() != "null")
                        {
                            throw new ArgumentException("A required non-nullable tool argument was null.", "arguments");
                        }
                    }
                }

                return await next(request, cancellationToken);
            }
            catch (Exception exception) when (IsJsonBindingException(exception))
            {
                string toolName = request.Params?.Name ?? "unknown";
                ILogger? logger = request.Services?.GetService<ILoggerFactory>()?
                    .CreateLogger(typeof(ToolErrorFilter).FullName!);
                logger?.LogWarning(CreateDiagnosticException(exception),
                    "JSON argument binding failed for MCP tool {ToolName}. Failure type: {FailureType}.",
                    FilterTools.Contains(toolName) ? toolName : "other", exception.GetType().Name);

                string message = FilterTools.Contains(toolName)
                    ? "Invalid 'filters' argument. Each filter must be an object with a 'type' property " +
                      "(Code, From, To, First, or Last) and a 'query' value matching that type. " +
                      "Example: {\"dimension\": {\"type\": \"Code\", \"query\": [\"value1\"]}}"
                    : "Invalid tool arguments. Check that all parameter values match the expected types.";

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = message }]
                };
            }
            catch (ArgumentException exception) when (exception.ParamName == "arguments")
            {
                string toolName = request.Params?.Name ?? "unknown";
                ILogger? logger = request.Services?.GetService<ILoggerFactory>()?
                    .CreateLogger(typeof(ToolErrorFilter).FullName!);
                logger?.LogWarning(CreateDiagnosticException(exception),
                    "MCP tool argument binding failed for {ToolName}.",
                    FilterTools.Contains(toolName) ? toolName : "other");

                return new CallToolResult
                {
                    IsError = true,
                    Content =
                    [
                        new TextContentBlock
                        {
                            Text = "Invalid tool arguments. Check that all required arguments are provided and values match their expected types."
                        }
                    ]
                };
            }
        };
    }

    private static bool IsJsonBindingException(Exception exception) =>
        exception is JsonException ||
        exception is NotSupportedException && exception.Source == "System.Text.Json";

    private static Exception CreateDiagnosticException(Exception exception)
    {
        // Serializer messages, paths and inner exceptions may echo caller-controlled data.
        Exception diagnostic = new("MCP tool argument binding failed.");
        if (exception.StackTrace is { } stackTrace)
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(diagnostic, stackTrace);
        }

        return diagnostic;
    }
}
