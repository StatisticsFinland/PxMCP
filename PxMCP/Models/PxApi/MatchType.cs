using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Type of text match in a search result.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchType>))]
public enum MatchType
{
    Contains,
    Exact
}
