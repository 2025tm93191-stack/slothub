using Booking.Domain.Bookings;
using BuildingBlocks.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace Booking.UnitTests;

public class BookingServiceTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryBookingRepository _repository = new();
    private readonly FakeScheduling _scheduling;
    private readonly FakePayments _payments = new();
    private readonly BookingService _service;

    public BookingServiceTests()
    {
        _scheduling = new FakeScheduling(_clock);
        _service = new BookingService(_repository, _scheduling, _payments, new BookingOptions(), _clock, NullLogger<BookingService>.Instance);
    }

    private Task<Booking.Domain.Bookings.Booking> CreateAsync() =>
        _service.CreateAsync(new NewBooking(_scheduling.TenantId, Guid.NewGuid(), new CustomerContact("c-1", "+919800000001", null)), CancellationToken.None);

    [Fact]
    public async Task Create_HoldsSlot_AndSnapshotsPrice()
    {
        var booking = await CreateAsync();

        Assert.Equal(BookingStatus.Held, booking.Status);
        Assert.Equal(50_000, booking.AmountMinor);
        Assert.Equal(_clock.Now.AddSeconds(30), booking.HoldExpiresAt);
    }

    [Fact]
    public async Task BR1_Create_IsRejected_WhenSlotIsFull()
    {
        _scheduling.CreateError = DomainException.Conflict(ErrorCodes.SlotUnavailable, "full");

        var error = await Assert.ThrowsAsync<DomainException>(CreateAsync);

        Assert.Equal(ErrorCodes.SlotUnavailable, error.Code);
    }

    [Fact]
    public async Task BR3_Pay_ChargesOnce_ThenConfirms_AndQueuesNotification()
    {
        var booking = await CreateAsync();

        var paid = await _service.PayAsync(booking.Id, CancellationToken.None);

        Assert.Equal(BookingStatus.Confirmed, paid.Status);
        Assert.Single(_payments.Charges);
        Assert.Single(_scheduling.Confirmed);
        Assert.Contains(_repository.Outbox, n => n.EventType == NotificationEvent.BookingConfirmed);
    }

    [Fact]
    public async Task Pay_Twice_DoesNotChargeTwice()
    {
        var booking = await CreateAsync();

        await _service.PayAsync(booking.Id, CancellationToken.None);
        await _service.PayAsync(booking.Id, CancellationToken.None);

        Assert.Single(_payments.Charges);
    }

    [Fact]
    public async Task Pay_Declined_FailsBooking_AndReleasesSlot()
    {
        var booking = await CreateAsync();
        _payments.Approve = false;

        var error = await Assert.ThrowsAsync<DomainException>(() => _service.PayAsync(booking.Id, CancellationToken.None));

        Assert.Equal(ErrorCodes.PaymentDeclined, error.Code);
        Assert.Equal(BookingStatus.Failed, booking.Status);
        Assert.Single(_scheduling.Released);
        Assert.Contains(_repository.Outbox, n => n.EventType == NotificationEvent.PaymentFailed);
    }

    [Fact]
    public async Task Pay_GatewayDown_KeepsBookingHeld_SoItCanBeRetried()
    {
        var booking = await CreateAsync();
        _payments.ChargeError = new DomainException(ErrorKind.Unavailable, ErrorCodes.PaymentGatewayUnavailable, "down");

        await Assert.ThrowsAsync<DomainException>(() => _service.PayAsync(booking.Id, CancellationToken.None));

        Assert.Equal(BookingStatus.Held, booking.Status);
        Assert.Empty(_scheduling.Released);
    }

    [Fact]
    public async Task BR3_HoldExpiredDuringPayment_RefundsPayment_AndExpiresBooking()
    {
        var booking = await CreateAsync();
        _scheduling.ConfirmError = DomainException.Conflict(ErrorCodes.HoldExpired, "expired");

        var error = await Assert.ThrowsAsync<DomainException>(() => _service.PayAsync(booking.Id, CancellationToken.None));

        Assert.Equal(ErrorCodes.HoldExpired, error.Code);
        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.Equal((_payments.PaymentId, 50_000L), Assert.Single(_payments.Refunds));
        Assert.Equal(50_000, booking.RefundedMinor);
    }

    [Fact]
    public async Task BR3_Pay_IsRefused_WhenHoldExpiresWithinSafetyMargin()
    {
        var booking = await CreateAsync();
        _clock.Advance(TimeSpan.FromSeconds(28));

        var error = await Assert.ThrowsAsync<DomainException>(() => _service.PayAsync(booking.Id, CancellationToken.None));

        Assert.Equal(ErrorCodes.HoldExpired, error.Code);
        Assert.Empty(_payments.Charges);
    }

    [Fact]
    public async Task Pay_IsRejected_WhileAnotherOperationHoldsTheBooking()
    {
        var booking = await CreateAsync();
        _repository.Lease(booking.Id);

        var error = await Assert.ThrowsAsync<DomainException>(() => _service.PayAsync(booking.Id, CancellationToken.None));

        Assert.Equal(ErrorCodes.BookingBusy, error.Code);
        Assert.Empty(_payments.Charges);
    }

    [Fact]
    public async Task BR5_CancelConfirmed_Within24Hours_RefundsHalf_AndReleasesSlot()
    {
        _scheduling.SlotStart = _clock.Now.AddHours(10);
        var booking = await CreateAsync();
        await _service.PayAsync(booking.Id, CancellationToken.None);

        var cancelled = await _service.CancelAsync(booking.Id, CancellationToken.None);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.Equal(25_000, cancelled.RefundedMinor);
        Assert.Equal(25_000, Assert.Single(_payments.Refunds).Amount);
        Assert.Single(_scheduling.Released);
    }

    [Fact]
    public async Task CancelHeld_ReleasesSlot_WithoutRefund()
    {
        var booking = await CreateAsync();

        var cancelled = await _service.CancelAsync(booking.Id, CancellationToken.None);

        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.Empty(_payments.Refunds);
        Assert.Single(_scheduling.Released);
    }

    [Fact]
    public async Task Reconcile_ExpiresBooking_WhenSchedulingExpiredTheHold()
    {
        var booking = await CreateAsync();
        _clock.Advance(TimeSpan.FromSeconds(31));
        _scheduling.HoldStatus = HoldInfo.Expired;

        var reconciled = await _service.ReconcileExpiredAsync(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.Contains(_repository.Outbox, n => n.EventType == NotificationEvent.BookingExpired);
    }

    [Fact]
    public async Task Reconcile_LeavesBookingHeld_UntilSchedulingHasSwept()
    {
        var booking = await CreateAsync();
        _clock.Advance(TimeSpan.FromSeconds(31));

        await _service.ReconcileExpiredAsync(CancellationToken.None);

        Assert.Equal(BookingStatus.Held, booking.Status);
    }
}
