using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Metadata;

/// <summary>
/// A sample value from a dimension category: code and its human-readable label.
/// </summary>
public class SampleValue
{
    [JsonPropertyName("code")]
    public required string Code { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }
}
