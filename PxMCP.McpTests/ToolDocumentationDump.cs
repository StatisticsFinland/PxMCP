using System.Text.Json;
using ModelContextProtocol.Client;

namespace PxMCP.McpTests;

/// <summary>
/// Diagnostic test that dumps the full tool documentation as an MCP client sees it.
/// Run with: dotnet test PxMCP.McpTests --filter ToolDocumentationDump -v normal
/// </summary>
[TestFixture]
public class ToolDocumentationDump : McpTestFixture
{
    [Test]
    public async Task DumpAllToolDocumentation()
    {
        IList<McpClientTool> tools = await Client.ListToolsAsync();

        foreach (McpClientTool tool in tools.OrderBy(t => t.Name))
        {
            Console.WriteLine($"=== {tool.Name} ===");
            Console.WriteLine($"Description: {tool.Description}");
            Console.WriteLine($"Input Schema: {JsonSerializer.Serialize(tool.JsonSchema, new JsonSerializerOptions { WriteIndented = true })}");
            Console.WriteLine($"Output Schema: {JsonSerializer.Serialize(tool.ReturnJsonSchema, new JsonSerializerOptions { WriteIndented = true })}");
            Console.WriteLine();
        }

        Assert.Pass($"Dumped {tools.Count} tools.");
    }
}
