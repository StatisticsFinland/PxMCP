using Microsoft.AspNetCore.Http.Features;

namespace PxMCP.Services;

/// <summary>
/// Validates MCP endpoint requests before the concurrency limiter and MCP transport handle them:
/// requests whose <c>Host</c> or <c>Origin</c> header is not allowed by <see cref="McpRequestPolicy"/> are rejected
/// with HTTP 403 Forbidden, and requests declaring a body larger than <see cref="MaxRequestBodyBytes"/> with HTTP 413.
/// Bodies without a declared length are limited by the server while the endpoint reads them, after the limiter.
/// Other endpoints, such as <c>/health</c>, are not affected.
/// </summary>
public sealed class McpRequestValidationMiddleware(
    RequestDelegate next,
    McpRequestPolicy policy,
    ILogger<McpRequestValidationMiddleware> logger)
{
    /// <summary>
    /// Path prefix of the MCP endpoint protected by this middleware.
    /// </summary>
    public static readonly PathString McpPath = new("/mcp");

    /// <summary>
    /// Largest accepted MCP request body. A large realistic filter with thousands of codes is a few tens of KB.
    /// </summary>
    public const long MaxRequestBodyBytes = 1024 * 1024;

    /// <summary>
    /// Validates the request and either short-circuits with 403 or 413, or invokes the next middleware.
    /// A declared <c>Content-Length</c> over the limit is rejected immediately; bodies without a declared length
    /// are limited through <see cref="IHttpMaxRequestBodySizeFeature"/> where the server supports it (Kestrel).
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(McpPath))
        {
            return next(context);
        }

        string? rejectedHeader = !policy.IsHostAllowed(context.Request.Host) ? "Host"
            : !policy.IsOriginAllowed(context.Request.Headers.Origin) ? "Origin"
            : null;
        if (rejectedHeader is not null)
        {
            // Header values are caller-controlled and deliberately not logged.
            logger.LogWarning("Rejected MCP request with a disallowed {HeaderName} header.", rejectedHeader);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        if (context.Request.ContentLength > MaxRequestBodyBytes)
        {
            logger.LogWarning("Rejected MCP request with a body larger than {MaxRequestBodyBytes} bytes.", MaxRequestBodyBytes);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySizeFeature)
        {
            bodySizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
        }

        return next(context);
    }
}
