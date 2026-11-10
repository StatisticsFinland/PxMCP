using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PxMCP.Configuration;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.IntegrationTests;

/// <summary>
/// Exercises the production HTTP host with a mocked PX API boundary.
/// </summary>
[TestFixture]
public sealed class HttpApplicationTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "list_databases",
        "list_tables",
        "search",
        "get_head_metadata",
        "get_table_metadata",
        "get_dimension_values",
        "get_data",
        "validate_query"
    ];

    private Microsoft.AspNetCore.Builder.WebApplication _app = null!;
    private Mock<IPxApiClient> _apiClientMock = null!;
    private HttpClient _client = null!;
    private Mock<ILogger> _bindingLogger = null!;

    [SetUp]
    public async Task SetUp()
    {
        _apiClientMock = new(MockBehavior.Strict);
        _bindingLogger = new();
        _bindingLogger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> logProvider = new();
        logProvider.Setup(provider => provider.CreateLogger(It.IsAny<string>()))
            .Returns((string category) => category == typeof(ToolErrorFilter).FullName
                ? _bindingLogger.Object
                : NullLogger.Instance);

        WebApplicationBuilder builder = CreateBuilder("https://pxapi.test.invalid");

        _app = Program.CreateApplication(builder, services =>
        {
            services.RemoveAll<IPxApiClient>();
            services.AddSingleton(_apiClientMock.Object);
            services.AddLogging(logging => logging.AddProvider(logProvider.Object));
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Test]
    public async Task Health_ReturnsHealthyUsingMockedPxApi()
    {
        _apiClientMock
            .Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DatabaseListingItem
                {
                    Id = "StatFin",
                    Name = "StatFin",
                    Description = "Statistics Finland",
                    TableCount = 1,
                    AvailableLanguages = ["fi", "en", "sv"],
                    Links = []
                }
            ]);

        using HttpResponseMessage response = await _client.GetAsync("/health");
        string body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.Contain("Healthy"));
        }

        _apiClientMock.Verify(
            client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task Health_ReturnsUnhealthyWithoutExposingUpstreamErrorDetails()
    {
        _apiClientMock
            .Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Sensitive upstream response details."));

        using HttpResponseMessage response = await _client.GetAsync("/health");
        string body = await response.Content.ReadAsStringAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body, Does.Contain("Unhealthy"));
            Assert.That(body, Does.Not.Contain("Sensitive upstream response details"));
        }

        _apiClientMock.Verify(
            client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void Application_RejectsMissingPxApiConfigurationSection()
    {
        WebApplicationBuilder builder = CreateBuilder(null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain("PxApi"));
    }

    [Test]
    public void Application_RejectsBlankPxApiBaseUrl()
    {
        WebApplicationBuilder builder = CreateBuilder(string.Empty);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain("PxApi:BaseUrl"));
    }

    [TestCase("not a url")]
    [TestCase("ftp://api.stat.fi/px/beta")]
    [TestCase("https://:@api.stat.fi/px/beta")]
    [TestCase("https://api.stat.fi/px/beta?mode=test")]
    [TestCase("https://api.stat.fi/px/beta#fragment")]
    public void Application_RejectsInvalidPxApiBaseUrl(string baseUrl)
    {
        WebApplicationBuilder builder = CreateBuilder(baseUrl);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Program.CreateApplication(builder))!;

        Assert.That(exception.Message, Does.Contain("PxApi:BaseUrl"));
    }

    [Test]
    public async Task McpEndpoint_InitializesAtDocumentedRoute()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"clientInfo\":{\"name\":\"integration-test\",\"version\":\"1.0\"}}}");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument payload = JsonDocument.Parse(ReadSseData(body));
        JsonElement result = payload.RootElement.GetProperty("result");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("serverInfo").GetProperty("name").GetString(), Is.Not.Empty);
            Assert.That(result.GetProperty("protocolVersion").GetString(), Is.EqualTo("2025-03-26"));
            JsonElement capabilities = result.GetProperty("capabilities");
            Assert.That(capabilities.TryGetProperty("tools", out _), Is.True);
            Assert.That(capabilities.TryGetProperty("resources", out _), Is.False);
            Assert.That(capabilities.TryGetProperty("prompts", out _), Is.False);
        }
    }

    /// <summary>
    /// Invalid required values must fail before invoking the upstream client.
    /// </summary>
    [TestCase("get_data", """{"database":null,"table":"population"}""")]
    [TestCase("get_data", """{"database":"TestDb","table":null}""")]
    [TestCase("get_data", """{"database":42,"table":"population"}""")]
    [TestCase("get_data", """{"database":"TestDb","table":[]}""")]
    [TestCase("list_tables", """{"database":null}""")]
    [TestCase("search", """{"query":null}""")]
    [TestCase("get_head_metadata", """{"database":"TestDb","table":null}""")]
    [TestCase("get_table_metadata", """{"database":"TestDb","table":null}""")]
    [TestCase("get_dimension_values", """{"database":"TestDb","table":"population","dimension":null}""")]
    [TestCase("validate_query", """{"database":null,"table":"population"}""")]
    public async Task McpEndpoint_InvalidRequiredValueDoesNotCallApi(string toolName, string arguments)
    {
        string json = """{"jsonrpc":"2.0","id":22,"method":"tools/call","params":{"name":""" +
            JsonSerializer.Serialize(toolName) + ""","arguments":""" + arguments + "}}";
        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("argument").IgnoreCase);
            Assert.That(result.TryGetProperty("structuredContent", out _), Is.False);
        }
        _apiClientMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Unknown tool names are protocol errors, not successful or failed tool executions.
    /// </summary>
    [Test]
    public async Task McpEndpoint_UnknownToolReturnsProtocolError()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            """{"jsonrpc":"2.0","id":23,"method":"tools/call","params":{"name":"unknown-tool","arguments":{}}}""");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument payload = JsonDocument.Parse(ReadSseData(body));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(payload.RootElement.TryGetProperty("result", out _), Is.False);
            Assert.That(payload.RootElement.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(-32602));
        }
        _apiClientMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Records SDK behavior for extra parameters without changing its compatibility policy.
    /// </summary>
    [Test]
    public async Task McpEndpoint_UnknownArgumentIsIgnoredWithoutChangingKnownArguments()
    {
        _apiClientMock.Setup(client => client.GetDatabasesAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using HttpResponseMessage response = await PostMcpAsync(
            """{"jsonrpc":"2.0","id":24,"method":"tools/call","params":{"name":"list_databases","arguments":{"lang":"en","unknown":"unused"}}}""");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        Assert.That(!result.TryGetProperty("isError", out JsonElement isError) || !isError.GetBoolean(), Is.True);
        _apiClientMock.Verify(client => client.GetDatabasesAsync("en", It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.VerifyNoOtherCalls();
    }

    [Test]
    public async Task McpEndpoint_ListsToolsThroughHttp()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument payload = JsonDocument.Parse(ReadSseData(body));
        JsonElement tools = payload.RootElement.GetProperty("result").GetProperty("tools");
        string[] actualToolNames = tools
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(actualToolNames, Is.EquivalentTo(ExpectedToolNames));
        }
    }

    [Test]
    public async Task McpEndpoint_InvokesToolAndReturnsMockedApiData()
    {
        _apiClientMock
            .Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DatabaseListingItem
                {
                    Id = "StatFin",
                    Name = "StatFin",
                    Description = null,
                    TableCount = 12,
                    AvailableLanguages = ["fi", "en"],
                    Links = []
                }
            ]);

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"list_databases\",\"arguments\":{}}}");
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument payload = JsonDocument.Parse(ReadSseData(body));
        JsonElement result = payload.RootElement.GetProperty("result");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                !result.TryGetProperty("isError", out JsonElement isError) || !isError.GetBoolean(),
                Is.True);
            Assert.That(
                result.GetProperty("structuredContent").GetProperty("databases")[0].GetProperty("id").GetString(),
                Is.EqualTo("StatFin"));
            Assert.That(
                result.GetProperty("structuredContent").GetProperty("databases")[0].GetProperty("tableCount").GetInt32(),
                Is.EqualTo(12));
        }

        _apiClientMock.Verify(
            client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task McpEndpoint_GetDataPassesValidatedFiltersAndReturnsDataset()
    {
        Dictionary<string, Filter>? capturedFilters = null;
        JsonStat2 metadata = CreateTableMetadata();
        JsonStat2 data = CreateDataResult();

        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);
        _apiClientMock
            .Setup(client => client.PostDataAsync("TestDb", "population", It.IsAny<Dictionary<string, Filter>>(), "en", It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>(
                (_, _, filters, _, _) => capturedFilters = filters)
            .ReturnsAsync(data);

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"lang\":\"en\",\"filters\":{\"year\":{\"type\":\"From\",\"query\":\"2020\"},\"region\":{\"type\":\"Code\",\"query\":[\"region-a\"]}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);
        JsonElement structuredContent = result.GetProperty("structuredContent");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                !result.TryGetProperty("isError", out JsonElement isError) || !isError.GetBoolean(),
                Is.True);
            Assert.That(structuredContent.GetProperty("label").GetString(), Is.EqualTo("Population test"));
            Assert.That(structuredContent.GetProperty("value")[0].GetDouble(), Is.EqualTo(123.0));
            Assert.That(structuredContent.GetProperty("status").GetProperty("0").GetString(), Is.EqualTo("p"));
            Assert.That(capturedFilters, Is.Not.Null.And.Count.EqualTo(2));
            Assert.That(capturedFilters!["year"], Is.TypeOf<FromFilter>());
            Assert.That(((FromFilter)capturedFilters["year"]).Query, Is.EqualTo("2020"));
            Assert.That(capturedFilters["region"], Is.TypeOf<CodeFilter>());
            Assert.That(((CodeFilter)capturedFilters["region"]).Query, Is.EqualTo(new[] { "region-a" }));
        }

        _apiClientMock.Verify(
            client => client.GetTableMetadataAsync("TestDb", "population", "en", It.IsAny<CancellationToken>()),
            Times.Once);
        _apiClientMock.Verify(
            client => client.PostDataAsync("TestDb", "population", It.IsAny<Dictionary<string, Filter>>(), "en", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsUnknownDimensionWithoutRequestingData()
    {
        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTableMetadata());

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"unknown-dimension\":{\"type\":\"Code\",\"query\":[\"x\"]}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);
        string errorText = GetToolErrorText(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(errorText, Does.Contain("Query validation failed"));
            Assert.That(errorText, Does.Contain("unknown-dimension"));
        }

        _apiClientMock.Verify(
            client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()),
            Times.Once);
        _apiClientMock.Verify(
            client => client.PostDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, Filter>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsInvalidCodeWithoutRequestingData()
    {
        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTableMetadata());

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"region\":{\"type\":\"Code\",\"query\":[\"unknown-code\"]}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);
        string errorText = GetToolErrorText(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(errorText, Does.Contain("Query validation failed"));
            Assert.That(errorText, Does.Contain("unknown-code"));
        }

        _apiClientMock.Verify(
            client => client.PostDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, Filter>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsNullFilterWithoutRequestingData()
    {
        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTableMetadata());

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":11,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"region\":null}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("must be an object"));
        }

        _apiClientMock.Verify(
            client => client.PostDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, Filter>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsNullFilterQueryWithoutRequestingData()
    {
        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTableMetadata());

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":12,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"region\":{\"type\":\"Code\",\"query\":null}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("query"));
        }

        _apiClientMock.Verify(
            client => client.PostDataAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, Filter>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task McpEndpoint_GetDataSurfacesUpstreamFailureAsToolError()
    {
        _apiClientMock
            .Setup(client => client.GetTableMetadataAsync("TestDb", "population", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTableMetadata());
        _apiClientMock
            .Setup(client => client.PostDataAsync("TestDb", "population", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 503 Service Unavailable.", null, HttpStatusCode.ServiceUnavailable));

        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"year\":{\"type\":\"From\",\"query\":\"2020\"}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);
        string errorText = GetToolErrorText(result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(errorText, Does.Contain("503"));
            Assert.That(errorText, Does.Contain("Service Unavailable"));
        }

        _apiClientMock.Verify(
            client => client.PostDataAsync("TestDb", "population", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsWrongFilterValueTypeWithoutCallingApi()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\",\"table\":\"population\",\"filters\":{\"year\":{\"type\":\"Code\",\"query\":\"2020\"}}}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("Invalid 'filters' argument"));
        }

        _apiClientMock.VerifyNoOtherCalls();
    }

    [Test]
    public async Task McpEndpoint_GetDataRejectsMissingRequiredArgumentWithoutCallingApi()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"get_data\",\"arguments\":{\"database\":\"TestDb\"}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(
                GetToolErrorText(result),
                Is.EqualTo("Invalid tool arguments. Check that all required arguments are provided and values match their expected types."));
        }

        _apiClientMock.VerifyNoOtherCalls();
    }

    [Test]
    public async Task McpEndpoint_SearchRejectsInvalidPageWithoutCallingApi()
    {
        using HttpResponseMessage response = await PostMcpAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"tools/call\",\"params\":{\"name\":\"search\",\"arguments\":{\"query\":\"population\",\"page\":0}}}");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("Page"));
        }

        _apiClientMock.Verify(
            client => client.SearchGlobalAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Preserves an independently written upstream dataset through the real API client and MCP HTTP response.
    /// </summary>
    [Test]
    public async Task McpEndpoint_GetDataPreservesUpstreamValuesStatusesAndMetadata()
    {
        const string upstreamJson = """
            {
              "version": "2.0", "class": "dataset",
              "id": ["observation", "metric"], "size": [5, 1],
              "label": "Synthetic fidelity test",
              "source": "Controlled test source", "updated": "2026-01-01T00:00:00Z",
              "note": ["Synthetic comparability note"],
              "dimension": {
                "observation": {
                  "label": "Observation",
                  "note": ["Synthetic classification note"],
                  "category": {
                    "index": ["zero", "missing", "suppressed", "not-applicable", "decimal"],
                    "label": {
                      "zero": "Zero", "missing": "Missing", "suppressed": "Suppressed",
                      "not-applicable": "Not applicable", "decimal": "Decimal"
                    },
                    "note": {"decimal": ["Synthetic preliminary note"]}
                  }
                },
                "metric": {
                  "label": "Metric",
                  "category": {
                    "index": ["amount"], "label": {"amount": "Amount"},
                    "unit": {"amount": {"label": "Percent", "symbol": "%", "position": "end", "decimals": 2}}
                  }
                }
              },
              "value": [0, null, null, null, 1.25],
              "status": {"1": "synthetic-missing", "2": "synthetic-suppressed", "3": "synthetic-not-applicable"},
              "role": {"metric": ["metric"]},
              "extension": {"test": {"enabled": true, "revision": 2, "extra": null}}
            }
            """;
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post &&
                    request.RequestUri!.AbsolutePath == "/px/beta/data/databases/TestDb/tables/fidelity"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(upstreamJson, Encoding.UTF8, "application/json")
            });
        using HttpClient upstreamHttpClient = new(handler.Object)
        {
            BaseAddress = new Uri("https://pxapi.test.invalid/px/beta/")
        };
        PxApiClient realApiClient = new(upstreamHttpClient, CreatePxApiOptions());
        _apiClientMock.Setup(client => client.PostDataAsync(
                "TestDb", "fidelity", null, null, It.IsAny<CancellationToken>()))
            .Returns((string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken token) =>
                realApiClient.PostDataAsync(database, table, filters, lang, token));

        using HttpResponseMessage response = await PostMcpAsync(
            """{"jsonrpc":"2.0","id":20,"method":"tools/call","params":{"name":"get_data","arguments":{"database":"TestDb","table":"fidelity"}}}""");
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(!result.TryGetProperty("isError", out JsonElement isError) || !isError.GetBoolean(), Is.True);
            Assert.That(result.TryGetProperty("structuredContent", out _), Is.True);
        }

        JsonElement structured = result.GetProperty("structuredContent");
        using JsonDocument expected = JsonDocument.Parse(upstreamJson);
        using JsonDocument text = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonElement.DeepEquals(text.RootElement, structured), Is.True);
            foreach (JsonProperty property in expected.RootElement.EnumerateObject())
            {
                Assert.That(structured.TryGetProperty(property.Name, out JsonElement actual), Is.True,
                    $"Upstream property '{property.Name}' must be preserved.");
                Assert.That(JsonElement.DeepEquals(actual, property.Value), Is.True,
                    $"Upstream property '{property.Name}' must retain its complete value.");
            }
        }
        _apiClientMock.Verify(client => client.PostDataAsync(
            "TestDb", "fidelity", null, null, It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.VerifyNoOtherCalls();
        handler.Protected().Verify(
            "SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Verifies aborting an HTTP tool request cancels upstream work, including metadata before data retrieval.
    /// </summary>
    [TestCase("data")]
    [TestCase("metadata")]
    [TestCase("head")]
    public async Task McpEndpoint_RequestAbortCancelsUpstreamWork(string stage)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (stage == "head")
        {
            _apiClientMock.Setup(client => client.GetTableMetadataAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateTableMetadata());
            _apiClientMock.Setup(client => client.GetMaxCellsAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()))
                .Returns((string _, string _, string? _, CancellationToken token) => WaitForCancellation<long>(token));
        }
        else if (stage == "metadata")
        {
            _apiClientMock.Setup(client => client.GetTableMetadataAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()))
                .Returns((string _, string _, string? _, CancellationToken token) => WaitForCancellation<JsonStat2>(token));
        }
        else
        {
            _apiClientMock.Setup(client => client.PostDataAsync(
                    "TestDb", "population", null, null, It.IsAny<CancellationToken>()))
                .Returns((string _, string _, Dictionary<string, Filter>? _, string? _, CancellationToken token) =>
                    WaitForCancellation<JsonStat2>(token));
        }
        string json = stage switch
        {
            "metadata" => """{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"get_data","arguments":{"database":"TestDb","table":"population","filters":{"year":{"type":"Code","query":["2020"]}}}}}""",
            "head" => """{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"validate_query","arguments":{"database":"TestDb","table":"population"}}}""",
            _ => """{"jsonrpc":"2.0","id":21,"method":"tools/call","params":{"name":"get_data","arguments":{"database":"TestDb","table":"population"}}}"""
        };
        using HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        Task<HttpResponseMessage> call = _client.SendAsync(request, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.That(async () =>
            {
                using HttpResponseMessage response = await call.WaitAsync(TimeSpan.FromSeconds(5));
            }, Throws.InstanceOf<OperationCanceledException>());
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (stage == "head")
            {
                _apiClientMock.Verify(client => client.GetTableMetadataAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()), Times.Once);
                _apiClientMock.Verify(client => client.GetMaxCellsAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()), Times.Once);
            }
            else if (stage == "metadata")
            {
                _apiClientMock.Verify(client => client.GetTableMetadataAsync(
                    "TestDb", "population", null, It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                _apiClientMock.Verify(client => client.PostDataAsync(
                    "TestDb", "population", null, null, It.IsAny<CancellationToken>()), Times.Once);
            }
            _apiClientMock.VerifyNoOtherCalls();
        }
        finally
        {
            cancellation.Cancel();
        }

        async Task<T> WaitForCancellation<T>(CancellationToken token)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The upstream operation must be canceled.");
            }
            finally
            {
                if (token.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                }
            }
        }
    }

    /// <summary>
    /// Verifies real API-client transport failures remain safe, explicit MCP errors over HTTP.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task McpEndpoint_TransportFailureDoesNotExposeUpstreamDiagnostics(bool post)
    {
        const string diagnostic = "Synthetic TLS failure for internal-host.test.invalid:8443";
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException(HttpRequestError.SecureConnectionError, diagnostic));
        using HttpClient upstreamHttpClient = new(handler.Object)
        {
            BaseAddress = new Uri("https://pxapi.test.invalid/px/beta/")
        };
        PxApiClient realApiClient = new(upstreamHttpClient, CreatePxApiOptions());
        if (post)
        {
            _apiClientMock.Setup(client => client.PostDataAsync(
                    "TestDb", "population", null, null, It.IsAny<CancellationToken>()))
                .Returns((string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken token) =>
                    realApiClient.PostDataAsync(database, table, filters, lang, token));
        }
        else
        {
            _apiClientMock.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
                .Returns((string? lang, CancellationToken token) => realApiClient.GetDatabasesAsync(lang, token));
        }
        string json = post
            ? """{"jsonrpc":"2.0","id":25,"method":"tools/call","params":{"name":"get_data","arguments":{"database":"TestDb","table":"population"}}}"""
            : """{"jsonrpc":"2.0","id":25,"method":"tools/call","params":{"name":"list_databases","arguments":{}}}""";

        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(result.TryGetProperty("structuredContent", out _), Is.False);
            Assert.That(GetToolErrorText(result),
                Is.EqualTo($"An error occurred invoking '{(post ? "get_data" : "list_databases")}': " +
                    "PX API request failed during transport. Try again later."));
            Assert.That(result.GetRawText(), Does.Not.Contain("internal-host"));
            Assert.That(result.GetRawText(), Does.Not.Contain("Synthetic TLS"));
        }
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Ensures dot segments and other unsafe identifiers are rejected by the real API client before any
    /// upstream request, and that the actionable tool error does not echo the rejected value.
    /// </summary>
    [TestCase("list_tables", """{"database":".."}""", "Invalid database identifier.")]
    [TestCase("search", """{"query":"population","database":""}""", "Invalid database identifier.")]
    [TestCase("search", """{"query":"   "}""", "Invalid search query.")]
    [TestCase("search", """{"query":"","database":"TestDb"}""", "Invalid search query.")]
    [TestCase("get_head_metadata", """{"database":"..","table":"population"}""", "Invalid database identifier.")]
    [TestCase("get_table_metadata", """{"database":"TestDb","table":".."}""", "Invalid table identifier.")]
    [TestCase("get_dimension_values", """{"database":"TestDb","table":"a/b","dimension":"Year"}""", "Invalid table identifier.")]
    [TestCase("get_data", """{"database":"..","table":".."}""", "Invalid database identifier.")]
    [TestCase("validate_query", """{"database":"TestDb","table":"population","lang":"en&debug=true"}""", "Invalid language code.")]
    [TestCase("list_databases", """{"lang":"../x"}""", "Invalid language code.")]
    public async Task McpEndpoint_InvalidIdentifierIsRejectedBeforeUpstreamRequest(
        string toolName, string arguments, string expectedMessagePrefix)
    {
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        using HttpClient upstreamHttpClient = new(handler.Object)
        {
            BaseAddress = new Uri("https://pxapi.test.invalid/px/beta/")
        };
        PxApiClient realApiClient = new(upstreamHttpClient, CreatePxApiOptions());
        _apiClientMock.Setup(client => client.GetDatabasesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string? lang, CancellationToken token) => realApiClient.GetDatabasesAsync(lang, token));
        _apiClientMock.Setup(client => client.GetTablesAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string database, string? lang, int page, int pageSize, CancellationToken token) =>
                realApiClient.GetTablesAsync(database, lang, page, pageSize, token));
        _apiClientMock.Setup(client => client.SearchGlobalAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string query, string? scope, string? lang, int page, int pageSize, CancellationToken token) =>
                realApiClient.SearchGlobalAsync(query, scope, lang, page, pageSize, token));
        _apiClientMock.Setup(client => client.SearchDatabaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string database, string query, string? scope, string? lang, int page, int pageSize, CancellationToken token) =>
                realApiClient.SearchDatabaseAsync(database, query, scope, lang, page, pageSize, token));
        _apiClientMock.Setup(client => client.GetTableMetadataAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string database, string table, string? lang, CancellationToken token) =>
                realApiClient.GetTableMetadataAsync(database, table, lang, token));
        _apiClientMock.Setup(client => client.PostDataAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, Filter>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken token) =>
                realApiClient.PostDataAsync(database, table, filters, lang, token));
        string json = """{"jsonrpc":"2.0","id":26,"method":"tools/call","params":{"name":""" + "\"" + toolName +
            "\",\"arguments\":" + arguments + "}}";

        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result),
                Does.StartWith($"An error occurred invoking '{toolName}': {expectedMessagePrefix}"));
            Assert.That(GetToolErrorText(result), Does.Not.Contain("a/b").And.Not.Contain("debug").And.Not.Contain("../x"));
        }
        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Ensures filter-binding diagnostics retain a stack trace without logging caller-controlled values or paths.
    /// </summary>
    [TestCase("get_data", """{"type":"Code","query":42}""")]
    [TestCase("validate_query", """{"type":"Code","query":42}""")]
    [TestCase("get_data", """{"type":"synthetic-private-input","query":[]}""")]
    [TestCase("validate_query", """{"query":[]}""")]
    public async Task McpEndpoint_BindingLogsDoNotContainCallerInput(string toolName, string filterJson)
    {
        const string marker = "synthetic-private-input";
        using JsonDocument filter = JsonDocument.Parse(filterJson);
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 26,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = new
                {
                    database = "TestDb",
                    table = "population",
                    filters = new Dictionary<string, JsonElement> { [marker] = filter.RootElement }
                }
            }
        });

        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);
        Moq.IInvocation log = _bindingLogger.Invocations.Single(invocation =>
            invocation.Method.Name == nameof(ILogger.Log) && (LogLevel)invocation.Arguments[0] == LogLevel.Warning);
        Exception exception = (Exception)log.Arguments[3];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(GetToolErrorText(result), Does.Contain("Invalid 'filters' argument"));
            Assert.That(log.Arguments[2].ToString(), Does.Not.Contain(marker));
            Assert.That(log.Arguments[2].ToString(), Does.Contain("Failure type:"));
            Assert.That(exception.ToString(), Does.Not.Contain(marker));
            Assert.That(exception.InnerException, Is.Null);
            Assert.That(exception.StackTrace, Is.Not.Null.And.Not.Empty);
        }
        _apiClientMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Keeps upstream deserialization failures distinct from argument binding errors through the real API client.
    /// </summary>
    [Test]
    public async Task McpEndpoint_UpstreamInvalidJsonIsNotArgumentBindingFailure(
        [Values("list_databases", "get_data", "get_table_metadata", "validate_query")] string toolName,
        [Values("null", "{", """{"value":"synthetic-private-upstream"}""")] string upstreamJson)
    {
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(upstreamJson, Encoding.UTF8, "application/json")
            });
        using HttpClient upstreamHttpClient = new(handler.Object)
        {
            BaseAddress = new Uri("https://pxapi.test.invalid/px/beta/")
        };
        PxApiClient realApiClient = new(upstreamHttpClient, CreatePxApiOptions());
        _apiClientMock.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .Returns((string? lang, CancellationToken token) => realApiClient.GetDatabasesAsync(lang, token));
        _apiClientMock.Setup(client => client.PostDataAsync(
                "TestDb", "population", null, null, It.IsAny<CancellationToken>()))
            .Returns((string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken token) =>
                realApiClient.PostDataAsync(database, table, filters, lang, token));
        _apiClientMock.Setup(client => client.GetTableMetadataAsync(
                "TestDb", "population", null, It.IsAny<CancellationToken>()))
            .Returns((string database, string table, string? lang, CancellationToken token) =>
                realApiClient.GetTableMetadataAsync(database, table, lang, token));
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 27,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = toolName == "list_databases"
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["database"] = "TestDb", ["table"] = "population" }
            }
        });

        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(result.GetProperty("isError").GetBoolean(), Is.True);
            Assert.That(result.TryGetProperty("structuredContent", out _), Is.False);
            Assert.That(GetToolErrorText(result), Does.Contain("PX API returned an invalid JSON response."));
            Assert.That(GetToolErrorText(result), Does.Not.Contain("Invalid tool arguments"));
            Assert.That(GetToolErrorText(result), Does.Not.Contain("Invalid 'filters'"));
            Assert.That(result.GetRawText(), Does.Not.Contain("synthetic-private-upstream"));
            Assert.That(_bindingLogger.Invocations, Is.Empty,
                "An upstream JSON error must not be logged as argument binding failure.");
            Assert.That(_apiClientMock.Invocations, Has.Count.EqualTo(1));
        }
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Exercises metadata GET plus data HEAD through the real API client, without fetching a dataset.
    /// </summary>
    [TestCase("2", false, false)]
    [TestCase("1", false, false)]
    [TestCase("3", false, false)]
    [TestCase("2", true, false)]
    [TestCase(null, false, false)]
    [TestCase("invalid", false, false)]
    [TestCase(null, false, true)]
    public async Task McpEndpoint_ValidateQuery_PreflightsSize(string? header, bool partialSelection, bool headFails)
    {
        Mock<HttpMessageHandler> handler = new(MockBehavior.Strict);
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(CreateTableMetadata()), Encoding.UTF8, "application/json")
                    };
                }
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Head));
                Assert.That(request.Content, Is.Null);
                Assert.That(request.RequestUri!.AbsolutePath, Does.Contain("/data/databases/TestDb/tables/population"));
                HttpResponseMessage head = new(headFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
                if (header is not null)
                {
                    head.Headers.TryAddWithoutValidation("X-Max-Cells", header);
                }
                return head;
            });
        using HttpClient upstream = new(handler.Object) { BaseAddress = new Uri("https://pxapi.test.invalid/px/beta/") };
        PxApiClient realClient = new(upstream, CreatePxApiOptions());
        _apiClientMock.Setup(client => client.GetTableMetadataAsync(
                "TestDb", "population", null, It.IsAny<CancellationToken>()))
            .Returns((string database, string table, string? lang, CancellationToken token) =>
                realClient.GetTableMetadataAsync(database, table, lang, token));
        _apiClientMock.Setup(client => client.GetMaxCellsAsync(
                "TestDb", "population", null, It.IsAny<CancellationToken>()))
            .Returns((string database, string table, string? lang, CancellationToken token) =>
                realClient.GetMaxCellsAsync(database, table, lang, token));
        Dictionary<string, object> filters = new()
        {
            ["year"] = new { type = "To", query = "2025" }
        };
        if (!partialSelection)
        {
            filters["region"] = new { type = "Code", query = new[] { "region-a" } };
        }
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 28, method = "tools/call",
            @params = new { name = "validate_query", arguments = new { database = "TestDb", table = "population", filters } }
        });

        using HttpResponseMessage response = await PostMcpAsync(json);
        JsonElement result = await ReadJsonRpcResultAsync(response);
        JsonElement structured = result.GetProperty("structuredContent");
        using JsonDocument text = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        bool unavailable = headFails || header is null || header == "invalid";
        bool exceeds = !partialSelection && !unavailable && header == "1";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(!result.TryGetProperty("isError", out JsonElement isError) || !isError.GetBoolean(), Is.True);
            Assert.That(JsonElement.DeepEquals(text.RootElement, structured), Is.True);
            Assert.That(structured.GetProperty("isValid").GetBoolean(), Is.EqualTo(!exceeds));
            Assert.That(structured.GetProperty("selectedCellCount").ToString(), Is.EqualTo(partialSelection ? "" : "2"));
            Assert.That(structured.GetProperty("maxCells").ToString(), Is.EqualTo(unavailable ? "" : header));
            Assert.That(structured.GetProperty("exceedsMaxCells").ValueKind,
                Is.EqualTo(partialSelection || unavailable ? JsonValueKind.Null : exceeds ? JsonValueKind.True : JsonValueKind.False));
            Assert.That(structured.GetProperty("issues").GetArrayLength(),
                Is.EqualTo(unavailable || partialSelection || exceeds ? 1 : 0));
        }
        _apiClientMock.Verify(client => client.GetMaxCellsAsync(
            "TestDb", "population", null, It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.Verify(client => client.GetTableMetadataAsync(
            "TestDb", "population", null, It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.VerifyNoOtherCalls();
        handler.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private async Task<HttpResponseMessage> PostMcpAsync(string json)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await _client.SendAsync(request);
    }

    private static PxApiOptions CreatePxApiOptions() => new() { BaseUrl = "https://pxapi.test.invalid/px/beta/" };

    private static WebApplicationBuilder CreateBuilder(string? pxApiBaseUrl)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();

        Dictionary<string, string?> configuration = [];
        if (pxApiBaseUrl is not null)
        {
            configuration["PxApi:BaseUrl"] = pxApiBaseUrl;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }

    private static string ReadSseData(string body)
    {
        string? dataLine = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("data: ", StringComparison.Ordinal));

        Assert.That(dataLine, Is.Not.Null, "MCP response should contain an SSE data event.");
        return dataLine!["data: ".Length..];
    }

    private static async Task<JsonElement> ReadJsonRpcResultAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument payload = JsonDocument.Parse(ReadSseData(body));
        return payload.RootElement.GetProperty("result").Clone();
    }

    private static string GetToolErrorText(JsonElement result)
    {
        Assert.That(result.TryGetProperty("content", out JsonElement content), Is.True);
        Assert.That(content.GetArrayLength(), Is.GreaterThan(0));
        return content[0].GetProperty("text").GetString()!;
    }

    private static JsonStat2 CreateTableMetadata()
    {
        return new JsonStat2
        {
            Version = "2.0",
            Class = "dataset",
            Id = ["year", "region"],
            Label = "Population test metadata",
            Source = "Test source",
            Updated = "2026-01-01T00:00:00Z",
            Dimension = new Dictionary<string, Dimension>
            {
                ["year"] = new()
                {
                    Label = "Year",
                    Category = new Category
                    {
                        Index = ["2020", "2025"],
                        Label = new Dictionary<string, string>
                        {
                            ["2020"] = "2020",
                            ["2025"] = "2025"
                        }
                    }
                },
                ["region"] = new()
                {
                    Label = "Region",
                    Category = new Category
                    {
                        Index = ["region-a"],
                        Label = new Dictionary<string, string> { ["region-a"] = "Region A" }
                    }
                }
            },
            Value = [],
            Size = [2, 1]
        };
    }

    private static JsonStat2 CreateDataResult()
    {
        return new JsonStat2
        {
            Version = "2.0",
            Class = "dataset",
            Id = ["year", "region"],
            Label = "Population test",
            Source = "Test source",
            Updated = "2026-01-01T00:00:00Z",
            Dimension = CreateTableMetadata().Dimension,
            Value = [123.0],
            Status = new Dictionary<string, string> { ["0"] = "p" },
            Size = [1, 1]
        };
    }
}
