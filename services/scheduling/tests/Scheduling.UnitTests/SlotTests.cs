using BuildingBlocks.Domain;
using Scheduling.Domain.Slots;

namespace Scheduling.UnitTests;

public class SlotTests
{
    [Fact]
    public void Create_SetsEndFromOfferingDuration_AndStartsEmpty()
    {
        var start = TestData.Now.AddDays(1);

        var slot = TestData.Slot(start, capacity: 5);

        Assert.Equal(start.AddMinutes(30), slot.EndAt);
        Assert.Equal(5, slot.Capacity);
        Assert.Equal(0, slot.ReservedCount);
        Assert.Equal(5, slot.Remaining);
    }

    [Fact]
    public void BR6_Create_RejectsSlotThatIsNotInTheFuture()
    {
        var error = Assert.Throws<DomainException>(() => TestData.Slot(TestData.Now));

        Assert.Equal(ErrorCodes.SlotInPast, error.Code);
    }

    [Fact]
    public void BR6_FutureSlot_CanBeHeld()
    {
        var slot = TestData.Slot(TestData.Now.AddHours(2));

        Assert.True(slot.CanBeHeld(TestData.Now));
    }

    [Fact]
    public void BR6_PastSlot_CannotBeHeld()
    {
        var slot = TestData.Slot(TestData.Now.AddHours(2));

        Assert.True(slot.HasStarted(TestData.Now.AddHours(2)));
        Assert.False(slot.CanBeHeld(TestData.Now.AddHours(3)));
    }

    [Fact]
    public void Create_RejectsZeroCapacity()
    {
        Assert.Throws<DomainException>(() => TestData.Slot(capacity: 0));
    }

    [Fact]
    public void Create_RejectsResourceAndOfferingFromDifferentTenants()
    {
        var (resource, offering) = TestData.Catalog(offeringTenant: Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => Slot.Create(resource, offering, TestData.Now.AddDays(1), 1, TestData.Now));

        Assert.Equal(ErrorCodes.ResourceOfferingMismatch, error.Code);
    }

    [Theory]
    [InlineData(0, 30, true)]   // identical
    [InlineData(15, 45, true)]  // starts inside
    [InlineData(-15, 15, true)] // ends inside
    [InlineData(30, 60, false)] // back to back
    [InlineData(-30, 0, false)] // back to back before
    public void Overlaps_DetectsIntersectingWindows(int startOffsetMinutes, int endOffsetMinutes, bool expected)
    {
        var slot = TestData.Slot(TestData.Now.AddDays(1));

        Assert.Equal(expected, slot.Overlaps(slot.StartAt.AddMinutes(startOffsetMinutes), slot.StartAt.AddMinutes(endOffsetMinutes)));
    }
}
