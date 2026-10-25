using BuildingBlocks.Chaos;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Messages;
using Notification.Infrastructure.Delivery;
using Notification.Infrastructure.Persistence;

namespace Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<NotificationDbContext>(configuration);
        services.AddChaos(configuration, MockMessageSender.ErrorMode);
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IMessageSender, MockMessageSender>();
        services.AddScoped<NotificationService>();

        var maxAttempts = configuration.GetValue("NOTIFY_MAX_ATTEMPTS", 5);
        var baseDelay = configuration.GetValue("NOTIFY_RETRY_BASE_SECONDS", 2);
        var pollInterval = configuration.GetValue("NOTIFY_POLL_INTERVAL_SECONDS", 2);
        if (maxAttempts < 1 || baseDelay < 1 || pollInterval < 1)
        {
            throw new InvalidOperationException("NOTIFY_MAX_ATTEMPTS, NOTIFY_RETRY_BASE_SECONDS and NOTIFY_POLL_INTERVAL_SECONDS must be at least 1.");
        }

        services.AddSingleton(new DeliveryOptions(
            TimeSpan.FromSeconds(pollInterval),
            BatchSize: 20,
            new RetryPolicy(maxAttempts, TimeSpan.FromSeconds(baseDelay), TimeSpan.FromMinutes(1))));
        services.AddHostedService<DeliveryWorker>();
        return services;
    }
}
