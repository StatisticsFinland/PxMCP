using System.Text.Json.Serialization;
using PxMCP.Models.PxApi;

namespace PxMCP.Models.Tools.Database;

/// <summary>
/// Wrapper for database listing results to conform to JSON object output schema.
/// </summary>
public class DatabaseListResult
{
    [JsonPropertyName("databases")]
    public required List<DatabaseListingItem> Databases { get; set; }
}
