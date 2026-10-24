using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Scheduling.Domain;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;

namespace Scheduling.Infrastructure.Persistence;

internal sealed class CatalogRepository(SchedulingDbContext db) : ICatalogRepository
{
    public Task<Tenant?> FindTenantAsync(Guid id, CancellationToken ct) =>
        db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Resource?> FindResourceAsync(Guid id, CancellationToken ct) =>
        db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<ServiceOffering?> FindOfferingAsync(Guid id, CancellationToken ct) =>
        db.Offerings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Resource>> ListResourcesAsync(Guid tenantId, CancellationToken ct) =>
        await db.Resources.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceOffering>> ListOfferingsAsync(Guid tenantId, CancellationToken ct) =>
        await db.Offerings.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);

    public async Task AddAsync<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : class
    {
        db.Add(entity);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class SlotRepository(SchedulingDbContext db) : ISlotRepository
{
    public Task<Slot?> FindAsync(Guid id, CancellationToken ct) =>
        db.Slots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<Slot>> SearchAsync(SlotQuery query, DateTimeOffset now, CancellationToken ct)
    {
        var slots = db.Slots.AsNoTracking();
        if (query.TenantId is { } tenantId)
        {
            slots = slots.Where(x => x.TenantId == tenantId);
        }

        if (query.ResourceId is { } resourceId)
        {
            slots = slots.Where(x => x.ResourceId == resourceId);
        }

        if (query.From is { } from)
        {
            slots = slots.Where(x => x.StartAt >= from);
        }

        if (query.To is { } to)
        {
            slots = slots.Where(x => x.StartAt < to);
        }

        if (query.AvailableOnly)
        {
            slots = slots.Where(x => x.ReservedCount < x.Capacity && x.StartAt > now);
        }

        var total = await slots.CountAsync(ct);
        var items = await slots
            .OrderBy(x => x.StartAt).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<Slot>(items, query.Page, query.PageSize, total);
    }

    public Task<bool> AnyOverlapAsync(Guid resourceId, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken ct) =>
        db.Slots.AnyAsync(x => x.ResourceId == resourceId && x.StartAt < endAt && startAt < x.EndAt, ct);

    public async Task AddRangeAsync(IReadOnlyList<Slot> slots, CancellationToken ct)
    {
        db.Slots.AddRange(slots);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsExclusionViolation(ex, SchedulingDbContext.SlotNoOverlapConstraint))
        {
            // Two concurrent batch requests passed the pre-check; the database constraint decided.
            throw DomainException.Conflict(ErrorCodes.SlotOverlap, "One or more slots overlap an existing slot of this resource.");
        }
    }
}

/// <summary><c>CONCURRENCY_MODE=naive</c> switches holds to check-then-update. Demo of the failure only — never in production.</summary>
public sealed record HoldConcurrencyOptions(bool Naive);

internal sealed class HoldRepository(SchedulingDbContext db, ILogger<HoldRepository> logger, HoldConcurrencyOptions? concurrency = null)
    : IHoldRepository
{
    public Task<Hold?> FindAsync(Guid id, CancellationToken ct) =>
        db.Holds.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Hold?> FindByBookingAsync(Guid bookingId, CancellationToken ct) =>
        db.Holds.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);

    public async Task<ReserveOutcome> ReserveAsync(Hold hold, DateTimeOffset now, CancellationToken ct)
    {
        if (concurrency?.Naive == true)
        {
            return await ReserveNaivelyAsync(hold, now, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Check and increment in ONE statement. PostgreSQL row-locks the slot while updating, so concurrent
        // requests queue on this row and each re-evaluates the WHERE clause against the latest committed value.
        var reserved = await db.Slots
            .Where(s => s.Id == hold.SlotId && s.ReservedCount < s.Capacity && s.StartAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.ReservedCount, s => s.ReservedCount + 1)
                    .SetProperty(s => s.Version, s => s.Version + 1),
                ct);

        if (reserved == 0)
        {
            await transaction.RollbackAsync(ct);
            logger.LogInformation("Slot {SlotId} has no capacity left; hold rejected for booking {BookingId}", hold.SlotId, hold.BookingId);
            return ReserveOutcome.SlotUnavailable;
        }

        db.Holds.Add(hold);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex, SchedulingDbContext.HoldBookingUniqueIndex))
        {
            // The same booking raced itself (retry arrived while the first call was in flight). Undo the increment.
            await transaction.RollbackAsync(ct);
            db.Entry(hold).State = EntityState.Detached;
            logger.LogInformation("Booking {BookingId} already holds a slot; duplicate hold request ignored", hold.BookingId);
            return ReserveOutcome.BookingAlreadyHeld;
        }

        await transaction.CommitAsync(ct);
        logger.LogInformation("Hold {HoldId} reserved slot {SlotId} for booking {BookingId} until {ExpiresAt}", hold.Id, hold.SlotId, hold.BookingId, hold.ExpiresAt);
        return ReserveOutcome.Reserved;
    }

    /// <summary>
    /// DEMO ONLY — what Challenge 1 warns against: read the slot, check capacity in C#, then write the incremented
    /// value. Concurrent requests all read the same old count, all pass the check, and each writes "old + 1":
    /// the increments are lost and far more holds are created than the slot's capacity.
    /// </summary>
    private async Task<ReserveOutcome> ReserveNaivelyAsync(Hold hold, DateTimeOffset now, CancellationToken ct)
    {
        var slot = await db.Slots.AsNoTracking().SingleAsync(s => s.Id == hold.SlotId, ct); // 1. read
        if (!slot.CanBeHeld(now))                                                            // 2. check
        {
            return ReserveOutcome.SlotUnavailable;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(50), ct); // stands in for any work between check and write

        var newCount = slot.ReservedCount + 1;                                               // 3. write a stale value
        await db.Slots.Where(s => s.Id == hold.SlotId).ExecuteUpdateAsync(set => set.SetProperty(s => s.ReservedCount, newCount), ct);
        db.Holds.Add(hold);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("NAIVE MODE: hold {HoldId} created after reading reserved_count = {Read}", hold.Id, slot.ReservedCount);
        return ReserveOutcome.Reserved;
    }

    public async Task<Hold> ModifyAsync(Guid id, Func<Hold, bool> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var hold = await db.Holds.FromSql($"SELECT * FROM holds WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw DomainException.NotFound(ErrorCodes.HoldNotFound, $"Hold {id} was not found.");

        var previous = hold.Status;
        var releaseCapacity = change(hold);
        await db.SaveChangesAsync(ct);

        if (releaseCapacity)
        {
            await db.Slots
                .Where(s => s.Id == hold.SlotId && s.ReservedCount > 0)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.ReservedCount, s => s.ReservedCount - 1)
                        .SetProperty(s => s.Version, s => s.Version + 1),
                    ct);
        }

        await transaction.CommitAsync(ct);
        if (previous != hold.Status)
        {
            logger.LogInformation("Hold {HoldId} moved {From} -> {To}; capacity returned: {CapacityReturned}", hold.Id, previous, hold.Status, releaseCapacity);
        }

        return hold;
    }

    public async Task<int> ExpireDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: a hold being confirmed right now (row locked by ModifyAsync) is skipped, never waited on,
        // and a second sweeper running concurrently picks different rows. Only HELD rows qualify.
        var due = await db.Holds
            .FromSql($"""
                SELECT * FROM holds
                WHERE status = 'HELD' AND expires_at <= {now}
                ORDER BY expires_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var expired = due.Where(hold => hold.Expire(now)).ToList();
        await db.SaveChangesAsync(ct);

        foreach (var group in expired.GroupBy(h => h.SlotId))
        {
            var count = group.Count();
            await db.Slots
                .Where(s => s.Id == group.Key && s.ReservedCount >= count)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.ReservedCount, s => s.ReservedCount - count)
                        .SetProperty(s => s.Version, s => s.Version + 1),
                    ct);
        }

        await transaction.CommitAsync(ct);
        foreach (var hold in expired)
        {
            logger.LogInformation("Hold {HoldId} for booking {BookingId} expired; capacity returned to slot {SlotId}", hold.Id, hold.BookingId, hold.SlotId);
        }

        return expired.Count;
    }
}
