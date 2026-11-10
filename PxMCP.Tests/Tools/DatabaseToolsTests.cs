using Moq;
using PxMCP.Models.PxApi;
using PxMCP.Models.Tools.Database;
using PxMCP.Services;
using PxMCP.Tools;

namespace PxMCP.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="DatabaseTools"/>.
/// </summary>
[TestFixture]
public class DatabaseToolsTests
{
    private Mock<IPxApiClient> _clientMock = null!;
    private DatabaseTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IPxApiClient>();
        _tools = new DatabaseTools(_clientMock.Object);
    }

    [Test]
    public async Task ListDatabases_ReturnsDatabaseList()
    {
        List<DatabaseListingItem> databases =
        [
            new()
            {
                Id = "StatFin",
                Name = "StatFin",
                Description = "Statistical database",
                TableCount = 1500,
                AvailableLanguages = ["fi", "sv", "en"],
                Links = [new Link { Href = "/meta/databases/StatFin/tables", Rel = "describedby", Method = "GET" }]
            }
        ];

        _clientMock.Setup(c => c.GetDatabasesAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(databases);

        DatabaseListResult result = await _tools.ListDatabases("en");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Databases, Has.Count.EqualTo(1));
            Assert.That(result.Databases[0].Id, Is.EqualTo("StatFin"));
            Assert.That(result.Databases[0].TableCount, Is.EqualTo(1500));
            Assert.That(result.Databases[0].Description, Is.EqualTo("Statistical database"));
        }
    }

    [Test]
    public async Task ListDatabases_WithNullLang_PassesNull()
    {
        _clientMock.Setup(c => c.GetDatabasesAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _tools.ListDatabases();

        _clientMock.Verify(c => c.GetDatabasesAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
