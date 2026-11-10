using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Search;
using PxMCP.Services;

namespace PxMCP.Tools;

/// <summary>
/// MCP tools for searching statistical tables.
/// </summary>
[McpServerToolType]
public class SearchTools(IPxApiClient client)
{
    [McpServerTool(Name = "search", UseStructuredContent = true, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
        "Searches for statistical tables matching a query. " +
        "Scope: omit database for global search, provide database for database-scoped search. " +
        "Returns results with table summaries (metrics measured with units, dimension names and value counts, time ranges) " +
        "so you can judge table relevance before calling metadata tools. " +
        "After finding a table, call get_head_metadata to get exact dimension IDs and sample codes for filter construction.")]
    public async Task<SearchResult> Search(
        [Description("The search query string (1-400 characters).")] string query,
        [Description("Optional database identifier to scope the search to a specific database.")] string? database = null,
        [Description("Optional search scope controlling which fields are searched: 'content', 'dimension', 'value', 'geo', or 'all' (case-insensitive). When omitted, searches default content fields (title, source, note, content variable, used-for).")] string? scope = null,
        [Description("Optional ISO 639-1 language code.")] string? lang = null,
        [Description("Page number (1-based). Default: 1.")] int page = 1,
        [Description("Results per page (1-100). Default: 20.")] int pageSize = 20,
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
            PxApiSearchResponse response = database is not null
                ? await client.SearchDatabaseAsync(database, query, scope, lang, page, pageSize, cancellationToken)
                : await client.SearchGlobalAsync(query, scope, lang, page, pageSize, cancellationToken);

            return ToSearchResult(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or PxApiArgumentException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    private static SearchResult ToSearchResult(PxApiSearchResponse response)
    {
        List<SearchResultItem> items = response.Results.Select(r => new SearchResultItem
        {
            Score = r.Score,
            Database = r.Database,
            Table = r.Table
        }).ToList();

        return new SearchResult
        {
            Results = items,
            PagingInfo = response.PagingInfo
        };
    }
}
