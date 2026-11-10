using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Data;
using PxMCP.Services;

namespace PxMCP.Tools;

/// <summary>
/// MCP tools for querying statistical data.
/// </summary>
[McpServerToolType]
public class DataTools(IPxApiClient client)
{
    [McpServerTool(Name = "get_data", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Queries statistical data from a table with optional filters. Returns data in JSON-stat 2.0 format. " +
        "Without filters, returns the table's default data selection. " +
        "Filters map dimension codes to filter objects. " +
        "Filter types: Code (specific values or '*' wildcard patterns), From/To (inclusive range boundaries; '*' wildcard supported), First/Last (positional). " +
        "Example filters: {\"timeperiod_y\":{\"type\":\"From\",\"query\":\"2020\"},\"alue_23_20260101\":{\"type\":\"Code\",\"query\":[\"SSS\"]},\"contentscode\":{\"type\":\"Code\",\"query\":[\"vaerak-vaesto\"]}}. " +
        "Before calling get_data, use get_head_metadata or get_table_metadata to identify dimension IDs, " +
        "and get_dimension_values when selecting codes. Do not construct filters from search/list_tables summaries.")]
    public async Task<JsonStat2> GetData(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Table identifier (e.g. '11re').")] string table,
        [Description("Optional filters keyed by dimension code. " +
            "Each filter has 'type' (one of: Code, From, To, First, Last) and 'query'. " +
            "Code: query is an array of value codes or wildcard patterns ('*' matches zero or more characters), e.g. {\"type\":\"Code\",\"query\":[\"SSS\",\"KU049\"]}. " +
            "From/To: query is a single inclusive string boundary; '*' wildcard is supported, e.g. {\"type\":\"From\",\"query\":\"2020\"}. " +
            "First/Last: query is a positive integer count, e.g. {\"type\":\"Last\",\"query\":5}. " +
            "Omit for default data selection.")] Dictionary<string, Filter>? filters = null,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (filters is { Count: > 0 })
            {
                JsonStat2 metadata = await client.GetTableMetadataAsync(database, table, lang, cancellationToken);
                ValidationResult validation = QueryValidator.Validate(metadata, filters);
                if (!validation.IsValid)
                {
                    string issues = string.Join("; ", validation.Issues
                        .Where(i => i.Severity == "error")
                        .Select(i => i.Message));
                    throw new McpException($"Query validation failed. {issues}");
                }
            }

            return await client.PostDataAsync(database, table, filters, lang, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "validate_query", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Validates a data query's filters against table metadata without executing the query. " +
        "Returns filter issues, selectedCellCount, maxCells from a data HEAD request, and exceedsMaxCells. " +
        "Counts are exact only for complete explicit selections; defaults and wildcard patterns are not estimated. " +
        "Unknown counts or unavailable limits are null with warnings; a valid result does not guarantee get_data will succeed. " +
        "Use this to check a filter plan before executing it, or to diagnose why a get_data call returned a validation error.")]
    public async Task<ValidationResult> ValidateQuery(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Table identifier (e.g. '11re').")] string table,
        [Description("Optional filters keyed by dimension code. " +
            "Each filter has 'type' (one of: Code, From, To, First, Last) and 'query'. " +
            "Code: query is an array of value codes or wildcard patterns ('*' matches zero or more characters), e.g. {\"type\":\"Code\",\"query\":[\"SSS\",\"KU049\"]}. " +
            "From/To: query is a single inclusive string boundary; '*' wildcard is supported, e.g. {\"type\":\"From\",\"query\":\"2020\"}. " +
            "First/Last: query is a positive integer count, e.g. {\"type\":\"Last\",\"query\":5}. " +
            "Omit for default data selection.")] Dictionary<string, Filter>? filters = null,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JsonStat2 metadata = await client.GetTableMetadataAsync(database, table, lang, cancellationToken);
            ValidationResult result = QueryValidator.Validate(metadata, filters);
            if (!result.IsValid)
            {
                return result;
            }

            try
            {
                result.MaxCells = await client.GetMaxCellsAsync(database, table, lang, cancellationToken);
            }
            catch (HttpRequestException)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Severity = "warning",
                    Message = "Size limit check unavailable: the PX API HEAD request failed or X-Max-Cells was missing or invalid. " +
                        "Filter validation is available, but query size has not been confirmed."
                });
            }

            QueryValidator.CheckSize(metadata, filters, result);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
