using Booking.Domain.Bookings;
using BuildingBlocks.Domain;
using BookingEntity = Booking.Domain.Bookings.Booking;

namespace Booking.UnitTests;

internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class InMemoryBookingRepository : IBookingRepository
{
    private readonly Dictionary<Guid, BookingEntity> _bookings = [];
    private readonly HashSet<Guid> _leased = [];

    public List<BookingNotification> Outbox { get; } = [];

    public void Lease(Guid id) => _leased.Add(id);

    public Task AddAsync(BookingEntity booking, CancellationToken ct)
    {
        _bookings[booking.Id] = booking;
        return SaveAsync(booking, ct);
    }

    public Task SaveAsync(BookingEntity booking, CancellationToken ct)
    {
        Outbox.AddRange(booking.PendingNotifications);
        booking.ClearPendingNotifications();
        return Task.CompletedTask;
    }

    public Task<BookingEntity?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(_bookings.GetValueOrDefault(id));

    public Task<BookingEntity?> GetReadOnlyAsync(Guid id, CancellationToken ct) => FindAsync(id, ct);

    public Task<PagedResult<BookingEntity>> ListAsync(BookingQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<BookingEntity>(_bookings.Values.ToList(), 1, _bookings.Count, _bookings.Count));

    public Task<bool> TryAcquireLeaseAsync(Guid id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct) =>
        Task.FromResult(_bookings.ContainsKey(id) && _leased.Add(id));

    public Task ReleaseLeaseAsync(Guid id)
    {
        _leased.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> FindExpiryCandidatesAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(_bookings.Values
            .Where(b => b.Status == BookingStatus.Held && b.HoldExpiresAt <= now && !_leased.Contains(b.Id))
            .Select(b => b.Id).ToList());
}

internal sealed class FakeScheduling(ManualClock clock) : ISchedulingClient
{
    public Guid TenantId { get; set; } = Guid.NewGuid();

    public long Price { get; set; } = 50_000;

    public DateTimeOffset SlotStart { get; set; } = clock.Now.AddDays(3);

    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);

    public DomainException? CreateError { get; set; }

    public DomainException? ConfirmError { get; set; }

    public string HoldStatus { get; set; } = HoldInfo.Held;

    public List<Guid> Confirmed { get; } = [];

    public List<Guid> Released { get; } = [];

    private HoldInfo? _hold;

    public Task<HoldInfo> CreateHoldAsync(Guid slotId, Guid bookingId, CancellationToken ct)
    {
        if (CreateError is not null)
        {
            throw CreateError;
        }

        _hold = new HoldInfo(Guid.NewGuid(), TenantId, slotId, HoldInfo.Held, Price, "INR", SlotStart, clock.Now + Ttl);
        return Task.FromResult(_hold);
    }

    public Task<HoldInfo> GetHoldAsync(Guid holdId, CancellationToken ct) => Task.FromResult(_hold! with { Status = HoldStatus });

    public Task<HoldInfo> ConfirmHoldAsync(Guid holdId, CancellationToken ct)
    {
        if (ConfirmError is not null)
        {
            throw ConfirmError;
        }

        Confirmed.Add(holdId);
        return Task.FromResult(_hold! with { Status = HoldInfo.Confirmed });
    }

    public Task<HoldInfo> ReleaseHoldAsync(Guid holdId, CancellationToken ct)
    {
        Released.Add(holdId);
        return Task.FromResult(_hold! with { Status = HoldInfo.Released });
    }
}

internal sealed class FakePayments : IPaymentClient
{
    public bool Approve { get; set; } = true;

    public DomainException? ChargeError { get; set; }

    public List<(Guid BookingId, long Amount)> Charges { get; } = [];

    public List<(Guid PaymentId, long Amount)> Refunds { get; } = [];

    public Guid PaymentId { get; } = Guid.NewGuid();

    public Task<PaymentOutcome> ChargeAsync(Guid bookingId, long amountMinor, string currency, CancellationToken ct)
    {
        if (ChargeError is not null)
        {
            throw ChargeError;
        }

        Charges.Add((bookingId, amountMinor));
        return Task.FromResult(new PaymentOutcome(PaymentId, Approve, Approve ? null : "CARD_DECLINED"));
    }

    public Task RefundAsync(Guid paymentId, Guid bookingId, long amountMinor, CancellationToken ct)
    {
        Refunds.Add((paymentId, amountMinor));
        return Task.CompletedTask;
    }
}
