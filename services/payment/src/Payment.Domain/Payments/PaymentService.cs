using BuildingBlocks.Domain;

namespace Payment.Domain.Payments;

/// <summary>Outcome of asking the (mock) gateway to charge a card. An unreachable gateway throws instead.</summary>
public sealed record GatewayResult(bool Approved, string? DeclineReason)
{
    public static GatewayResult Approve() => new(true, null);

    public static GatewayResult Decline(string reason) => new(false, reason);
}

public interface IPaymentGateway
{
    /// <summary>Charges the payment. Throws <see cref="DomainException"/> with kind Unavailable if the gateway errors.</summary>
    Task<GatewayResult> ChargeAsync(Payment payment, CancellationToken ct);
}

public interface IPaymentRepository
{
    Task<Payment?> FindAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Payment>> ListByBookingAsync(Guid bookingId, CancellationToken ct);

    Task AddAsync(Payment payment, CancellationToken ct);

    /// <summary>Loads the payment under a row lock, applies <paramref name="change"/> and saves in one transaction.</summary>
    Task<T> ModifyAsync<T>(Guid id, Func<Payment, T> change, CancellationToken ct);
}

public sealed class PaymentService(IPaymentRepository repository, IPaymentGateway gateway, TimeProvider clock)
{
    /// <summary>
    /// Charges a booking. Returns the stored payment, which is SUCCEEDED or FAILED (declined).
    /// If the gateway is unavailable nothing is stored and a 503-kind error is thrown, so the caller may retry.
    /// </summary>
    /// <remarks>
    /// Once started, a charge is always finished and recorded even if the caller disconnects (e.g. its timeout
    /// fired): abandoning it half-way could take money without a record. The caller's retry, carrying the same
    /// Idempotency-Key, then receives the stored result instead of a second charge.
    /// </remarks>
    public async Task<Payment> ChargeAsync(Guid bookingId, long amountMinor, string currency, CancellationToken ct)
    {
        var payment = Payment.Create(bookingId, amountMinor, currency, clock.GetUtcNow());

        var result = await gateway.ChargeAsync(payment, CancellationToken.None);
        if (result.Approved)
        {
            payment.MarkSucceeded(clock.GetUtcNow());
        }
        else
        {
            payment.MarkFailed(result.DeclineReason ?? "DECLINED", clock.GetUtcNow());
        }

        await repository.AddAsync(payment, CancellationToken.None);
        return payment;
    }

    public async Task<Payment> GetAsync(Guid paymentId, CancellationToken ct) =>
        await repository.FindAsync(paymentId, ct)
        ?? throw DomainException.NotFound(ErrorCodes.PaymentNotFound, $"Payment {paymentId} was not found.");

    public Task<IReadOnlyList<Payment>> ListByBookingAsync(Guid bookingId, CancellationToken ct) =>
        repository.ListByBookingAsync(bookingId, ct);

    public Task<(Payment Payment, Refund Refund)> RefundAsync(Guid paymentId, long amountMinor, CancellationToken ct) =>
        repository.ModifyAsync(paymentId, payment => (payment, payment.Refund(amountMinor, clock.GetUtcNow())), ct);
}
