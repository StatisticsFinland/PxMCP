namespace PxMCP.Configuration;

/// <summary>
/// Concurrency limits for the MCP endpoint. Requests beyond <see cref="MaxConcurrentRequests"/> wait in a queue
/// of up to <see cref="QueueLimit"/> requests; further requests are rejected with HTTP 503 and <c>Retry-After</c>.
/// </summary>
public class McpLimitsOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "McpLimits";

    /// <summary>
    /// Maximum number of MCP requests processed at the same time per application instance. Must be at least 1. Default: 64.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 64;

    /// <summary>
    /// Maximum number of MCP requests waiting for a free slot, processed oldest first. Must be 0 or greater;
    /// 0 rejects immediately when all slots are busy. Default: 32.
    /// </summary>
    public int QueueLimit { get; set; } = 32;
}
