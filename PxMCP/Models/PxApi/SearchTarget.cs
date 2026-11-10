using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Specifies which fields the search engine should match against.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchTarget>))]
public enum SearchTarget
{
    Content,
    Dimension,
    Value,
    Geo,
    All
}
