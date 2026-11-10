using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Represents an API link with href, relation, and HTTP method.
/// </summary>
public class Link
{
    [JsonPropertyName("href")]
    public required string Href { get; set; }

    [JsonPropertyName("rel")]
    public required string Rel { get; set; }

    [JsonPropertyName("method")]
    public required string Method { get; set; }
}
