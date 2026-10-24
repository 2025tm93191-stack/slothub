using BuildingBlocks.Domain;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Slots;

namespace Scheduling.Domain.Holds;

public enum HoldStatus
{
    Held,
    Confirmed,
    Released,
    Expired,
}

/// <summary>
/// One unit of a slot's capacity reserved for one booking.
/// State machine: HELD → CONFIRMED | RELEASED | EXPIRED, and CONFIRMED → RELEASED.
/// RELEASED and EXPIRED give the capacity back; they are terminal.
/// </summary>
public sealed class Hold
{
    private Hold()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid SlotId { get; private set; }

    /// <summary>Reference to the booking in the Booking service (no foreign key across services). Unique.</summary>
    public Guid BookingId { get; private set; }

    public HoldStatus Status { get; private set; }

    /// <summary>Price snapshot taken from the offering when the hold was created.</summary>
    public long PriceMinor { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>Slot start snapshot, so the Booking service can apply its cancellation policy without another call.</summary>
    public DateTimeOffset SlotStartAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status is HoldStatus.Held or HoldStatus.Confirmed;

    public static Hold Create(Slot slot, ServiceOffering offering, Guid bookingId, TimeSpan ttl, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = slot.TenantId,
            SlotId = slot.Id,
            BookingId = bookingId,
            Status = HoldStatus.Held,
            PriceMinor = offering.PriceMinor,
            Currency = offering.Currency,
            SlotStartAt = slot.StartAt,
            ExpiresAt = now + ttl,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// BR2/BR3: a hold can be confirmed only while it is HELD and not yet past <see cref="ExpiresAt"/>.
    /// Confirming an already confirmed hold is a no-op (safe retry).
    /// </summary>
    public void Confirm(DateTimeOffset now)
    {
        switch (Status)
        {
            case HoldStatus.Confirmed:
                return;
            case HoldStatus.Expired:
            case HoldStatus.Held when now >= ExpiresAt:
                throw DomainException.Conflict(ErrorCodes.HoldExpired, $"Hold {Id} expired at {ExpiresAt:O}.");
            case HoldStatus.Held:
                MoveTo(HoldStatus.Confirmed, now);
                return;
            default:
                throw new InvalidStateTransitionException(nameof(Hold), Status, HoldStatus.Confirmed);
        }
    }

    /// <summary>
    /// Releases a HELD or CONFIRMED hold (cancellation, failed payment).
    /// Returns <c>true</c> if capacity must be returned to the slot; releasing twice is a no-op.
    /// </summary>
    public bool Release(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        MoveTo(HoldStatus.Released, now);
        return true;
    }

    /// <summary>
    /// BR2: expires a HELD hold whose TTL has passed. Returns <c>true</c> if capacity must be returned.
    /// Never touches a CONFIRMED (paid) hold, and is a no-op when run twice.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != HoldStatus.Held || now < ExpiresAt)
        {
            return false;
        }

        MoveTo(HoldStatus.Expired, now);
        return true;
    }

    private void MoveTo(HoldStatus next, DateTimeOffset now)
    {
        Status = next;
        UpdatedAt = now;
    }
}
