using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Services;
using PxMCP.Tools;

namespace PxMCP.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="TableTools"/>.
/// </summary>
[TestFixture]
public class TableToolsTests
{
    private Mock<IPxApiClient> _clientMock = null!;
    private TableTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IPxApiClient>();
        _tools = new TableTools(_clientMock.Object);
    }

    [Test]
    public async Task ListTables_ReturnsPagedTableList()
    {
        PagedTableList tables = new()
        {
            Tables =
            [
                new TableListingItem
                {
                    Table = new TableSummary
                    {
                        TableId = "statfin_vaerak_pxt_11re",
                        Title = "Population by age and sex",
                        Metrics = [new MetricInfo { Name = "Population", Unit = "Persons" }],
                        TimeRange = new TimeRange { From = "2014", To = "2023" },
                        Dimensions = [new DimensionInfo { Name = "Age", Size = 21 }],
                        LastUpdated = new DateTime(2024, 4, 26, 8, 0, 0, DateTimeKind.Utc)
                    },
                    Links = [new Link { Href = "/meta/databases/StatFin/tables/statfin_vaerak_pxt_11re", Rel = "self", Method = "GET" }]
                }
            ],
            PagingInfo = new PagingInfo { CurrentPage = 1, PageSize = 20, TotalItems = 1526 }
        };

        _clientMock.Setup(c => c.GetTablesAsync("StatFin", "en", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tables);

        PagedTableList result = await _tools.ListTables("StatFin", "en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Tables, Has.Count.EqualTo(1));
            Assert.That(result.Tables[0].Table.TableId, Is.EqualTo("statfin_vaerak_pxt_11re"));
            Assert.That(result.Tables[0].Table.Title, Is.EqualTo("Population by age and sex"));
            Assert.That(result.PagingInfo.TotalItems, Is.EqualTo(1526));
        }
    }

    [Test]
    public async Task ListTables_PassesPagingParameters()
    {
        _clientMock.Setup(c => c.GetTablesAsync("StatFin", null, 3, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedTableList
            {
                Tables = [],
                PagingInfo = new PagingInfo { CurrentPage = 3, PageSize = 10, TotalItems = 0 }
            });

        await _tools.ListTables("StatFin", page: 3, pageSize: 10);

        _clientMock.Verify(c => c.GetTablesAsync("StatFin", null, 3, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(1, 1)]
    [TestCase(1, 100)]
    public async Task ListTables_WithBoundaryPaging_CallsApi(int page, int pageSize)
    {
        _clientMock.Setup(c => c.GetTablesAsync("StatFin", null, page, pageSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedTableList
            {
                Tables = [],
                PagingInfo = new PagingInfo { CurrentPage = page, PageSize = pageSize, TotalItems = 0 }
            });

        await _tools.ListTables("StatFin", page: page, pageSize: pageSize);

        _clientMock.Verify(c => c.GetTablesAsync("StatFin", null, page, pageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(0, 20, "Page must be greater than or equal to 1.")]
    [TestCase(-1, 20, "Page must be greater than or equal to 1.")]
    [TestCase(1, 0, "Page size must be between 1 and 100.")]
    [TestCase(1, 101, "Page size must be between 1 and 100.")]
    public void ListTables_WithOutOfRangePaging_ThrowsMcpExceptionWithoutCallingApi(int page, int pageSize, string expectedMessage)
    {
        ModelContextProtocol.McpException exception = Assert.ThrowsAsync<ModelContextProtocol.McpException>(
            () => _tools.ListTables("StatFin", page: page, pageSize: pageSize))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo(expectedMessage));
            _clientMock.Verify(
                client => client.GetTablesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void ListTables_WithInvalidIdentifier_ThrowsMcpExceptionWithValidatorMessage()
    {
        _clientMock.Setup(client => client.GetTablesAsync(
                "..", null, 1, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PxApiArgumentException(PxApiArgumentValidator.InvalidDatabaseMessage));

        ModelContextProtocol.McpException exception = Assert.ThrowsAsync<ModelContextProtocol.McpException>(
            () => _tools.ListTables(".."))!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Is.EqualTo(PxApiArgumentValidator.InvalidDatabaseMessage));
            Assert.That(exception.InnerException, Is.TypeOf<PxApiArgumentException>());
        }
    }
}
