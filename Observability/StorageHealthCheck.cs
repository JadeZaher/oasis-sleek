using Microsoft.Extensions.Diagnostics.HealthChecks;
using OASIS.WebAPI.Data;

namespace OASIS.WebAPI.Observability;

/// <summary>
/// Health check that verifies the primary storage database is reachable.
/// Reports Healthy when EF Core can open a connection; Unhealthy otherwise.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private readonly OASISDbContext _db;

    public StorageHealthCheck(OASISDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection established.")
                : HealthCheckResult.Unhealthy("Database connection check returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                description: "Database connection failed with an exception.",
                exception: ex);
        }
    }
}
