using System.Net;
using System.Text.Json;
using Moq;
using Moq.Protected;
using PxMCP.Configuration;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PxApiClient"/>.
/// </summary>
[TestFixture]
public class PxApiClientTests
{
    private const string TableSummaryJson = """
        {
          "tableId":"synthetic-table",
          "title":"Synthetic table",
          "metrics":[{"name":"Population","unit":"persons"}],
          "timeRange":{"from":"2020","to":"2025"},
          "dimensions":[{"name":"Region","size":2}],
          "lastUpdated":"2026-01-02T03:04:05Z"
        }
        """;

    private Mock<HttpMessageHandler> _handlerMock = null!;
    private PxApiClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        HttpClient httpClient = new(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://test-api.example.com")
        };
        _client = new PxApiClient(httpClient, CreateOptions());
    }

    private static PxApiOptions CreateOptions(double timeoutSeconds = 3, double searchTimeoutSeconds = 15) => new()
    {
        BaseUrl = "https://test-api.example.com",
        TimeoutSeconds = timeoutSeconds,
        SearchTimeoutSeconds = searchTimeoutSeconds
    };

    private static Task InvokeClientMethod(PxApiClient client, string method, CancellationToken cancellationToken) => method switch
    {
        "GetDatabases" => client.GetDatabasesAsync(null, cancellationToken),
        "GetTables" => client.GetTablesAsync("StatFin", null, 1, 10, cancellationToken),
        "SearchGlobal" => client.SearchGlobalAsync("population", null, null, 1, 10, cancellationToken),
        "SearchDatabase" => client.SearchDatabaseAsync("StatFin", "population", null, null, 1, 10, cancellationToken),
        "GetTableMetadata" => client.GetTableMetadataAsync("StatFin", "table1", null, cancellationToken),
        "GetMaxCells" => client.GetMaxCellsAsync("StatFin", "table1", null, cancellationToken),
        "PostData" => client.PostDataAsync("StatFin", "table1", null, null, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown client method.")
    };

    private void SetupResponse(HttpStatusCode statusCode, string content, Action<HttpRequestMessage>? requestCallback = null,
        string[]? maxCellsHeaders = null)
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                requestCallback?.Invoke(req);
                HttpResponseMessage response = new(statusCode)
                {
                    Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                };
                if (maxCellsHeaders is not null)
                {
                    response.Headers.TryAddWithoutValidation("X-Max-Cells", maxCellsHeaders);
                }
                return response;
            });
    }

    /// <summary>
    /// HEAD reads a dynamic positive limit without sending filters or fetching dataset content.
    /// </summary>
    [TestCase("1", 1L)]
    [TestCase("50000", 50000L)]
    [TestCase(" 50000 ", 50000L)]
    [TestCase("9223372036854775807", long.MaxValue)]
    public async Task GetMaxCellsAsync_ReadsHeadHeader(string header, long expected)
    {
        SetupResponse(HttpStatusCode.OK, "not-json", request =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Head));
                Assert.That(request.Content, Is.Null);
                Assert.That(request.RequestUri!.AbsolutePath, Does.Contain("/data/databases/StatFin/tables/table1"));
                Assert.That(request.RequestUri.Query, Is.EqualTo("?lang=en"));
            }
        }, [header]);

        long limit = await _client.GetMaxCellsAsync("StatFin", "table1", "en");

        Assert.That(limit, Is.EqualTo(expected));
    }

    /// <summary>
    /// Missing, ambiguous and malformed headers are explicit upstream errors rather than invented limits.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("+50000")]
    [TestCase("1.5")]
    [TestCase("9223372036854775808")]
    [TestCase("50000,60000")]
    [TestCase("synthetic-private-input")]
    public void GetMaxCellsAsync_InvalidHeader_ThrowsSafeError(string? header)
    {
        SetupResponse(HttpStatusCode.OK, "", maxCellsHeaders: header is null ? null : [header]);

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetMaxCellsAsync("StatFin", "table1", null))!;

        Assert.That(exception.Message, Is.EqualTo("PX API size limit is unavailable: X-Max-Cells is missing or invalid."));
    }

    /// <summary>
    /// Conflicting repeated header values must not produce an arbitrary maximum.
    /// </summary>
    [Test]
    public void GetMaxCellsAsync_DuplicateHeaders_Throws()
    {
        SetupResponse(HttpStatusCode.OK, "", maxCellsHeaders: ["50000", "60000"]);
        Assert.ThrowsAsync<HttpRequestException>(() => _client.GetMaxCellsAsync("StatFin", "table1", null));
    }

    /// <summary>
    /// HEAD status failures remain explicit and do not expose response bodies.
    /// </summary>
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public void GetMaxCellsAsync_StatusError_Throws(HttpStatusCode status)
    {
        SetupResponse(status, "synthetic-private-input", maxCellsHeaders: ["50000"]);
        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetMaxCellsAsync("StatFin", "table1", null))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(exception.Message, Does.Not.Contain("synthetic-private-input"));
        }
    }

    /// <summary>
    /// Data errors include the dynamic header limit only when it is valid, without adding a HEAD request.
    /// </summary>
    [TestCase("12345", true)]
    [TestCase("invalid", false)]
    [TestCase(null, false)]
    public void PostDataAsync_TooLarge_ReportsValidHeaderLimit(string? header, bool hasLimit)
    {
        SetupResponse(HttpStatusCode.RequestEntityTooLarge, "synthetic-private-input", request =>
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post)),
            header is null ? null : [header]);

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.PostDataAsync("StatFin", "table1", null, null))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
            Assert.That(exception.Message.Contains("Maximum allowed cells: 12345."), Is.EqualTo(hasLimit));
            Assert.That(exception.Message, Does.Contain("Narrow the selection"));
            Assert.That(exception.Message, Does.Not.Contain("synthetic-private-input"));
        }
        _handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    #region GetDatabasesAsync

    /// <summary>
    /// Keeps transport diagnostics in the exception chain rather than in the tool-facing message.
    /// </summary>
    [Test]
    public async Task HttpTransportFailure_ReturnsSafeError(
        [Values(false, true)] bool post,
        [Values(HttpRequestError.NameResolutionError, HttpRequestError.ConnectionError,
            HttpRequestError.SecureConnectionError, HttpRequestError.ResponseEnded)] HttpRequestError error)
    {
        const string diagnostic = "Synthetic transport failure at internal-host.test.invalid:8443/request-detail";
        HttpRequestException upstream = new(error, diagnostic);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(upstream);

        Task request = post
            ? _client.PostDataAsync("StatFin", "table1", null, null)
            : _client.GetDatabasesAsync(null);

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(async () => await request)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("PX API request failed during transport. Try again later."));
            Assert.That(exception.Message, Does.Not.Contain("internal-host"));
            Assert.That(exception.InnerException, Is.SameAs(upstream));
            Assert.That(exception.HttpRequestError, Is.EqualTo(error));
            Assert.That(exception.StatusCode, Is.Null);
        }
    }

    /// <summary>
    /// Distinguishes an actual HTTP client timeout from caller cancellation for GET, POST and HEAD.
    /// </summary>
    [TestCase("GET")]
    [TestCase("POST")]
    [TestCase("HEAD")]
    public async Task HttpTimeout_ReturnsExplicitUpstreamError(string method)
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The timeout must cancel the handler.");
            });
        using HttpClient httpClient = new(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://test-api.example.com"),
            Timeout = TimeSpan.FromMilliseconds(100)
        };
        PxApiClient client = new(httpClient, CreateOptions());

        Task request = method switch
        {
            "POST" => client.PostDataAsync("StatFin", "table1", null, null),
            "HEAD" => client.GetMaxCellsAsync("StatFin", "table1", null),
            _ => client.GetDatabasesAsync(null)
        };

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await request.WaitAsync(TimeSpan.FromSeconds(5)))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("PX API request timed out. Try again later or narrow the query."));
            Assert.That(exception.InnerException, Is.InstanceOf<OperationCanceledException>());
        }
    }

    /// <summary>
    /// Every client method uses its expected timeout: search methods use <see cref="PxApiOptions.SearchTimeoutSeconds"/>
    /// and all others use <see cref="PxApiOptions.TimeoutSeconds"/>. Each case checks both directions: the method times
    /// out when only its own timeout is short, and is not canceled when only the other timeout is short.
    /// </summary>
    [TestCase("GetDatabases", false)]
    [TestCase("GetTables", false)]
    [TestCase("SearchGlobal", true)]
    [TestCase("SearchDatabase", true)]
    [TestCase("GetTableMetadata", false)]
    [TestCase("GetMaxCells", false)]
    [TestCase("PostData", false)]
    public async Task RequestTimeout_UsesExpectedTimeoutForEachMethod(string method, bool usesSearchTimeout)
    {
        TaskCompletionSource<CancellationToken> requestToken = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                requestToken.TrySetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The request must be canceled.");
            });
        using HttpClient httpClient = new(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://test-api.example.com"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        PxApiOptions ownTimeoutShort = usesSearchTimeout
            ? CreateOptions(timeoutSeconds: 60, searchTimeoutSeconds: 0.1)
            : CreateOptions(timeoutSeconds: 0.1, searchTimeoutSeconds: 60);
        PxApiOptions otherTimeoutShort = usesSearchTimeout
            ? CreateOptions(timeoutSeconds: 0.1, searchTimeoutSeconds: 60)
            : CreateOptions(timeoutSeconds: 60, searchTimeoutSeconds: 0.1);

        Task timedOutRequest = InvokeClientMethod(new PxApiClient(httpClient, ownTimeoutShort), method, CancellationToken.None);
        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            async () => await timedOutRequest.WaitAsync(TimeSpan.FromSeconds(5)))!;

        requestToken = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource callerSource = new();
        Task pendingRequest = InvokeClientMethod(new PxApiClient(httpClient, otherTimeoutShort), method, callerSource.Token);
        CancellationToken pendingToken = await requestToken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("PX API request timed out. Try again later or narrow the query."));
            Assert.That(pendingToken.IsCancellationRequested, Is.False);
        }

        await callerSource.CancelAsync();
        Assert.That(async () => await pendingRequest.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>
    /// Caller cancellation that races with the request timeout remains cancellation, not a timeout error.
    /// </summary>
    [Test]
    public void RequestTimeout_PreCanceledCallerTokenRemainsCancellation()
    {
        SetupResponse(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(_handlerMock.Object) { BaseAddress = new Uri("https://test-api.example.com") };
        PxApiClient client = new(httpClient, CreateOptions(timeoutSeconds: 0.001));

        Assert.That(async () => await client.GetDatabasesAsync(null, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>
    /// A response body larger than the configured buffer limit returns a caller-safe size error instead of
    /// the generic transport error.
    /// </summary>
    [TestCase("GET")]
    [TestCase("POST")]
    public void OversizedResponse_ReturnsExplicitSizeError(string method)
    {
        SetupResponse(HttpStatusCode.OK, new string(' ', 1024) + "[]");
        using HttpClient httpClient = new(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://test-api.example.com"),
            MaxResponseContentBufferSize = 256
        };
        PxApiClient client = new(httpClient, CreateOptions());

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(() => method == "POST"
            ? client.PostDataAsync("StatFin", "table1", null, null)
            : client.GetDatabasesAsync(null))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message,
                Is.EqualTo("PX API response exceeded the maximum supported size. Narrow the query or apply filters."));
            Assert.That(exception.HttpRequestError, Is.EqualTo(HttpRequestError.ConfigurationLimitExceeded));
        }
    }

    [Test]
    public async Task GetDatabasesAsync_WithLang_IncludesLangInUrl()
    {
        string? capturedUrl = null;
        string json = JsonSerializer.Serialize(new List<DatabaseListingItem>
        {
            new()
            {
                Id = "StatFin",
                Name = "StatFin",
                TableCount = 0,
                AvailableLanguages = ["fi", "en"],
                Links = [new Link { Href = "/tables", Rel = "describedby", Method = "GET" }]
            }
        });

        SetupResponse(HttpStatusCode.OK, json, req => capturedUrl = req.RequestUri?.ToString());

        List<DatabaseListingItem> result = await _client.GetDatabasesAsync("en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo("StatFin"));
            Assert.That(capturedUrl, Does.Contain("lang=en"));
        }
    }

    [Test]
    public async Task GetDatabasesAsync_WithoutLang_NoLangParam()
    {
        string? capturedUrl = null;
        SetupResponse(HttpStatusCode.OK, "[]", req => capturedUrl = req.RequestUri?.ToString());

        await _client.GetDatabasesAsync(null);

        Assert.That(capturedUrl, Does.Not.Contain("lang="));
    }

    [TestCase("GET")]
    [TestCase("POST")]
    [TestCase("HEAD")]
    public async Task HttpRequest_PropagatesCallerCancellationToHttpHandler(string method)
    {
        TaskCompletionSource<CancellationToken> tokenReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellationSource = new();

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken token) =>
            {
                tokenReceived.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
                };
            });

        Task request = method switch
        {
            "POST" => _client.PostDataAsync("StatFin", "table1", null, null, cancellationSource.Token),
            "HEAD" => _client.GetMaxCellsAsync("StatFin", "table1", null, cancellationSource.Token),
            _ => _client.GetDatabasesAsync(null, cancellationSource.Token)
        };
        CancellationToken httpToken = await tokenReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(httpToken.CanBeCanceled, Is.True);
        cancellationSource.Cancel();

        Assert.That(async () => await request.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void GetDatabasesAsync_OnError_ThrowsWithStatusCode()
    {
        SetupResponse(HttpStatusCode.BadRequest, "\"Unsupported language\"");

        HttpRequestException? ex = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetDatabasesAsync("xx"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain("400"));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ex.Message, Does.Not.Contain("Unsupported language"),
                "Response body must not be included in the exception message.");
        }
    }

    [TestCase("null")]
    [TestCase("{")]
    public void GetDatabasesAsync_WithInvalidJson_ThrowsWithoutReturningNull(string content)
    {
        SetupResponse(HttpStatusCode.OK, content);

        HttpRequestException? exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetDatabasesAsync(null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("PX API returned an invalid JSON response."));
            Assert.That(exception.Message, Does.Not.Contain(content));
            Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
        }
    }

    /// <summary>
    /// Rejects partial responses even when their JSON can be deserialized.
    /// </summary>
    [Test]
    public void GetDatabasesAsync_PartialContent_ThrowsInsteadOfReturningIncompleteList()
    {
        SetupResponse(HttpStatusCode.PartialContent, "[]");

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetDatabasesAsync(null))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain("incomplete"));
        }
    }

    #endregion

    #region GetTablesAsync

    /// <summary>
    /// Checks nonempty nested listing contracts using literal upstream JSON, not DTO-generated fixtures.
    /// </summary>
    [Test]
    public async Task GetTablesAsync_NonemptyListing_PreservesNestedSummary()
    {
        string json = $$"""
            {"tables":[{"table":{{TableSummaryJson}},"links":[{"href":"/metadata","rel":"describedby","method":"GET"}]}],
             "pagingInfo":{"currentPage":2,"pageSize":10,"totalItems":12}
            }
            """;
        SetupResponse(HttpStatusCode.OK, json);

        PagedTableList result = await _client.GetTablesAsync("StatFin", "en", 2, 10);
        JsonElement actual = JsonSerializer.SerializeToElement(result);
        using JsonDocument expected = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Tables, Has.Count.EqualTo(1));
            Assert.That(result.Tables[0].Table.LastUpdated.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(JsonElement.DeepEquals(actual, expected.RootElement), Is.True,
                "Every supplied nested field must survive deserialization and reserialization.");
        }
    }

    [Test]
    public async Task GetTablesAsync_BuildsCorrectUrl()
    {
        string? capturedUrl = null;
        string json = JsonSerializer.Serialize(new PagedTableList
        {
            Tables = [],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 0 }
        });

        SetupResponse(HttpStatusCode.OK, json, req => capturedUrl = req.RequestUri?.ToString());

        await _client.GetTablesAsync("StatFin", "en", 2, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedUrl, Does.Contain("/meta/databases/StatFin/tables"));
            Assert.That(capturedUrl, Does.Contain("lang=en"));
            Assert.That(capturedUrl, Does.Contain("page=2"));
            Assert.That(capturedUrl, Does.Contain("pageSize=10"));
        }
    }

    #endregion

    #region SearchGlobalAsync

    /// <summary>
    /// Required nullable match fields must be present even though their values may be null.
    /// </summary>
    [TestCase("""{"matchType":"Exact","matchedText":null}""")]
    [TestCase("""{"path":null,"matchType":"Exact"}""")]
    public void Search_MissingRequiredNullableMatchField_ReturnsUpstreamError(string matchJson)
    {
        string json = $$"""
            {
              "query":{"q":"synthetic","target":"All","lang":"en"},
              "results":[{
                "database":{"id":"StatFin","name":"StatFin"},
                "table":{{TableSummaryJson}},
                "matches":[{{matchJson}}]
              }],
              "pagingInfo":{"currentPage":1,"pageSize":20,"totalItems":1}
            }
            """;
        SetupResponse(HttpStatusCode.OK, json);

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.SearchGlobalAsync("synthetic", null, "en", 1, 20))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo("PX API returned an invalid JSON response."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
        }
    }

    /// <summary>
    /// Checks every documented target, both search routes, nullable match text and optional result fields.
    /// </summary>
    [Test]
    public async Task Search_NonemptyResult_PreservesContract(
        [Values(false, true)] bool databaseScoped,
        [Values("Content", "Dimension", "Value", "Geo", "All")] string target)
    {
        string json = $$"""
            {
              "query":{"q":"synthetic","target":"{{target}}","lang":"en"},
              "results":[{
                "score":1.25,
                "database":{"id":"StatFin","name":"StatFin"},
                "table":{{TableSummaryJson}},
                "matches":[
                  {"path":"table.title","matchType":"Contains","matchedText":"Synthetic"},
                  {"path":null,"matchType":"Exact","matchedText":null}
                ],
                "links":[{"href":"/data","rel":"data","method":"POST"}]
              }],
              "pagingInfo":{"currentPage":1,"pageSize":20,"totalItems":1}
            }
            """;
        SetupResponse(HttpStatusCode.OK, json);

        PxApiSearchResponse result = databaseScoped
            ? await _client.SearchDatabaseAsync("StatFin", "synthetic", null, "en", 1, 20)
            : await _client.SearchGlobalAsync("synthetic", null, "en", 1, 20);
        using JsonDocument expected = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Query.Target.ToString(), Is.EqualTo(target));
            Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result), expected.RootElement), Is.True);
        }
    }

    /// <summary>
    /// Accepts both omitted and explicitly null optional search-result properties.
    /// </summary>
    [TestCase("")]
    [TestCase("\"score\":null,\"matches\":null,\"links\":null,")]
    public async Task Search_OptionalResultProperties_CanBeOmittedOrNull(string optionalProperties)
    {
        string json = $$"""
            {
              "query":{"q":"synthetic","target":"All","lang":"en"},
              "results":[{ {{optionalProperties}}
                "database":{"id":"StatFin","name":"StatFin"},
                "table":{{TableSummaryJson}}
              }],
              "pagingInfo":{"currentPage":1,"pageSize":20,"totalItems":1}
            }
            """;
        SetupResponse(HttpStatusCode.OK, json);

        PxApiSearchResponse result = await _client.SearchGlobalAsync("synthetic", null, "en", 1, 20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Results[0].Score, Is.Null);
            Assert.That(result.Results[0].Matches, Is.Null);
            Assert.That(result.Results[0].Links, Is.Null);
        }
    }

    [Test]
    public async Task SearchGlobalAsync_BuildsCorrectUrl()
    {
        string? capturedUrl = null;
        string json = JsonSerializer.Serialize(new PxApiSearchResponse
        {
            Query = new SearchQueryInfo { Q = "population", Target = SearchTarget.Content, Lang = "en" },
            Results = [],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 10, TotalItems = 0 }
        });

        SetupResponse(HttpStatusCode.OK, json, req => capturedUrl = req.RequestUri?.ToString());

        await _client.SearchGlobalAsync("population", "Table", "en", 1, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedUrl, Does.Contain("/meta/search"));
            Assert.That(capturedUrl, Does.Contain("q=population"));
            Assert.That(capturedUrl, Does.Contain("scope=Table"));
        }
    }

    [Test]
    public async Task SearchGlobalAsync_EscapesQueryParameters()
    {
        string? capturedUrl = null;
        SetupResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new PxApiSearchResponse
        {
            Query = new SearchQueryInfo { Q = "population & income", Target = SearchTarget.Content, Lang = "en" },
            Results = [],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 10, TotalItems = 0 }
        }), request => capturedUrl = request.RequestUri?.OriginalString);

        await _client.SearchGlobalAsync("population & income", "dimension&all", "en", 1, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedUrl, Does.Contain("q=population%20%26%20income"));
            Assert.That(capturedUrl, Does.Contain("scope=dimension%26all"));
            Assert.That(capturedUrl, Does.Contain("lang=en&"));
        }
    }

    /// <summary>
    /// Every request carries the client's configured HTTP version and policy. <c>SendAsync</c> does not apply
    /// <see cref="HttpClient.DefaultRequestVersion"/> to caller-constructed messages, so without explicit copying every
    /// request would silently use HTTP/1.1. The second case rejects a hard-coded HTTP/2 version.
    /// </summary>
    [TestCase("2.0", HttpVersionPolicy.RequestVersionOrLower)]
    [TestCase("1.1", HttpVersionPolicy.RequestVersionExact)]
    public void AllRequestMethods_SendConfiguredHttpVersionAndPolicy(string version, HttpVersionPolicy policy)
    {
        List<(HttpMethod Method, Version Version, HttpVersionPolicy Policy)> sent = [];
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                sent.Add((request.Method, request.Version, request.VersionPolicy));
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
        using HttpClient httpClient = new(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://test-api.example.com"),
            DefaultRequestVersion = Version.Parse(version),
            DefaultVersionPolicy = policy
        };
        PxApiClient client = new(httpClient, CreateOptions());
        string[] methods = ["GetDatabases", "GetTables", "SearchGlobal", "SearchDatabase", "GetTableMetadata", "GetMaxCells", "PostData"];

        foreach (string method in methods)
        {
            Assert.ThrowsAsync<HttpRequestException>(() => InvokeClientMethod(client, method, CancellationToken.None));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Has.Count.EqualTo(methods.Length));
            Assert.That(sent.Select(request => request.Method.Method).Distinct(), Is.EquivalentTo(new[] { "GET", "HEAD", "POST" }));
            Assert.That(sent.Select(request => request.Version), Is.All.EqualTo(Version.Parse(version)));
            Assert.That(sent.Select(request => request.Policy), Is.All.EqualTo(policy));
        }
    }

    /// <summary>
    /// Every method that builds a PX API URL rejects unsafe identifiers before sending any request.
    /// </summary>
    [Test]
    public void AllRequestMethods_RejectInvalidArgumentsWithoutSendingRequest()
    {
        const string valid = "StatFin";
        const string table = "table1";
        Func<Task>[] invalidCalls =
        [
            () => _client.GetDatabasesAsync("en&debug=true"),
            () => _client.GetTablesAsync("..", null, 1, 10),
            () => _client.GetTablesAsync(valid, " ", 1, 10),
            () => _client.SearchGlobalAsync("population", null, "../x", 1, 10),
            () => _client.SearchGlobalAsync(" ", null, null, 1, 10),
            () => _client.SearchGlobalAsync(new string('a', 401), null, null, 1, 10),
            () => _client.SearchDatabaseAsync("", "population", null, null, 1, 10),
            () => _client.SearchDatabaseAsync(valid, new string('a', 401), null, null, 1, 10),
            () => _client.GetTableMetadataAsync("..", table, null),
            () => _client.GetTableMetadataAsync(valid, "..", null),
            () => _client.GetMaxCellsAsync(valid, "a/b", null),
            () => _client.PostDataAsync(".", table, null, null),
            () => _client.PostDataAsync(valid, table, null, "en-")
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach (Func<Task> call in invalidCalls)
            {
                Assert.That(call, Throws.TypeOf<PxApiArgumentException>());
            }
        }

        _handlerMock.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region SearchDatabaseAsync

    [Test]
    public async Task SearchDatabaseAsync_BuildsCorrectUrl()
    {
        string? capturedUrl = null;
        string json = JsonSerializer.Serialize(new PxApiSearchResponse
        {
            Query = new SearchQueryInfo { Q = "population", Target = SearchTarget.Content, Lang = "fi" },
            Results = [],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 10, TotalItems = 0 }
        });

        SetupResponse(HttpStatusCode.OK, json, req => capturedUrl = req.RequestUri?.ToString());

        await _client.SearchDatabaseAsync("StatFin", "population", null, null, 1, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedUrl, Does.Contain("/meta/databases/StatFin/search"));
            Assert.That(capturedUrl, Does.Contain("q=population"));
        }
    }

    #endregion

    #region GetTableMetadataAsync

    [Test]
    public async Task GetTableMetadataAsync_ReturnsDeserializedMetadata()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        string json = JsonSerializer.Serialize(metadata);

        SetupResponse(HttpStatusCode.OK, json);

        JsonStat2 result = await _client.GetTableMetadataAsync("StatFin", "table1", "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Label, Is.EqualTo("Test Table"));
            Assert.That(result.Id, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void GetTableMetadataAsync_NotFound_Throws()
    {
        SetupResponse(HttpStatusCode.NotFound, "\"Table not found\"");

        HttpRequestException? ex = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.GetTableMetadataAsync("StatFin", "nonexistent", null));

        Assert.That(ex!.Message, Does.Contain("404"));
    }

    #endregion

    #region PostDataAsync

    /// <summary>
    /// Compares the complete outgoing selection with independently written filter JSON.
    /// </summary>
    [TestCase("Code", """{"year":{"query":["2020","202*"],"type":"Code"}}""")]
    [TestCase("From", """{"year":{"query":"2020","type":"From"}}""")]
    [TestCase("To", """{"year":{"query":"2022","type":"To"}}""")]
    [TestCase("First", """{"year":{"query":2,"type":"First"}}""")]
    [TestCase("Last", """{"year":{"query":2,"type":"Last"}}""")]
    public async Task PostDataAsync_AllFilterTypes_SendExactSelection(string filterType, string expectedJson)
    {
        string? capturedBody = null;
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken token) =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(CreateSampleMetadata()),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });
        Filter filter = filterType switch
        {
            "Code" => new CodeFilter { Query = ["2020", "202*"] },
            "From" => new FromFilter { Query = "2020" },
            "To" => new ToFilter { Query = "2022" },
            "First" => new FirstFilter { Query = 2 },
            "Last" => new LastFilter { Query = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(filterType))
        };
        Dictionary<string, Filter> filters = new() { ["year"] = filter };

        await _client.PostDataAsync("StatFin", "table1", filters, null);

        Assert.That(capturedBody, Is.Not.Null);
        using JsonDocument actual = JsonDocument.Parse(capturedBody!);
        using JsonDocument expected = JsonDocument.Parse(expectedJson);
        Assert.That(JsonElement.DeepEquals(actual.RootElement, expected.RootElement), Is.True,
            "Selection must retain exact filter types, query shapes and values regardless of property order.");
    }

    [Test]
    public async Task PostDataAsync_SendsPostWithJsonBody()
    {
        HttpMethod? capturedMethod = null;
        string? capturedBody = null;

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedMethod = req.Method;
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                JsonStat2 metadata = CreateSampleMetadata();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(metadata), System.Text.Encoding.UTF8, "application/json")
                };
            });

        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new FromFilter { Query = "2020" }
        };

        await _client.PostDataAsync("StatFin", "table1", filters, "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(capturedBody, Does.Contain("From"));
            Assert.That(capturedBody, Does.Contain("2020"));
        }
    }

    [Test]
    public async Task PostDataAsync_WithNullFilters_SendsEmptyObject()
    {
        string? capturedBody = null;

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().Result;
                JsonStat2 metadata = CreateSampleMetadata();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(metadata), System.Text.Encoding.UTF8, "application/json")
                };
            });

        await _client.PostDataAsync("StatFin", "table1", null, null);

        Assert.That(capturedBody, Is.EqualTo("{}"));
    }

    [Test]
    public async Task PostDataAsync_SetsJsonAcceptHeader()
    {
        string? acceptHeader = null;

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                acceptHeader = req.Headers.Accept.ToString();
                JsonStat2 metadata = CreateSampleMetadata();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(metadata), System.Text.Encoding.UTF8, "application/json")
                };
            });

        await _client.PostDataAsync("StatFin", "table1", null, null);

        Assert.That(acceptHeader, Does.Contain("application/json"));
    }

    [Test]
    public void PostDataAsync_413_ThrowsWithMessage()
    {
        SetupResponse(HttpStatusCode.RequestEntityTooLarge, "\"Request exceeds maximum allowed cell count\"");

        HttpRequestException? ex = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.PostDataAsync("StatFin", "table1", null, null));

        Assert.That(ex!.Message, Does.Contain("413"));
        Assert.That(ex.Message, Does.Contain("maximum allowed cell count"));
        Assert.That(ex.Message, Does.Contain("Narrow the selection or apply filters"));
        Assert.That(ex.Message, Does.Not.Contain("Request exceeds maximum allowed cell count"));
    }

    [Test]
    public void PostDataAsync_WithNullJson_ThrowsInsteadOfReturningNull()
    {
        SetupResponse(HttpStatusCode.OK, "null");

        HttpRequestException? exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.PostDataAsync("StatFin", "table1", null, null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("PX API returned an invalid JSON response."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
        }
    }

    /// <summary>
    /// Rejects partial datasets rather than presenting them as complete statistical data.
    /// </summary>
    [Test]
    public void PostDataAsync_PartialContent_ThrowsInsteadOfReturningIncompleteDataset()
    {
        SetupResponse(HttpStatusCode.PartialContent, JsonSerializer.Serialize(CreateSampleMetadata()));

        HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(
            () => _client.PostDataAsync("StatFin", "table1", null, null))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain("incomplete"));
        }
    }

    #endregion

    #region Helpers

    private static JsonStat2 CreateSampleMetadata()
    {
        return new JsonStat2
        {
            Version = "2.0",
            Class = "dataset",
            Id = ["year", "tiedot"],
            Label = "Test Table",
            Source = "Test Source",
            Updated = "2024-01-01T00:00:00Z",
            Dimension = new Dictionary<string, Dimension>
            {
                ["year"] = new()
                {
                    Label = "Year",
                    Category = new Category
                    {
                        Index = ["2020", "2021", "2022"],
                        Label = new Dictionary<string, string>
                        {
                            ["2020"] = "2020", ["2021"] = "2021", ["2022"] = "2022"
                        }
                    }
                },
                ["tiedot"] = new()
                {
                    Label = "Information",
                    Category = new Category
                    {
                        Index = ["vaesto"],
                        Label = new Dictionary<string, string>
                        {
                            ["vaesto"] = "Population"
                        },
                        Unit = new Dictionary<string, Unit>
                        {
                            ["vaesto"] = new() { Label = "Persons", Decimals = 0 }
                        }
                    }
                }
            },
            Value = [100.0, 200.0, 300.0],
            Size = [3, 1],
            Role = new Dictionary<string, List<string>>
            {
                ["time"] = ["year"],
                ["metric"] = ["tiedot"]
            }
        };
    }

    #endregion
}
