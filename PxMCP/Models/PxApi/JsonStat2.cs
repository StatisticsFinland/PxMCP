using System.Text.Json;
using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents a JSON-stat 2.0 dataset with dimensions, values, and metadata.
/// </summary>
public class JsonStat2
{
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("class")]
    public required string Class { get; set; }

    [JsonPropertyName("id")]
    public required List<string> Id { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("note")]
    public List<string>? Note { get; set; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Source { get; set; }

    [JsonPropertyName("updated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Updated { get; set; }

    [JsonPropertyName("dimension")]
    public required Dictionary<string, Dimension> Dimension { get; set; }

    [JsonPropertyName("value")]
    public required List<double?> Value { get; set; }

    [JsonPropertyName("status")]
    public Dictionary<string, string>? Status { get; set; }

    [JsonPropertyName("extension")]
    public Dictionary<string, JsonElement>? Extension { get; set; }

    [JsonPropertyName("size")]
    public required List<int> Size { get; set; }

    [JsonPropertyName("role")]
    public Dictionary<string, List<string>>? Role { get; set; }
}
