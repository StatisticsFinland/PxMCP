using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Information about the search query that was executed.
/// </summary>
public class SearchQueryInfo
{
    [JsonPropertyName("q")]
    public required string Q { get; set; }

    [JsonPropertyName("target")]
    public required SearchTarget Target { get; set; }

    [JsonPropertyName("lang")]
    public required string Lang { get; set; }
}
