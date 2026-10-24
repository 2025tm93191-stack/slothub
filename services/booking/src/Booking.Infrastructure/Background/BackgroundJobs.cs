using System.Net.Http.Json;
using Booking.Domain.Bookings;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Background;

public sealed record BackgroundOptions(TimeSpan OutboxPollInterval, TimeSpan ExpirySweepInterval);

/// <summary>
/// Delivers outbox messages to the Notification service (at-least-once; Notification de-duplicates).
/// A failing or stopped Notification service never affects bookings: messages wait and are retried with backoff.
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    BackgroundOptions options,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const string NotificationClient = "notification";
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.OutboxPollInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch failed; will retry on the next tick");
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var http = httpClients.CreateClient(NotificationClient);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var due = await db.Outbox
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in due)
        {
            try
            {
                using var content = new StringContent(message.Payload, System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync("api/v1/notifications", content, ct);
                response.EnsureSuccessStatusCode();
                message.ProcessedAt = clock.GetUtcNow();
                logger.LogInformation("Outbox message {MessageId} ({Type}) delivered to Notification", message.Id, message.Type);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                message.NextAttemptAt = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, message.Attempts)));
                logger.LogWarning("Outbox message {MessageId} ({Type}) attempt {Attempt} failed: {Error}; next at {NextAttemptAt}",
                    message.Id, message.Type, message.Attempts, message.LastError, message.NextAttemptAt);
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

/// <summary>Periodically reconciles HELD bookings whose hold time has passed (see <see cref="BookingService.ReconcileExpiredAsync"/>).</summary>
internal sealed class BookingExpiryReconciler(
    IServiceScopeFactory scopes,
    BackgroundOptions options,
    TimeProvider clock,
    ILogger<BookingExpiryReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.ExpirySweepInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reconciled = await scope.ServiceProvider.GetRequiredService<BookingService>().ReconcileExpiredAsync(stoppingToken);
                if (reconciled > 0)
                {
                    logger.LogInformation("Booking expiry reconciliation updated {Count} booking(s)", reconciled);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Booking expiry reconciliation failed; will retry on the next tick");
            }
        }
    }
}
