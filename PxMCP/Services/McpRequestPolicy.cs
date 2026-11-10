using Microsoft.Extensions.Primitives;
using PxMCP.Configuration;

namespace PxMCP.Services;

/// <summary>
/// Validated, immutable form of <see cref="McpSecurityOptions"/> that decides whether an MCP request's
/// <c>Host</c> and <c>Origin</c> headers are allowed.
/// </summary>
public sealed class McpRequestPolicy
{
    /// <summary>
    /// Host names accepted when <see cref="McpSecurityOptions.AllowedHosts"/> is not configured.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultAllowedHosts = ["localhost", "127.0.0.1", "[::1]"];

    private readonly bool _allowAnyHost;
    private readonly HashSet<string> _allowedHosts;
    private readonly HashSet<string> _allowedOrigins;

    private McpRequestPolicy(bool allowAnyHost, HashSet<string> allowedHosts, HashSet<string> allowedOrigins)
    {
        _allowAnyHost = allowAnyHost;
        _allowedHosts = allowedHosts;
        _allowedOrigins = allowedOrigins;
    }

    /// <summary>
    /// Validates <paramref name="options"/> and creates a policy, applying loopback defaults for missing values.
    /// </summary>
    /// <exception cref="InvalidOperationException">A configured host or origin is invalid.</exception>
    public static McpRequestPolicy Create(McpSecurityOptions? options)
    {
        string[] configuredHosts = options?.AllowedHosts ?? [];
        string[] configuredOrigins = options?.AllowedOrigins ?? [];

        bool allowAnyHost = configuredHosts is ["*"];
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        if (!allowAnyHost)
        {
            foreach (string host in configuredHosts.Length == 0 ? DefaultAllowedHosts : configuredHosts)
            {
                if (string.IsNullOrWhiteSpace(host) ||
                    host.Contains('*') ||
                    (host.Contains(':') && !host.StartsWith('[')) ||
                    Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
                {
                    throw new InvalidOperationException(
                        $"Configuration '{McpSecurityOptions.SectionName}:AllowedHosts' must contain host names without ports or wildcards, or a single '*'.");
                }

                hosts.Add(host);
            }
        }

        HashSet<string> origins = new(StringComparer.Ordinal);
        foreach (string origin in configuredOrigins)
        {
            if (!TryNormalizeOrigin(origin, out string? normalized))
            {
                throw new InvalidOperationException(
                    $"Configuration '{McpSecurityOptions.SectionName}:AllowedOrigins' must contain absolute HTTP or HTTPS origins without path, query or fragment.");
            }

            origins.Add(normalized);
        }

        return new McpRequestPolicy(allowAnyHost, hosts, origins);
    }

    /// <summary>
    /// Returns whether the request's <c>Host</c> header names an allowed host. The port is ignored.
    /// </summary>
    public bool IsHostAllowed(HostString host)
    {
        return _allowAnyHost || (host.HasValue && _allowedHosts.Contains(host.Host));
    }

    /// <summary>
    /// Returns whether the request's <c>Origin</c> header is absent, a loopback origin, or an allowed origin.
    /// Multiple values, <c>null</c> and non-HTTP origins are rejected.
    /// </summary>
    public bool IsOriginAllowed(StringValues origin)
    {
        if (StringValues.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (origin.Count != 1 || !TryParseOrigin(origin[0], out Uri? uri))
        {
            return false;
        }

        return uri.IsLoopback || _allowedOrigins.Contains(Normalize(uri));
    }

    private static bool TryNormalizeOrigin(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? normalized)
    {
        normalized = TryParseOrigin(value, out Uri? uri) ? Normalize(uri) : null;
        return normalized is not null;
    }

    private static bool TryParseOrigin(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            parsed.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string Normalize(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
}
