using System.Text.Json.Serialization;

namespace PxMCP.Models.Tools.Data;

/// <summary>
/// A single validation issue found when checking a data query against table metadata.
/// </summary>
public class ValidationIssue
{
    [JsonPropertyName("dimension")]
    public string? Dimension { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("severity")]
    public required string Severity { get; set; }
}
