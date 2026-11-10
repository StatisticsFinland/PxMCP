using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents summary information about a dimension: its name and the number of values it contains.
/// </summary>
public class DimensionInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("size")]
    public required int Size { get; set; }
}
