using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Health;

public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// <c>GET /health</c>: liveness (process is up, no dependencies checked).
    /// <c>GET /health/ready</c>: readiness (own database reachable).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app, string serviceName)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = (context, report) => WriteAsync(context, report, serviceName),
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = (context, report) => WriteAsync(context, report, serviceName),
        });

        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report, string serviceName)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString().ToUpperInvariant(),
            service = serviceName,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString().ToUpperInvariant(), entry.Value.Description }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
    }
}

/// <summary>Readiness check: can this service reach its own database?</summary>
public sealed class DbContextHealthCheck<TContext>(TContext db) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database reachable")
            : HealthCheckResult.Unhealthy("Database unreachable");
}
