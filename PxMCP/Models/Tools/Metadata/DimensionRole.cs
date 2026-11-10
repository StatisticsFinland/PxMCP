using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Metadata;

/// <summary>
/// The role a dimension plays in the table structure.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DimensionRole>))]
public enum DimensionRole
{
    /// <summary>Time dimension.</summary>
    [JsonStringEnumMemberName("time")]
    Time,

    /// <summary>Geographic dimension.</summary>
    [JsonStringEnumMemberName("geo")]
    Geo,

    /// <summary>Metric/content dimension.</summary>
    [JsonStringEnumMemberName("metric")]
    Metric,

    /// <summary>Other classificatory dimension.</summary>
    [JsonStringEnumMemberName("other")]
    Other
}
