using BuildingBlocks.Domain;

namespace Scheduling.Domain.Slots;

public sealed record NewSlot(DateTimeOffset StartAt, int Capacity);

public sealed class SlotService(ICatalogRepository catalog, ISlotRepository slots, TimeProvider clock)
{
    /// <summary>
    /// Creates a batch of slots for one resource and offering. Each slot lasts the offering's duration.
    /// Slots of the same resource may not overlap, neither within the batch nor with existing slots.
    /// </summary>
    public async Task<IReadOnlyList<Slot>> CreateBatchAsync(Guid resourceId, Guid offeringId, IReadOnlyList<NewSlot> requested, CancellationToken ct)
    {
        var resource = await catalog.FindResourceAsync(resourceId, ct)
            ?? throw DomainException.NotFound(ErrorCodes.ResourceNotFound, $"Resource {resourceId} was not found.");
        var offering = await catalog.FindOfferingAsync(offeringId, ct)
            ?? throw DomainException.NotFound(ErrorCodes.OfferingNotFound, $"Offering {offeringId} was not found.");

        var now = clock.GetUtcNow();
        var created = requested
            .Select(s => Slot.Create(resource, offering, s.StartAt, s.Capacity, now))
            .OrderBy(s => s.StartAt)
            .ToList();

        for (var i = 1; i < created.Count; i++)
        {
            if (created[i].Overlaps(created[i - 1].StartAt, created[i - 1].EndAt))
            {
                throw DomainException.Conflict(ErrorCodes.SlotOverlap, $"Requested slots at {created[i - 1].StartAt:O} and {created[i].StartAt:O} overlap.");
            }
        }

        foreach (var slot in created)
        {
            if (await slots.AnyOverlapAsync(resourceId, slot.StartAt, slot.EndAt, ct))
            {
                throw DomainException.Conflict(ErrorCodes.SlotOverlap, $"Slot at {slot.StartAt:O} overlaps an existing slot of resource {resourceId}.");
            }
        }

        await slots.AddRangeAsync(created, ct);
        return created;
    }

    public async Task<Slot> GetAsync(Guid slotId, CancellationToken ct) =>
        await slots.FindAsync(slotId, ct)
        ?? throw DomainException.NotFound(ErrorCodes.SlotNotFound, $"Slot {slotId} was not found.");

    public Task<PagedResult<Slot>> SearchAsync(SlotQuery query, CancellationToken ct) =>
        slots.SearchAsync(query, clock.GetUtcNow(), ct);
}
