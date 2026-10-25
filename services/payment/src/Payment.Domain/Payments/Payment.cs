using BuildingBlocks.Domain;

namespace Payment.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    PartiallyRefunded,
    Refunded,
}

/// <summary>
/// A charge for one booking. Lifecycle: PENDING → SUCCEEDED | FAILED; SUCCEEDED → PARTIALLY_REFUNDED → REFUNDED.
/// Total refunds can never exceed the charged amount.
/// </summary>
public sealed class Payment
{
    private readonly List<Refund> _refunds = [];

    private Payment()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Reference to the booking in the Booking service (no foreign key across services).</summary>
    public Guid BookingId { get; private set; }

    public long AmountMinor { get; private set; }

    public string Currency { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    public long RefundedMinor { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<Refund> Refunds => _refunds;

    public long RefundableMinor => AmountMinor - RefundedMinor;

    public static Payment Create(Guid bookingId, long amountMinor, string currency, DateTimeOffset now)
    {
        if (amountMinor <= 0)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "Amount must be greater than zero.");
        }

        return new Payment
        {
            Id = Guid.CreateVersion7(),
            BookingId = bookingId,
            AmountMinor = amountMinor,
            Currency = currency.ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkSucceeded(DateTimeOffset now)
    {
        EnsurePending(PaymentStatus.Succeeded);
        Status = PaymentStatus.Succeeded;
        UpdatedAt = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        EnsurePending(PaymentStatus.Failed);
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = now;
    }

    /// <summary>Refunds part or all of a successful payment.</summary>
    public Refund Refund(long amountMinor, DateTimeOffset now)
    {
        if (amountMinor <= 0)
        {
            throw DomainException.Validation(ErrorCodes.ValidationFailed, "Refund amount must be greater than zero.");
        }

        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
        {
            throw DomainException.Conflict(ErrorCodes.PaymentNotRefundable, $"Payment {Id} is {Status} and cannot be refunded.");
        }

        if (amountMinor > RefundableMinor)
        {
            throw DomainException.Conflict(
                ErrorCodes.RefundExceedsPayment,
                $"Refund of {amountMinor} exceeds the refundable amount {RefundableMinor} of payment {Id}.");
        }

        var refund = Payments.Refund.Create(Id, amountMinor, now);
        _refunds.Add(refund);
        RefundedMinor += amountMinor;
        Status = RefundedMinor == AmountMinor ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
        return refund;
    }

    private void EnsurePending(PaymentStatus next)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidStateTransitionException(nameof(Payment), Status, next);
        }
    }
}

public sealed class Refund
{
    private Refund()
    {
    }

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public long AmountMinor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Refund Create(Guid paymentId, long amountMinor, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), PaymentId = paymentId, AmountMinor = amountMinor, CreatedAt = now };
}
