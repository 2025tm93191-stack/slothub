using BuildingBlocks.Domain;

namespace Notification.Domain.Messages;

public enum EventType
{
    BookingConfirmed,
    BookingCancelled,
    BookingExpired,
    PaymentFailed,
    BookingReminder,
}

public enum Channel
{
    Sms,
    Email,
}

public enum MessageStatus
{
    Pending,
    FailedRetrying,
    Sent,
    Dead,
}

/// <summary>
/// One customer message. At most one per (booking, event type, channel), so duplicate requests never
/// send twice. Lifecycle: PENDING → SENT, or PENDING → FAILED_RETRYING → … → SENT | DEAD.
/// </summary>
public sealed class Message
{
    private readonly List<DeliveryAttempt> _attempts = [];

    private Message()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Reference to the booking in the Booking service (no foreign key across services).</summary>
    public Guid BookingId { get; private set; }

    public EventType EventType { get; private set; }

    public Channel Channel { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public MessageStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public IReadOnlyList<DeliveryAttempt> DeliveryAttempts => _attempts;

    public bool IsFinal => Status is MessageStatus.Sent or MessageStatus.Dead;

    public static Message Create(Guid bookingId, EventType eventType, Channel channel, string recipient, string body, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BookingId = bookingId,
            EventType = eventType,
            Channel = channel,
            Recipient = recipient.Trim(),
            Body = body,
            Status = MessageStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        };

    public void RecordSuccess(DateTimeOffset now)
    {
        EnsureDeliverable();
        Attempts++;
        _attempts.Add(DeliveryAttempt.Create(Id, Attempts, success: true, error: null, now));
        Status = MessageStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <summary>Records a failed delivery and schedules the next attempt, or gives up after <paramref name="policy"/>.MaxAttempts.</summary>
    public void RecordFailure(string error, RetryPolicy policy, DateTimeOffset now)
    {
        EnsureDeliverable();
        Attempts++;
        _attempts.Add(DeliveryAttempt.Create(Id, Attempts, success: false, error, now));
        LastError = error;

        if (Attempts >= policy.MaxAttempts)
        {
            Status = MessageStatus.Dead;
            return;
        }

        Status = MessageStatus.FailedRetrying;
        NextAttemptAt = now + policy.DelayAfter(Attempts);
    }

    private void EnsureDeliverable()
    {
        if (IsFinal)
        {
            throw new InvalidStateTransitionException(nameof(Message), Status, "another delivery attempt");
        }
    }
}

public sealed class DeliveryAttempt
{
    private DeliveryAttempt()
    {
    }

    public long Id { get; private set; }

    public Guid MessageId { get; private set; }

    public int AttemptNumber { get; private set; }

    public bool Success { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset At { get; private set; }

    internal static DeliveryAttempt Create(Guid messageId, int attemptNumber, bool success, string? error, DateTimeOffset now) =>
        new() { MessageId = messageId, AttemptNumber = attemptNumber, Success = success, Error = error, At = now };
}
