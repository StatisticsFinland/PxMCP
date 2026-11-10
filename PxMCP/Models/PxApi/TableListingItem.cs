using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a table and its essential metadata used in listings.
/// </summary>
public class TableListingItem
{
    [JsonPropertyName("table")]
    public required TableSummary Table { get; set; }

    [JsonPropertyName("links")]
    public required List<Link> Links { get; set; }
}
