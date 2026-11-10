using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Data;

/// <summary>
/// Result of validating filters, with optional size preflight information from validate_query.
/// </summary>
public class ValidationResult
{
    [JsonPropertyName("isValid")]
    public required bool IsValid { get; set; }

    [JsonPropertyName("issues")]
    public required List<ValidationIssue> Issues { get; set; }

    /// <summary>
    /// Exact selected cell count, or null when defaults, wildcard semantics or invalid filters make it unknown.
    /// </summary>
    [JsonPropertyName("selectedCellCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? SelectedCellCount { get; set; }

    /// <summary>
    /// Current positive X-Max-Cells limit, or null when the HEAD check is unavailable or not performed.
    /// </summary>
    [JsonPropertyName("maxCells")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? MaxCells { get; set; }

    /// <summary>
    /// Whether the exact count exceeds the limit, or null when either is unknown.
    /// </summary>
    [JsonPropertyName("exceedsMaxCells")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool? ExceedsMaxCells { get; set; }
}
