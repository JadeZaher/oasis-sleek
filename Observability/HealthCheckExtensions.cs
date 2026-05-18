using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OASIS.WebAPI.Observability;

/// <summary>
/// Registers health checks and maps the /health endpoint with a JSON response writer.
/// No external packages — uses only Microsoft.Extensions.Diagnostics.HealthChecks (framework-native).
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers AddHealthChecks() with the OASIS storage and provider-monitor checks.
    /// Call from Program.cs: builder.Services.AddOasisHealthChecks();
    /// </summary>
    public static IServiceCollection AddOasisHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck<StorageHealthCheck>(
                name: "storage-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "db"])
            .AddCheck<ProviderHealthMonitorHealthCheck>(
                name: "provider-monitor",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready", "providers"]);

        return services;
    }

    /// <summary>
    /// Maps GET /health returning a JSON payload listing each check's name, status, and description.
    /// Call from Program.cs: app.MapOasisHealth();
    /// </summary>
    public static void MapOasisHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteJsonResponseAsync
        });
    }

    private static async Task WriteJsonResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var result = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                exception = entry.Value.Exception?.Message
            })
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json, Encoding.UTF8);
    }
}
