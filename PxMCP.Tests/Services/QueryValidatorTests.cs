using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Data;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Unit tests for <see cref="QueryValidator"/>.
/// </summary>
[TestFixture]
public class QueryValidatorTests
{
    #region Validate

    /// <summary>
    /// Records that even nonmatching wildcard patterns are currently left to the upstream API.
    /// </summary>
    [TestCase("Code")]
    [TestCase("From")]
    [TestCase("To")]
    public void Validate_NonmatchingWildcard_IsNotChecked(string filterType)
    {
        Filter filter = filterType switch
        {
            "Code" => new CodeFilter { Query = ["no-such-value*"] },
            "From" => new FromFilter { Query = "no-such-value*" },
            "To" => new ToFilter { Query = "no-such-value*" },
            _ => throw new ArgumentOutOfRangeException(nameof(filterType))
        };
        Dictionary<string, Filter> filters = new() { ["year"] = filter };

        ValidationResult result = QueryValidator.Validate(CreateSampleMetadata(), filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public void Validate_NullFilters_ReturnsValid()
    {
        JsonStat2 metadata = CreateSampleMetadata();

        ValidationResult result = QueryValidator.Validate(metadata, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public void Validate_EmptyFilters_ReturnsValid()
    {
        JsonStat2 metadata = CreateSampleMetadata();

        ValidationResult result = QueryValidator.Validate(metadata, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Is.Empty);
        }
    }

    [Test]
    public void Validate_UnknownDimension_ReturnsError()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        Dictionary<string, Filter> filters = new()
        {
            ["nonexistent"] = new CodeFilter { Query = ["1"] }
        };

        ValidationResult result = QueryValidator.Validate(metadata, filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Dimension, Is.EqualTo("nonexistent"));
            Assert.That(result.Issues[0].Severity, Is.EqualTo("error"));
            Assert.That(result.Issues[0].Message, Does.Contain("Unknown dimension"));
        }
    }

    [Test]
    public void Validate_NullFilter_ReturnsError()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        Dictionary<string, Filter> filters = new()
        {
            ["year"] = null!
        };

        ValidationResult result = QueryValidator.Validate(metadata, filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Dimension, Is.EqualTo("year"));
            Assert.That(result.Issues[0].Message, Does.Contain("must be an object"));
            Assert.That(result.Issues[0].Severity, Is.EqualTo("error"));
        }
    }

    [Test]
    public void Validate_MultipleIssues_ReportsAll()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        Dictionary<string, Filter> filters = new()
        {
            ["unknown"] = new CodeFilter { Query = ["1"] },
            ["year"] = new CodeFilter { Query = ["9999"] }
        };

        ValidationResult result = QueryValidator.Validate(metadata, filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Validate_WarningsOnly_IsStillValid()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new FirstFilter { Query = 100 }
        };

        ValidationResult result = QueryValidator.Validate(metadata, filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].Severity, Is.EqualTo("warning"));
        }
    }

    #endregion

    #region ValidateCodeFilter

    [Test]
    public void ValidateCodeFilter_ValidCodes_NoIssues()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateCodeFilter("year", new CodeFilter { Query = ["2020", "2021"] }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateCodeFilter_InvalidCode_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateCodeFilter("year", new CodeFilter { Query = ["2020", "9999"] }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("9999"));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    [Test]
    public void ValidateCodeFilter_NullQuery_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateCodeFilter("year", new CodeFilter { Query = null! }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("must be an array"));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    [Test]
    public void ValidateCodeFilter_NullCode_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateCodeFilter("year", new CodeFilter { Query = ["2020", null!] }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("must be strings"));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    [Test]
    public void ValidateCodeFilter_WildcardCode_IsSkipped()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateCodeFilter("year", new CodeFilter { Query = ["202*"] }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    #endregion

    #region ValidateFromFilter

    [Test]
    public void ValidateFromFilter_ValidBoundary_NoIssues()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFromFilter("year", new FromFilter { Query = "2020" }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateFromFilter_InvalidBoundary_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFromFilter("year", new FromFilter { Query = "1900" }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("1900"));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    [Test]
    public void ValidateFromFilter_Wildcard_IsSkipped()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFromFilter("year", new FromFilter { Query = "202*" }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    #endregion

    #region ValidateToFilter

    [Test]
    public void ValidateToFilter_ValidBoundary_NoIssues()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateToFilter("year", new ToFilter { Query = "2022" }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateToFilter_InvalidBoundary_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateToFilter("year", new ToFilter { Query = "3000" }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Message, Does.Contain("3000"));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    #endregion

    #region ValidateFirstFilter

    [Test]
    public void ValidateFirstFilter_ValidCount_NoIssues()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFirstFilter("year", new FirstFilter { Query = 2 }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateFirstFilter_ExceedsSize_AddsWarning()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFirstFilter("year", new FirstFilter { Query = 100 }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo("warning"));
            Assert.That(issues[0].Message, Does.Contain("exceeds"));
        }
    }

    [Test]
    public void ValidateFirstFilter_NonPositive_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateFirstFilter("year", new FirstFilter { Query = 0 }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
            Assert.That(issues[0].Message, Does.Contain("positive"));
        }
    }

    #endregion

    #region ValidateLastFilter

    [Test]
    public void ValidateLastFilter_ValidCount_NoIssues()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateLastFilter("year", new LastFilter { Query = 2 }, dimension, issues);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidateLastFilter_ExceedsSize_AddsWarning()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateLastFilter("year", new LastFilter { Query = 100 }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo("warning"));
        }
    }

    [Test]
    public void ValidateLastFilter_NonPositive_AddsError()
    {
        Dimension dimension = CreateYearDimension();
        List<ValidationIssue> issues = [];

        QueryValidator.ValidateLastFilter("year", new LastFilter { Query = -1 }, dimension, issues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo("error"));
        }
    }

    #endregion

    #region Helpers

    private static Dimension CreateYearDimension()
    {
        return new Dimension
        {
            Label = "Year",
            Category = new Category
            {
                Index = ["2020", "2021", "2022"],
                Label = new Dictionary<string, string>
                {
                    ["2020"] = "2020", ["2021"] = "2021", ["2022"] = "2022"
                }
            }
        };
    }

    private static JsonStat2 CreateSampleMetadata()
    {
        return new JsonStat2
        {
            Version = "2.0",
            Class = "dataset",
            Id = ["year", "tiedot"],
            Label = "Test Table",
            Source = "Test Source",
            Updated = "2024-01-01T00:00:00Z",
            Dimension = new Dictionary<string, Dimension>
            {
                ["year"] = CreateYearDimension(),
                ["tiedot"] = new()
                {
                    Label = "Information",
                    Category = new Category
                    {
                        Index = ["vaesto"],
                        Label = new Dictionary<string, string> { ["vaesto"] = "Population" }
                    }
                }
            },
            Value = [100.0, 200.0, 300.0],
            Size = [3, 1],
            Role = new Dictionary<string, List<string>>
            {
                ["time"] = ["year"],
                ["metric"] = ["tiedot"]
            }
        };
    }

    #endregion
}
