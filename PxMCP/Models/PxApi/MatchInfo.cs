using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Describes a text match within a search result.
/// </summary>
public class MatchInfo
{
    [JsonPropertyName("path")]
    public required string? Path { get; set; }

    [JsonPropertyName("matchType")]
    public required MatchType MatchType { get; set; }

    [JsonPropertyName("matchedText")]
    public required string? MatchedText { get; set; }
}
