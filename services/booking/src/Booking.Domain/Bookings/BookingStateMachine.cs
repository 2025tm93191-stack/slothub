using BuildingBlocks.Domain;

namespace Booking.Domain.Bookings;

public enum BookingStatus
{
    Pending,
    Held,
    Confirmed,
    Rejected,
    Failed,
    Expired,
    Cancelled,
}

/// <summary>
/// The only allowed booking transitions. Anything else is rejected with 409 INVALID_STATE_TRANSITION.
/// <code>
/// PENDING   → HELD | REJECTED | FAILED
/// HELD      → CONFIRMED | FAILED | EXPIRED | CANCELLED
/// CONFIRMED → CANCELLED
/// REJECTED, FAILED, EXPIRED, CANCELLED are terminal.
/// </code>
/// </summary>
public static class BookingStateMachine
{
    private static readonly IReadOnlyDictionary<BookingStatus, BookingStatus[]> Allowed = new Dictionary<BookingStatus, BookingStatus[]>
    {
        [BookingStatus.Pending] = [BookingStatus.Held, BookingStatus.Rejected, BookingStatus.Failed],
        [BookingStatus.Held] = [BookingStatus.Confirmed, BookingStatus.Failed, BookingStatus.Expired, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.Cancelled],
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsTerminal(BookingStatus status) => !Allowed.ContainsKey(status);

    public static void EnsureCanTransition(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidStateTransitionException(nameof(Booking), from, to);
        }
    }
}
