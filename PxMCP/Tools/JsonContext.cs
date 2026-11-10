using System.Text.Json.Serialization;
using PxMCP.Models.PxApi;

namespace PxMCP.Tools;

/// <summary>
/// Source-generated JSON serializer context for AOT-compatible serialization.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, Filter>))]
[JsonSerializable(typeof(CodeFilter))]
[JsonSerializable(typeof(FromFilter))]
[JsonSerializable(typeof(ToFilter))]
[JsonSerializable(typeof(FirstFilter))]
[JsonSerializable(typeof(LastFilter))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class JsonContext : JsonSerializerContext;
