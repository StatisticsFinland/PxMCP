using System.Text.Json;
using ModelContextProtocol.Client;

namespace PxMCP.McpTests;

/// <summary>
/// Tests that verify the JSON schema structure of MCP tool parameters.
/// </summary>
[TestFixture]
public class ToolSchemaTests : McpTestFixture
{
    [Test]
    public async Task AllTools_DeclareObjectOutputSchemas()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        string[] expectedTools =
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

        foreach (string toolName in expectedTools)
        {
            McpClientTool tool = tools.Single(candidate => candidate.Name == toolName);
            JsonElement? outputSchema = tool.ReturnJsonSchema;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outputSchema, Is.Not.Null, $"Tool '{toolName}' must advertise an output schema.");
                Assert.That(outputSchema!.Value.GetProperty("type").GetString(), Is.EqualTo("object"),
                    $"Tool '{toolName}' output schema must have an object root.");
                Assert.That(outputSchema.Value.GetProperty("properties").EnumerateObject(), Is.Not.Empty,
                    $"Tool '{toolName}' output schema must describe its result properties.");
            }
        }
    }

    [Test]
    public async Task GetData_FiltersParameter_IsObjectType()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool getData = tools.Single(t => t.Name == "get_data");
        JsonElement schema = getData.JsonSchema;

        JsonElement props = schema.GetProperty("properties");
        JsonElement filters = props.GetProperty("filters");

        // Nullable parameters may have type as ["object", "null"] or just "object"
        if (filters.TryGetProperty("type", out JsonElement typeElement))
        {
            if (typeElement.ValueKind == JsonValueKind.Array)
            {
                List<string?> types = typeElement.EnumerateArray().Select(e => e.GetString()).ToList();
                Assert.That(types, Does.Contain("object"),
                    "filters parameter should include 'object' type, not 'string'");
            }
            else
            {
                Assert.That(typeElement.GetString(), Is.EqualTo("object"),
                    "filters parameter should have type 'object', not 'string'");
            }
        }
        else
        {
            // With polymorphic types, the schema may use anyOf/oneOf instead of a flat type
            bool hasAnyOf = filters.TryGetProperty("anyOf", out _);
            bool hasOneOf = filters.TryGetProperty("oneOf", out _);
            Assert.That(hasAnyOf || hasOneOf, Is.True,
                "filters parameter should have 'type', 'anyOf', or 'oneOf'");
        }
    }

    [Test]
    public async Task GetData_FiltersParameter_IsNotRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool getData = tools.Single(t => t.Name == "get_data");
        JsonElement schema = getData.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.That(required, Does.Not.Contain("filters"));
    }

    [Test]
    public async Task GetData_FiltersDescriptionDocumentsWildcardSemantics()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool getData = tools.Single(tool => tool.Name == "get_data");
        JsonElement filters = getData.JsonSchema.GetProperty("properties").GetProperty("filters");
        string description = filters.GetProperty("description").GetString()!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description, Does.Contain("wildcard"));
            Assert.That(description, Does.Contain("inclusive"));
            Assert.That(description, Does.Contain("First/Last"));
        }
    }

    [Test]
    public async Task GetData_DatabaseAndTable_AreRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool getData = tools.Single(t => t.Name == "get_data");
        JsonElement schema = getData.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(required, Does.Contain("database"));
            Assert.That(required, Does.Contain("table"));
        }
    }

    /// <summary>
    /// Ensures agents can distinguish local filter validation from guaranteed upstream success.
    /// </summary>
    [Test]
    public async Task ValidateQuery_DescriptionExplainsValidationLimits()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool tool = tools.Single(candidate => candidate.Name == "validate_query");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Description, Does.Contain("wildcard patterns are not estimated"));
            Assert.That(tool.Description, Does.Contain("maxCells from a data HEAD request"));
            Assert.That(tool.Description, Does.Contain("does not guarantee get_data will succeed"));
        }
    }

    [Test]
    public async Task GetData_LangParameter_IsNotRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool getData = tools.Single(t => t.Name == "get_data");
        JsonElement schema = getData.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.That(required, Does.Not.Contain("lang"));
    }

    [Test]
    public async Task Search_QueryIsRequired_OptionalParamsAreNot()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(t => t.Name == "search");
        JsonElement schema = search.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(required, Does.Contain("query"));
            Assert.That(required, Does.Not.Contain("database"));
            Assert.That(required, Does.Not.Contain("scope"));
            Assert.That(required, Does.Not.Contain("lang"));
        }
    }

    [Test]
    public async Task Search_ScopeDescriptionListsContentScope()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool search = tools.Single(tool => tool.Name == "search");
        JsonElement scope = search.JsonSchema.GetProperty("properties").GetProperty("scope");

        Assert.That(scope.GetProperty("description").GetString(), Does.Contain("'content'"));
    }

    [Test]
    public async Task ListTables_DatabaseIsRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool listTables = tools.Single(t => t.Name == "list_tables");
        JsonElement schema = listTables.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.That(required, Does.Contain("database"));
    }

    [Test]
    public async Task ListTables_PageAndPageSize_AreNotRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool listTables = tools.Single(t => t.Name == "list_tables");
        JsonElement schema = listTables.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(required, Does.Not.Contain("page"));
            Assert.That(required, Does.Not.Contain("pageSize"));
        }
    }

    [Test]
    public async Task AllTools_AllParameters_HaveDescriptions()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        foreach (McpClientTool tool in tools)
        {
            JsonElement schema = tool.JsonSchema;
            if (schema.TryGetProperty("properties", out JsonElement props))
            {
                foreach (JsonProperty prop in props.EnumerateObject())
                {
                    // Skip cancellationToken — not an MCP parameter
                    if (prop.Name == "cancellationToken") continue;

                    Assert.That(
                        prop.Value.TryGetProperty("description", out JsonElement desc) && desc.GetString()?.Length > 0,
                        Is.True,
                        $"Tool '{tool.Name}' parameter '{prop.Name}' has no description.");
                }
            }
        }
    }

    [Test]
    public async Task GetDimensionValues_DimensionIsRequired()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        McpClientTool tool = tools.Single(t => t.Name == "get_dimension_values");
        JsonElement schema = tool.JsonSchema;

        List<string?> required = schema.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(required, Does.Contain("database"));
            Assert.That(required, Does.Contain("table"));
            Assert.That(required, Does.Contain("dimension"));
        }
    }
}
