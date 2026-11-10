using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Metadata;

/// <summary>
/// Structural overview of a table for query construction: dimensions, roles, and sample values.
/// </summary>
public class HeadMetadataResult
{
    [JsonPropertyName("tableId")]
    public required string TableId { get; set; }

    [JsonPropertyName("dimensions")]
    public required List<DimensionOverview> Dimensions { get; set; }
}
