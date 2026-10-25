using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Payment.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Payment.IntegrationTests;

/// <summary>The real Payment API (idempotency middleware included) on a real PostgreSQL.</summary>
public sealed class PaymentApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Configuration comes from environment variables, exactly as in production.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("DB_MIGRATE_ON_STARTUP", "true");
        Environment.SetEnvironmentVariable("CHAOS_FAILURE_RATE", "0");
        Environment.SetEnvironmentVariable("CHAOS_LATENCY_MS", "200"); // widen the window for concurrent duplicates
        Factory = new WebApplicationFactory<Program>();
        _ = Factory.Server; // starts the host, which applies migrations
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public int CountPayments(Guid bookingId)
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Payments.Count(p => p.BookingId == bookingId);
    }
}

[Collection("Payment API")]
public class IdempotencyTests(PaymentApiFixture fixture)
{
    private static HttpRequestMessage Charge(Guid bookingId, string key, long amount = 50_000)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = JsonContent.Create(new { bookingId, amountMinor = amount, currency = "INR" }),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task BR4_Repeat_ReplaysOriginalResponse_WithoutSecondCharge()
    {
        var client = fixture.Factory.CreateClient();
        var bookingId = Guid.NewGuid();
        var key = $"it-{Guid.NewGuid()}";

        var first = await client.SendAsync(Charge(bookingId, key));
        var second = await client.SendAsync(Charge(bookingId, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, fixture.CountPayments(bookingId));
    }

    [Fact]
    public async Task BR4_SameKeyDifferentBody_Returns422()
    {
        var client = fixture.Factory.CreateClient();
        var bookingId = Guid.NewGuid();
        var key = $"it-{Guid.NewGuid()}";

        await client.SendAsync(Charge(bookingId, key, 50_000));
        var mismatch = await client.SendAsync(Charge(bookingId, key, 99_000));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", await CodeAsync(mismatch));
        Assert.Equal(1, fixture.CountPayments(bookingId));
    }

    [Fact]
    public async Task BR4_MissingKey_Returns400()
    {
        var request = Charge(Guid.NewGuid(), "x");
        request.Headers.Remove("Idempotency-Key");

        var response = await fixture.Factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", await CodeAsync(response));
    }

    [Fact]
    public async Task BR4_SimultaneousIdenticalRequests_ChargeExactlyOnce()
    {
        var client = fixture.Factory.CreateClient();
        var bookingId = Guid.NewGuid();
        var key = $"it-{Guid.NewGuid()}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.SendAsync(Charge(bookingId, key))));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("REQUEST_IN_PROGRESS", await CodeAsync(conflict));
        }

        Assert.Equal(1, fixture.CountPayments(bookingId));
    }

    [Fact]
    public async Task BR4_ServerError_ReleasesKey_SoTheRetrySucceeds()
    {
        var client = fixture.Factory.CreateClient();
        var bookingId = Guid.NewGuid();
        var key = $"it-{Guid.NewGuid()}";

        await client.PutAsJsonAsync("/api/v1/admin/chaos", new { failureRate = 1.0, latencyMs = 0, mode = "ERROR" });
        var failed = await client.SendAsync(Charge(bookingId, key));
        await client.PutAsJsonAsync("/api/v1/admin/chaos", new { failureRate = 0.0, latencyMs = 200, mode = "DECLINE" });
        var retried = await client.SendAsync(Charge(bookingId, key));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, fixture.CountPayments(bookingId));
    }
}
