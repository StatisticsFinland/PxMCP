using ModelContextProtocol.Client;

namespace PxMCP.McpTests;

/// <summary>
/// Tests that verify tool discovery and registration via the MCP protocol.
/// </summary>
[TestFixture]
public class ToolDiscoveryTests : McpTestFixture
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

    [Test]
    public async Task ListTools_ReturnsExpectedToolCount()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        Assert.That(tools, Has.Count.EqualTo(ExpectedToolNames.Length));
    }

    [Test]
    [TestCaseSource(nameof(ExpectedToolNames))]
    public async Task ListTools_ContainsTool(string toolName)
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        Assert.That(tools.Select(t => t.Name), Does.Contain(toolName));
    }

    [Test]
    public async Task AllTools_HaveNonEmptyDescription()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        foreach (McpClientTool tool in tools)
        {
            Assert.That(tool.Description, Is.Not.Null.And.Not.Empty,
                $"Tool '{tool.Name}' has no description.");
        }
    }

    [Test]
    public async Task AllTools_HaveJsonSchema()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        foreach (McpClientTool tool in tools)
        {
            Assert.That(tool.JsonSchema.ValueKind, Is.Not.EqualTo(System.Text.Json.JsonValueKind.Undefined),
                $"Tool '{tool.Name}' has no JSON schema.");
        }
    }

    [Test]
    public async Task AllTools_HaveExpectedAnnotations()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();
        foreach (McpClientTool tool in tools)
        {
            ModelContextProtocol.Protocol.ToolAnnotations? annotations = tool.ProtocolTool.Annotations;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(annotations, Is.Not.Null, $"Tool '{tool.Name}' has no annotations.");
                Assert.That(annotations!.ReadOnlyHint, Is.True, $"Tool '{tool.Name}': ReadOnlyHint should be true.");
                Assert.That(annotations.DestructiveHint, Is.False, $"Tool '{tool.Name}': DestructiveHint should be false.");
                Assert.That(annotations.IdempotentHint, Is.True, $"Tool '{tool.Name}': IdempotentHint should be true.");
                Assert.That(annotations.OpenWorldHint, Is.True, $"Tool '{tool.Name}': OpenWorldHint should be true.");
            }
        }
    }
}
