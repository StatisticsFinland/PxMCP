using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a measurement unit for a metric dimension value.
/// </summary>
public class Unit
{
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("decimals")]
    public required int Decimals { get; set; }
}
