using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Tests for the PX API health check.
/// </summary>
[TestFixture]
public class PxApiHealthCheckTests
{
    /// <summary>
    /// A canceled health-check request propagates cancellation instead of reporting an upstream outage.
    /// </summary>
    [Test]
    public void CheckHealthAsync_WhenCanceled_PropagatesCancellation()
    {
        Mock<IPxApiClient> clientMock = new(MockBehavior.Strict);
        Mock<ILogger<PxApiHealthCheck>> loggerMock = new();
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        clientMock
            .Setup(client => client.GetDatabasesAsync(null, cancellationSource.Token))
            .Returns(Task.FromCanceled<List<Models.PxApi.DatabaseListingItem>>(cancellationSource.Token));

        PxApiHealthCheck healthCheck = new(clientMock.Object, loggerMock.Object);
        HealthCheckContext context = new()
        {
            Registration = new HealthCheckRegistration(
                "pxapi",
                _ => healthCheck,
                HealthStatus.Unhealthy,
                [],
                null)
        };

        Assert.ThrowsAsync<TaskCanceledException>(
            async () => await healthCheck.CheckHealthAsync(context, cancellationSource.Token));

        loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
