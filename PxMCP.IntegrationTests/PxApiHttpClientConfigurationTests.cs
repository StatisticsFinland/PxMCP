using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using PxMCP.Services;

namespace PxMCP.IntegrationTests;

/// <summary>
/// Verifies the production PX API HTTP client configuration: connection pooling, timeouts, HTTP version and
/// response size limits, plus startup validation of the related options.
/// </summary>
[TestFixture]
public sealed class PxApiHttpClientConfigurationTests
{
    private const string ClientName = nameof(IPxApiClient);

    [Test]
    public async Task Application_ConfiguresPxApiHttpClientFromOptions()
    {
        await using WebApplication app = Program.CreateApplication(CreateBuilder(new()
        {
            ["PxApi:TimeoutSeconds"] = "2",
            ["PxApi:SearchTimeoutSeconds"] = "7.5",
            ["PxApi:MaxConnectionsPerServer"] = "5"
        }));

        using HttpClient client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        SocketsHttpHandler handler = GetPrimaryHandler(app);
        HttpClientFactoryOptions factoryOptions =
            app.Services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(ClientName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://pxapi.test.invalid/px/beta/")));
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7.5)));
            Assert.That(client.MaxResponseContentBufferSize, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(client.DefaultRequestVersion, Is.EqualTo(HttpVersion.Version20));
            Assert.That(client.DefaultVersionPolicy, Is.EqualTo(HttpVersionPolicy.RequestVersionOrLower));
            Assert.That(handler.MaxConnectionsPerServer, Is.EqualTo(5));
            Assert.That(handler.PooledConnectionIdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(handler.PooledConnectionLifetime, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(factoryOptions.HandlerLifetime, Is.EqualTo(Timeout.InfiniteTimeSpan));
        }
    }

    [Test]
    public async Task Application_UsesDefaultsAndNoConnectionLimitWhenNotConfigured()
    {
        await using WebApplication app = Program.CreateApplication(CreateBuilder([]));

        using HttpClient client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        SocketsHttpHandler handler = GetPrimaryHandler(app);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(handler.MaxConnectionsPerServer, Is.EqualTo(new SocketsHttpHandler().MaxConnectionsPerServer));
            Assert.That(handler.MaxConnectionsPerServer, Is.EqualTo(int.MaxValue));
        }
    }

    [TestCase("PxApi:TimeoutSeconds", "0")]
    [TestCase("PxApi:TimeoutSeconds", "-1")]
    [TestCase("PxApi:TimeoutSeconds", "301")]
    [TestCase("PxApi:TimeoutSeconds", "NaN")]
    [TestCase("PxApi:SearchTimeoutSeconds", "0")]
    [TestCase("PxApi:SearchTimeoutSeconds", "Infinity")]
    [TestCase("PxApi:MaxConnectionsPerServer", "0")]
    [TestCase("PxApi:MaxConnectionsPerServer", "-5")]
    public void Application_RejectsInvalidPxApiLimits(string key, string value)
    {
        WebApplicationBuilder builder = CreateBuilder(new() { [key] = value });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain(key));
    }

    /// <summary>
    /// Requests sent by the production typed client actually carry HTTP/2 with downgrade allowed. Asserting only
    /// <see cref="HttpClient.DefaultRequestVersion"/> is not enough: it does not apply to caller-constructed messages.
    /// Only the innermost network handler is replaced, so the production client and DI composition are exercised.
    /// </summary>
    [Test]
    public async Task ProductionClient_SendsHttp2WithFallbackForGetPostAndHead()
    {
        CapturingHandler capture = new();
        await using WebApplication app = Program.CreateApplication(CreateBuilder([]), services =>
            services.AddHttpClient<IPxApiClient, PxApiClient>().ConfigurePrimaryHttpMessageHandler(() => capture));
        IPxApiClient client = app.Services.GetRequiredService<IPxApiClient>();

        Assert.ThrowsAsync<HttpRequestException>(() => client.GetDatabasesAsync(null));
        Assert.ThrowsAsync<HttpRequestException>(() => client.PostDataAsync("StatFin", "table1", null, null));
        Assert.ThrowsAsync<HttpRequestException>(() => client.GetMaxCellsAsync("StatFin", "table1", null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capture.Requests.Select(request => request.Method), Is.EqualTo(new[] { "GET", "POST", "HEAD" }));
            Assert.That(capture.Requests.Select(request => request.Version), Is.All.EqualTo(HttpVersion.Version20));
            Assert.That(capture.Requests.Select(request => request.Policy), Is.All.EqualTo(HttpVersionPolicy.RequestVersionOrLower));
        }
    }

    private static SocketsHttpHandler GetPrimaryHandler(WebApplication app)
    {
        HttpMessageHandler handler = app.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        return handler as SocketsHttpHandler
            ?? throw new InvalidOperationException($"Expected a SocketsHttpHandler but found {handler.GetType().Name}.");
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

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Method, Version Version, HttpVersionPolicy Policy)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.Version, request.VersionPolicy));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
