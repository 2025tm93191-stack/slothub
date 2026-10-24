using BuildingBlocks.Domain;
using Scheduling.Domain.Holds;

namespace Scheduling.UnitTests;

public class HoldTests
{
    [Fact]
    public void NewHold_IsHeld_WithPriceSnapshot_AndExpiry()
    {
        var hold = TestData.Hold();

        Assert.Equal(HoldStatus.Held, hold.Status);
        Assert.Equal(50_000, hold.PriceMinor);
        Assert.Equal("INR", hold.Currency);
        Assert.Equal(TestData.Now + TestData.HoldTtl, hold.ExpiresAt);
    }

    [Fact]
    public void BR2_HeldHold_CanBeConfirmed_BeforeExpiry()
    {
        var hold = TestData.Hold();

        hold.Confirm(TestData.Now.AddSeconds(29));

        Assert.Equal(HoldStatus.Confirmed, hold.Status);
    }

    [Fact]
    public void BR2_HeldHold_CannotBeConfirmed_OnceTtlHasPassed()
    {
        var hold = TestData.Hold();

        var error = Assert.Throws<DomainException>(() => hold.Confirm(TestData.Now.AddSeconds(30)));

        Assert.Equal(ErrorCodes.HoldExpired, error.Code);
        Assert.Equal(HoldStatus.Held, hold.Status);
    }

    [Fact]
    public void Confirm_IsIdempotent_ForAnAlreadyConfirmedHold()
    {
        var hold = TestData.Hold();
        hold.Confirm(TestData.Now);

        hold.Confirm(TestData.Now.AddMinutes(5));

        Assert.Equal(HoldStatus.Confirmed, hold.Status);
    }

    [Fact]
    public void ReleasedHold_CannotBeConfirmed()
    {
        var hold = TestData.Hold();
        hold.Release(TestData.Now);

        var error = Assert.Throws<InvalidStateTransitionException>(() => hold.Confirm(TestData.Now));

        Assert.Equal(ErrorCodes.InvalidStateTransition, error.Code);
    }

    [Fact]
    public void ExpiredHold_CannotBeConfirmed()
    {
        var hold = TestData.Hold();
        hold.Expire(TestData.Now.AddMinutes(1));

        var error = Assert.Throws<DomainException>(() => hold.Confirm(TestData.Now.AddMinutes(1)));

        Assert.Equal(ErrorCodes.HoldExpired, error.Code);
    }

    [Fact]
    public void Release_FromHeld_ReturnsCapacity()
    {
        var hold = TestData.Hold();

        Assert.True(hold.Release(TestData.Now));
        Assert.Equal(HoldStatus.Released, hold.Status);
    }

    [Fact]
    public void Release_FromConfirmed_ReturnsCapacity()
    {
        var hold = TestData.Hold();
        hold.Confirm(TestData.Now);

        Assert.True(hold.Release(TestData.Now));
        Assert.Equal(HoldStatus.Released, hold.Status);
    }

    [Fact]
    public void Release_Twice_ReturnsCapacityOnlyOnce()
    {
        var hold = TestData.Hold();

        Assert.True(hold.Release(TestData.Now));
        Assert.False(hold.Release(TestData.Now));
    }

    [Fact]
    public void BR2_Expire_ReleasesHeldHold_AfterTtl()
    {
        var hold = TestData.Hold();

        Assert.True(hold.Expire(TestData.Now.AddSeconds(30)));
        Assert.Equal(HoldStatus.Expired, hold.Status);
    }

    [Fact]
    public void BR2_Expire_BeforeTtl_DoesNothing()
    {
        var hold = TestData.Hold();

        Assert.False(hold.Expire(TestData.Now.AddSeconds(29)));
        Assert.Equal(HoldStatus.Held, hold.Status);
    }

    [Fact]
    public void BR2_ConfirmedHold_IsNeverExpired()
    {
        var hold = TestData.Hold();
        hold.Confirm(TestData.Now);

        Assert.False(hold.Expire(TestData.Now.AddHours(1)));
        Assert.Equal(HoldStatus.Confirmed, hold.Status);
    }

    [Fact]
    public void BR2_Expire_Twice_ReturnsCapacityOnlyOnce()
    {
        var hold = TestData.Hold();
        var later = TestData.Now.AddMinutes(1);

        Assert.True(hold.Expire(later));
        Assert.False(hold.Expire(later));
    }
}
