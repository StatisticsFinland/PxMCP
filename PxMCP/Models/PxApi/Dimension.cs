using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a statistical dimension with its label, notes, and category values.
/// </summary>
public class Dimension
{
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("note")]
    public List<string>? Note { get; set; }

    [JsonPropertyName("category")]
    public required Category Category { get; set; }
}
