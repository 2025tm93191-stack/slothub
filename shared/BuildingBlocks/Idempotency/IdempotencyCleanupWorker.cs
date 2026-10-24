using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Idempotency;

/// <summary>
/// Deletes idempotency records past their retention (<c>IDEMPOTENCY_TTL_HOURS</c>), every
/// <c>IDEMPOTENCY_CLEANUP_INTERVAL_MINUTES</c> (default 60). A key older than the retention can be reused.
/// </summary>
internal sealed class IdempotencyCleanupWorker<TContext>(
    IServiceScopeFactory scopes,
    IdempotencyOptions options,
    TimeProvider clock,
    ILogger<IdempotencyCleanupWorker<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CleanupInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var now = clock.GetUtcNow();
                var deleted = await scope.ServiceProvider.GetRequiredService<TContext>().Set<IdempotencyRecord>()
                    .Where(x => x.ExpiresAt < now)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} expired idempotency key(s)", deleted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idempotency key cleanup failed; will retry on the next tick");
            }
        }
    }
}
