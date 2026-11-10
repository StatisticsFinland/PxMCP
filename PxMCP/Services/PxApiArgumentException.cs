namespace PxMCP.Services;

/// <summary>
/// Thrown when a caller-supplied PX API argument is rejected before any upstream request is sent.
/// The message is safe to return to MCP clients and never contains the rejected value.
/// </summary>
/// <param name="message">Caller-safe description of the expected argument format.</param>
public sealed class PxApiArgumentException(string message) : ArgumentException(message);
