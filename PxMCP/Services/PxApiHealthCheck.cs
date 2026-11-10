using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PxMCP.Services;

/// <summary>
/// Health check that verifies connectivity to the upstream PX API
/// by listing available databases.
/// </summary>
public class PxApiHealthCheck(IPxApiClient pxApiClient, ILogger<PxApiHealthCheck> logger) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<Models.PxApi.DatabaseListingItem> databases = await pxApiClient.GetDatabasesAsync(lang: null, cancellationToken);

            return HealthCheckResult.Healthy(
                $"PX API is reachable. {databases.Count} database(s) available.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PX API health check failed");

            return HealthCheckResult.Unhealthy(
                "PX API is not reachable.",
                exception: ex);
        }
    }
}
