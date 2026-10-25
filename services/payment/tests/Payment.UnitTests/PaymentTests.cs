using BuildingBlocks.Domain;
using Payment.Domain.Payments;
using PaymentEntity = Payment.Domain.Payments.Payment;

namespace Payment.UnitTests;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static PaymentEntity Succeeded(long amount = 50_000)
    {
        var payment = PaymentEntity.Create(Guid.NewGuid(), amount, "inr", Now);
        payment.MarkSucceeded(Now);
        return payment;
    }

    [Fact]
    public void Create_StartsPending_WithUpperCaseCurrency()
    {
        var payment = PaymentEntity.Create(Guid.NewGuid(), 50_000, "inr", Now);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("INR", payment.Currency);
    }

    [Fact]
    public void Create_RejectsNonPositiveAmount()
    {
        Assert.Throws<DomainException>(() => PaymentEntity.Create(Guid.NewGuid(), 0, "INR", Now));
    }

    [Fact]
    public void MarkFailed_StoresReason()
    {
        var payment = PaymentEntity.Create(Guid.NewGuid(), 50_000, "INR", Now);

        payment.MarkFailed("CARD_DECLINED", Now);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("CARD_DECLINED", payment.FailureReason);
    }

    [Fact]
    public void SettledPayment_CannotChangeOutcome()
    {
        var payment = Succeeded();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkFailed("late", Now));
        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkSucceeded(Now));
    }

    [Fact]
    public void PartialRefund_ThenFullRefund_UpdatesStatus()
    {
        var payment = Succeeded(50_000);

        payment.Refund(25_000, Now);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);

        payment.Refund(25_000, Now);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(50_000, payment.RefundedMinor);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void Refund_CannotExceedRemainingAmount()
    {
        var payment = Succeeded(50_000);
        payment.Refund(40_000, Now);

        var error = Assert.Throws<DomainException>(() => payment.Refund(10_001, Now));

        Assert.Equal(ErrorCodes.RefundExceedsPayment, error.Code);
        Assert.Equal(40_000, payment.RefundedMinor);
    }

    [Fact]
    public void FailedPayment_CannotBeRefunded()
    {
        var payment = PaymentEntity.Create(Guid.NewGuid(), 50_000, "INR", Now);
        payment.MarkFailed("CARD_DECLINED", Now);

        var error = Assert.Throws<DomainException>(() => payment.Refund(1, Now));

        Assert.Equal(ErrorCodes.PaymentNotRefundable, error.Code);
    }

    [Fact]
    public void FullyRefundedPayment_CannotBeRefundedAgain()
    {
        var payment = Succeeded(50_000);
        payment.Refund(50_000, Now);

        var error = Assert.Throws<DomainException>(() => payment.Refund(1, Now));

        Assert.Equal(ErrorCodes.PaymentNotRefundable, error.Code);
    }
}
