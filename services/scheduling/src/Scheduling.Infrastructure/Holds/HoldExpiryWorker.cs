using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scheduling.Domain.Holds;

namespace Scheduling.Infrastructure.Holds;

public sealed record HoldExpiryOptions(TimeSpan SweepInterval);

/// <summary>Challenge 3B: periodically expires holds whose TTL has passed (env <c>HOLD_SWEEP_INTERVAL_SECONDS</c>).</summary>
internal sealed class HoldExpiryWorker(
    IServiceScopeFactory scopes,
    HoldExpiryOptions options,
    TimeProvider clock,
    ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.SweepInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<HoldService>().ExpireDueAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("Hold expiry sweep expired {Count} hold(s)", expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Hold expiry sweep failed; will retry on the next tick");
            }
        }
    }
}
