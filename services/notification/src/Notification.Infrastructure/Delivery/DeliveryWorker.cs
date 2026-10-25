using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notification.Domain.Messages;
using Notification.Infrastructure.Persistence;

namespace Notification.Infrastructure.Delivery;

public sealed record DeliveryOptions(TimeSpan PollInterval, int BatchSize, RetryPolicy RetryPolicy);

/// <summary>
/// Delivers due messages in batches. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so several
/// replicas can run the worker without sending the same message twice.
/// </summary>
internal sealed class DeliveryWorker(
    IServiceScopeFactory scopes,
    DeliveryOptions options,
    TimeProvider clock,
    ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval, clock);
        do
        {
            try
            {
                while (await DeliverBatchAsync(stoppingToken) == options.BatchSize)
                {
                    // A full batch means more may be due; keep draining.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification delivery batch failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> DeliverBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var due = await db.Messages
            .FromSql($"""
                SELECT * FROM messages
                WHERE status IN ('PENDING', 'FAILED_RETRYING') AND next_attempt_at <= {now}
                ORDER BY next_attempt_at
                LIMIT {options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in due)
        {
            try
            {
                await sender.SendAsync(message, ct);
                message.RecordSuccess(clock.GetUtcNow());
                logger.LogInformation("Notification {MessageId} delivered on attempt {Attempt}", message.Id, message.Attempts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.RecordFailure(ex.Message, options.RetryPolicy, clock.GetUtcNow());
                logger.LogWarning(
                    "Notification {MessageId} attempt {Attempt} failed: {Error}; status {Status}, next attempt {NextAttemptAt}",
                    message.Id, message.Attempts, ex.Message, message.Status, message.NextAttemptAt);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return due.Count;
    }
}
