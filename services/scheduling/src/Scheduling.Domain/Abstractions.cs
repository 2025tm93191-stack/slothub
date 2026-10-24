using BuildingBlocks.Domain;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;

namespace Scheduling.Domain;

public interface ICatalogRepository
{
    Task<Tenant?> FindTenantAsync(Guid id, CancellationToken ct);

    Task<Resource?> FindResourceAsync(Guid id, CancellationToken ct);

    Task<ServiceOffering?> FindOfferingAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Resource>> ListResourcesAsync(Guid tenantId, CancellationToken ct);

    Task<IReadOnlyList<ServiceOffering>> ListOfferingsAsync(Guid tenantId, CancellationToken ct);

    Task AddAsync<TEntity>(TEntity entity, CancellationToken ct)
        where TEntity : class;
}

public sealed record SlotQuery(Guid? TenantId, Guid? ResourceId, DateTimeOffset? From, DateTimeOffset? To, bool AvailableOnly, int Page, int PageSize);

public interface ISlotRepository
{
    Task<Slot?> FindAsync(Guid id, CancellationToken ct);

    Task<PagedResult<Slot>> SearchAsync(SlotQuery query, DateTimeOffset now, CancellationToken ct);

    Task<bool> AnyOverlapAsync(Guid resourceId, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken ct);

    /// <summary>Inserts all slots in one transaction. Throws SLOT_OVERLAP if the database exclusion constraint fires.</summary>
    Task AddRangeAsync(IReadOnlyList<Slot> slots, CancellationToken ct);
}

public enum ReserveOutcome
{
    Reserved,
    SlotUnavailable,
    BookingAlreadyHeld,
}

public interface IHoldRepository
{
    Task<Hold?> FindAsync(Guid id, CancellationToken ct);

    Task<Hold?> FindByBookingAsync(Guid bookingId, CancellationToken ct);

    /// <summary>
    /// BR1: atomically takes one unit of the slot's capacity and inserts the hold, in one transaction.
    /// Uses a single conditional UPDATE (<c>reserved_count &lt; capacity AND start_at &gt; now</c>), never check-then-update.
    /// </summary>
    Task<ReserveOutcome> ReserveAsync(Hold hold, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Loads the hold under a row lock, applies <paramref name="change"/> and saves it.
    /// When the change returns <c>true</c> one unit of capacity is returned to the slot in the same transaction.
    /// </summary>
    Task<Hold> ModifyAsync(Guid id, Func<Hold, bool> change, CancellationToken ct);

    /// <summary>
    /// BR2: claims up to <paramref name="batchSize"/> due HELD holds (<c>FOR UPDATE SKIP LOCKED</c>), applies
    /// <see cref="Hold.Expire"/> to each and returns their capacity, all in one transaction. Returns how many expired.
    /// </summary>
    Task<int> ExpireDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
}
