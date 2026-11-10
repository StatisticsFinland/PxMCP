using System.Net;
using ModelContextProtocol;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Data;
using PxMCP.Services;
using PxMCP.Tools;

namespace PxMCP.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="DataTools"/>.
/// </summary>
[TestFixture]
public class DataToolsTests
{
    private Mock<IPxApiClient> _clientMock = null!;
    private DataTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IPxApiClient>();
        _clientMock.Setup(client => client.GetMaxCellsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000L);
        _tools = new DataTools(_clientMock.Object);
    }

    #region GetData

    [Test]
    public async Task GetData_WithoutFilters_CallsWithNullFilters()
    {
        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        JsonStat2 result = await _tools.GetData("StatFin", "table1");

        _clientMock.Verify(c => c.PostDataAsync("StatFin", "table1", null, null, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(client => client.GetMaxCellsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(result.Value, Is.Not.Empty);
    }

    [Test]
    public async Task GetData_WithCodeFilter_PassesCorrectly()
    {
        Dictionary<string, Filter>? capturedFilters = null;

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>((_, _, f, _, _) => capturedFilters = f)
            .ReturnsAsync(CreateSampleData());

        Dictionary<string, Filter> filters = new()
        {
            ["tiedot"] = new CodeFilter { Query = ["vaesto"] }
        };

        await _tools.GetData("StatFin", "table1", filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedFilters, Is.Not.Null);
            Assert.That(capturedFilters!, Does.ContainKey("tiedot"));
            Assert.That(capturedFilters!["tiedot"], Is.InstanceOf<CodeFilter>());
            Assert.That(((CodeFilter)capturedFilters!["tiedot"]).Query, Is.EqualTo(new[] { "vaesto" }));
        }
    }

    [Test]
    public async Task GetData_WithFromFilter_PassesCorrectly()
    {
        Dictionary<string, Filter>? capturedFilters = null;

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>((_, _, f, _, _) => capturedFilters = f)
            .ReturnsAsync(CreateSampleData());

        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new FromFilter { Query = "2020" }
        };

        await _tools.GetData("StatFin", "table1", filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedFilters!["year"], Is.InstanceOf<FromFilter>());
            Assert.That(((FromFilter)capturedFilters!["year"]).Query, Is.EqualTo("2020"));
        }
    }

    [Test]
    public async Task GetData_WithFirstFilter_PassesCorrectly()
    {
        Dictionary<string, Filter>? capturedFilters = null;

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), null, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>((_, _, f, _, _) => capturedFilters = f)
            .ReturnsAsync(CreateSampleData());

        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new FirstFilter { Query = 2 }
        };

        await _tools.GetData("StatFin", "table1", filters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedFilters!["year"], Is.InstanceOf<FirstFilter>());
            Assert.That(((FirstFilter)capturedFilters!["year"]).Query, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetData_WithCombinedFilters_PassesAllTypes()
    {
        Dictionary<string, Filter>? capturedFilters = null;

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMultiDimensionMetadata());

        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", It.IsAny<Dictionary<string, Filter>>(), "en", It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, Filter>?, string?, CancellationToken>((_, _, f, _, _) => capturedFilters = f)
            .ReturnsAsync(CreateSampleData());

        Dictionary<string, Filter> filters = new()
        {
            ["gender"] = new CodeFilter { Query = ["1", "2"] },
            ["year"] = new FromFilter { Query = "2020" },
            ["age"] = new ToFilter { Query = "81-90" },
            ["region"] = new FirstFilter { Query = 3 },
            ["rooms"] = new LastFilter { Query = 2 }
        };

        await _tools.GetData("StatFin", "table1", filters, "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(capturedFilters, Has.Count.EqualTo(5));
            Assert.That(capturedFilters!["gender"], Is.InstanceOf<CodeFilter>());
            Assert.That(capturedFilters["year"], Is.InstanceOf<FromFilter>());
            Assert.That(capturedFilters["age"], Is.InstanceOf<ToFilter>());
            Assert.That(capturedFilters["region"], Is.InstanceOf<FirstFilter>());
            Assert.That(capturedFilters["rooms"], Is.InstanceOf<LastFilter>());
        }
    }

    [Test]
    public async Task GetData_PassesFiltersDirectlyToClient()
    {
        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new FromFilter { Query = "2020" }
        };

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", filters, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        await _tools.GetData("StatFin", "table1", filters);

        _clientMock.Verify(c => c.PostDataAsync("StatFin", "table1", filters, null, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(client => client.GetMaxCellsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetData_PassesLangParameter()
    {
        _clientMock.Setup(c => c.PostDataAsync("StatFin", "table1", null, "sv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleData());

        await _tools.GetData("StatFin", "table1", lang: "sv");

        _clientMock.Verify(c => c.PostDataAsync("StatFin", "table1", null, "sv", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetData_WithInvalidFilters_ThrowsMcpException()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        Dictionary<string, Filter> filters = new()
        {
            ["unknown_dimension"] = new CodeFilter { Query = ["val1"] }
        };

        McpException ex = Assert.ThrowsAsync<McpException>(() => _tools.GetData("StatFin", "table1", filters))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Does.Contain("Query validation failed"));
            Assert.That(ex.Message, Does.Contain("unknown_dimension"));
        }
    }

    #endregion

    #region ValidateQuery

    [Test]
    public async Task ValidateQuery_CallsGetTableMetadataAsync()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        await _tools.ValidateQuery("StatFin", "table1", lang: "en");

        _clientMock.Verify(c => c.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ValidateQuery_NullFilters_ReturnsValid()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        ValidationResult result = await _tools.ValidateQuery("StatFin", "table1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.SelectedCellCount, Is.Null);
            Assert.That(result.MaxCells, Is.EqualTo(50000));
            Assert.That(result.ExceedsMaxCells, Is.Null);
            Assert.That(result.Issues.Single().Message, Does.Contain("count is unknown"));
        }
    }

    /// <summary>
    /// Confirms HEAD-derived limits, exact threshold comparisons and no data fetch during preflight.
    /// </summary>
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(1, true)]
    public async Task ValidateQuery_CompleteSelection_ChecksLiveLimit(long limit, bool exceeds)
    {
        _clientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());
        _clientMock.Setup(client => client.GetMaxCellsAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(limit);
        Dictionary<string, Filter> filters = new()
        {
            ["year"] = new LastFilter { Query = 2 },
            ["tiedot"] = new CodeFilter { Query = ["vaesto"] }
        };

        ValidationResult result = await _tools.ValidateQuery("StatFin", "table1", filters, "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.EqualTo(!exceeds));
            Assert.That(result.SelectedCellCount, Is.EqualTo(2));
            Assert.That(result.MaxCells, Is.EqualTo(limit));
            Assert.That(result.ExceedsMaxCells, Is.EqualTo(exceeds));
            Assert.That(result.Issues.Any(issue => issue.Severity == "error"), Is.EqualTo(exceeds));
        }
        _clientMock.Verify(client => client.GetMaxCellsAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(client => client.PostDataAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, Filter>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A failed size check is explicit but does not discard successful filter validation or exact counts.
    /// </summary>
    [Test]
    public async Task ValidateQuery_HeadFailure_ReturnsWarningAndUnknownLimit()
    {
        _clientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());
        _clientMock.Setup(client => client.GetMaxCellsAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Synthetic internal upstream details"));

        ValidationResult result = await _tools.ValidateQuery("StatFin", "table1", new()
        {
            ["year"] = new FirstFilter { Query = 1 },
            ["tiedot"] = new FirstFilter { Query = 1 }
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.SelectedCellCount, Is.EqualTo(1));
            Assert.That(result.MaxCells, Is.Null);
            Assert.That(result.ExceedsMaxCells, Is.Null);
            Assert.That(result.Issues.Single().Severity, Is.EqualTo("warning"));
            Assert.That(result.Issues.Single().Message, Does.Contain("Size limit check unavailable"));
            Assert.That(result.Issues.Single().Message, Does.Not.Contain("internal"));
        }
    }

    /// <summary>
    /// Invalid filters avoid HEAD, and get_data never introduces a preflight request.
    /// </summary>
    [Test]
    public async Task ValidateQuery_InvalidFilters_DoesNotRequestLimit()
    {
        _clientMock.Setup(client => client.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        ValidationResult result = await _tools.ValidateQuery("StatFin", "table1", new()
        {
            ["unknown"] = new CodeFilter { Query = ["x"] }
        });

        Assert.That(result.IsValid, Is.False);
        _clientMock.Verify(client => client.GetMaxCellsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Helpers

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
                ["year"] = new()
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
                },
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

    private static JsonStat2 CreateMultiDimensionMetadata()
    {
        return new JsonStat2
        {
            Version = "2.0",
            Class = "dataset",
            Label = "Multi-Dimension Test Table",
            Source = "Test Source",
            Updated = "2024-01-01T00:00:00Z",
            Id = ["gender", "year", "age", "region", "rooms"],
            Dimension = new Dictionary<string, Dimension>
            {
                ["gender"] = new()
                {
                    Label = "Gender",
                    Category = new Category
                    {
                        Index = ["1", "2"],
                        Label = new Dictionary<string, string> { ["1"] = "Male", ["2"] = "Female" }
                    }
                },
                ["year"] = new()
                {
                    Label = "Year",
                    Category = new Category
                    {
                        Index = ["2019", "2020", "2021", "2022"],
                        Label = new Dictionary<string, string> { ["2019"] = "2019", ["2020"] = "2020", ["2021"] = "2021", ["2022"] = "2022" }
                    }
                },
                ["age"] = new()
                {
                    Label = "Age group",
                    Category = new Category
                    {
                        Index = ["0-17", "18-64", "65-80", "81-90"],
                        Label = new Dictionary<string, string> { ["0-17"] = "0-17", ["18-64"] = "18-64", ["65-80"] = "65-80", ["81-90"] = "81-90" }
                    }
                },
                ["region"] = new()
                {
                    Label = "Region",
                    Category = new Category
                    {
                        Index = ["R01", "R02", "R03", "R04", "R05"],
                        Label = new Dictionary<string, string> { ["R01"] = "Region 1", ["R02"] = "Region 2", ["R03"] = "Region 3", ["R04"] = "Region 4", ["R05"] = "Region 5" }
                    }
                },
                ["rooms"] = new()
                {
                    Label = "Rooms",
                    Category = new Category
                    {
                        Index = ["1", "2", "3"],
                        Label = new Dictionary<string, string> { ["1"] = "1 room", ["2"] = "2 rooms", ["3"] = "3 rooms" }
                    }
                }
            },
            Value = [],
            Size = [2, 4, 4, 5, 3]
        };
    }

    private static JsonStat2 CreateSampleData()
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
                ["year"] = new()
                {
                    Label = "Year",
                    Category = new Category
                    {
                        Index = ["2020"],
                        Label = new Dictionary<string, string> { ["2020"] = "2020" }
                    }
                },
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
            Value = [5500000.0],
            Size = [1, 1]
        };
    }

    #endregion
}
