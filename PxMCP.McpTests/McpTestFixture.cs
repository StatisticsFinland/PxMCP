using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using PxMCP.Configuration;
using PxMCP.Services;

namespace PxMCP.McpTests;

/// <summary>
/// Base fixture providing in-process MCP server and client for integration testing.
/// </summary>
public abstract class McpTestFixture
{
    private IHost _host = null!;
    private Pipe _clientToServer = null!;
    private Pipe _serverToClient = null!;

    protected McpClient Client { get; private set; } = null!;
    protected Mock<IPxApiClient> ApiClientMock { get; private set; } = null!;
    protected JsonSerializerOptions ToolSerializerOptions { get; } = new(McpJsonUtilities.DefaultOptions)
    {
        AllowOutOfOrderMetadataProperties = true
    };

    [SetUp]
    public async Task BaseSetUp()
    {
        AppContext.SetSwitch("System.Text.Json.Serialization.RespectNullableAnnotations", true);

        ApiClientMock = new Mock<IPxApiClient>();
        ApiClientMock.Setup(client => client.GetMaxCellsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000L);

        _clientToServer = new Pipe();
        _serverToClient = new Pipe();

        IHostBuilder builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IPxApiClient>(ApiClientMock.Object);
            services.AddSingleton(new PxApiOptions { BaseUrl = "https://test.example.com" });

            services.AddMcpServer(options =>
            {
                options.Filters.Request.CallToolFilters.Add(ToolErrorFilter.Create());
            })
                .WithStreamServerTransport(
                    _clientToServer.Reader.AsStream(),
                    _serverToClient.Writer.AsStream())
                .WithToolsFromAssembly(typeof(PxMCP.Tools.DataTools).Assembly, serializerOptions: ToolSerializerOptions);
        });

        _host = builder.Build();
        await _host.StartAsync();

        Client = await McpClient.CreateAsync(
            new StreamClientTransport(
                _clientToServer.Writer.AsStream(),
                _serverToClient.Reader.AsStream()));
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        if (Client is not null)
            await Client.DisposeAsync();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
