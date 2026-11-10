using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a dimension category with its index, labels, notes, and units.
/// </summary>
public class Category
{
    [JsonPropertyName("index")]
    public required List<string> Index { get; set; }

    [JsonPropertyName("label")]
    public required Dictionary<string, string> Label { get; set; }

    [JsonPropertyName("note")]
    public Dictionary<string, List<string>>? Note { get; set; }

    [JsonPropertyName("unit")]
    public Dictionary<string, Unit>? Unit { get; set; }
}
