using System.Diagnostics.CodeAnalysis;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Data;

namespace PxMCP.Services;

/// <summary>
/// Validates data query filters and conservatively counts explicit selections against a supplied size limit.
/// </summary>
public static class QueryValidator
{
    /// <summary>
    /// Validates all filters against the table metadata and returns a result with any issues found.
    /// Wildcard patterns are not expanded or checked for matches, and selected cell counts
    /// and upstream size limits are not checked.
    /// </summary>
    public static ValidationResult Validate(JsonStat2 metadata, Dictionary<string, Filter>? filters)
    {
        List<ValidationIssue> issues = [];

        if (filters is null or { Count: 0 })
        {
            return new ValidationResult { IsValid = true, Issues = issues };
        }

        foreach ((string dimensionCode, Filter filter) in filters)
        {
            if (filter is null)
            {
                issues.Add(new ValidationIssue
                {
                    Dimension = dimensionCode,
                    Message = $"Filter for dimension '{dimensionCode}' must be an object.",
                    Severity = "error"
                });
                continue;
            }

            if (!metadata.Dimension.TryGetValue(dimensionCode, out Dimension? dimension))
            {
                string available = string.Join(", ", metadata.Dimension.Keys);
                issues.Add(new ValidationIssue
                {
                    Dimension = dimensionCode,
                    Message = $"Unknown dimension '{dimensionCode}'. Available dimensions: {available}",
                    Severity = "error"
                });
                continue;
            }

            ValidateFilter(dimensionCode, filter, dimension, issues);
        }

        bool hasErrors = issues.Exists(i => i.Severity == "error");
        return new ValidationResult { IsValid = !hasErrors, Issues = issues };
    }

    /// <summary>
    /// Adds a conservative size check without changing filters or fetching data.
    /// Only complete, explicit, unambiguous selections receive an exact count.
    /// </summary>
    public static void CheckSize(JsonStat2 metadata, Dictionary<string, Filter>? filters, ValidationResult result)
    {
        if (!result.IsValid)
        {
            return;
        }

        result.SelectedCellCount = CountSelectedCells(metadata, filters, out string? uncertainty);
        if (uncertainty is not null)
        {
            result.Issues.Add(new ValidationIssue { Severity = "warning", Message = uncertainty });
        }

        if (result.SelectedCellCount is long cells && result.MaxCells is long limit)
        {
            result.ExceedsMaxCells = cells > limit;
            if (cells > limit)
            {
                result.IsValid = false;
                result.Issues.Add(new ValidationIssue
                {
                    Severity = "error",
                    Message = $"The selected cell count ({cells}) exceeds the current PX API maximum ({limit}). Narrow the selection."
                });
            }
        }
    }

    internal static void ValidateFilter(string dimensionCode, Filter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        switch (filter)
        {
            case CodeFilter codeFilter:
                ValidateCodeFilter(dimensionCode, codeFilter, dimension, issues);
                break;
            case FromFilter fromFilter:
                ValidateFromFilter(dimensionCode, fromFilter, dimension, issues);
                break;
            case ToFilter toFilter:
                ValidateToFilter(dimensionCode, toFilter, dimension, issues);
                break;
            case FirstFilter firstFilter:
                ValidateFirstFilter(dimensionCode, firstFilter, dimension, issues);
                break;
            case LastFilter lastFilter:
                ValidateLastFilter(dimensionCode, lastFilter, dimension, issues);
                break;
        }
    }

    internal static void ValidateCodeFilter(string dimensionCode, CodeFilter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        if (filter.Query is null)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"Code filter query for dimension '{dimensionCode}' must be an array of codes.",
                Severity = "error"
            });
            return;
        }

        HashSet<string> validCodes = [.. dimension.Category.Index];

        foreach (string? code in filter.Query)
        {
            if (code is null)
            {
                issues.Add(new ValidationIssue
                {
                    Dimension = dimensionCode,
                    Message = $"Code filter values for dimension '{dimensionCode}' must be strings.",
                    Severity = "error"
                });
                continue;
            }

            if (!code.Contains('*') && !validCodes.Contains(code))
            {
                issues.Add(new ValidationIssue
                {
                    Dimension = dimensionCode,
                    Message = $"Invalid code '{code}' for dimension '{dimensionCode}'.",
                    Severity = "error"
                });
            }
        }
    }

    internal static void ValidateFromFilter(string dimensionCode, FromFilter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        if (filter.Query is null)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"From boundary for dimension '{dimensionCode}' must be a string.",
                Severity = "error"
            });
            return;
        }

        if (!filter.Query.Contains('*') && !dimension.Category.Index.Contains(filter.Query))
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"From boundary '{filter.Query}' not found in dimension '{dimensionCode}'.",
                Severity = "error"
            });
        }
    }

    internal static void ValidateToFilter(string dimensionCode, ToFilter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        if (filter.Query is null)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"To boundary for dimension '{dimensionCode}' must be a string.",
                Severity = "error"
            });
            return;
        }

        if (!filter.Query.Contains('*') && !dimension.Category.Index.Contains(filter.Query))
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"To boundary '{filter.Query}' not found in dimension '{dimensionCode}'.",
                Severity = "error"
            });
        }
    }

    internal static void ValidateFirstFilter(string dimensionCode, FirstFilter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        if (filter.Query <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"First count must be positive, got {filter.Query}.",
                Severity = "error"
            });
        }
        else if (filter.Query > dimension.Category.Index.Count)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"First count {filter.Query} exceeds available values ({dimension.Category.Index.Count}) in dimension '{dimensionCode}'.",
                Severity = "warning"
            });
        }
    }

    internal static void ValidateLastFilter(string dimensionCode, LastFilter filter, Dimension dimension, List<ValidationIssue> issues)
    {
        if (filter.Query <= 0)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"Last count must be positive, got {filter.Query}.",
                Severity = "error"
            });
        }
        else if (filter.Query > dimension.Category.Index.Count)
        {
            issues.Add(new ValidationIssue
            {
                Dimension = dimensionCode,
                Message = $"Last count {filter.Query} exceeds available values ({dimension.Category.Index.Count}) in dimension '{dimensionCode}'.",
                Severity = "warning"
            });
        }
    }

    private static long? CountSelectedCells(JsonStat2 metadata, Dictionary<string, Filter>? filters, out string? uncertainty)
    {
        uncertainty = "Selected cell count is unknown: provide an explicit, unambiguous filter for every dimension. " +
            "Default selections, wildcard filters, empty or duplicate code selections, and oversized positional counts are not estimated.";
        if (filters is null || metadata.Id.Count == 0 ||
            metadata.Id.Distinct(StringComparer.Ordinal).Count() != metadata.Id.Count ||
            metadata.Id.Count != metadata.Dimension.Count)
        {
            return null;
        }

        long cells = 1;
        foreach (string dimensionId in metadata.Id)
        {
            if (!metadata.Dimension.TryGetValue(dimensionId, out Dimension? dimension) ||
                !filters.TryGetValue(dimensionId, out Filter? filter))
            {
                return null;
            }

            List<string> codes = dimension.Category.Index;
            int? count = filter switch
            {
                CodeFilter code when IsExactCodeSelection(code.Query, codes) => code.Query.Count,
                FromFilter from when from.Query is not null && !from.Query.Contains('*') &&
                    codes.IndexOf(from.Query) is int index && index >= 0 => codes.Count - index,
                ToFilter to when to.Query is not null && !to.Query.Contains('*') &&
                    codes.IndexOf(to.Query) is int index && index >= 0 => index + 1,
                FirstFilter first when first.Query > 0 && first.Query <= codes.Count => first.Query,
                LastFilter last when last.Query > 0 && last.Query <= codes.Count => last.Query,
                _ => null
            };
            if (count is null || codes.Count == 0)
            {
                return null;
            }

            if (cells > long.MaxValue / count.Value)
            {
                uncertainty = "Selected cell count is unknown: the exact count exceeds the supported 64-bit integer range.";
                return null;
            }

            cells *= count.Value;
        }

        uncertainty = null;
        return cells;
    }

    /// <summary>
    /// Returns whether <paramref name="query"/> is a non-empty list of unique, literal codes that all exist in
    /// <paramref name="codes"/>. Uses hash lookups so large selections stay linear in their size.
    /// </summary>
    private static bool IsExactCodeSelection([NotNullWhen(true)] List<string>? query, List<string> codes)
    {
        if (query is not { Count: > 0 })
        {
            return false;
        }

        HashSet<string> validCodes = new(codes, StringComparer.Ordinal);
        HashSet<string> selectedCodes = new(StringComparer.Ordinal);
        foreach (string? value in query)
        {
            if (value is null || value.Contains('*') || !validCodes.Contains(value) || !selectedCodes.Add(value))
            {
                return false;
            }
        }

        return true;
    }
}
