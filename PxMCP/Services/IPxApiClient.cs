using PxMCP.Models.PxApi;

namespace PxMCP.Services;

/// <summary>
/// Interface for communicating with the PX statistical data API.
/// </summary>
public interface IPxApiClient
{
    /// <summary>
    /// Lists all available databases.
    /// </summary>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of database entries.</returns>
    Task<List<DatabaseListingItem>> GetDatabasesAsync(string? lang, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tables in a database with pagination.
    /// </summary>
    /// <param name="database">Database identifier.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of tables.</returns>
    Task<PagedTableList> GetTablesAsync(string database, string? lang, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for tables across all databases.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="scope">Optional search scope filter.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with pagination info.</returns>
    Task<PxApiSearchResponse> SearchGlobalAsync(string query, string? scope, string? lang, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for tables within a specific database.
    /// </summary>
    /// <param name="database">Database identifier to scope the search.</param>
    /// <param name="query">Search query string.</param>
    /// <param name="scope">Optional search scope filter.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with pagination info.</returns>
    Task<PxApiSearchResponse> SearchDatabaseAsync(string database, string query, string? scope, string? lang, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full metadata for a table in JSON-stat 2.0 format.
    /// </summary>
    /// <param name="database">Database identifier.</param>
    /// <param name="table">Table identifier.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The table metadata as a JSON-stat 2.0 dataset.</returns>
    Task<JsonStat2> GetTableMetadataAsync(string database, string table, string? lang, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current positive X-Max-Cells limit from the data endpoint with HEAD, without fetching data.
    /// Throws an HTTP request exception if the request fails or the header is missing or invalid.
    /// </summary>
    /// <param name="database">Database identifier.</param>
    /// <param name="table">Table identifier.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current maximum number of cells a data query may select.</returns>
    Task<long> GetMaxCellsAsync(string database, string table, string? lang, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a data query with optional filters and returns the result.
    /// </summary>
    /// <param name="database">Database identifier.</param>
    /// <param name="table">Table identifier.</param>
    /// <param name="filters">Optional dimension filters keyed by dimension code.</param>
    /// <param name="lang">Optional ISO 639-1 language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query result as a JSON-stat 2.0 dataset.</returns>
    Task<JsonStat2> PostDataAsync(string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken cancellationToken = default);
}
