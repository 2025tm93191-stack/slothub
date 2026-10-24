using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Domain;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;
using Scheduling.Infrastructure.Holds;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSchedulingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<SchedulingDbContext>(configuration);
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();

        var concurrencyMode = configuration.GetValue("CONCURRENCY_MODE", "atomic")!.ToLowerInvariant();
        if (concurrencyMode is not ("atomic" or "naive"))
        {
            throw new InvalidOperationException("CONCURRENCY_MODE must be 'atomic' (default) or 'naive' (demo of the failure only).");
        }

        services.AddSingleton(new HoldConcurrencyOptions(concurrencyMode == "naive"));

        var ttlSeconds = configuration.GetValue("HOLD_TTL_SECONDS", 30);
        if (ttlSeconds is < 1 or > 86_400)
        {
            throw new InvalidOperationException("HOLD_TTL_SECONDS must be between 1 and 86400.");
        }

        services.AddSingleton(new HoldOptions { TtlSeconds = ttlSeconds });

        var sweepSeconds = configuration.GetValue("HOLD_SWEEP_INTERVAL_SECONDS", 5);
        if (sweepSeconds < 1)
        {
            throw new InvalidOperationException("HOLD_SWEEP_INTERVAL_SECONDS must be at least 1.");
        }

        services.AddSingleton(new HoldExpiryOptions(TimeSpan.FromSeconds(sweepSeconds)));
        if (configuration.GetValue("HOLD_EXPIRY_WORKER_ENABLED", true))
        {
            services.AddHostedService<HoldExpiryWorker>();
        }

        services.AddScoped<CatalogService>();
        services.AddScoped<SlotService>();
        services.AddScoped<HoldService>();
        return services;
    }
}
