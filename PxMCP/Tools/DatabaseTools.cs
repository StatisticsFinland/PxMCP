using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Database;
using PxMCP.Services;

namespace PxMCP.Tools;

/// <summary>
/// MCP tools for discovering available statistical databases.
/// </summary>
[McpServerToolType]
public class DatabaseTools(IPxApiClient client)
{
    [McpServerTool(Name = "list_databases", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Lists all available statistical databases with their identifiers, names, descriptions, table counts, and available languages. " +
        "Use this as the first step to discover what databases are available. " +
        "Typical workflow: list_databases → search/list_tables → get_head_metadata → get_dimension_values/get_table_metadata → validate_query → get_data.")]
    public async Task<DatabaseListResult> ListDatabases(
        [Description("Optional ISO 639-1 language code (e.g. 'en', 'fi', 'sv'). Defaults to the server's default language.")] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<DatabaseListingItem> databases = await client.GetDatabasesAsync(lang, cancellationToken);
            return new DatabaseListResult { Databases = databases };
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
