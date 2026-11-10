using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.IntegrationTests;

/// <summary>
/// Verifies Host and Origin validation of the MCP endpoint through the production HTTP host.
/// </summary>
[TestFixture]
public sealed class McpRequestValidationTests
{
    private const string ToolsListJson = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

    private const string ListTablesJson =
        """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_tables","arguments":{"database":"StatFin"}}}""";

    [TestCase(null, null)]
    [TestCase("localhost:5277", null)]
    [TestCase("127.0.0.1:5277", "http://127.0.0.1:5277")]
    [TestCase("[::1]:5277", "http://[::1]:5277")]
    [TestCase("localhost", "http://localhost:6274")]
    [TestCase("LOCALHOST", "https://localhost")]
    public async Task DefaultPolicy_AcceptsLoopbackHostAndOrigin(string? host, string? origin)
    {
        await using WebApplication app = await StartAsync([]);

        using HttpResponseMessage response = await PostMcpAsync(app, host, origin);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(null, "http://evil.example")]
    [TestCase(null, "http://localhost.evil.example")]
    [TestCase(null, "null")]
    [TestCase(null, "file://localhost")]
    [TestCase("evil.example", null)]
    [TestCase("evil.example:5277", "http://evil.example:5277")]
    [TestCase("192.168.1.10:5277", null)]
    public async Task DefaultPolicy_RejectsNonLoopbackHostOrOriginWith403(string? host, string? origin)
    {
        await using WebApplication app = await StartAsync([]);

        using HttpResponseMessage response = await PostMcpAsync(app, host, origin);
        string body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(body, Is.Empty);
        }
    }

    [Test]
    public async Task DefaultPolicy_RejectsMultipleOriginHeaders()
    {
        await using WebApplication app = await StartAsync([]);
        using HttpRequestMessage request = CreateMcpRequest(null, null);
        request.Headers.TryAddWithoutValidation("Origin", ["http://localhost", "http://evil.example"]);

        using HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task DefaultPolicy_DoesNotApplyToHealthEndpoint()
    {
        Mock<IPxApiClient> apiClient = new(MockBehavior.Strict);
        apiClient.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await using WebApplication app = await StartAsync([], apiClient);
        using HttpRequestMessage request = new(HttpMethod.Get, "/health");
        request.Headers.Host = "10.0.0.5:8080";

        using HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ConfiguredPolicy_ReplacesLoopbackHostsAndAddsExactOrigins()
    {
        await using WebApplication app = await StartAsync(new()
        {
            ["McpSecurity:AllowedHosts:0"] = "pxmcp.example.org",
            ["McpSecurity:AllowedOrigins:0"] = "https://chat.example.org"
        });

        using HttpResponseMessage allowed = await PostMcpAsync(app, "pxmcp.example.org", "https://chat.example.org");
        using HttpResponseMessage loopbackOrigin = await PostMcpAsync(app, "pxmcp.example.org", "http://localhost:6274");
        using HttpResponseMessage noOrigin = await PostMcpAsync(app, "PXMCP.example.org:443", null);
        using HttpResponseMessage loopbackHost = await PostMcpAsync(app, "localhost", null);
        using HttpResponseMessage otherOrigin = await PostMcpAsync(app, "pxmcp.example.org", "https://other.example.org");
        using HttpResponseMessage otherPort = await PostMcpAsync(app, "pxmcp.example.org", "https://chat.example.org:8443");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loopbackOrigin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(noOrigin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loopbackHost.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(otherOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(otherPort.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    [Test]
    public async Task WildcardHost_DisablesHostValidationButKeepsOriginValidation()
    {
        await using WebApplication app = await StartAsync(new() { ["McpSecurity:AllowedHosts:0"] = "*" });

        using HttpResponseMessage anyHost = await PostMcpAsync(app, "internal-name:8080", null);
        using HttpResponseMessage badOrigin = await PostMcpAsync(app, "internal-name:8080", "http://evil.example");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(anyHost.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(badOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    /// <summary>
    /// A body of exactly the limit is accepted; one byte more is rejected with 413 before the MCP transport,
    /// and Host/Origin validation still runs first.
    /// </summary>
    [Test]
    public async Task RequestBody_RejectsDeclaredLengthOverLimitWith413()
    {
        await using WebApplication app = await StartAsync([]);
        using HttpClient client = app.GetTestClient();
        int limit = checked((int)McpRequestValidationMiddleware.MaxRequestBodyBytes);

        using HttpRequestMessage atLimitRequest = CreatePaddedMcpRequest(limit, origin: null);
        using HttpRequestMessage overLimitRequest = CreatePaddedMcpRequest(limit + 1, origin: null);
        using HttpRequestMessage overLimitBadOriginRequest = CreatePaddedMcpRequest(limit + 1, "http://evil.example");

        using HttpResponseMessage atLimit = await client.SendAsync(atLimitRequest);
        using HttpResponseMessage overLimit = await client.SendAsync(overLimitRequest);
        string overLimitBody = await overLimit.Content.ReadAsStringAsync();
        using HttpResponseMessage overLimitBadOrigin = await client.SendAsync(overLimitBadOriginRequest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atLimit.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(overLimit.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(overLimitBody, Is.Empty);
            Assert.That(overLimitBadOrigin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }
    }

    /// <summary>
    /// Bodies without a declared length go through the production pipeline on a real Kestrel server: Host/Origin
    /// validation, the concurrency limiter and the MCP transport. Kestrel enforces the 1 MB limit while the MCP
    /// endpoint reads the body. With one slot and no queue, a follow-up request succeeding proves the rejected
    /// request released its slot.
    /// </summary>
    [Test]
    public async Task ChunkedRequestBody_OverLimitIsRejectedByProductionPipelineAndReleasesSlot()
    {
        Mock<IPxApiClient> apiClient = new(MockBehavior.Strict);
        apiClient.Setup(client => client.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedTableList
            {
                Tables = [],
                PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 0 }
            });
        await using WebApplication app = await StartKestrelAsync(new()
        {
            ["McpLimits:MaxConcurrentRequests"] = "1",
            ["McpLimits:QueueLimit"] = "0"
        }, apiClient);
        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.Single()
            ?? throw new InvalidOperationException("Kestrel did not report its listening address.");
        using HttpClient client = new(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new Uri(address),
            Timeout = TimeSpan.FromSeconds(10)
        };
        int limit = checked((int)McpRequestValidationMiddleware.MaxRequestBodyBytes);

        using HttpRequestMessage underLimitRequest = CreateChunkedMcpRequest(64 * 1024);
        using HttpResponseMessage underLimit = await client.SendAsync(underLimitRequest);
        string underLimitBody = await underLimit.Content.ReadAsStringAsync();
        using HttpRequestMessage overLimitRequest = CreateChunkedMcpRequest(limit + 1);
        using HttpResponseMessage overLimit = await client.SendAsync(overLimitRequest);
        using HttpRequestMessage followUpRequest = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(ListTablesJson, Encoding.UTF8, "application/json")
        };
        followUpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        followUpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using HttpResponseMessage followUp = await client.SendAsync(followUpRequest);
        string followUpBody = await followUp.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(underLimit.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(underLimitBody, Does.Contain("list_tables"), "The chunked request must reach the MCP transport.");
            Assert.That(overLimit.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(followUp.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The rejected request must release its slot.");
            Assert.That(followUpBody, Does.Not.Contain("\"isError\":true"));
        }

        apiClient.Verify(
            api => api.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("McpSecurity:AllowedHosts:0", "pxmcp.example.org:443")]
    [TestCase("McpSecurity:AllowedHosts:0", "*.example.org")]
    [TestCase("McpSecurity:AllowedHosts:0", " ")]
    [TestCase("McpSecurity:AllowedOrigins:0", "https://chat.example.org/path")]
    [TestCase("McpSecurity:AllowedOrigins:0", "chat.example.org")]
    [TestCase("McpSecurity:AllowedOrigins:0", "null")]
    public void Application_RejectsInvalidMcpSecurityConfiguration(string key, string value)
    {
        WebApplicationBuilder builder = CreateBuilder(new() { [key] = value });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain(key[..key.LastIndexOf(':')]));
    }

    private static async Task<WebApplication> StartAsync(
        Dictionary<string, string?> configuration,
        Mock<IPxApiClient>? apiClient = null)
    {
        Mock<IPxApiClient> client = apiClient ?? new(MockBehavior.Strict);
        WebApplication app = Program.CreateApplication(CreateBuilder(configuration), services =>
        {
            services.RemoveAll<IPxApiClient>();
            services.AddSingleton(client.Object);
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartKestrelAsync(
        Dictionary<string, string?> configuration,
        Mock<IPxApiClient> apiClient)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        configuration["PxApi:BaseUrl"] = "https://pxapi.test.invalid/px/beta/";
        builder.Configuration.AddInMemoryCollection(configuration);
        WebApplication app = Program.CreateApplication(builder, services =>
        {
            services.RemoveAll<IPxApiClient>();
            services.AddSingleton(apiClient.Object);
        });
        await app.StartAsync();
        return app;
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> configuration)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        configuration["PxApi:BaseUrl"] = "https://pxapi.test.invalid/px/beta/";
        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }

    private static async Task<HttpResponseMessage> PostMcpAsync(WebApplication app, string? host, string? origin)
    {
        using HttpRequestMessage request = CreateMcpRequest(host, origin);
        return await app.GetTestClient().SendAsync(request);
    }

    private static HttpRequestMessage CreatePaddedMcpRequest(int totalBytes, string? origin)
    {
        // JSON allows trailing whitespace, so padding keeps the request valid while setting an exact length.
        string json = ToolsListJson + new string(' ', totalBytes - Encoding.UTF8.GetByteCount(ToolsListJson));
        HttpRequestMessage request = CreateMcpRequest(null, origin);
        request.Content?.Dispose();
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage CreateMcpRequest(string? host, string? origin)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(ToolsListJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return request;
    }

    private static HttpRequestMessage CreateChunkedMcpRequest(int totalBytes)
    {
        byte[] body = Encoding.UTF8.GetBytes(
            ToolsListJson + new string(' ', totalBytes - Encoding.UTF8.GetByteCount(ToolsListJson)));
        HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new UnknownLengthContent(body),
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.TransferEncodingChunked = true;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body, 0, body.Length);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            stream.WriteAsync(body, 0, body.Length, cancellationToken);
    }
}
