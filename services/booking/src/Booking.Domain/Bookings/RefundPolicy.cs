using BuildingBlocks.Domain;

namespace Booking.Domain.Bookings;

/// <summary>
/// BR5 — cancellation refund policy for a confirmed booking:
/// at least 24 h before the slot starts → 100 %; less than 24 h → 50 % (rounded down);
/// once the slot has started → cancellation is not allowed.
/// </summary>
public static class RefundPolicy
{
    public static readonly TimeSpan FullRefundNotice = TimeSpan.FromHours(24);

    public static long Calculate(long amountMinor, DateTimeOffset slotStartAt, DateTimeOffset now)
    {
        if (now >= slotStartAt)
        {
            throw DomainException.Conflict(ErrorCodes.CancellationNotAllowed, $"The slot started at {slotStartAt:O}; the booking can no longer be cancelled.");
        }

        return slotStartAt - now >= FullRefundNotice ? amountMinor : amountMinor / 2;
    }
}
