using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Response from the PX API search endpoint.
/// </summary>
public class PxApiSearchResponse
{
    [JsonPropertyName("query")]
    public required SearchQueryInfo Query { get; set; }

    [JsonPropertyName("results")]
    public required List<PxApiSearchResultItem> Results { get; set; }

    [JsonPropertyName("pagingInfo")]
    public required PagingInfo PagingInfo { get; set; }
}
