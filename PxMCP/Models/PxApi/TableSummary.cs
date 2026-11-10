using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Contains essential summary information about a statistical table, including
/// its identification, metrics, time range, dimension structure, and update timestamp.
/// </summary>
public class TableSummary
{
    [JsonPropertyName("tableId")]
    public required string TableId { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("metrics")]
    public required List<MetricInfo> Metrics { get; set; }

    [JsonPropertyName("timeRange")]
    public required TimeRange TimeRange { get; set; }

    [JsonPropertyName("dimensions")]
    public required List<DimensionInfo> Dimensions { get; set; }

    [JsonPropertyName("lastUpdated")]
    public required DateTime LastUpdated { get; set; }
}
