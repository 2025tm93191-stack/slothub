using BuildingBlocks.Domain;

namespace Notification.Domain.Messages;

public interface IMessageRepository
{
    /// <summary>Inserts the message unless one already exists for (booking, event type, channel); returns the stored one.</summary>
    Task<(Message Message, bool Created)> AddIfNewAsync(Message message, CancellationToken ct);

    Task<Message?> FindAsync(Guid id, CancellationToken ct);

    Task<PagedResult<Message>> ListAsync(Guid? bookingId, int page, int pageSize, CancellationToken ct);
}

/// <summary>The (mock) SMS / email provider. Throws when delivery fails.</summary>
public interface IMessageSender
{
    Task SendAsync(Message message, CancellationToken ct);
}

public sealed record NewNotification(Guid BookingId, EventType EventType, Channel Channel, string Recipient, IReadOnlyDictionary<string, string>? Data);

public sealed class NotificationService(IMessageRepository repository, TimeProvider clock)
{
    /// <summary>Accepts a notification for asynchronous delivery. Duplicate requests return the original message.</summary>
    public Task<(Message Message, bool Created)> AcceptAsync(NewNotification request, CancellationToken ct)
    {
        var body = MessageTemplates.Render(request.EventType, request.BookingId, request.Data);
        var message = Message.Create(request.BookingId, request.EventType, request.Channel, request.Recipient, body, clock.GetUtcNow());
        return repository.AddIfNewAsync(message, ct);
    }

    public async Task<Message> GetAsync(Guid id, CancellationToken ct) =>
        await repository.FindAsync(id, ct)
        ?? throw DomainException.NotFound(ErrorCodes.NotFound, $"Notification {id} was not found.");

    public Task<PagedResult<Message>> ListAsync(Guid? bookingId, int page, int pageSize, CancellationToken ct) =>
        repository.ListAsync(bookingId, page, pageSize, ct);
}
