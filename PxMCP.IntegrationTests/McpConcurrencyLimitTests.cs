using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.IntegrationTests;

/// <summary>
/// Verifies the MCP endpoint concurrency limit through the production HTTP host. A mocked <c>list_tables</c>
/// upstream call blocks until released, so slots and queue positions can be filled deterministically.
/// </summary>
[TestFixture]
public sealed class McpConcurrencyLimitTests
{
    private const string ListTablesJson =
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_tables","arguments":{"database":"StatFin"}}}""";

    private Mock<IPxApiClient> _apiClient = null!;
    private TaskCompletionSource _release = null!;
    private SemaphoreSlim _upstreamStarted = null!;

    [SetUp]
    public void SetUp()
    {
        _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _upstreamStarted = new(0);
        _apiClient = new(MockBehavior.Strict);
        _apiClient.Setup(client => client.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                _upstreamStarted.Release();
                await _release.Task;
                return new PagedTableList
                {
                    Tables = [],
                    PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 0 }
                };
            });
        _apiClient.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [TearDown]
    public void TearDown()
    {
        _release.TrySetResult();
        _upstreamStarted.Dispose();
    }

    [Test]
    public async Task FullLimitWithoutQueue_RejectsWith503AndRetryAfter()
    {
        await using WebApplication app = await StartAsync(new()
        {
            ["McpLimits:MaxConcurrentRequests"] = "1",
            ["McpLimits:QueueLimit"] = "0"
        });
        using HttpClient client = CreateClient(app);

        Task<HttpResponseMessage> first = PostMcpAsync(client, ListTablesJson);
        await WaitForUpstreamCallsAsync(1);

        using HttpResponseMessage rejected = await PostMcpAsync(client, ListTablesJson);
        string rejectedBody = await rejected.Content.ReadAsStringAsync();
        using HttpResponseMessage health = await client.GetAsync("/health");
        using HttpResponseMessage forbidden = await PostMcpAsync(client, ListTablesJson, origin: "http://evil.example");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(rejected.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(rejected.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/plain"));
            Assert.That(rejected.Content.Headers.ContentType?.CharSet, Is.EqualTo("utf-8"));
            Assert.That(rejectedBody, Is.EqualTo("Server is busy. Retry later."));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK), "/health must not be limited.");
            Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
                "Host/Origin validation must run before the concurrency limit.");
            Assert.That(first.IsCompleted, Is.False);
        }

        _release.SetResult();
        using HttpResponseMessage firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using HttpResponseMessage afterRelease = await PostMcpAsync(client, ListTablesJson);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterRelease.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The slot must be released after completion.");
        }

        _apiClient.Verify(
            api => api.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task QueuedRequest_WaitsForFreeSlotAndOverflowIsRejected()
    {
        await using WebApplication app = await StartAsync(new()
        {
            ["McpLimits:MaxConcurrentRequests"] = "1",
            ["McpLimits:QueueLimit"] = "1"
        });
        using HttpClient client = CreateClient(app);

        Task<HttpResponseMessage> first = PostMcpAsync(client, ListTablesJson);
        await WaitForUpstreamCallsAsync(1);

        // Either request may win the single queue position; exactly one must be rejected immediately.
        Task<HttpResponseMessage>[] contenders = [PostMcpAsync(client, ListTablesJson), PostMcpAsync(client, ListTablesJson)];
        Task<HttpResponseMessage> rejectedTask = await Task.WhenAny(contenders).WaitAsync(TimeSpan.FromSeconds(5));
        Task<HttpResponseMessage> queued = contenders[0] == rejectedTask ? contenders[1] : contenders[0];
        using HttpResponseMessage overflow = await rejectedTask;
        bool queuedReachedUpstream = await _upstreamStarted.WaitAsync(TimeSpan.FromMilliseconds(200));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overflow.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(queuedReachedUpstream, Is.False, "The queued request must wait for a free slot.");
            Assert.That(queued.IsCompleted, Is.False);
        }

        _release.SetResult();
        using HttpResponseMessage firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using HttpResponseMessage queuedResponse = await queued.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(queuedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        _apiClient.Verify(
            api => api.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// The queue is processed oldest first: when it is full, the newest request is rejected and the request that
    /// queued earlier keeps its position. A newest-first queue would instead evict the earlier request.
    /// The pause between requests lets the earlier one reach the queue first.
    /// </summary>
    [Test]
    public async Task FullQueue_RejectsNewestRequestAndKeepsEarlierQueuedRequest()
    {
        await using WebApplication app = await StartAsync(new()
        {
            ["McpLimits:MaxConcurrentRequests"] = "1",
            ["McpLimits:QueueLimit"] = "1"
        });
        using HttpClient client = CreateClient(app);

        Task<HttpResponseMessage> first = PostMcpAsync(client, ListTablesJson);
        await WaitForUpstreamCallsAsync(1);
        Task<HttpResponseMessage> earlier = PostMcpAsync(client, ListTablesJson);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Task<HttpResponseMessage> newest = PostMcpAsync(client, ListTablesJson);

        Task<HttpResponseMessage> completedFirst = await Task.WhenAny(earlier, newest).WaitAsync(TimeSpan.FromSeconds(5));
        using HttpResponseMessage rejected = await completedFirst;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completedFirst, Is.SameAs(newest), "The newest request must be the one rejected.");
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(earlier.IsCompleted, Is.False);
        }

        _release.SetResult();
        using HttpResponseMessage firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using HttpResponseMessage earlierResponse = await earlier.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(earlierResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Without configuration, exactly 64 requests run at once, 32 more wait, and the next one is rejected.
    /// </summary>
    [Test]
    public async Task DefaultLimits_Run64ConcurrentAndQueue32()
    {
        const int maxConcurrent = 64;
        const int queueLimit = 32;
        await using WebApplication app = await StartAsync([]);
        using HttpClient client = CreateClient(app);

        Task<HttpResponseMessage>[] accepted =
            [.. Enumerable.Range(0, maxConcurrent + queueLimit).Select(_ => PostMcpAsync(client, ListTablesJson))];
        await WaitForUpstreamCallsAsync(maxConcurrent);
        bool extraReachedUpstream = await _upstreamStarted.WaitAsync(TimeSpan.FromMilliseconds(300));
        using HttpResponseMessage overflow = await PostMcpAsync(client, ListTablesJson).WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(extraReachedUpstream, Is.False, $"Only {maxConcurrent} requests may run at once.");
            Assert.That(accepted.Count(request => request.IsCompleted), Is.Zero, $"{queueLimit} requests must wait in the queue.");
            Assert.That(overflow.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        }

        _release.SetResult();
        HttpResponseMessage[] responses = await Task.WhenAll(accepted).WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.That(responses.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        _apiClient.Verify(
            api => api.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()),
            Times.Exactly(maxConcurrent + queueLimit));
    }

    [TestCase("McpLimits:MaxConcurrentRequests", "0")]
    [TestCase("McpLimits:MaxConcurrentRequests", "-1")]
    [TestCase("McpLimits:QueueLimit", "-1")]
    public void Application_RejectsInvalidMcpLimits(string key, string value)
    {
        WebApplicationBuilder builder = CreateBuilder(new() { [key] = value });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain(key));
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private async Task WaitForUpstreamCallsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Assert.That(await _upstreamStarted.WaitAsync(TimeSpan.FromSeconds(5)), Is.True,
                $"Expected {count} upstream call(s) to start.");
        }
    }

    private async Task<WebApplication> StartAsync(Dictionary<string, string?> configuration)
    {
        WebApplication app = Program.CreateApplication(CreateBuilder(configuration), services =>
        {
            services.RemoveAll<IPxApiClient>();
            services.AddSingleton(_apiClient.Object);
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

    private static async Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string json, string? origin = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return await client.SendAsync(request);
    }
}
