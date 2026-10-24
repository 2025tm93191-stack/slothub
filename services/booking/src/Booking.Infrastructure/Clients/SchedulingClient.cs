using System.Net.Http.Json;
using Booking.Domain.Bookings;

namespace Booking.Infrastructure.Clients;

internal sealed class SchedulingClient(HttpClient http) : ISchedulingClient
{
    private const string Service = "Scheduling";

    public Task<HoldInfo> CreateHoldAsync(Guid slotId, Guid bookingId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/v1/holds", new { slotId, bookingId }, ct);

    public Task<HoldInfo> GetHoldAsync(Guid holdId, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"api/v1/holds/{holdId}", null, ct);

    public Task<HoldInfo> ConfirmHoldAsync(Guid holdId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/v1/holds/{holdId}/confirm", null, ct);

    public Task<HoldInfo> ReleaseHoldAsync(Guid holdId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/v1/holds/{holdId}/release", null, ct);

    private async Task<HoldInfo> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: DownstreamHttp.Json);
        }

        using var response = await DownstreamHttp.SendAsync(http, request, Service, ct);
        return await DownstreamHttp.ReadAsync<HoldInfo>(response, ct);
    }
}
