using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.McpTests;

/// <summary>
/// Tests that validate the search tool's output documentation and response structure.
/// Ensures agents consuming these tools see correct schemas and receive well-structured responses.
/// </summary>
[TestFixture]
public class SearchToolOutputTests : McpTestFixture
{
    #region Tool Description Quality

    [Test]
    public async Task Search_Description_ExplainsScopeOptions()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(t => t.Name == "search");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(search.Description, Does.Contain("global").IgnoreCase,
                "Description should mention global search when database is omitted.");
            Assert.That(search.Description, Does.Contain("database").IgnoreCase,
                "Description should mention database-scoped search.");
        }
    }

    [Test]
    public async Task Search_Description_MentionsTableSummaries()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(t => t.Name == "search");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(search.Description, Does.Contain("summaries").IgnoreCase.Or.Contain("dimension").IgnoreCase,
                "Description should mention table summaries or dimensions.");
            Assert.That(search.Description, Does.Contain("relevance").IgnoreCase,
                "Description should mention agents can judge relevance from results.");
        }
    }

    #endregion

    #region Input Schema Documentation

    [Test]
    public async Task Search_ScopeParameter_DescribesAllowedValues()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(t => t.Name == "search");
        JsonElement schema = search.JsonSchema;

        JsonElement scopeParam = schema.GetProperty("properties").GetProperty("scope");
        string description = scopeParam.GetProperty("description").GetString()!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description, Does.Contain("dimension").IgnoreCase);
            Assert.That(description, Does.Contain("value").IgnoreCase);
            Assert.That(description, Does.Contain("geo").IgnoreCase);
            Assert.That(description, Does.Contain("all").IgnoreCase);
        }
    }

    [Test]
    public async Task Search_PageAndPageSize_HaveDefaultsInDescription()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(t => t.Name == "search");
        JsonElement props = search.JsonSchema.GetProperty("properties");

        string pageDesc = props.GetProperty("page").GetProperty("description").GetString()!;
        string pageSizeDesc = props.GetProperty("pageSize").GetProperty("description").GetString()!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pageDesc, Does.Contain("1"),
                "Page parameter description should mention default value.");
            Assert.That(pageSizeDesc, Does.Contain("20"),
                "PageSize parameter description should mention default value.");
        }
    }

    #endregion

    #region Output Schema Structure

    [Test]
    public async Task Search_Tool_UsesStructuredContent()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        Assert.That(result.StructuredContent, Is.Not.Null,
            "Search tool must use structured content so agents receive a typed JSON response.");
    }

    [Test]
    public async Task Search_Response_ContainsResultsAndPaging()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement content = result.StructuredContent!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.TryGetProperty("results", out _), Is.True,
                "Response must contain 'results' array.");
            Assert.That(content.TryGetProperty("pagingInfo", out _), Is.True,
                "Response must contain 'pagingInfo' for pagination.");
        }
    }

    [Test]
    public async Task Search_Response_DoesNotContainQueryEcho()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement content = result.StructuredContent!.Value;

        Assert.That(content.TryGetProperty("query", out _), Is.False,
            "Response should not echo the query back to reduce token usage.");
    }

    [Test]
    public async Task Search_PagingInfo_ContainsPageMetadata()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement paging = result.StructuredContent!.Value.GetProperty("pagingInfo");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paging.TryGetProperty("currentPage", out _), Is.True,
                "Paging info must include 'currentPage'.");
            Assert.That(paging.TryGetProperty("pageSize", out _), Is.True,
                "Paging info must include 'pageSize'.");
            Assert.That(paging.TryGetProperty("totalItems", out _), Is.True,
                "Paging info must include 'totalItems'.");
        }
    }

    #endregion

    #region Result Item Structure

    [Test]
    public async Task Search_ResultItem_ContainsCoreIdentifiers()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement firstResult = result.StructuredContent!.Value.GetProperty("results")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult.TryGetProperty("score", out _), Is.True,
                "Result item must include 'score' for relevance ranking.");
            Assert.That(firstResult.TryGetProperty("database", out _), Is.True,
                "Result item must include 'database' reference.");
            Assert.That(firstResult.TryGetProperty("table", out _), Is.True,
                "Result item must include 'table' summary.");
        }
    }

    [Test]
    public async Task Search_ResultItem_DatabaseHasIdAndName()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement database = result.StructuredContent!.Value
            .GetProperty("results")[0]
            .GetProperty("database");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(database.GetProperty("id").GetString(), Is.EqualTo("StatFin"),
                "Database ref must have 'id' usable in subsequent API calls.");
            Assert.That(database.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty,
                "Database ref must have human-readable 'name'.");
        }
    }

    [Test]
    public async Task Search_ResultItem_TableHasCodeAndName()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement table = result.StructuredContent!.Value
            .GetProperty("results")[0]
            .GetProperty("table");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetProperty("tableId").GetString(), Is.EqualTo("statfin_vaerak_pxt_11ra"),
                "Table summary must have 'tableId' usable in subsequent API calls.");
            Assert.That(table.GetProperty("title").GetString(), Is.Not.Null.And.Not.Empty,
                "Table summary must have human-readable 'title'.");
        }
    }

    #endregion

    #region Table Summary Structure

    [Test]
    public async Task Search_ResultItem_TableHasMetrics()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement table = result.StructuredContent!.Value
            .GetProperty("results")[0]
            .GetProperty("table");

        JsonElement metrics = table.GetProperty("metrics");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.GetArrayLength(), Is.GreaterThan(0),
                "Metrics array should list available metrics.");

            JsonElement firstValue = metrics[0];
            Assert.That(firstValue.GetProperty("name").GetString(), Is.EqualTo("Population"),
                "Metric must have 'name'.");
            Assert.That(firstValue.GetProperty("unit").GetString(), Is.EqualTo("Number"),
                "Metric must have 'unit'.");
        }
    }

    [Test]
    public async Task Search_ResultItem_TableHasTimeRange()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement table = result.StructuredContent!.Value
            .GetProperty("results")[0]
            .GetProperty("table");

        JsonElement timeRange = table.GetProperty("timeRange");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeRange.GetProperty("from").GetString(), Is.EqualTo("1990"),
                "Time range must have 'from'.");
            Assert.That(timeRange.GetProperty("to").GetString(), Is.EqualTo("2023"),
                "Time range must have 'to'.");
        }
    }

    [Test]
    public async Task Search_ResultItem_TableHasDimensions()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement table = result.StructuredContent!.Value
            .GetProperty("results")[0]
            .GetProperty("table");

        JsonElement dimensions = table.GetProperty("dimensions");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dimensions.GetArrayLength(), Is.GreaterThan(0),
                "Dimensions should list non-content, non-time dimensions.");

            JsonElement firstDim = dimensions[0];
            Assert.That(firstDim.GetProperty("name").GetString(), Is.EqualTo("Region"),
                "Dimension must have 'name'.");
            Assert.That(firstDim.GetProperty("size").GetInt32(), Is.GreaterThan(0),
                "Dimension must have 'size' so agents can estimate result volume.");
        }
    }

    #endregion

    #region Excluded Fields

    [Test]
    public async Task Search_ResultItem_DoesNotContainMatchesOrLinks()
    {
        SetupSearchWithResults();

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        JsonElement firstResult = result.StructuredContent!.Value.GetProperty("results")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult.TryGetProperty("matches", out _), Is.False,
                "Matches should be excluded to reduce token usage.");
            Assert.That(firstResult.TryGetProperty("links", out _), Is.False,
                "Links should be excluded to reduce token usage.");
        }
    }

    #endregion

    #region Empty Results

    [Test]
    public async Task Search_EmptyResults_ReturnsValidStructure()
    {
        ApiClientMock.Setup(c => c.SearchGlobalAsync("nonexistent", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PxApiSearchResponse
            {
                Query = new SearchQueryInfo { Q = "nonexistent", Target = SearchTarget.Content, Lang = "en" },
                Results = [],
                PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 10, TotalItems = 0 }
            });

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "nonexistent"
        });

        JsonElement content = result.StructuredContent!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.GetProperty("results").GetArrayLength(), Is.EqualTo(0),
                "Empty results should be an empty array, not null.");
            Assert.That(content.GetProperty("pagingInfo").GetProperty("totalItems").GetInt32(), Is.EqualTo(0));
        }
    }

    #endregion

    #region Helpers

    private void SetupSearchWithResults()
    {
        PxApiSearchResponse searchResponse = CreateSearchResponseWithOneResult();

        ApiClientMock.Setup(c => c.SearchGlobalAsync("population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResponse);
    }

    private static PxApiSearchResponse CreateSearchResponseWithOneResult()
    {
        return new PxApiSearchResponse
        {
            Query = new SearchQueryInfo
            {
                Q = "population",
                Target = SearchTarget.Content,
                Lang = "en"
            },
            Results =
            [
                new PxApiSearchResultItem
                {
                    Score = 42.5,
                    Database = new SearchDatabaseRef { Id = "StatFin", Name = "StatFin database" },
                    Table = new TableSummary
                    {
                        TableId = "statfin_vaerak_pxt_11ra",
                        Title = "Population by area and year",
                        Metrics =
                        [
                            new MetricInfo { Name = "Population", Unit = "Number" },
                            new MetricInfo { Name = "Population change %", Unit = "Percent" }
                        ],
                        TimeRange = new TimeRange { From = "1990", To = "2023" },
                        Dimensions = [new DimensionInfo { Name = "Region", Size = 2 }],
                        LastUpdated = new DateTime(2024, 3, 15, 6, 0, 0, DateTimeKind.Utc)
                    },
                    Matches =
                    [
                        new MatchInfo
                        {
                            Path = "table.label",
                            MatchType = Models.PxApi.MatchType.Contains,
                            MatchedText = "Population"
                        }
                    ]
                }
            ],
            PagingInfo = new PagingInfo
            {
                CurrentPage = 1,
                PageSize = 10,
                TotalItems = 1
            }
        };
    }

    #endregion
}
