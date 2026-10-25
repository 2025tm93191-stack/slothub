using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notification.Domain.Messages;

namespace Notification.Infrastructure.Persistence;

internal sealed class MessageRepository(NotificationDbContext db, ILogger<MessageRepository> logger) : IMessageRepository
{
    public async Task<(Message Message, bool Created)> AddIfNewAsync(Message message, CancellationToken ct)
    {
        db.Messages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Notification {MessageId} {EventType} queued for booking {BookingId}", message.Id, message.EventType, message.BookingId);
            return (message, true);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, NotificationDbContext.MessageDedupIndex))
        {
            db.Entry(message).State = EntityState.Detached;
            var existing = await db.Messages.AsNoTracking().Include(x => x.DeliveryAttempts)
                .SingleAsync(x => x.BookingId == message.BookingId && x.EventType == message.EventType && x.Channel == message.Channel, ct);
            logger.LogInformation("Duplicate {EventType} notification for booking {BookingId} suppressed; returning {MessageId}", message.EventType, message.BookingId, existing.Id);
            return (existing, false);
        }
    }

    public Task<Message?> FindAsync(Guid id, CancellationToken ct) =>
        db.Messages.AsNoTracking().Include(x => x.DeliveryAttempts).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<Message>> ListAsync(Guid? bookingId, int page, int pageSize, CancellationToken ct)
    {
        var messages = db.Messages.AsNoTracking();
        if (bookingId is { } id)
        {
            messages = messages.Where(x => x.BookingId == id);
        }

        var total = await messages.CountAsync(ct);
        var items = await messages.Include(x => x.DeliveryAttempts)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return new PagedResult<Message>(items, page, pageSize, total);
    }
}
