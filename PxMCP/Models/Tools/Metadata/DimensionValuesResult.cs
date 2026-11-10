using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Metadata;

/// <summary>
/// Result containing all codes and labels for a specific dimension.
/// </summary>
public class DimensionValuesResult
{
    [JsonPropertyName("dimension")]
    public required string Dimension { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("valueCount")]
    public required int ValueCount { get; set; }

    [JsonPropertyName("values")]
    public required Dictionary<string, string> Values { get; set; }
}
