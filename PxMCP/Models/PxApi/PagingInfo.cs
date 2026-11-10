using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Pagination metadata for list and search responses.
/// </summary>
public class PagingInfo
{
    [JsonPropertyName("currentPage")]
    public required int CurrentPage { get; set; }

    [JsonPropertyName("pageSize")]
    public required int PageSize { get; set; }

    [JsonPropertyName("totalItems")]
    public required int TotalItems { get; set; }
}
