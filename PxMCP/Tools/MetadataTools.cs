using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Metadata;
using PxMCP.Services;

namespace PxMCP.Tools;

/// <summary>
/// MCP tools for inspecting table metadata, structure, and dimension values.
/// </summary>
[McpServerToolType]
public class MetadataTools(IPxApiClient client)
{
    private const int MaxSampleValues = 10;

    [McpServerTool(Name = "get_head_metadata", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Returns a structural overview of a table: exact dimension IDs, labels, roles, value counts, and sample codes. " +
        "Use after search/list_tables and before constructing filters. " +
        "sampleCodes contains valid codes that may be used directly; if the needed value is not included, " +
        "call get_dimension_values or get_table_metadata for the full list.")]
    public async Task<HeadMetadataResult> GetHeadMetadata(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Table identifier (e.g. '11re').")] string table,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JsonStat2 metadata = await client.GetTableMetadataAsync(database, table, lang, cancellationToken);
            return BuildHeadMetadata(table, metadata);
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "get_table_metadata", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Returns the full metadata of a table in JSON-stat 2.0 format, including all dimension codes, labels, units, and notes. " +
        "Use when you need complete dimension details to construct data queries with precise filter values. " +
        "Use get_head_metadata first unless full raw JSON-stat metadata is needed.")]
    public async Task<JsonStat2> GetTableMetadata(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Table identifier (e.g. '11re').")] string table,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetTableMetadataAsync(database, table, lang, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "get_dimension_values", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Returns all codes and labels for a specific dimension in a table. " +
        "Use this to discover valid filter values before querying data. " +
        "The dimension argument must be an exact dimension ID returned by get_head_metadata or get_table_metadata.")]
    public async Task<DimensionValuesResult> GetDimensionValues(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Table identifier (e.g. '11re').")] string table,
        [Description("Dimension code (e.g. 'timeperiod_y', 'alue_23_20260101'). Use get_head_metadata to find available dimension codes.")] string dimension,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JsonStat2 metadata = await client.GetTableMetadataAsync(database, table, lang, cancellationToken);

            if (!metadata.Dimension.TryGetValue(dimension, out Dimension? dim))
            {
                string available = string.Join(", ", metadata.Dimension.Keys);
                throw new McpException($"Dimension '{dimension}' not found. Available dimensions: {available}");
            }

            DimensionValuesResult result = new()
            {
                Dimension = dimension,
                Label = dim.Label,
                ValueCount = dim.Category.Index.Count,
                Values = dim.Category.Label
            };

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    internal static HeadMetadataResult BuildHeadMetadata(string tableId, JsonStat2 metadata)
    {
        Dictionary<string, DimensionRole> roleMap = BuildRoleMap(metadata.Role);

        List<DimensionOverview> dimensions = [];

        foreach (string dimCode in metadata.Id)
        {
            Dimension dim = metadata.Dimension[dimCode];
            DimensionRole role = roleMap.GetValueOrDefault(dimCode, DimensionRole.Other);

            List<SampleValue> sampleCodes = dim.Category.Index
                .Take(MaxSampleValues)
                .Select(code => new SampleValue
                {
                    Code = code,
                    Label = dim.Category.Label[code]
                })
                .ToList();

            dimensions.Add(new DimensionOverview
            {
                Id = dimCode,
                Label = dim.Label,
                Role = role,
                ValueCount = dim.Category.Index.Count,
                SampleCodes = sampleCodes
            });
        }

        return new HeadMetadataResult
        {
            TableId = tableId,
            Dimensions = dimensions
        };
    }

    private static Dictionary<string, DimensionRole> BuildRoleMap(Dictionary<string, List<string>>? roles)
    {
        Dictionary<string, DimensionRole> map = [];
        if (roles is null)
        {
            return map;
        }

        foreach ((string roleName, List<string> dimCodes) in roles)
        {
            DimensionRole normalizedRole = roleName.ToLowerInvariant() switch
            {
                "time" => DimensionRole.Time,
                "geo" or "geography" => DimensionRole.Geo,
                "metric" or "content" => DimensionRole.Metric,
                _ => DimensionRole.Other
            };

            foreach (string dimCode in dimCodes)
            {
                map[dimCode] = normalizedRole;
            }
        }

        return map;
    }
}
