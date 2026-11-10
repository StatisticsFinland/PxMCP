using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PxMCP.Models.PxApi;
using PxMCP.Services;

namespace PxMCP.Tools;

/// <summary>
/// MCP tools for listing tables within a statistical database.
/// </summary>
[McpServerToolType]
public class TableTools(IPxApiClient client)
{
    [McpServerTool(Name = "list_tables", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Lists tables in a database with pagination. Returns table summaries including code, name, " +
        "metric values with units, time range, dimension names and sizes, and last updated date. " +
        "After selecting a table, call get_head_metadata before constructing filters.")]
    public async Task<PagedTableList> ListTables(
        [Description("Database identifier (e.g. 'StatFin').")] string database,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        [Description("Page number (1-based). Default: 1.")] int page = 1,
        [Description("Items per page (1-100). Default: 20.")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new McpException("Page must be greater than or equal to 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new McpException("Page size must be between 1 and 100.");
        }

        try
        {
            return await client.GetTablesAsync(database, lang, page, pageSize, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
