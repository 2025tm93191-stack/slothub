using Booking.Domain.Bookings;
using BuildingBlocks.Domain;
using BookingEntity = Booking.Domain.Bookings.Booking;

namespace Booking.UnitTests;

public class BookingStateMachineTests
{
    [Theory]
    [InlineData(BookingStatus.Pending, BookingStatus.Held)]
    [InlineData(BookingStatus.Pending, BookingStatus.Rejected)]
    [InlineData(BookingStatus.Pending, BookingStatus.Failed)]
    [InlineData(BookingStatus.Held, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Held, BookingStatus.Failed)]
    [InlineData(BookingStatus.Held, BookingStatus.Expired)]
    [InlineData(BookingStatus.Held, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Cancelled)]
    public void AllowedTransitions(BookingStatus from, BookingStatus to) =>
        Assert.True(BookingStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(BookingStatus.Pending, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Expired)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Held)]
    [InlineData(BookingStatus.Expired, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Failed, BookingStatus.Held)]
    [InlineData(BookingStatus.Rejected, BookingStatus.Held)]
    public void DisallowedTransitions(BookingStatus from, BookingStatus to)
    {
        Assert.False(BookingStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidStateTransitionException>(() => BookingStateMachine.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Failed)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.Cancelled)]
    public void TerminalStates(BookingStatus status) => Assert.True(BookingStateMachine.IsTerminal(status));
}

public class RefundPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BR5_Refund_IsFull_AtLeast24HoursBefore() =>
        Assert.Equal(50_000, RefundPolicy.Calculate(50_000, Start, Start.AddHours(-24)));

    [Fact]
    public void BR5_Refund_IsHalf_Within24Hours() =>
        Assert.Equal(25_000, RefundPolicy.Calculate(50_000, Start, Start.AddHours(-24).AddSeconds(1)));

    [Fact]
    public void BR5_Refund_RoundsHalfDown() =>
        Assert.Equal(24_999, RefundPolicy.Calculate(49_999, Start, Start.AddMinutes(-5)));

    [Fact]
    public void BR5_Cancellation_IsNotAllowed_OnceSlotHasStarted()
    {
        var error = Assert.Throws<DomainException>(() => RefundPolicy.Calculate(50_000, Start, Start));

        Assert.Equal(ErrorCodes.CancellationNotAllowed, error.Code);
    }
}

public class BookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static BookingEntity Held(DateTimeOffset? expiresAt = null)
    {
        var booking = BookingEntity.Create(Guid.NewGuid(), Guid.NewGuid(), new CustomerContact("c-1", "+919800000001", "a@b.com"), Now);
        booking.MarkHeld(Guid.NewGuid(), 50_000, "INR", Now.AddDays(2), expiresAt ?? Now.AddSeconds(30), Now);
        return booking;
    }

    [Fact]
    public void Create_StartsPending_WithHistory()
    {
        var booking = BookingEntity.Create(Guid.NewGuid(), Guid.NewGuid(), new CustomerContact("c-1", null, "a@b.com"), Now);

        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Single(booking.History);
    }

    [Fact]
    public void BR3_Booking_CannotConfirm_WithoutPayment()
    {
        var booking = Held();

        var error = Assert.Throws<DomainException>(() => booking.Confirm(Now));

        Assert.Equal(ErrorCodes.PaymentNotCompleted, error.Code);
        Assert.Equal(BookingStatus.Held, booking.Status);
    }

    [Fact]
    public void BR3_Booking_Confirms_AfterPayment_AndNotifiesEveryChannel()
    {
        var booking = Held();
        booking.RecordPayment(Guid.NewGuid(), Now);

        booking.Confirm(Now);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(2, booking.PendingNotifications.Count(n => n.EventType == NotificationEvent.BookingConfirmed));
        Assert.Equal("INR 500.00", booking.PendingNotifications[0].Data["amount"]);
    }

    [Fact]
    public void BR3_EnsurePayable_Fails_WhenHoldExpiresWithinMargin()
    {
        var booking = Held(Now.AddSeconds(3));

        var error = Assert.Throws<DomainException>(() => booking.EnsurePayable(Now, TimeSpan.FromSeconds(3)));

        Assert.Equal(ErrorCodes.HoldExpired, error.Code);
    }

    [Fact]
    public void EnsurePayable_Fails_ForCancelledBooking()
    {
        var booking = Held();
        booking.Cancel(0, Now);

        Assert.Throws<InvalidStateTransitionException>(() => booking.EnsurePayable(Now, TimeSpan.Zero));
    }

    [Fact]
    public void EveryChange_BumpsVersion_AndRecordsHistory()
    {
        var booking = Held();
        var version = booking.Version;

        booking.Cancel(0, Now);

        Assert.Equal(version + 1, booking.Version);
        Assert.Equal(BookingStatus.Cancelled, booking.History[^1].ToStatus);
    }
}
