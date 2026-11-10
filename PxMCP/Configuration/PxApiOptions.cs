namespace PxMCP.Configuration;

/// <summary>
/// Configuration options for the PX API backend connection.
/// </summary>
public class PxApiOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "PxApi";

    /// <summary>
    /// Largest accepted value for <see cref="TimeoutSeconds"/> and <see cref="SearchTimeoutSeconds"/>.
    /// </summary>
    public const double MaxTimeoutSeconds = 300;

    /// <summary>
    /// Absolute HTTP or HTTPS base URL of the PX API, including its path prefix (for example <c>/px/beta</c>).
    /// </summary>
    public required string BaseUrl { get; set; }

    /// <summary>
    /// Timeout in seconds for each PX API request other than search, including reading the response body.
    /// Must be greater than 0 and at most <see cref="MaxTimeoutSeconds"/>. Default: 3.
    /// </summary>
    public double TimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// Timeout in seconds for each PX API search request, including reading the response body.
    /// Must be greater than 0 and at most <see cref="MaxTimeoutSeconds"/>. Default: 15.
    /// </summary>
    public double SearchTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Optional maximum number of simultaneous connections to the PX API server per application instance.
    /// When set, additional requests wait for a free connection instead of opening new ones; use this on hosts
    /// with a limited outbound connection or port budget. When omitted (<see langword="null"/>), connections are
    /// not limited. Must be at least 1 when set.
    /// </summary>
    public int? MaxConnectionsPerServer { get; set; }
}
