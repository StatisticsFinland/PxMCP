using System.Text.Json.Serialization;
using PxMCP.Models.PxApi;

namespace PxMCP.Models.Tools.Search;

/// <summary>
/// Trimmed search response for MCP tool output containing only fields needed for table selection.
/// </summary>
public class SearchResult
{
    [JsonPropertyName("results")]
    public required List<SearchResultItem> Results { get; set; }

    [JsonPropertyName("pagingInfo")]
    public required PagingInfo PagingInfo { get; set; }
}
