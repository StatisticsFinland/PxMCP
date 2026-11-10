using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ModelContextProtocol;
using PxMCP.Configuration;
using PxMCP.Services;

namespace PxMCP;

/// <summary>
/// Application entry point. Configures and starts the MCP server.
/// </summary>
public class Program
{
    /// <summary>
    /// Pooled PX API connections idle longer than this are closed, staying below common NAT and load balancer
    /// idle timeouts so a silently dropped connection is never reused.
    /// </summary>
    internal static readonly TimeSpan PxApiConnectionIdleTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// PX API connections are replaced after this lifetime so DNS changes are picked up without rotating handlers.
    /// </summary>
    internal static readonly TimeSpan PxApiConnectionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Largest PX API response body buffered in memory. A response at PX API's default 50,000-cell limit is a few MB.
    /// </summary>
    internal const long MaxPxApiResponseBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Name of the concurrency limiter policy applied to the MCP endpoint.
    /// </summary>
    internal const string McpConcurrencyPolicyName = "mcp";

    /// <summary>
    /// <c>Retry-After</c> value, in seconds, sent when the MCP concurrency limit and queue are full.
    /// </summary>
    internal const int McpRetryAfterSeconds = 1;

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        WebApplication app = CreateApplication(builder);
        app.Run();
    }

    internal static WebApplication CreateApplication(
        WebApplicationBuilder builder,
        Action<IServiceCollection>? configureServices = null)
    {
        AppContext.SetSwitch("System.Text.Json.Serialization.RespectNullableAnnotations", true);

        PxApiOptions pxApiOptions = builder.Configuration
            .GetSection(PxApiOptions.SectionName)
            .Get<PxApiOptions>()
            ?? throw new InvalidOperationException($"Missing '{PxApiOptions.SectionName}' configuration section.");
        Uri pxApiBaseAddress = CreatePxApiBaseAddress(pxApiOptions.BaseUrl);
        ValidatePxApiLimits(pxApiOptions);
        McpRequestPolicy mcpRequestPolicy = McpRequestPolicy.Create(builder.Configuration
            .GetSection(McpSecurityOptions.SectionName)
            .Get<McpSecurityOptions>());
        McpLimitsOptions mcpLimits = builder.Configuration
            .GetSection(McpLimitsOptions.SectionName)
            .Get<McpLimitsOptions>() ?? new();
        ValidateMcpLimits(mcpLimits);

        builder.Services.AddSingleton(pxApiOptions);
        builder.Services.AddSingleton(mcpRequestPolicy);

        // Bounds concurrent MCP work per instance. Each tool call makes at most one PX API request at a time, so this
        // also bounds outbound connections from tool calls; /health probes call the PX API outside this limit.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
            options.OnRejected = async (context, cancellationToken) =>
            {
                // Debug level: under sustained overload every rejected request would otherwise flood the logs.
                context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>()
                    .LogDebug("Rejected MCP request: the concurrency limit and queue are full.");
                context.HttpContext.Response.Headers.RetryAfter = McpRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("Server is busy. Retry later.", cancellationToken);
            };
            options.AddConcurrencyLimiter(McpConcurrencyPolicyName, limiter =>
            {
                limiter.PermitLimit = mcpLimits.MaxConcurrentRequests;
                limiter.QueueLimit = mcpLimits.QueueLimit;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        // One long-lived handler. The optional connection cap makes excess requests wait for a pooled connection
        // instead of opening new ones, for hosts with a limited outbound connection or port budget.
        builder.Services.AddHttpClient<IPxApiClient, PxApiClient>(client =>
            {
                client.BaseAddress = pxApiBaseAddress;
                client.Timeout = TimeSpan.FromSeconds(Math.Max(pxApiOptions.TimeoutSeconds, pxApiOptions.SearchTimeoutSeconds));
                client.MaxResponseContentBufferSize = MaxPxApiResponseBytes;
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                SocketsHttpHandler handler = new()
                {
                    PooledConnectionIdleTimeout = PxApiConnectionIdleTimeout,
                    PooledConnectionLifetime = PxApiConnectionLifetime
                };
                if (pxApiOptions.MaxConnectionsPerServer is int maxConnections)
                {
                    handler.MaxConnectionsPerServer = maxConnections;
                }

                return handler;
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        builder.Services
            .AddHealthChecks()
            .AddCheck<PxApiHealthCheck>("pxapi");

        JsonSerializerOptions toolSerializerOptions = new(McpJsonUtilities.DefaultOptions)
        {
            AllowOutOfOrderMetadataProperties = true
        };

        builder.Services
            .AddMcpServer(options =>
            {
                options.Filters.Request.CallToolFilters.Add(ToolErrorFilter.Create());
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly(serializerOptions: toolSerializerOptions);

        configureServices?.Invoke(builder.Services);

        WebApplication app = builder.Build();

        app.UseMiddleware<McpRequestValidationMiddleware>();
        app.UseRateLimiter();
        app.MapHealthChecks("/health");
        app.MapMcp(McpRequestValidationMiddleware.McpPath).RequireRateLimiting(McpConcurrencyPolicyName);

        return app;
    }

    private static Uri CreatePxApiBaseAddress(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{PxApiOptions.SectionName}:BaseUrl' must be an absolute HTTP or HTTPS URL.");
        }

        string normalizedBaseUrl = baseUrl.Trim();
        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out Uri? parsedBaseAddress) ||
            parsedBaseAddress.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(parsedBaseAddress.UserInfo) ||
            !string.IsNullOrEmpty(parsedBaseAddress.Query) ||
            !string.IsNullOrEmpty(parsedBaseAddress.Fragment))
        {
            throw new InvalidOperationException(
                $"Configuration '{PxApiOptions.SectionName}:BaseUrl' must be an absolute HTTP or HTTPS URL without user information, query, or fragment.");
        }

        return new Uri(normalizedBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static void ValidatePxApiLimits(PxApiOptions options)
    {
        EnsureValidTimeout(options.TimeoutSeconds, nameof(PxApiOptions.TimeoutSeconds));
        EnsureValidTimeout(options.SearchTimeoutSeconds, nameof(PxApiOptions.SearchTimeoutSeconds));
        if (options.MaxConnectionsPerServer is < 1)
        {
            throw new InvalidOperationException(
                $"Configuration '{PxApiOptions.SectionName}:{nameof(PxApiOptions.MaxConnectionsPerServer)}' must be at least 1 when set. Omit it for no limit.");
        }
    }

    private static void ValidateMcpLimits(McpLimitsOptions options)
    {
        if (options.MaxConcurrentRequests < 1)
        {
            throw new InvalidOperationException(
                $"Configuration '{McpLimitsOptions.SectionName}:{nameof(McpLimitsOptions.MaxConcurrentRequests)}' must be at least 1.");
        }

        if (options.QueueLimit < 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{McpLimitsOptions.SectionName}:{nameof(McpLimitsOptions.QueueLimit)}' must be 0 or greater.");
        }
    }

    private static void EnsureValidTimeout(double seconds, string name)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > PxApiOptions.MaxTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"Configuration '{PxApiOptions.SectionName}:{name}' must be greater than 0 and at most {PxApiOptions.MaxTimeoutSeconds} seconds.");
        }
    }
}
