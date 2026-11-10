using System.Text.RegularExpressions;

namespace PxMCP.Services;

/// <summary>
/// Validates caller-supplied identifiers and search queries that become PX API URL path segments or query values.
/// Database, table and search query rules mirror PX API's own validation, so valid upstream values are accepted
/// while dot segments, separators, other characters and oversized queries are rejected before network use.
/// </summary>
internal static partial class PxApiArgumentValidator
{
    private const int MaxIdentifierLength = 50;

    /// <summary>
    /// Largest accepted search query length, matching PX API's own search query limit.
    /// </summary>
    internal const int MaxSearchQueryLength = 400;

    internal const string InvalidDatabaseMessage =
        "Invalid database identifier. Database identifiers must be 1-50 letters or digits; " +
        "use an identifier returned by list_databases or search.";

    internal const string InvalidTableMessage =
        "Invalid table identifier. Table identifiers must be 1-50 letters, digits, '_' or '-'; " +
        "use an identifier returned by list_tables or search.";

    internal const string InvalidLanguageMessage =
        "Invalid language code. Use an ISO 639-1 code such as 'en', 'fi' or 'sv', or omit it to use the default language.";

    internal const string InvalidSearchQueryMessage =
        "Invalid search query. The query must not be empty and must be at most 400 characters.";

    /// <summary>
    /// Ensures that <paramref name="database"/> contains only letters or digits and is at most 50 characters.
    /// </summary>
    /// <exception cref="PxApiArgumentException">The identifier is invalid.</exception>
    internal static void EnsureValidDatabase(string? database)
    {
        if (!IsValidIdentifier(database, allowSeparators: false))
        {
            throw new PxApiArgumentException(InvalidDatabaseMessage);
        }
    }

    /// <summary>
    /// Ensures that <paramref name="table"/> contains only letters, digits, '_' or '-' and is at most 50 characters.
    /// </summary>
    /// <exception cref="PxApiArgumentException">The identifier is invalid.</exception>
    internal static void EnsureValidTable(string? table)
    {
        if (!IsValidIdentifier(table, allowSeparators: true))
        {
            throw new PxApiArgumentException(InvalidTableMessage);
        }
    }

    /// <summary>
    /// Ensures that <paramref name="lang"/> is omitted (null or empty) or a short language tag such as 'en' or 'sv-FI'.
    /// </summary>
    /// <exception cref="PxApiArgumentException">The language code is invalid.</exception>
    internal static void EnsureValidLanguage(string? lang)
    {
        if (!string.IsNullOrEmpty(lang) && !LanguagePattern().IsMatch(lang))
        {
            throw new PxApiArgumentException(InvalidLanguageMessage);
        }
    }

    /// <summary>
    /// Ensures that <paramref name="query"/> is not empty or whitespace and is at most <see cref="MaxSearchQueryLength"/>
    /// characters, matching PX API's own search query rules.
    /// </summary>
    /// <exception cref="PxApiArgumentException">The query is invalid.</exception>
    internal static void EnsureValidSearchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxSearchQueryLength)
        {
            throw new PxApiArgumentException(InvalidSearchQueryMessage);
        }
    }

    private static bool IsValidIdentifier(string? value, bool allowSeparators)
    {
        return !string.IsNullOrEmpty(value) &&
            value.Length <= MaxIdentifierLength &&
            value.All(c => char.IsLetterOrDigit(c) || (allowSeparators && c is '_' or '-'));
    }

    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
