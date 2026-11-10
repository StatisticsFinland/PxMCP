using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Services;
using MatchType = PxMCP.Models.PxApi.MatchType;

namespace PxMCP.McpTests;

/// <summary>
/// Tests that verify MCP tool invocation through the protocol transport.
/// </summary>
[TestFixture]
public class ToolInvocationTests : McpTestFixture
{
    /// <summary>
    /// Verifies an explicit MCP cancellation notification reaches the upstream operation.
    /// </summary>
    [TestCase("list_databases")]
    [TestCase("list_tables")]
    [TestCase("search")]
    [TestCase("get_head_metadata")]
    [TestCase("get_table_metadata")]
    [TestCase("get_dimension_values")]
    [TestCase("get_data")]
    [TestCase("validate_query")]
    [TestCase("get_data_filtered")]
    [TestCase("validate_query_head")]
    public async Task AllTools_CancellationNotification_CancelsUpstreamOperation(string scenario)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ApiClientMock.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .Returns((string? _, CancellationToken token) => WaitForCancellation<List<DatabaseListingItem>>(token));
        ApiClientMock.Setup(client => client.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()))
            .Returns((string _, string? _, int _, int _, CancellationToken token) =>
                WaitForCancellation<PagedTableList>(token));
        ApiClientMock.Setup(client => client.SearchGlobalAsync("test", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .Returns((string _, string? _, string? _, int _, int _, CancellationToken token) =>
                WaitForCancellation<PxApiSearchResponse>(token));
        ApiClientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string? _, CancellationToken token) => WaitForCancellation<JsonStat2>(token));
        ApiClientMock.Setup(client => client.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .Returns((string _, string _, Dictionary<string, Filter>? _, string? _, CancellationToken token) =>
                WaitForCancellation<JsonStat2>(token));
        if (scenario == "validate_query_head")
        {
            ApiClientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateSampleData());
            ApiClientMock.Setup(client => client.GetMaxCellsAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
                .Returns((string _, string _, string? _, CancellationToken token) => WaitForCancellation<long>(token));
        }
        Dictionary<string, object?> arguments = scenario switch
        {
            "list_databases" => [],
            "list_tables" => new() { ["database"] = "StatFin" },
            "search" => new() { ["query"] = "test" },
            _ => new() { ["database"] = "StatFin", ["table"] = "table1" }
        };
        if (scenario == "get_dimension_values")
        {
            arguments["dimension"] = "year";
        }
        if (scenario == "get_data_filtered")
        {
            arguments["filters"] = new Dictionary<string, object?>
            {
                ["year"] = new Dictionary<string, object?> { ["type"] = "Code", ["query"] = new[] { "2020" } }
            };
        }

        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        string toolName = scenario switch
        {
            "get_data_filtered" => "get_data",
            "validate_query_head" => "validate_query",
            _ => scenario
        };
        RequestId requestId = new("cancel-test");
        Dictionary<string, object?> parameters = new() { ["name"] = toolName, ["arguments"] = arguments };
        Task<CallToolResult> call = Client.SendRequestAsync<Dictionary<string, object?>, CallToolResult>(
            "tools/call", parameters, requestId: requestId, cancellationToken: cancellation.Token).AsTask();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.That(async () => await call.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InstanceOf<OperationCanceledException>());

            await Client.SendNotificationAsync("notifications/cancelled",
                new CancelledNotificationParams { RequestId = requestId });
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (scenario == "get_data_filtered")
            {
                ApiClientMock.Verify(client => client.PostDataAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, Filter>>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            }
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
    /// Validates nonempty results recursively against advertised schemas and checks text/structured agreement.
    /// </summary>
    [TestCase("list_databases", "databases")]
    [TestCase("list_tables", "tables")]
    [TestCase("search", "results")]
    [TestCase("get_head_metadata", "dimensions")]
    [TestCase("get_table_metadata", "dimension")]
    [TestCase("get_dimension_values", "values")]
    [TestCase("get_data", "value")]
    [TestCase("validate_query", "isValid")]
    public async Task AllTools_TextAndStructuredResultsAgree(string toolName, string expectedProperty)
    {
        ApiClientMock.Setup(client => client.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DatabaseListingItem
                {
                    Id = "StatFin", Name = "Statistics", Description = null, TableCount = 1,
                    AvailableLanguages = ["en"],
                    Links = [new Link { Href = "/tables", Rel = "describedby", Method = "GET" }]
                }
            ]);
        TableSummary summary = new()
        {
            TableId = "table1", Title = "Test table",
            Metrics = [new MetricInfo { Name = "Population", Unit = "persons" }],
            TimeRange = new TimeRange { From = "2020", To = "2025" },
            Dimensions = [new DimensionInfo { Name = "Region", Size = 2 }],
            LastUpdated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        ApiClientMock.Setup(client => client.GetTablesAsync("StatFin", null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedTableList
            {
                Tables = [new TableListingItem { Table = summary, Links = [] }],
                PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 1 }
            });
        PxApiSearchResponse searchResponse = CreateEmptySearchResponse();
        searchResponse.Results =
        [
            new PxApiSearchResultItem
            {
                Database = new SearchDatabaseRef { Id = "StatFin", Name = "Statistics" },
                Table = summary, Score = 1.25, Links = null,
                Matches =
                [
                    new MatchInfo { Path = null, MatchType = MatchType.Exact, MatchedText = null },
                    new MatchInfo { Path = "table.title", MatchType = MatchType.Contains, MatchedText = "Test" }
                ]
            }
        ];
        searchResponse.PagingInfo.TotalItems = 1;
        ApiClientMock.Setup(client => client.SearchGlobalAsync("test", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResponse);
        JsonStat2 dataset = CreateSampleData();
        dataset.Value = [0, null];
        dataset.Size = [2, 1];
        dataset.Dimension["year"].Category.Index.Add("2021");
        dataset.Dimension["year"].Category.Label.Add("2021", "2021");
        dataset.Note = ["Synthetic note"];
        dataset.Source = null;
        dataset.Updated = null;
        dataset.Status = new() { ["1"] = "missing" };
        dataset.Role = new() { ["time"] = ["year"], ["metric"] = ["tiedot"] };
        dataset.Extension = new()
        {
            ["synthetic"] = JsonSerializer.SerializeToElement(new { nested = new object?[] { null, 1, "text" } })
        };
        dataset.Dimension["tiedot"].Category.Unit = new()
        {
            ["vaesto"] = new Unit { Label = "Persons", Decimals = 0, Symbol = null, Position = "end" }
        };
        ApiClientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataset);
        ApiClientMock.Setup(client => client.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataset);

        Dictionary<string, object?> arguments = toolName switch
        {
            "list_databases" => [],
            "list_tables" => new() { ["database"] = "StatFin" },
            "search" => new() { ["query"] = "test" },
            "get_dimension_values" => new()
            {
                ["database"] = "StatFin", ["table"] = "table1", ["dimension"] = "year"
            },
            "validate_query" => new()
            {
                ["database"] = "StatFin", ["table"] = "table1",
                ["filters"] = new Dictionary<string, object?>
                {
                    ["unknown"] = new { type = "Code", query = new[] { "value" } }
                }
            },
            _ => new() { ["database"] = "StatFin", ["table"] = "table1" }
        };
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool tool = tools.Single(candidate => candidate.Name == toolName);
        CallToolResult result = await Client.CallToolAsync(toolName, arguments);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.Not.True);
            Assert.That(result.StructuredContent, Is.Not.Null);
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
            Assert.That(tool.ReturnJsonSchema, Is.Not.Null);
        }

        JsonElement structured = result.StructuredContent!.Value;
        using JsonDocument text = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        JsonElement outputSchema = tool.ReturnJsonSchema!.Value;
        JsonSchema schema = JsonSchema.FromText(outputSchema.GetRawText());
        EvaluationResults evaluation = schema.Evaluate(structured);
        JsonElement properties = outputSchema.GetProperty("properties");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonElement.DeepEquals(text.RootElement, structured), Is.True,
                "Text and structured content must represent the same result.");
            Assert.That(evaluation.IsValid, Is.True,
                $"Tool '{toolName}' must satisfy its entire advertised output schema: {JsonSerializer.Serialize(evaluation)}");
            Assert.That(structured.TryGetProperty(expectedProperty, out _), Is.True);
            foreach (JsonProperty property in structured.EnumerateObject())
            {
                Assert.That(properties.TryGetProperty(property.Name, out _), Is.True,
                    $"Returned root property '{property.Name}' must be documented in the output schema.");
            }

            JsonNode corrupted = JsonNode.Parse(structured.GetRawText())!;
            switch (toolName)
            {
                case "list_databases":
                    corrupted["databases"]![0]!["links"]![0]!["href"] = 42;
                    break;
                case "list_tables":
                    corrupted["tables"]![0]!["table"]!["dimensions"]![0]!["size"] = "not-an-integer";
                    break;
                case "search":
                    corrupted["results"]![0]!["table"]!["metrics"]![0]!["unit"] = 42;
                    break;
                case "get_head_metadata":
                    corrupted["dimensions"]![0]!["valueCount"] = "not-an-integer";
                    break;
                case "get_dimension_values":
                    corrupted["values"]!["2020"] = 42;
                    break;
                case "validate_query":
                    corrupted["issues"]![0]!["message"] = 42;
                    break;
                default:
                    corrupted["dimension"]!["tiedot"]!["category"]!["unit"]!["vaesto"]!["decimals"] = "not-an-integer";
                    break;
            }
            Assert.That(schema.Evaluate(JsonSerializer.SerializeToElement(corrupted)).IsValid, Is.False,
                $"The schema for '{toolName}' must reject a deliberately corrupted nested value.");
            if (outputSchema.TryGetProperty("required", out JsonElement required))
            {
                foreach (JsonElement property in required.EnumerateArray())
                {
                    Assert.That(structured.TryGetProperty(property.GetString()!, out _), Is.True,
                        $"Required root property '{property.GetString()}' must be returned.");
                }
            }
        }
    }

    /// <summary>
    /// Validates new numeric/nullable size fields against the advertised schema, including an unavailable limit.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ValidateQuery_SizeFieldsConformToSchema(bool headFails)
    {
        ApiClientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());
        if (headFails)
        {
            ApiClientMock.Setup(client => client.GetMaxCellsAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Synthetic failure"));
        }
        else
        {
            ApiClientMock.Setup(client => client.GetMaxCellsAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
        }

        CallToolResult result = await Client.CallToolAsync("validate_query", new Dictionary<string, object?>
        {
            ["database"] = "StatFin", ["table"] = "table1",
            ["filters"] = new Dictionary<string, object>
            {
                ["year"] = new { type = "First", query = 1 },
                ["tiedot"] = new { type = "Last", query = 1 }
            }
        });
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        JsonSchema schema = JsonSchema.FromText(tools.Single(tool => tool.Name == "validate_query").ReturnJsonSchema!.Value.GetRawText());
        JsonElement structured = result.StructuredContent!.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.Not.True);
            Assert.That(schema.Evaluate(structured).IsValid, Is.True);
            Assert.That(structured.GetProperty("selectedCellCount").GetInt64(), Is.EqualTo(1));
            Assert.That(structured.GetProperty("maxCells").ValueKind, Is.EqualTo(headFails ? JsonValueKind.Null : JsonValueKind.Number));
            Assert.That(structured.GetProperty("exceedsMaxCells").ValueKind, Is.EqualTo(headFails ? JsonValueKind.Null : JsonValueKind.False));
            Assert.That(structured.GetProperty("issues").GetArrayLength(), Is.EqualTo(headFails ? 1 : 0));
        }
        JsonNode corrupted = JsonNode.Parse(structured.GetRawText())!;
        corrupted["maxCells"] = "not-a-number";
        Assert.That(schema.Evaluate(JsonSerializer.SerializeToElement(corrupted)).IsValid, Is.False);
    }

    /// <summary>
    /// Records property-order behavior with the unmodified MCP SDK defaults.
    /// </summary>
    [TestCase("{\"type\":\"Code\",\"query\":[\"KU091\"]}", true)]
    [TestCase("{\"query\":[\"KU091\"],\"type\":\"Code\"}", false)]
    public void FilterDeserialization_WithSdkDefaults_RequiresTypeFirst(string filterJson, bool typeFirst)
    {
        string json = "{\"alue_23_20260101\":" + filterJson + "}";
        TestContext.Out.WriteLine($"Raw JSON: {json}");
        TestContext.Out.WriteLine($"System.Text.Json: {typeof(JsonSerializer).Assembly.FullName}");
        TestContext.Out.WriteLine($"AllowOutOfOrderMetadataProperties: {McpJsonUtilities.DefaultOptions.AllowOutOfOrderMetadataProperties}");

        if (!typeFirst)
        {
            NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
                JsonSerializer.Deserialize<Dictionary<string, Filter>>(json, McpJsonUtilities.DefaultOptions))!;
            TestContext.Out.WriteLine(exception.ToString());
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception.Message, Does.Contain("must specify a type discriminator"));
                Assert.That(exception.Message, Does.Contain("Path: $.alue_23_20260101"));
            }
            return;
        }

        Dictionary<string, Filter>? filters = JsonSerializer.Deserialize<Dictionary<string, Filter>>(
            json, McpJsonUtilities.DefaultOptions);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(filters!["alue_23_20260101"], Is.TypeOf<CodeFilter>());
            Assert.That(((CodeFilter)filters["alue_23_20260101"]).Query, Is.EqualTo(new[] { "KU091" }));
        }
    }

    /// <summary>
    /// Verifies all filter shapes and property orders with the actual tool registration options.
    /// </summary>
    [Test]
    public void FilterDeserialization_WithToolOptions_AcceptsEitherPropertyOrder(
        [Values("Code", "From", "To", "First", "Last")] string filterType,
        [Values(true, false)] bool typeFirst)
    {
        string filterJson = CreateFilterJson(filterType, typeFirst);
        string json = "{\"alue_23_20260101\":" + filterJson + "}";
        TestContext.Out.WriteLine($"Raw JSON: {json}");

        Dictionary<string, Filter>? filters = JsonSerializer.Deserialize<Dictionary<string, Filter>>(
            json, ToolSerializerOptions);

        AssertFilter(filters!["alue_23_20260101"], filterType);
    }

    /// <summary>
    /// Exercises MCP argument binding for both filter tools, all query shapes and both key orders.
    /// </summary>
    [Test]
    public async Task FilterTools_AcceptEitherPropertyOrder(
        [Values("get_data", "validate_query")] string toolName,
        [Values("Code", "From", "To", "First", "Last")] string filterType,
        [Values(true, false)] bool typeFirst)
    {
        Dictionary<string, Filter>? capturedFilters = null;
        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", "fi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());
        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), "fi", It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>(
                (_, _, filters, _, _) => capturedFilters = filters)
            .ReturnsAsync(CreateSampleData());

        string rawArguments = "{\"database\":\"StatFin\",\"table\":\"table1\",\"lang\":\"fi\",\"filters\":{\"year\":" +
            CreateFilterJson(filterType, typeFirst) + "}}";
        TestContext.Out.WriteLine($"Raw tool arguments: {rawArguments}");
        Dictionary<string, JsonElement> arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawArguments)!;

        CallToolResult result = await Client.CallToolAsync(toolName,
            arguments.ToDictionary(argument => argument.Key, argument => (object?)argument.Value));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.Not.True);
            Assert.That(result.StructuredContent, Is.Not.Null);
        }
        if (toolName == "get_data")
        {
            Assert.That(capturedFilters, Is.Not.Null);
            AssertFilter(capturedFilters!["year"], filterType);
        }
        else
        {
            Assert.That(result.StructuredContent!.Value.GetProperty("isValid").GetBoolean(), Is.True);
        }
        ApiClientMock.Verify(c => c.GetTableMetadataAsync("StatFin", "table1", "fi", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ListDatabases_ReturnsDatabaseList()
    {
        ApiClientMock.Setup(c => c.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DatabaseListingItem
                {
                    Id = "StatFin", Name = "StatFin",
                    Description = "Statistics Finland",
                    TableCount = 0,
                    AvailableLanguages = ["fi", "en"],
                    Links = []
                }
            ]);

        CallToolResult result = await Client.CallToolAsync("list_databases", new Dictionary<string, object?>());

        Assert.That(result.StructuredContent, Is.Not.Null);
        JsonElement content = result.StructuredContent!.Value;
        Assert.That(content.GetProperty("databases")[0].GetProperty("id").GetString(), Is.EqualTo("StatFin"));
    }

    [Test]
    public async Task GetData_WithoutFilters_CallsApiWithNullFilters()
    {
        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        CallToolResult result = await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1"
        });

        Assert.That(result.StructuredContent, Is.Not.Null);
        JsonElement content = result.StructuredContent!.Value;
        Assert.That(content.GetProperty("value").GetArrayLength(), Is.GreaterThan(0));
        ApiClientMock.Verify(c => c.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetData_WithFilters_PassesTypedFiltersThroughProtocol()
    {
        Dictionary<string, Filter>? capturedFilters = null;

        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>((_, _, f, _, _) => capturedFilters = f)
            .ReturnsAsync(CreateSampleData());

        Dictionary<string, object?> filters = new()
        {
            ["year"] = new Dictionary<string, object?> { ["type"] = "From", ["query"] = "2020" }
        };

        await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1",
            ["filters"] = filters
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedFilters, Is.Not.Null);
            Assert.That(capturedFilters!, Does.ContainKey("year"));
            Assert.That(capturedFilters!["year"], Is.InstanceOf<FromFilter>());
        }
    }

    [Test]
    public async Task GetData_WithLang_PassesLangParameter()
    {
        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "table1", null, "sv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1",
            ["lang"] = "sv"
        });

        ApiClientMock.Verify(c => c.PostDataAsync("StatFin", "table1", null, "sv", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Search_WithDatabase_CallsDatabaseScopedSearch()
    {
        ApiClientMock.Setup(c => c.SearchDatabaseAsync("StatFin", "population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptySearchResponse());

        await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population",
            ["database"] = "StatFin"
        });

        ApiClientMock.Verify(c => c.SearchDatabaseAsync("StatFin", "population", null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Search_WithoutScope_CallsGlobalSearch()
    {
        ApiClientMock.Setup(c => c.SearchGlobalAsync("population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptySearchResponse());

        await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "population"
        });

        ApiClientMock.Verify(c => c.SearchGlobalAsync("population", null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetDimensionValues_InvalidDimension_ReturnsError()
    {
        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        CallToolResult result = await Client.CallToolAsync("get_dimension_values", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1",
            ["dimension"] = "nonexistent"
        });

        Assert.That(result.IsError, Is.True);
    }

    #region Error Handling

    /// <summary>
    /// Ensures a client-classified upstream timeout gives corrective guidance without success content.
    /// </summary>
    [Test]
    public async Task GetData_UpstreamTimeout_ReturnsActionableToolError()
    {
        ApiClientMock.Setup(client => client.PostDataAsync(
                "StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "PX API request timed out. Try again later or narrow the query.",
                new TaskCanceledException("Synthetic timeout", new TimeoutException())));

        CallToolResult result = await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin", ["table"] = "table1"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent, Is.Null);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("timed out"));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("narrow the query"));
        }
    }

    /// <summary>
    /// Ensures metadata failures remain tool errors and never cause a fallback data request.
    /// </summary>
    [TestCase("get_dimension_values", System.Net.HttpStatusCode.NotFound)]
    [TestCase("get_dimension_values", System.Net.HttpStatusCode.ServiceUnavailable)]
    [TestCase("validate_query", System.Net.HttpStatusCode.NotFound)]
    [TestCase("validate_query", System.Net.HttpStatusCode.ServiceUnavailable)]
    [TestCase("get_data", System.Net.HttpStatusCode.NotFound)]
    [TestCase("get_data", System.Net.HttpStatusCode.ServiceUnavailable)]
    public async Task MetadataDependentTools_ApiFailure_ReturnsErrorWithoutDataRequest(
        string toolName, System.Net.HttpStatusCode statusCode)
    {
        ApiClientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException($"PX API returned {(int)statusCode}.", null, statusCode));
        Dictionary<string, object?> arguments = new()
        {
            ["database"] = "StatFin",
            ["table"] = "table1"
        };
        if (toolName == "get_dimension_values")
        {
            arguments["dimension"] = "year";
        }
        else if (toolName == "get_data")
        {
            arguments["filters"] = new Dictionary<string, object?>
            {
                ["year"] = new Dictionary<string, object?> { ["type"] = "Code", ["query"] = new[] { "2020" } }
            };
        }

        CallToolResult result = await Client.CallToolAsync(toolName, arguments);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.StructuredContent, Is.Null,
                "An upstream failure must not look like a successful dataset.");
            Assert.That(result.Content, Has.Count.EqualTo(1));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain(((int)statusCode).ToString()));
        }
        ApiClientMock.Verify(client => client.GetTableMetadataAsync(
            "StatFin", "table1", null, It.IsAny<CancellationToken>()), Times.Once);
        ApiClientMock.VerifyNoOtherCalls();
    }

    [TestCase(System.Net.HttpStatusCode.NotFound)]
    [TestCase(System.Net.HttpStatusCode.PartialContent)]
    public async Task GetData_ApiError_ReturnsToolError(System.Net.HttpStatusCode statusCode)
    {
        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "missing", null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException($"PX API returned {(int)statusCode}.", null, statusCode));

        CallToolResult result = await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "missing"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True,
                "API errors should be tool execution errors (isError: true), not protocol errors.");
            Assert.That(result.StructuredContent, Is.Null,
                "An incomplete result must not be presented as a successful dataset.");
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain(((int)statusCode).ToString()),
                "Error message should include the HTTP status code so the LLM can self-correct.");
        }
    }

    [Test]
    public async Task GetData_TooManyCells_ReturnsActionableToolError()
    {
        ApiClientMock.Setup(c => c.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(
                "PX API returned 413 Request Entity Too Large. The query exceeds PX API's maximum allowed cell count. Narrow the selection or apply filters. Maximum allowed cells: 12345.",
                null,
                System.Net.HttpStatusCode.RequestEntityTooLarge));

        CallToolResult result = await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True);
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("maximum allowed cell count"));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("apply filters"));
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("Maximum allowed cells: 12345."));
        }
    }

    [Test]
    public async Task ListDatabases_ApiError_ReturnsToolError()
    {
        ApiClientMock.Setup(c => c.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 503 Service Unavailable.", null, System.Net.HttpStatusCode.ServiceUnavailable));

        CallToolResult result = await Client.CallToolAsync("list_databases", new Dictionary<string, object?>());

        Assert.That(result.IsError, Is.True,
            "API errors from list_databases should be tool execution errors.");
    }

    [Test]
    public async Task ListTables_ApiError_ReturnsToolError()
    {
        ApiClientMock.Setup(c => c.GetTablesAsync("invalid", null, 1, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 404 Not Found.", null, System.Net.HttpStatusCode.NotFound));

        CallToolResult result = await Client.CallToolAsync("list_tables", new Dictionary<string, object?>
        {
            ["database"] = "invalid"
        });

        Assert.That(result.IsError, Is.True,
            "API errors from list_tables should be tool execution errors.");
    }

    [Test]
    public async Task Search_ApiError_ReturnsToolError()
    {
        ApiClientMock.Setup(c => c.SearchGlobalAsync("test", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 500 Internal Server Error.", null, System.Net.HttpStatusCode.InternalServerError));

        CallToolResult result = await Client.CallToolAsync("search", new Dictionary<string, object?>
        {
            ["query"] = "test"
        });

        Assert.That(result.IsError, Is.True,
            "API errors from search should be tool execution errors.");
    }

    [Test]
    public async Task GetTableSummary_ApiError_ReturnsToolError()
    {
        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "missing", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 404 Not Found.", null, System.Net.HttpStatusCode.NotFound));

        CallToolResult result = await Client.CallToolAsync("get_head_metadata", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "missing"
        });

        Assert.That(result.IsError, Is.True,
            "API errors from get_head_metadata should be tool execution errors.");
    }

    [Test]
    public async Task GetTableMetadata_ApiError_ReturnsToolError()
    {
        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "missing", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PX API returned 404 Not Found.", null, System.Net.HttpStatusCode.NotFound));

        CallToolResult result = await Client.CallToolAsync("get_table_metadata", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "missing"
        });

        Assert.That(result.IsError, Is.True,
            "API errors from get_table_metadata should be tool execution errors.");
    }

    [Test]
    public async Task GetData_WithInvalidFilters_ReturnsActionableError()
    {
        ApiClientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        CallToolResult result = await Client.CallToolAsync("get_data", new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1",
            ["filters"] = new Dictionary<string, object?>
            {
                ["unknown_dimension"] = new Dictionary<string, object?> { ["type"] = "Code", ["query"] = new[] { "val1" } }
            }
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True,
                "Semantically invalid filters should produce a tool error, not an API call.");
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("Query validation failed"),
                "Error should identify that validation failed.");
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("unknown_dimension"),
                "Error should name the problematic dimension.");
        }
    }

    /// <summary>
    /// Ensures binding failures are actionable without exposing internal type names or calling the API.
    /// </summary>
    [Test]
    public async Task FilterTools_MalformedFilters_ReturnToolError(
        [Values("get_data", "validate_query")] string toolName,
        [Values("{\"type\":\"InvalidType\",\"query\":\"2020\"}",
            "{\"query\":[\"2020\"]}",
            "{\"type\":\"Code\",\"query\":\"2020\"}",
            "{\"type\":\"From\",\"query\":1}",
            "{\"type\":\"To\",\"query\":1}",
            "{\"type\":\"First\",\"query\":true}",
            "{\"type\":\"Last\",\"query\":true}")] string filterJson)
    {
        using JsonDocument filters = JsonDocument.Parse("{\"year\":" + filterJson + "}");
        CallToolResult result = await Client.CallToolAsync(toolName, new Dictionary<string, object?>
        {
            ["database"] = "StatFin",
            ["table"] = "table1",
            ["filters"] = filters.RootElement
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsError, Is.True,
                "Malformed filter JSON should be a tool error, not an unhandled exception.");
            Assert.That(result.Content, Is.Not.Empty);
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Contain("Invalid 'filters' argument"),
                "Error message should specifically identify the filters parameter.");
            Assert.That(((TextContentBlock)result.Content[0]).Text, Does.Not.Contain("PxMCP."),
                "Error message must not expose internal type names.");
        }
        ApiClientMock.VerifyNoOtherCalls();
    }

    #endregion

    #region Helpers

    private static string CreateFilterJson(string filterType, bool typeFirst)
    {
        string query = filterType switch
        {
            "Code" => "[\"2020\"]",
            "From" or "To" => "\"2020\"",
            "First" or "Last" => "1",
            _ => throw new ArgumentOutOfRangeException(nameof(filterType))
        };
        return typeFirst
            ? $"{{\"type\":\"{filterType}\",\"query\":{query}}}"
            : $"{{\"query\":{query},\"type\":\"{filterType}\"}}";
    }

    private static void AssertFilter(Filter filter, string filterType)
    {
        Type expectedType = filterType switch
        {
            "Code" => typeof(CodeFilter),
            "From" => typeof(FromFilter),
            "To" => typeof(ToFilter),
            "First" => typeof(FirstFilter),
            "Last" => typeof(LastFilter),
            _ => throw new ArgumentOutOfRangeException(nameof(filterType))
        };
        Assert.That(filter, Is.TypeOf(expectedType));
        switch (filter)
        {
            case CodeFilter code:
                Assert.That(code.Query, Is.EqualTo(new[] { "2020" }));
                break;
            case FromFilter from:
                Assert.That(from.Query, Is.EqualTo("2020"));
                break;
            case ToFilter to:
                Assert.That(to.Query, Is.EqualTo("2020"));
                break;
            case FirstFilter first:
                Assert.That(first.Query, Is.EqualTo(1));
                break;
            case LastFilter last:
                Assert.That(last.Query, Is.EqualTo(1));
                break;
        }
    }

    private static JsonStat2 CreateSampleData()
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
                        Index = ["2020"],
                        Label = new Dictionary<string, string> { ["2020"] = "2020" }
                    }
                },
                ["tiedot"] = new()
                {
                    Label = "Information",
                    Category = new Category
                    {
                        Index = ["vaesto"],
                        Label = new Dictionary<string, string> { ["vaesto"] = "Population" }
                    }
                }
            },
            Value = [5500000.0],
            Size = [1, 1]
        };
    }

    private static PxApiSearchResponse CreateEmptySearchResponse()
    {
        return new PxApiSearchResponse
        {
            Query = new SearchQueryInfo { Q = "test", Target = SearchTarget.Content, Lang = "en" },
            Results = [],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 0 }
        };
    }

    #endregion
}
