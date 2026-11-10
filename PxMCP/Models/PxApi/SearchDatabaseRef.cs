using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Reference to a database returned with a search result.
/// </summary>
public class SearchDatabaseRef
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }
}
