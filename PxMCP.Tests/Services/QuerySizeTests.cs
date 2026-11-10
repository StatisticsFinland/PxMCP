using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Data;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Tests conservative preflight counts independently of upstream data retrieval.
/// </summary>
[TestFixture]
public sealed class QuerySizeTests
{
    /// <summary>
    /// Explicit code selections and inclusive ranges use metadata order rather than lexical ordering.
    /// </summary>
    [TestCase("Code", 2)]
    [TestCase("From", 3)]
    [TestCase("To", 2)]
    [TestCase("First", 2)]
    [TestCase("Last", 2)]
    public void CheckSize_ExplicitFilters_CountsCartesianProduct(string type, long expected)
    {
        JsonStat2 metadata = CreateMetadata(2);
        Filter filter = type switch
        {
            "Code" => new CodeFilter { Query = ["Z", "C"] },
            "From" => new FromFilter { Query = "A" },
            "To" => new ToFilter { Query = "A" },
            "First" => new FirstFilter { Query = 2 },
            "Last" => new LastFilter { Query = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        Dictionary<string, Filter> filters = new()
        {
            ["d0"] = filter,
            ["d1"] = new CodeFilter { Query = ["Z", "A"] }
        };
        ValidationResult result = QueryValidator.Validate(metadata, filters);
        result.MaxCells = expected * 2;

        QueryValidator.CheckSize(metadata, filters, result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.SelectedCellCount, Is.EqualTo(expected * 2));
            Assert.That(result.ExceedsMaxCells, Is.False);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    /// <summary>
    /// Defaults, wildcard boundaries and ambiguous selections remain unknown rather than apparently safe.
    /// </summary>
    [TestCase("omitted")]
    [TestCase("null")]
    [TestCase("wildcardCode")]
    [TestCase("wildcardFrom")]
    [TestCase("wildcardTo")]
    [TestCase("empty")]
    [TestCase("duplicate")]
    [TestCase("oversizedFirst")]
    [TestCase("oversizedLast")]
    public void CheckSize_UncertainSelections_DoNotClaimWithinLimit(string scenario)
    {
        JsonStat2 metadata = CreateMetadata(1);
        Filter filter = scenario switch
        {
            "wildcardCode" => new CodeFilter { Query = ["*"] },
            "wildcardFrom" => new FromFilter { Query = "*" },
            "wildcardTo" => new ToFilter { Query = "*" },
            "empty" => new CodeFilter { Query = [] },
            "duplicate" => new CodeFilter { Query = ["Z", "Z"] },
            "oversizedFirst" => new FirstFilter { Query = 5 },
            "oversizedLast" => new LastFilter { Query = 5 },
            _ => new FirstFilter { Query = 1 }
        };
        Dictionary<string, Filter>? filters = scenario switch
        {
            "null" => null,
            "omitted" => [],
            _ => new() { ["d0"] = filter }
        };
        ValidationResult result = QueryValidator.Validate(metadata, filters);
        result.MaxCells = 50000;

        QueryValidator.CheckSize(metadata, filters, result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.SelectedCellCount, Is.Null);
            Assert.That(result.ExceedsMaxCells, Is.Null);
            Assert.That(result.Issues.Any(issue => issue.Message.Contains("count is unknown")), Is.True);
            Assert.That(result.Issues.All(issue => issue.Severity == "warning"), Is.True);
        }
    }

    /// <summary>
    /// A valid but enormous metadata Cartesian product must not overflow or wrap to a small count.
    /// </summary>
    [Test]
    public void CheckSize_Overflow_ReportsUnknown()
    {
        JsonStat2 metadata = CreateMetadata(32);
        Dictionary<string, Filter> filters = metadata.Id.ToDictionary(id => id,
            _ => (Filter)new FirstFilter { Query = 4 });
        ValidationResult result = QueryValidator.Validate(metadata, filters);
        result.MaxCells = long.MaxValue;

        QueryValidator.CheckSize(metadata, filters, result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.SelectedCellCount, Is.Null);
            Assert.That(result.ExceedsMaxCells, Is.Null);
            Assert.That(result.Issues.Single().Message, Does.Contain("64-bit"));
        }
    }

    /// <summary>
    /// Known counts stay useful when the upstream limit could not be obtained.
    /// </summary>
    [Test]
    public void CheckSize_NoLimit_ReportsCountButNotComparison()
    {
        JsonStat2 metadata = CreateMetadata(1);
        Dictionary<string, Filter> filters = new() { ["d0"] = new FirstFilter { Query = 4 } };
        ValidationResult result = QueryValidator.Validate(metadata, filters);

        QueryValidator.CheckSize(metadata, filters, result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.SelectedCellCount, Is.EqualTo(4));
            Assert.That(result.MaxCells, Is.Null);
            Assert.That(result.ExceedsMaxCells, Is.Null);
        }
    }

    /// <summary>
    /// Large explicit code selections are counted in linear time. With 100,000 codes a per-value list scan would need
    /// billions of comparisons; the generous time bound only rejects that quadratic behavior.
    /// </summary>
    [Test]
    public void CheckSize_LargeCodeSelection_IsCountedQuickly()
    {
        const int codeCount = 100_000;
        List<string> codes = [.. Enumerable.Range(0, codeCount).Select(index => $"c{index}")];
        JsonStat2 metadata = new()
        {
            Class = "dataset", Version = "2.0", Label = "Synthetic metadata",
            Source = null, Updated = null, Id = ["d0"], Size = [codeCount], Value = [],
            Dimension = new()
            {
                ["d0"] = new Dimension
                {
                    Label = "Dimension",
                    Category = new Category { Index = codes, Label = codes.ToDictionary(code => code, code => code) }
                }
            }
        };
        Dictionary<string, Filter> filters = new() { ["d0"] = new CodeFilter { Query = [.. codes.AsEnumerable().Reverse()] } };
        ValidationResult result = QueryValidator.Validate(metadata, filters);
        result.MaxCells = codeCount;

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        QueryValidator.CheckSize(metadata, filters, result);
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.SelectedCellCount, Is.EqualTo(codeCount));
            Assert.That(result.ExceedsMaxCells, Is.False);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        }
    }

    private static JsonStat2 CreateMetadata(int dimensions)
    {
        List<string> ids = Enumerable.Range(0, dimensions).Select(index => $"d{index}").ToList();
        return new JsonStat2
        {
            Class = "dataset", Version = "2.0", Label = "Synthetic metadata",
            Source = null, Updated = null, Id = ids, Size = [.. ids.Select(_ => 4)], Value = [],
            Dimension = ids.ToDictionary(id => id, _ => new Dimension
            {
                Label = "Dimension",
                Category = new Category
                {
                    Index = ["Z", "A", "B", "C"],
                    Label = new() { ["Z"] = "Z", ["A"] = "A", ["B"] = "B", ["C"] = "C" }
                }
            })
        };
    }
}
