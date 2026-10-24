using BuildingBlocks.Domain;

namespace Scheduling.Domain.Holds;

public sealed class HoldOptions
{
    /// <summary>BR2: how long a hold lasts before it expires (env <c>HOLD_TTL_SECONDS</c>).</summary>
    public int TtlSeconds { get; init; } = 30;
}

public sealed record HoldResult(Hold Hold, bool Created);

public sealed class HoldService(
    ICatalogRepository catalog,
    ISlotRepository slots,
    IHoldRepository holds,
    HoldOptions options,
    TimeProvider clock)
{
    /// <summary>
    /// Holds one unit of a slot for a booking. Calling again with the same booking returns the existing hold
    /// without using more capacity, so the Booking service can retry safely.
    /// </summary>
    public async Task<HoldResult> CreateAsync(Guid slotId, Guid bookingId, CancellationToken ct)
    {
        if (await ExistingHoldAsync(slotId, bookingId, ct) is { } existing)
        {
            return new HoldResult(existing, Created: false);
        }

        var slot = await slots.FindAsync(slotId, ct)
            ?? throw DomainException.NotFound(ErrorCodes.SlotNotFound, $"Slot {slotId} was not found.");

        var now = clock.GetUtcNow();
        if (slot.HasStarted(now))
        {
            throw DomainException.Conflict(ErrorCodes.SlotInPast, $"Slot {slotId} started at {slot.StartAt:O} and can no longer be held.");
        }

        var offering = await catalog.FindOfferingAsync(slot.OfferingId, ct)
            ?? throw DomainException.NotFound(ErrorCodes.OfferingNotFound, $"Offering {slot.OfferingId} was not found.");

        var hold = Hold.Create(slot, offering, bookingId, TimeSpan.FromSeconds(options.TtlSeconds), now);

        return await holds.ReserveAsync(hold, now, ct) switch
        {
            ReserveOutcome.Reserved => new HoldResult(hold, Created: true),
            ReserveOutcome.BookingAlreadyHeld => new HoldResult((await ExistingHoldAsync(slotId, bookingId, ct))!, Created: false),
            _ => throw DomainException.Conflict(ErrorCodes.SlotUnavailable, $"Slot {slotId} has no remaining capacity."),
        };
    }

    public async Task<Hold> GetAsync(Guid holdId, CancellationToken ct) =>
        await holds.FindAsync(holdId, ct)
        ?? throw DomainException.NotFound(ErrorCodes.HoldNotFound, $"Hold {holdId} was not found.");

    public Task<Hold> ConfirmAsync(Guid holdId, CancellationToken ct) =>
        holds.ModifyAsync(holdId, hold =>
        {
            hold.Confirm(clock.GetUtcNow());
            return false;
        }, ct);

    public Task<Hold> ReleaseAsync(Guid holdId, CancellationToken ct) =>
        holds.ModifyAsync(holdId, hold => hold.Release(clock.GetUtcNow()), ct);

    /// <summary>
    /// BR2 / Challenge 3B: expires every HELD hold whose TTL has passed and returns its capacity.
    /// Safe to run twice or on several replicas at once; CONFIRMED (paid) holds are never touched.
    /// </summary>
    public async Task<int> ExpireDueAsync(CancellationToken ct)
    {
        const int batchSize = 100;
        var total = 0;
        int expired;
        do
        {
            expired = await holds.ExpireDueAsync(clock.GetUtcNow(), batchSize, ct);
            total += expired;
        }
        while (expired == batchSize);

        return total;
    }

    private async Task<Hold?> ExistingHoldAsync(Guid slotId, Guid bookingId, CancellationToken ct)
    {
        var existing = await holds.FindByBookingAsync(bookingId, ct);
        if (existing is not null && existing.SlotId != slotId)
        {
            throw DomainException.Conflict(ErrorCodes.BookingAlreadyHasHold, $"Booking {bookingId} already holds slot {existing.SlotId}.");
        }

        return existing;
    }
}
