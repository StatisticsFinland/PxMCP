using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a metric with its name and unit of measurement.
/// </summary>
public class MetricInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("unit")]
    public required string Unit { get; set; }
}
