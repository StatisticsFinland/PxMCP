using System.Text.Json.Serialization;

namespace PxMCP.Models.PxApi;

/// <summary>
/// Base class for data query filters. Discriminated by the "type" property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CodeFilter), "Code")]
[JsonDerivedType(typeof(FromFilter), "From")]
[JsonDerivedType(typeof(ToFilter), "To")]
[JsonDerivedType(typeof(FirstFilter), "First")]
[JsonDerivedType(typeof(LastFilter), "Last")]
public abstract class Filter;

/// <summary>
/// Filter that selects specific dimension values by their codes.
/// Example: {"type":"Code","query":["SSS","KU049"]}
/// </summary>
public class CodeFilter : Filter
{
    [JsonPropertyName("query")]
    public required List<string> Query { get; set; }
}

/// <summary>
/// Filter that selects values starting from a boundary value (inclusive).
/// Example: {"type":"From","query":"2020"}
/// </summary>
public class FromFilter : Filter
{
    [JsonPropertyName("query")]
    public required string Query { get; set; }
}

/// <summary>
/// Filter that selects values up to a boundary value (inclusive).
/// Example: {"type":"To","query":"2023"}
/// </summary>
public class ToFilter : Filter
{
    [JsonPropertyName("query")]
    public required string Query { get; set; }
}

/// <summary>
/// Filter that selects the first N values in dimension order.
/// Example: {"type":"First","query":5}
/// </summary>
public class FirstFilter : Filter
{
    [JsonPropertyName("query")]
    public required int Query { get; set; }
}

/// <summary>
/// Filter that selects the last N values in dimension order.
/// Example: {"type":"Last","query":5}
/// </summary>
public class LastFilter : Filter
{
    [JsonPropertyName("query")]
    public required int Query { get; set; }
}
