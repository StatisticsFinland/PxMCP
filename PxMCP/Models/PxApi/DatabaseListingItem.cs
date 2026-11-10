using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a database entry in the PX API database listing.
/// </summary>
public class DatabaseListingItem
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tableCount")]
    public required int TableCount { get; set; }

    [JsonPropertyName("availableLanguages")]
    public required List<string> AvailableLanguages { get; set; }

    [JsonPropertyName("links")]
    public required List<Link> Links { get; set; }
}
