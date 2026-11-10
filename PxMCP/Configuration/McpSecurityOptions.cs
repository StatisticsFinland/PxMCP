namespace PxMCP.Configuration;

/// <summary>
/// HTTP request validation options for the MCP endpoint, protecting it against DNS rebinding
/// and cross-origin browser requests.
/// </summary>
public class McpSecurityOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "McpSecurity";

    /// <summary>
    /// Host names (without port) accepted in the <c>Host</c> header of MCP requests, compared case-insensitively.
    /// When empty, only loopback names are accepted: <c>localhost</c>, <c>127.0.0.1</c> and <c>[::1]</c>.
    /// A single <c>*</c> disables host validation, for example behind a reverse proxy that validates hosts itself.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>
    /// Additional exact browser origins (<c>scheme://host[:port]</c>) allowed to call the MCP endpoint.
    /// Requests without an <c>Origin</c> header and requests from loopback origins are always accepted.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
