using ModelContextProtocol;
using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Metadata;
using PxMCP.Services;
using PxMCP.Tools;

namespace PxMCP.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="MetadataTools"/>.
/// </summary>
[TestFixture]
public class MetadataToolsTests
{
    private Mock<IPxApiClient> _clientMock = null!;
    private MetadataTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IPxApiClient>();
        _tools = new MetadataTools(_clientMock.Object);
    }

    #region GetHeadMetadata

    [Test]
    public async Task GetHeadMetadata_ReturnsTableId()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        HeadMetadataResult result = await _tools.GetHeadMetadata("StatFin", "table1", "en");

        Assert.That(result.TableId, Is.EqualTo("table1"));
    }

    [Test]
    public async Task GetHeadMetadata_ReturnsDimensionsWithRoles()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        HeadMetadataResult result = await _tools.GetHeadMetadata("StatFin", "table1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Dimensions, Has.Count.EqualTo(2));
            Assert.That(result.Dimensions[0].Id, Is.EqualTo("year"));
            Assert.That(result.Dimensions[0].Role, Is.EqualTo(DimensionRole.Time));
            Assert.That(result.Dimensions[1].Id, Is.EqualTo("tiedot"));
            Assert.That(result.Dimensions[1].Role, Is.EqualTo(DimensionRole.Metric));
        }
    }

    [Test]
    public async Task GetHeadMetadata_IncludesValueCountAndSampleCodes()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        HeadMetadataResult result = await _tools.GetHeadMetadata("StatFin", "table1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Dimensions[0].ValueCount, Is.EqualTo(3));
            Assert.That(result.Dimensions[0].SampleCodes, Has.Count.EqualTo(3));
            Assert.That(result.Dimensions[0].SampleCodes[0].Code, Is.EqualTo("2020"));
            Assert.That(result.Dimensions[0].SampleCodes[0].Label, Is.EqualTo("2020"));
        }
    }

    [Test]
    public async Task GetHeadMetadata_LimitsSampleCodesToMax()
    {
        JsonStat2 metadata = CreateSampleMetadata();
        metadata.Id.Add("region");
        metadata.Dimension["region"] = new Dimension
        {
            Label = "Region",
            Category = new Category
            {
                Index = Enumerable.Range(1, 20).Select(i => $"R{i:D2}").ToList(),
                Label = Enumerable.Range(1, 20).ToDictionary(i => $"R{i:D2}", i => $"Region {i}")
            }
        };

        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        HeadMetadataResult result = await _tools.GetHeadMetadata("StatFin", "table1");

        DimensionOverview regionDim = result.Dimensions.First(d => d.Id == "region");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(regionDim.ValueCount, Is.EqualTo(20));
            Assert.That(regionDim.SampleCodes, Has.Count.EqualTo(10));
            Assert.That(regionDim.Role, Is.EqualTo(DimensionRole.Other));
        }
    }

    [Test]
    public async Task GetHeadMetadata_IncludesMetricDimension()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        HeadMetadataResult result = await _tools.GetHeadMetadata("StatFin", "table1");

        DimensionOverview metricDim = result.Dimensions.First(d => d.Role == DimensionRole.Metric);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(metricDim.Id, Is.EqualTo("tiedot"));
            Assert.That(metricDim.SampleCodes[0].Code, Is.EqualTo("vaesto"));
            Assert.That(metricDim.SampleCodes[0].Label, Is.EqualTo("Population"));
        }
    }

    #endregion

    #region GetTableMetadata

    [Test]
    public async Task GetTableMetadata_ReturnsJsonStat2()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        JsonStat2 result = await _tools.GetTableMetadata("StatFin", "table1", "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Version, Is.EqualTo("2.0"));
            Assert.That(result.Class, Is.EqualTo("dataset"));
            Assert.That(result.Dimension, Is.Not.Empty);
        }
    }

    #endregion

    #region GetDimensionValues

    [Test]
    public async Task GetDimensionValues_ReturnsValuesForValidDimension()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        DimensionValuesResult result = await _tools.GetDimensionValues("StatFin", "table1", "year");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Dimension, Is.EqualTo("year"));
            Assert.That(result.Values, Does.ContainKey("2020"));
            Assert.That(result.Values, Does.ContainKey("2021"));
            Assert.That(result.Values, Does.ContainKey("2022"));
            Assert.That(result.ValueCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void GetDimensionValues_InvalidDimension_ThrowsMcpException()
    {
        _clientMock.Setup(c => c.GetTableMetadataAsync("StatFin", "table1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleMetadata());

        McpException? ex = Assert.ThrowsAsync<McpException>(
            () => _tools.GetDimensionValues("StatFin", "table1", "nonexistent"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain("nonexistent"));
            Assert.That(ex.Message, Does.Contain("Available dimensions"));
            Assert.That(ex.Message, Does.Contain("year"));
        }
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
                            ["2020"] = "2020",
                            ["2021"] = "2021",
                            ["2022"] = "2022"
                        }
                    }
                },
                ["tiedot"] = new()
                {
                    Label = "Information",
                    Category = new Category
                    {
                        Index = ["vaesto"],
                        Label = new Dictionary<string, string>
                        {
                            ["vaesto"] = "Population"
                        },
                        Unit = new Dictionary<string, Unit>
                        {
                            ["vaesto"] = new() { Label = "Persons", Decimals = 0 }
                        }
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
