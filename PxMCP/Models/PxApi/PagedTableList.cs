using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// A paginated list of tables within a database.
/// </summary>
public class PagedTableList
{
    [JsonPropertyName("tables")]
    public required List<TableListingItem> Tables { get; set; }

    [JsonPropertyName("pagingInfo")]
    public required PagingInfo PagingInfo { get; set; }
}
