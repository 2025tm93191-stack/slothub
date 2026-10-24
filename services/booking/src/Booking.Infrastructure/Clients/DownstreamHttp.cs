using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Domain;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Booking.Infrastructure.Clients;

/// <summary>Problem Details as returned by every SlotHub service.</summary>
internal sealed record DownstreamProblem(int? Status, string? Code, string? Detail, Guid? PaymentId);

/// <summary>
/// Sends a request through the service's resilience pipeline and turns every failure into a <see cref="DomainException"/>:
/// 4xx keep the downstream error code; 5xx, timeouts, open circuits and network errors become
/// DOWNSTREAM_UNAVAILABLE / DOWNSTREAM_TIMEOUT (or the downstream code, e.g. PAYMENT_GATEWAY_UNAVAILABLE).
/// </summary>
internal static class DownstreamHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, string service, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (TimeoutRejectedException)
        {
            throw new DomainException(ErrorKind.Timeout, ErrorCodes.DownstreamTimeout, $"{service} did not respond in time.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DomainException(ErrorKind.Timeout, ErrorCodes.DownstreamTimeout, $"{service} did not respond in time.");
        }
        catch (BrokenCircuitException)
        {
            throw new DomainException(ErrorKind.Unavailable, ErrorCodes.DownstreamCircuitOpen,
                $"{service} failed repeatedly; calls are paused for a short time (circuit open). Retry shortly.");
        }
        catch (HttpRequestException ex)
        {
            throw new DomainException(ErrorKind.Unavailable, ErrorCodes.DownstreamUnavailable, $"{service} is unreachable: {ex.Message}");
        }

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            return response;
        }

        using (response)
        {
            var problem = await ReadProblemAsync(response, ct);
            var status = (int)response.StatusCode;
            var code = problem?.Code ?? (status >= 500 ? ErrorCodes.DownstreamUnavailable : ErrorCodes.BadRequest);
            var detail = $"{service}: {problem?.Detail ?? response.ReasonPhrase}";
            throw status switch
            {
                >= 500 => new DomainException(ErrorKind.Unavailable, code, detail),
                404 => DomainException.NotFound(code, detail),
                409 => DomainException.Conflict(code, detail),
                422 => new DomainException(ErrorKind.Unprocessable, code, detail),
                _ => DomainException.Validation(code, detail),
            };
        }
    }

    public static async Task<DownstreamProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<DownstreamProblem>(Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(Json, ct)
        ?? throw new DomainException(ErrorKind.Unavailable, ErrorCodes.DownstreamUnavailable, "Empty response from downstream service.");
}
