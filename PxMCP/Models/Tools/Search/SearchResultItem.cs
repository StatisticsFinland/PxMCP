using System.Text.Json.Serialization;
using PxMCP.Models.PxApi;

namespace PxMCP.Models.Tools.Search;

/// <summary>
/// A single search result item trimmed for MCP tool output.
/// Contains only the fields needed for table selection.
/// </summary>
public class SearchResultItem
{
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("database")]
    public required SearchDatabaseRef Database { get; set; }

    [JsonPropertyName("table")]
    public required TableSummary Table { get; set; }
}
