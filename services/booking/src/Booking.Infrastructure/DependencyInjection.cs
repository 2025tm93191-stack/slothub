using Booking.Domain.Bookings;
using Booking.Infrastructure.Background;
using Booking.Infrastructure.Clients;
using Booking.Infrastructure.Persistence;
using BuildingBlocks.Correlation;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Booking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBookingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<BookingDbContext>(configuration);
        services.AddIdempotency<BookingDbContext>(configuration);
        services.AddScoped<IBookingRepository, BookingRepository>();

        services.AddSingleton(new BookingOptions
        {
            HoldSafetyMargin = TimeSpan.FromSeconds(PositiveInt(configuration, "HOLD_SAFETY_MARGIN_SECONDS", 3, allowZero: true)),
            LeaseDuration = TimeSpan.FromSeconds(PositiveInt(configuration, "BOOKING_LEASE_SECONDS", 60)),
        });
        services.AddScoped<BookingService>();

        AddDownstreamClients(services, configuration);

        services.AddSingleton(new BackgroundOptions(
            TimeSpan.FromSeconds(PositiveInt(configuration, "OUTBOX_POLL_INTERVAL_SECONDS", 2)),
            TimeSpan.FromSeconds(PositiveInt(configuration, "HOLD_SWEEP_INTERVAL_SECONDS", 5))));
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<BookingExpiryReconciler>();
        return services;
    }

    /// <summary>
    /// Scheduling and Payment calls go through a resilience pipeline: per-attempt timeout, retries with exponential
    /// backoff + jitter on timeouts/5xx, and a circuit breaker. Retrying POSTs is safe because every call is idempotent
    /// (holds are unique per booking; payments carry idempotency keys).
    /// </summary>
    private static void AddDownstreamClients(IServiceCollection services, IConfiguration configuration)
    {
        var attemptTimeout = TimeSpan.FromSeconds(PositiveInt(configuration, "HTTP_ATTEMPT_TIMEOUT_SECONDS", 2));
        var totalTimeout = TimeSpan.FromSeconds(PositiveInt(configuration, "HTTP_TOTAL_TIMEOUT_SECONDS", 20));
        var maxRetries = PositiveInt(configuration, "HTTP_MAX_RETRIES", 3, allowZero: true);
        if (totalTimeout <= attemptTimeout)
        {
            throw new InvalidOperationException("HTTP_TOTAL_TIMEOUT_SECONDS must be greater than HTTP_ATTEMPT_TIMEOUT_SECONDS.");
        }

        void Configure(HttpStandardResilienceOptions o)
        {
            o.AttemptTimeout.Timeout = attemptTimeout;
            o.TotalRequestTimeout.Timeout = totalTimeout;
            o.Retry.MaxRetryAttempts = Math.Max(1, maxRetries);
            o.Retry.ShouldRetryAfterHeader = false;
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.Retry.Delay = TimeSpan.FromMilliseconds(300);
            if (maxRetries == 0)
            {
                o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
            }

            o.CircuitBreaker.FailureRatio = 0.5;
            o.CircuitBreaker.MinimumThroughput = 5;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, attemptTimeout.TotalSeconds * 2));
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        }

        services.AddHttpClient<ISchedulingClient, SchedulingClient>(c => c.BaseAddress = ServiceUrl(configuration, "Scheduling"))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(Configure);

        services.AddHttpClient<IPaymentClient, PaymentClient>(c => c.BaseAddress = ServiceUrl(configuration, "Payment"))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(Configure);

        // The outbox retries on its own schedule, so the notification client only needs a short timeout.
        services.AddHttpClient(OutboxDispatcher.NotificationClient, c =>
            {
                c.BaseAddress = ServiceUrl(configuration, "Notification");
                c.Timeout = TimeSpan.FromSeconds(3);
            })
            .AddHttpMessageHandler<CorrelationIdHandler>();
    }

    private static Uri ServiceUrl(IConfiguration configuration, string service)
    {
        var value = configuration[$"Services:{service}:BaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Missing or invalid environment variable Services__{service}__BaseUrl.");
        }

        return new Uri(uri.ToString().TrimEnd('/') + "/");
    }

    private static int PositiveInt(IConfiguration configuration, string key, int defaultValue, bool allowZero = false)
    {
        var value = configuration.GetValue(key, defaultValue);
        if (value < 0 || (value == 0 && !allowZero))
        {
            throw new InvalidOperationException($"{key} must be {(allowZero ? "zero or more" : "at least 1")}.");
        }

        return value;
    }
}
