using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// A single result item from a PX API search.
/// </summary>
public class PxApiSearchResultItem
{
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("database")]
    public required SearchDatabaseRef Database { get; set; }

    [JsonPropertyName("table")]
    public required TableSummary Table { get; set; }

    [JsonPropertyName("matches")]
    public List<MatchInfo>? Matches { get; set; }

    [JsonPropertyName("links")]
    public List<Link>? Links { get; set; }
}
