using BuildingBlocks.Domain;
using Scheduling.Domain.Catalog;

namespace Scheduling.Domain.Slots;

/// <summary>
/// A bookable time window of one resource with a fixed capacity (1 for a doctor, N for a class).
/// <see cref="ReservedCount"/> counts active holds (HELD + CONFIRMED) and is only changed by atomic SQL updates.
/// </summary>
public sealed class Slot
{
    private Slot()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ResourceId { get; private set; }

    public Guid OfferingId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    public int Capacity { get; private set; }

    public int ReservedCount { get; private set; }

    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int Remaining => Capacity - ReservedCount;

    /// <summary>BR6: a slot that has already started cannot be held.</summary>
    public bool HasStarted(DateTimeOffset now) => StartAt <= now;

    /// <summary>BR1 + BR6: there is capacity left and the slot is still in the future.</summary>
    public bool CanBeHeld(DateTimeOffset now) => !HasStarted(now) && Remaining > 0;

    public bool Overlaps(DateTimeOffset startAt, DateTimeOffset endAt) => StartAt < endAt && startAt < EndAt;

    /// <summary>Creates a slot whose length equals the offering duration.</summary>
    public static Slot Create(Resource resource, ServiceOffering offering, DateTimeOffset startAt, int capacity, DateTimeOffset now)
    {
        if (resource.TenantId != offering.TenantId)
        {
            throw DomainException.Validation(ErrorCodes.ResourceOfferingMismatch, "Resource and offering belong to different tenants.");
        }

        if (capacity < 1)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "Capacity must be at least 1.");
        }

        if (startAt <= now)
        {
            throw DomainException.Validation(ErrorCodes.SlotInPast, $"Slot start {startAt:O} is not in the future.");
        }

        return new Slot
        {
            Id = Guid.CreateVersion7(),
            TenantId = resource.TenantId,
            ResourceId = resource.Id,
            OfferingId = offering.Id,
            StartAt = startAt.ToUniversalTime(),
            EndAt = startAt.ToUniversalTime().AddMinutes(offering.DurationMinutes),
            Capacity = capacity,
            ReservedCount = 0,
            Version = 0,
            CreatedAt = now,
        };
    }
}
