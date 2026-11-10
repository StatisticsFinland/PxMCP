using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PxMCP.Configuration;
using PxMCP.Models.PxApi;

namespace PxMCP.Services;

/// <summary>
/// Typed HTTP client that wraps the PX statistical data API.
/// Each request, including reading its response body, is limited to <see cref="PxApiOptions.TimeoutSeconds"/>,
/// or <see cref="PxApiOptions.SearchTimeoutSeconds"/> for search requests.
/// </summary>
public class PxApiClient(HttpClient httpClient, PxApiOptions options) : IPxApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    private readonly TimeSpan _searchTimeout = TimeSpan.FromSeconds(options.SearchTimeoutSeconds);

    /// <inheritdoc />
    public async Task<List<DatabaseListingItem>> GetDatabasesAsync(string? lang, CancellationToken cancellationToken = default)
    {
        PxApiArgumentValidator.EnsureValidLanguage(lang);
        string url = BuildUrl("/meta/databases", lang: lang);
        return await GetAsync<List<DatabaseListingItem>>(url, _requestTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedTableList> GetTablesAsync(string database, string? lang, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidateRouteArguments(database, table: null, lang);
        string url = BuildUrl($"/meta/databases/{Uri.EscapeDataString(database)}/tables", lang: lang, ("page", page.ToString()), ("pageSize", pageSize.ToString()));
        return await GetAsync<PagedTableList>(url, _requestTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PxApiSearchResponse> SearchGlobalAsync(string query, string? scope, string? lang, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        PxApiArgumentValidator.EnsureValidLanguage(lang);
        PxApiArgumentValidator.EnsureValidSearchQuery(query);
        string url = BuildSearchUrl("/meta/search", query, scope, lang, page, pageSize);
        return await GetAsync<PxApiSearchResponse>(url, _searchTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PxApiSearchResponse> SearchDatabaseAsync(string database, string query, string? scope, string? lang, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ValidateRouteArguments(database, table: null, lang);
        PxApiArgumentValidator.EnsureValidSearchQuery(query);
        string url = BuildSearchUrl($"/meta/databases/{Uri.EscapeDataString(database)}/search", query, scope, lang, page, pageSize);
        return await GetAsync<PxApiSearchResponse>(url, _searchTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JsonStat2> GetTableMetadataAsync(string database, string table, string? lang, CancellationToken cancellationToken = default)
    {
        ValidateRouteArguments(database, table, lang);
        string url = BuildUrl($"/meta/databases/{Uri.EscapeDataString(database)}/tables/{Uri.EscapeDataString(table)}", lang: lang);
        return await GetAsync<JsonStat2>(url, _requestTimeout, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetMaxCellsAsync(string database, string table, string? lang, CancellationToken cancellationToken = default)
    {
        ValidateRouteArguments(database, table, lang);
        string url = BuildUrl($"/data/databases/{Uri.EscapeDataString(database)}/tables/{Uri.EscapeDataString(table)}", lang: lang);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Head, url);
        using HttpResponseMessage response = await SendAsync(request, _requestTimeout, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ReadMaxCells(response)
            ?? throw new HttpRequestException("PX API size limit is unavailable: X-Max-Cells is missing or invalid.");
    }

    /// <inheritdoc />
    public async Task<JsonStat2> PostDataAsync(string database, string table, Dictionary<string, Filter>? filters, string? lang, CancellationToken cancellationToken = default)
    {
        ValidateRouteArguments(database, table, lang);
        string url = BuildUrl($"/data/databases/{Uri.EscapeDataString(database)}/tables/{Uri.EscapeDataString(table)}", lang: lang);

        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, url);
        request.Headers.Accept.ParseAdd("application/json");

        Dictionary<string, Filter> body = filters ?? [];
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using HttpResponseMessage response = await SendAsync(request, _requestTimeout, cancellationToken);
        await EnsureSuccessAsync(
            response,
            cancellationToken,
            "The query exceeds PX API's maximum allowed cell count. Narrow the selection or apply filters.");

        return await ReadJsonAsync<JsonStat2>(response.Content, cancellationToken);
    }

    private static void ValidateRouteArguments(string database, string? table, string? lang)
    {
        PxApiArgumentValidator.EnsureValidDatabase(database);
        if (table is not null)
        {
            PxApiArgumentValidator.EnsureValidTable(table);
        }

        PxApiArgumentValidator.EnsureValidLanguage(lang);
    }

    /// <summary>
    /// Creates a request that uses the client's configured HTTP version and version policy.
    /// <see cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/> does not apply
    /// <see cref="HttpClient.DefaultRequestVersion"/> to caller-constructed messages, which default to HTTP/1.1.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string url) => new(method, url)
    {
        Version = httpClient.DefaultRequestVersion,
        VersionPolicy = httpClient.DefaultVersionPolicy
    };

    private async Task<T> GetAsync<T>(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, url);
        using HttpResponseMessage response = await SendAsync(request, timeout, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadJsonAsync<T>(response.Content, cancellationToken);
    }

    /// <summary>
    /// Sends the request and buffers the complete response body within <paramref name="timeout"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await httpClient.SendAsync(request, timeoutSource.Token);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested &&
            (timeoutSource.IsCancellationRequested || exception.InnerException is TimeoutException))
        {
            throw new HttpRequestException(
                "PX API request timed out. Try again later or narrow the query.", exception);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            throw new HttpRequestException(
                exception.HttpRequestError,
                "PX API response exceeded the maximum supported size. Narrow the query or apply filters.",
                exception,
                exception.StatusCode);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException(
                exception.HttpRequestError,
                "PX API request failed during transport. Try again later.",
                exception,
                exception.StatusCode);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            T? result = await content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result is null
                ? throw new JsonException("PX API returned a null JSON response.")
                : result;
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("PX API returned an invalid JSON response.", exception);
        }
    }

    private static Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? oversizedResponseMessage = null)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException(
                "PX API returned 206 Partial Content. The response is incomplete and cannot be returned as a complete result.",
                inner: null,
                response.StatusCode);
        }

        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        string message = $"PX API returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge && oversizedResponseMessage is not null)
        {
            message = $"{message} {oversizedResponseMessage}";
            if (ReadMaxCells(response) is long maxCells)
            {
                message = $"{message} Maximum allowed cells: {maxCells.ToString(CultureInfo.InvariantCulture)}.";
            }
        }

        throw new HttpRequestException(
            message,
            inner: null,
            response.StatusCode);
    }

    private static long? ReadMaxCells(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Max-Cells", out IEnumerable<string>? values))
        {
            return null;
        }

        string[] entries = values.ToArray();
        return entries.Length == 1 &&
            long.TryParse(entries[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long limit) &&
            limit > 0
                ? limit
                : null;
    }

    private static string BuildUrl(string path, string? lang = null, params (string key, string value)[] queryParams)
    {
        string relativePath = path.TrimStart('/');
        List<string> parts = [];

        if (!string.IsNullOrEmpty(lang))
            parts.Add($"lang={Uri.EscapeDataString(lang)}");

        foreach ((string key, string value) in queryParams)
        {
            if (!string.IsNullOrEmpty(value))
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        return parts.Count > 0 ? $"{relativePath}?{string.Join("&", parts)}" : relativePath;
    }

    private static string BuildSearchUrl(string path, string query, string? scope, string? lang, int page, int pageSize)
    {
        return BuildUrl(path, lang: lang,
            ("q", query),
            ("scope", scope ?? ""),
            ("page", page.ToString()),
            ("pageSize", pageSize.ToString()));
    }
}
