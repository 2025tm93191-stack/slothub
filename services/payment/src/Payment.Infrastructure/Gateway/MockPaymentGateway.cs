using BuildingBlocks.Chaos;
using BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;
using Payment.Domain.Payments;
using PaymentEntity = Payment.Domain.Payments.Payment;

namespace Payment.Infrastructure.Gateway;

/// <summary>
/// Stand-in for a real card gateway. Latency and failures are driven by <see cref="ChaosState"/>:
/// mode DECLINE returns a declined result (402), mode ERROR simulates the gateway being down (503).
/// </summary>
internal sealed class MockPaymentGateway(ChaosState chaos, ILogger<MockPaymentGateway> logger) : IPaymentGateway
{
    public const string DeclineMode = "DECLINE";
    public const string ErrorMode = "ERROR";

    public async Task<GatewayResult> ChargeAsync(PaymentEntity payment, CancellationToken ct)
    {
        await chaos.DelayAsync(ct);

        if (chaos.ShouldFail())
        {
            if (chaos.Current.Mode == ErrorMode)
            {
                logger.LogWarning("Gateway error (simulated) while charging booking {BookingId}", payment.BookingId);
                throw new DomainException(ErrorKind.Unavailable, ErrorCodes.PaymentGatewayUnavailable, "The payment gateway is unavailable. Retry later.");
            }

            logger.LogInformation("Gateway declined {AmountMinor} {Currency} for booking {BookingId} (simulated)", payment.AmountMinor, payment.Currency, payment.BookingId);
            return GatewayResult.Decline("CARD_DECLINED");
        }

        logger.LogInformation("Gateway approved {AmountMinor} {Currency} for booking {BookingId}", payment.AmountMinor, payment.Currency, payment.BookingId);
        return GatewayResult.Approve();
    }
}
