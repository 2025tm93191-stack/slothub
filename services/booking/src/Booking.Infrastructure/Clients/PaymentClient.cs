using System.Net;
using System.Net.Http.Json;
using Booking.Domain.Bookings;
using BuildingBlocks.Domain;
using BuildingBlocks.Idempotency;
using Microsoft.Extensions.Logging;

namespace Booking.Infrastructure.Clients;

/// <summary>
/// Calls Payment with deterministic idempotency keys: <c>pay-{bookingId}</c> and <c>refund-{bookingId}</c>.
/// Retries by the resilience pipeline (timeouts, 5xx) therefore replay instead of charging twice.
/// If an earlier attempt is still running on the Payment side (409 REQUEST_IN_PROGRESS), wait and ask again.
/// </summary>
internal sealed class PaymentClient(HttpClient http, TimeProvider clock, ILogger<PaymentClient> logger) : IPaymentClient
{
    private const string Service = "Payment";
    private const int InProgressPolls = 10;

    private sealed record PaymentDto(Guid Id, string Status, string? FailureReason);

    public async Task<PaymentOutcome> ChargeAsync(Guid bookingId, long amountMinor, string currency, CancellationToken ct)
    {
        using var response = await SendWithInProgressPollingAsync(
            () => Request(HttpMethod.Post, "api/v1/payments", $"pay-{bookingId}", new { bookingId, amountMinor, currency }),
            ct);

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            var problem = await DownstreamHttp.ReadProblemAsync(response, ct);
            return new PaymentOutcome(problem?.PaymentId ?? Guid.Empty, Approved: false, problem?.Detail);
        }

        var payment = await DownstreamHttp.ReadAsync<PaymentDto>(response, ct);
        return new PaymentOutcome(payment.Id, Approved: true, null);
    }

    public async Task RefundAsync(Guid paymentId, Guid bookingId, long amountMinor, CancellationToken ct)
    {
        using var response = await SendWithInProgressPollingAsync(
            () => Request(HttpMethod.Post, $"api/v1/payments/{paymentId}/refunds", $"refund-{bookingId}", new { amountMinor }),
            ct);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: DownstreamHttp.Json) };
        request.Headers.Add(IdempotencyMiddleware.HeaderName, idempotencyKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendWithInProgressPollingAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        for (var poll = 1; ; poll++)
        {
            using var request = createRequest();
            try
            {
                return await DownstreamHttp.SendAsync(http, request, Service, ct);
            }
            catch (DomainException ex) when (ex.Code == ErrorCodes.RequestInProgress && poll < InProgressPolls)
            {
                logger.LogInformation("Payment still processing an earlier attempt (poll {Poll}); waiting", poll);
                await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
            }
            catch (DomainException ex) when (ex.Code == ErrorCodes.RequestInProgress)
            {
                throw new DomainException(ErrorKind.Timeout, ErrorCodes.DownstreamTimeout, "Payment is still processing; retry later.");
            }
        }
    }
}
