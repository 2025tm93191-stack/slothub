using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;

namespace Scheduling.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan HoldTtl = TimeSpan.FromSeconds(30);

    public static (Resource Resource, ServiceOffering Offering) Catalog(Guid? offeringTenant = null)
    {
        var tenantId = Guid.NewGuid();
        var resource = Resource.Create(tenantId, "Dr. Mehta", "person", Now);
        var offering = ServiceOffering.Create(offeringTenant ?? tenantId, "Teeth cleaning", 30, 50_000, "inr", Now);
        return (resource, offering);
    }

    public static Slot Slot(DateTimeOffset? startAt = null, int capacity = 1)
    {
        var (resource, offering) = Catalog();
        return Domain.Slots.Slot.Create(resource, offering, startAt ?? Now.AddDays(1), capacity, Now);
    }

    public static Hold Hold()
    {
        var (resource, offering) = Catalog();
        var slot = Domain.Slots.Slot.Create(resource, offering, Now.AddDays(1), 1, Now);
        return Domain.Holds.Hold.Create(slot, offering, Guid.NewGuid(), HoldTtl, Now);
    }
}
