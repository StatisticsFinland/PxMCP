using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Metadata;

/// <summary>
/// Overview of a single dimension including its role and sample codes.
/// </summary>
public class DimensionOverview
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("role")]
    public required DimensionRole Role { get; set; }

    [JsonPropertyName("valueCount")]
    public required int ValueCount { get; set; }

    [JsonPropertyName("sampleCodes")]
    public required List<SampleValue> SampleCodes { get; set; }
}
