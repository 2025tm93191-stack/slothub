using BuildingBlocks.Domain;

namespace Booking.Domain.Bookings;

/// <summary>What Booking needs to know about a hold owned by the Scheduling service.</summary>
public sealed record HoldInfo(
    Guid Id,
    Guid TenantId,
    Guid SlotId,
    string Status,
    long PriceMinor,
    string Currency,
    DateTimeOffset SlotStartAt,
    DateTimeOffset ExpiresAt)
{
    public const string Held = "HELD";
    public const string Confirmed = "CONFIRMED";
    public const string Released = "RELEASED";
    public const string Expired = "EXPIRED";
}

public sealed record PaymentOutcome(Guid PaymentId, bool Approved, string? DeclineReason);

/// <summary>
/// Scheduling service API. Every call is safe to retry: holds are unique per booking, confirm and release are no-ops
/// when repeated. Failures surface as <see cref="DomainException"/> carrying the downstream error code.
/// </summary>
public interface ISchedulingClient
{
    Task<HoldInfo> CreateHoldAsync(Guid slotId, Guid bookingId, CancellationToken ct);

    Task<HoldInfo> GetHoldAsync(Guid holdId, CancellationToken ct);

    Task<HoldInfo> ConfirmHoldAsync(Guid holdId, CancellationToken ct);

    Task<HoldInfo> ReleaseHoldAsync(Guid holdId, CancellationToken ct);
}

/// <summary>
/// Payment service API. Calls carry deterministic idempotency keys derived from the booking
/// (<c>pay-{bookingId}</c>, <c>refund-{bookingId}</c>), so retries can never charge or refund twice.
/// </summary>
public interface IPaymentClient
{
    /// <summary>Returns the outcome (approved or declined). Throws an Unavailable/Timeout error if the gateway is down.</summary>
    Task<PaymentOutcome> ChargeAsync(Guid bookingId, long amountMinor, string currency, CancellationToken ct);

    Task RefundAsync(Guid paymentId, Guid bookingId, long amountMinor, CancellationToken ct);
}

public sealed record BookingQuery(string? CustomerId, BookingStatus? Status, int Page, int PageSize);

public interface IBookingRepository
{
    Task AddAsync(Booking booking, CancellationToken ct);

    /// <summary>Saves changes plus any pending notifications into the outbox, in one transaction (optimistic concurrency on Version).</summary>
    Task SaveAsync(Booking booking, CancellationToken ct);

    /// <summary>Tracked load for modification.</summary>
    Task<Booking?> FindAsync(Guid id, CancellationToken ct);

    Task<Booking?> GetReadOnlyAsync(Guid id, CancellationToken ct);

    Task<PagedResult<Booking>> ListAsync(BookingQuery query, CancellationToken ct);

    /// <summary>
    /// Takes a short lease on the booking (conditional UPDATE on <c>locked_until</c>) so that pay, cancel and the expiry
    /// reconciler never work on the same booking at the same time. Returns false if another operation holds it.
    /// </summary>
    Task<bool> TryAcquireLeaseAsync(Guid id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct);

    Task ReleaseLeaseAsync(Guid id);

    /// <summary>HELD bookings whose hold has passed its expiry time and that nobody is working on.</summary>
    Task<IReadOnlyList<Guid>> FindExpiryCandidatesAsync(DateTimeOffset now, int limit, CancellationToken ct);
}
