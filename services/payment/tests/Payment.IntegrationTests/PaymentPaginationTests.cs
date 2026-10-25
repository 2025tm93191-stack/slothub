using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Payment.IntegrationTests;

// Share a single Testcontainers fixture; tests that change chaos settings must not run in parallel.
[CollectionDefinition("Payment API")]
public sealed class PaymentApiCollection : ICollectionFixture<PaymentApiFixture> { }

[Collection("Payment API")]
public class PaymentPaginationTests(PaymentApiFixture fixture)
{
    [Fact]
    public async Task ListPayments_ReturnsStablePagesAndNoDuplicates()
    {
        var client = fixture.Factory.CreateClient();
        var bookingId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            using var charge = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
            {
                Content = JsonContent.Create(new { bookingId, amountMinor = 100L + i, currency = "INR" }),
            };
            charge.Headers.Add("Idempotency-Key", $"pagination-{Guid.NewGuid()}");
            using var response = await client.SendAsync(charge);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var p1 = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments?bookingId={bookingId}&page=1&pageSize=2");
        var p2 = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments?bookingId={bookingId}&page=2&pageSize=2");
        var p3 = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments?bookingId={bookingId}&page=3&pageSize=2");
        var empty = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments?bookingId={bookingId}&page=4&pageSize=2");

        Assert.Equal(2, p1.GetArrayLength());
        Assert.Equal(2, p2.GetArrayLength());
        Assert.Single(p3.EnumerateArray());
        Assert.Equal(0, empty.GetArrayLength());

        var ids = p1.EnumerateArray().Concat(p2.EnumerateArray()).Concat(p3.EnumerateArray())
            .Select(p => p.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(5, ids.Distinct().Count());

        var firstPageAgain = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments?bookingId={bookingId}&page=1&pageSize=2");
        Assert.Equal(p1.ToString(), firstPageAgain.ToString());
    }

    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=-1&pageSize=20")]
    [InlineData("page=1&pageSize=0")]
    [InlineData("page=1&pageSize=101")]
    public async Task ListPayments_InvalidPaging_ReturnsBadRequest(string query)
    {
        var response = await fixture.Factory.CreateClient()
            .GetAsync($"/api/v1/payments?bookingId={Guid.NewGuid()}&{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
