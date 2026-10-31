using BuildingBlocks.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers the service's own DbContext (PostgreSQL, snake_case names) from
    /// <c>ConnectionStrings__Default</c>, plus a readiness health check for it.
    /// </summary>
    public static IServiceCollection AddServiceDbContext<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required environment variable ConnectionStrings__{ConnectionStringName}.");
        }

        services.AddDbContext<TContext>(options => options.UseServiceDefaults(connectionString));
        services.AddHealthChecks().AddCheck<DbContextHealthCheck<TContext>>("database", tags: [HealthEndpoints.ReadyTag]);
        return services;
    }

    public static DbContextOptionsBuilder UseServiceDefaults(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    /// <summary>
    /// Applies pending EF Core migrations when <c>DB_MIGRATE_ON_STARTUP=true</c> or <c>MIGRATE_ONLY=true</c>, retrying
    /// while the database starts. Returns <c>true</c> when <c>MIGRATE_ONLY=true</c>: the process should exit instead of
    /// serving traffic. Kubernetes uses that mode in a one-off Job, so several replicas never migrate concurrently.
    /// </summary>
    public static async Task<bool> MigrateDatabaseIfEnabledAsync<TContext>(this WebApplication app, int maxAttempts = 10)
        where TContext : DbContext
    {
        var migrateOnly = app.Configuration.GetValue("MIGRATE_ONLY", false);
        if (migrateOnly || app.Configuration.GetValue("DB_MIGRATE_ON_STARTUP", false))
        {
            await MigrateAsync<TContext>(app, maxAttempts);
        }

        return migrateOnly;
    }

    private static async Task MigrateAsync<TContext>(WebApplication app, int maxAttempts)
        where TContext : DbContext
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
                app.Logger.LogInformation("Database migrations applied");
                return;
            }
            catch (NpgsqlException ex) when (attempt < maxAttempts)
            {
                app.Logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{MaxAttempts}), retrying", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
