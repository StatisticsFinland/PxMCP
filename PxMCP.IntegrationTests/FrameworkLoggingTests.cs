using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using PxMCP.Services;

namespace PxMCP.IntegrationTests;

/// <summary>
/// Exercises framework logging with the production typed HTTP client and a mocked upstream handler.
/// </summary>
[TestFixture]
public sealed class FrameworkLoggingTests
{
    /// <summary>
    /// Checks the normal logging profile with caller-controlled URL paths and query text over HTTP/MCP.
    /// </summary>
    [TestCase("Information")]
    [TestCase("Debug")]
    [TestCase("Trace")]
    public async Task Search_DoesNotLogCallerInputInSuccessfulHttpCall(string minimumLevel)
    {
        const string marker = "synthetic-private-input";
        const string databaseMarker = "syntheticPrivateDb";
        Mock<ILogger> logger = new();
        logger.Setup(instance => instance.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> provider = new();
        provider.Setup(instance => instance.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        Mock<HttpMessageHandler> handler = new();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"query":{"q":"test","target":"Content","lang":"en"},"results":[],"pagingInfo":{"currentPage":1,"pageSize":20,"totalItems":0}}""",
                    Encoding.UTF8, "application/json")
            });
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PxApi:BaseUrl"] = "https://pxapi.test.invalid/px/beta/",
            ["Logging:LogLevel:Default"] = minimumLevel,
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(provider.Object);
        await using WebApplication app = Program.CreateApplication(builder, services =>
        {
            services.AddHttpClient<IPxApiClient, PxApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => handler.Object);
        });
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new
                {
                    name = "search",
                    arguments = new { database = databaseMarker, query = marker }
                }
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        string[] entries = logger.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log))
            .Select(invocation => invocation.Arguments[2].ToString()!)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("structuredContent"));
            Assert.That(body, Does.Not.Contain("\"isError\":true"));
            Assert.That(entries, Has.None.Contains(marker));
            Assert.That(entries, Has.None.Contains(databaseMarker));
            Assert.That(entries, Has.None.Contains("Start processing HTTP request"));
            Assert.That(entries, Has.None.Contains("Sending HTTP request"));
        }
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(message =>
                message.RequestUri != null &&
                message.RequestUri.AbsolutePath.Contains(databaseMarker) &&
                message.RequestUri.Query.Contains(marker)),
            ItExpr.IsAny<CancellationToken>());
    }
}
