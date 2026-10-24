using System.Globalization;
using BuildingBlocks.Domain;

namespace Booking.Domain.Bookings;

public sealed record CustomerContact(string Id, string? Phone, string? Email);

public enum NotificationEvent
{
    BookingConfirmed,
    BookingCancelled,
    BookingExpired,
    PaymentFailed,
}

public enum NotificationChannel
{
    Sms,
    Email,
}

/// <summary>A customer message to send once the booking change is committed (written to the transactional outbox).</summary>
public sealed record BookingNotification(
    Guid BookingId,
    NotificationEvent EventType,
    NotificationChannel Channel,
    string Recipient,
    IReadOnlyDictionary<string, string> Data);

public sealed class BookingStatusChange
{
    private BookingStatusChange()
    {
    }

    public long Id { get; private set; }

    public Guid BookingId { get; private set; }

    public BookingStatus? FromStatus { get; private set; }

    public BookingStatus ToStatus { get; private set; }

    public string Reason { get; private set; } = null!;

    public DateTimeOffset At { get; private set; }

    internal static BookingStatusChange Create(Guid bookingId, BookingStatus? from, BookingStatus to, string reason, DateTimeOffset at) =>
        new() { BookingId = bookingId, FromStatus = from, ToStatus = to, Reason = reason, At = at };
}

/// <summary>
/// A customer's booking of one slot. Owns the booking state machine (<see cref="BookingStateMachine"/>);
/// slot capacity and money are owned by Scheduling and Payment and referenced by ID only.
/// </summary>
public sealed class Booking
{
    private readonly List<BookingStatusChange> _history = [];
    private readonly List<BookingNotification> _notifications = [];

    private Booking()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid SlotId { get; private set; }

    public string CustomerId { get; private set; } = null!;

    public string? CustomerPhone { get; private set; }

    public string? CustomerEmail { get; private set; }

    public BookingStatus Status { get; private set; }

    public Guid? HoldId { get; private set; }

    public Guid? PaymentId { get; private set; }

    /// <summary>Price snapshot taken from the hold.</summary>
    public long AmountMinor { get; private set; }

    public string? Currency { get; private set; }

    public DateTimeOffset? SlotStartAt { get; private set; }

    public DateTimeOffset? HoldExpiresAt { get; private set; }

    public long RefundedMinor { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Optimistic concurrency token; incremented on every change.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<BookingStatusChange> History => _history;

    public IReadOnlyList<BookingNotification> PendingNotifications => _notifications;

    public static Booking Create(Guid tenantId, Guid slotId, CustomerContact customer, DateTimeOffset now)
    {
        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SlotId = slotId,
            CustomerId = customer.Id.Trim(),
            CustomerPhone = string.IsNullOrWhiteSpace(customer.Phone) ? null : customer.Phone.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(customer.Email) ? null : customer.Email.Trim(),
            Status = BookingStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        booking._history.Add(BookingStatusChange.Create(booking.Id, null, BookingStatus.Pending, "Booking requested", now));
        return booking;
    }

    public void MarkHeld(Guid holdId, long priceMinor, string currency, DateTimeOffset slotStartAt, DateTimeOffset holdExpiresAt, DateTimeOffset now)
    {
        MoveTo(BookingStatus.Held, $"Slot held until {holdExpiresAt:O}", now);
        HoldId = holdId;
        AmountMinor = priceMinor;
        Currency = currency;
        SlotStartAt = slotStartAt;
        HoldExpiresAt = holdExpiresAt;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        MoveTo(BookingStatus.Rejected, reason, now);
        FailureReason = reason;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        MoveTo(BookingStatus.Failed, reason, now);
        FailureReason = reason;
        if (reason == ErrorCodes.PaymentDeclined)
        {
            Notify(NotificationEvent.PaymentFailed, new Dictionary<string, string>());
        }
    }

    /// <summary>
    /// BR3 precondition: payment may start only while the booking is HELD and the hold will still be active
    /// after <paramref name="safetyMargin"/> (time needed to charge and confirm).
    /// </summary>
    public void EnsurePayable(DateTimeOffset now, TimeSpan safetyMargin)
    {
        if (Status == BookingStatus.Expired || (Status == BookingStatus.Held && HoldExpiresAt - safetyMargin <= now))
        {
            throw DomainException.Conflict(ErrorCodes.HoldExpired, $"The hold for booking {Id} expired (or expires too soon) at {HoldExpiresAt:O}.");
        }

        BookingStateMachine.EnsureCanTransition(Status, BookingStatus.Confirmed);
    }

    /// <summary>Records a successful charge before confirming, so a crash after charging can be reconciled.</summary>
    public void RecordPayment(Guid paymentId, DateTimeOffset now)
    {
        if (Status != BookingStatus.Held)
        {
            throw new InvalidStateTransitionException(nameof(Booking), Status, "payment recorded");
        }

        PaymentId = paymentId;
        Touch(now);
    }

    /// <summary>BR3: a booking is confirmed only after a successful payment (or when the price is zero).</summary>
    public void Confirm(DateTimeOffset now)
    {
        if (PaymentId is null && AmountMinor > 0)
        {
            throw DomainException.Conflict(ErrorCodes.PaymentNotCompleted, $"Booking {Id} has no successful payment and cannot be confirmed.");
        }

        MoveTo(BookingStatus.Confirmed, PaymentId is null ? "Confirmed (free)" : $"Paid with payment {PaymentId}", now);
        Notify(NotificationEvent.BookingConfirmed, new Dictionary<string, string>
        {
            ["slotStart"] = SlotStartAt?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty,
            ["amount"] = Money(AmountMinor),
        });
    }

    /// <summary>The hold expired before the booking was confirmed. Any payment taken has been refunded (<paramref name="refundedMinor"/>).</summary>
    public void Expire(string reason, long refundedMinor, DateTimeOffset now)
    {
        MoveTo(BookingStatus.Expired, reason, now);
        FailureReason = reason;
        RefundedMinor = refundedMinor;
        Notify(NotificationEvent.BookingExpired, new Dictionary<string, string>());
    }

    public void Cancel(long refundedMinor, DateTimeOffset now)
    {
        MoveTo(BookingStatus.Cancelled, refundedMinor > 0 ? $"Cancelled by customer; refunded {Money(refundedMinor)}" : "Cancelled by customer", now);
        RefundedMinor = refundedMinor;
        Notify(NotificationEvent.BookingCancelled, new Dictionary<string, string> { ["refundAmount"] = Money(refundedMinor) });
    }

    public void ClearPendingNotifications() => _notifications.Clear();

    private void MoveTo(BookingStatus next, string reason, DateTimeOffset now)
    {
        BookingStateMachine.EnsureCanTransition(Status, next);
        _history.Add(BookingStatusChange.Create(Id, Status, next, reason, now));
        Status = next;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private void Notify(NotificationEvent eventType, Dictionary<string, string> data)
    {
        if (CustomerPhone is not null)
        {
            _notifications.Add(new BookingNotification(Id, eventType, NotificationChannel.Sms, CustomerPhone, data));
        }

        if (CustomerEmail is not null)
        {
            _notifications.Add(new BookingNotification(Id, eventType, NotificationChannel.Email, CustomerEmail, data));
        }
    }

    private string Money(long minor) => string.Create(CultureInfo.InvariantCulture, $"{Currency} {minor / 100m:0.00}");
}
