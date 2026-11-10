using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Search;
using PxMCP.Services;
using PxMCP.Tools;

namespace PxMCP.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="SearchTools"/>.
/// </summary>
[TestFixture]
public class SearchToolsTests
{
    private Mock<IPxApiClient> _clientMock = null!;
    private SearchTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IPxApiClient>();
        _tools = new SearchTools(_clientMock.Object);
    }

    [Test]
    public async Task Search_WithoutDatabase_CallsSearchGlobal()
    {
        _clientMock.Setup(c => c.SearchGlobalAsync("population", null, "en", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResponse("population"));

        SearchResult result = await _tools.Search("population", lang: "en");

        _clientMock.Verify(c => c.SearchGlobalAsync("population", null, "en", 1, 20, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(result.Results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Search_WithDatabase_CallsSearchDatabase()
    {
        _clientMock.Setup(c => c.SearchDatabaseAsync("StatFin", "population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResponse("population"));

        await _tools.Search("population", database: "StatFin");

        _clientMock.Verify(c => c.SearchDatabaseAsync("StatFin", "population", null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Search_ReturnsTableSummaries()
    {
        _clientMock.Setup(c => c.SearchGlobalAsync("population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResponse("population"));

        SearchResult result = await _tools.Search("population");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Results[0].Table.Title, Is.EqualTo("Test Table"));
            Assert.That(result.Results[0].Table.TableId, Is.EqualTo("table1"));
            Assert.That(result.Results[0].Table.TimeRange.From, Is.EqualTo("2020"));
            Assert.That(result.Results[0].Table.TimeRange.To, Is.EqualTo("2022"));
            Assert.That(result.Results[0].Table.Metrics[0].Name, Is.EqualTo("Population"));
        }
    }

    [Test]
    public async Task Search_DoesNotCallGetTableMetadata()
    {
        _clientMock.Setup(c => c.SearchGlobalAsync("population", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResponse("population"));

        await _tools.Search("population");

        _clientMock.Verify(c => c.GetTableMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(0, 20, "Page must be greater than or equal to 1.")]
    [TestCase(-1, 20, "Page must be greater than or equal to 1.")]
    [TestCase(1, 0, "Page size must be between 1 and 100.")]
    [TestCase(1, 101, "Page size must be between 1 and 100.")]
    public void Search_WithOutOfRangePaging_ThrowsMcpExceptionWithoutCallingApi(int page, int pageSize, string expectedMessage)
    {
        ModelContextProtocol.McpException exception = Assert.ThrowsAsync<ModelContextProtocol.McpException>(
            () => _tools.Search("population", page: page, pageSize: pageSize))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
            _clientMock.Verify(
                client => client.SearchGlobalAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(1, 1)]
    [TestCase(1, 100)]
    public async Task Search_WithBoundaryPaging_CallsApi(int page, int pageSize)
    {
        _clientMock.Setup(c => c.SearchGlobalAsync("population", null, null, page, pageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResponse("population"));

        await _tools.Search("population", page: page, pageSize: pageSize);

        _clientMock.Verify(c => c.SearchGlobalAsync("population", null, null, page, pageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helpers

    private static PxApiSearchResponse CreateSearchResponse(string query)
    {
        return new PxApiSearchResponse
        {
            Query = new SearchQueryInfo { Q = query, Target = SearchTarget.Content, Lang = "fi" },
            Results =
            [
                new PxApiSearchResultItem
                {
                    Score = 1.0,
                    Database = new SearchDatabaseRef { Id = "StatFin", Name = "StatFin" },
                    Table = new TableSummary
                    {
                        TableId = "table1",
                        Title = "Test Table",
                        Metrics = [new MetricInfo { Name = "Population", Unit = "Persons" }],
                        TimeRange = new TimeRange { From = "2020", To = "2022" },
                        Dimensions = [new DimensionInfo { Name = "Region", Size = 3 }],
                        LastUpdated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    Matches = [new MatchInfo { Path = "table.name", MatchType = PxMCP.Models.PxApi.MatchType.Contains, MatchedText = query }]
                }
            ],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 10, TotalItems = 1 }
        };
    }

    #endregion
}
