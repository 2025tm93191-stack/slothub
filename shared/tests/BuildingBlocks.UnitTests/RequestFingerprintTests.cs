using BuildingBlocks.Idempotency;

namespace BuildingBlocks.UnitTests;

public class RequestFingerprintTests
{
    private const string Path = "/api/v1/bookings";

    [Fact]
    public void BR4_SamePayload_GivesSameFingerprint_RegardlessOfFormatting()
    {
        var compact = RequestFingerprint.Compute("POST", Path, """{"slotId":"a","customer":{"id":"c1","phone":"+91"}}""");
        var reordered = RequestFingerprint.Compute("post", Path.ToUpperInvariant(), """
            {
              "customer": { "phone": "+91", "id": "c1" },
              "slotId": "a"
            }
            """);

        Assert.Equal(compact, reordered);
    }

    [Fact]
    public void BR4_DifferentPayload_GivesDifferentFingerprint()
    {
        var first = RequestFingerprint.Compute("POST", Path, """{"slotId":"a"}""");
        var second = RequestFingerprint.Compute("POST", Path, """{"slotId":"b"}""");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void BR4_DifferentPath_GivesDifferentFingerprint()
    {
        Assert.NotEqual(
            RequestFingerprint.Compute("POST", "/api/v1/bookings/1/payment", string.Empty),
            RequestFingerprint.Compute("POST", "/api/v1/bookings/2/payment", string.Empty));
    }

    [Fact]
    public void ArrayOrder_IsSignificant()
    {
        Assert.NotEqual(
            RequestFingerprint.Compute("POST", Path, "[1,2]"),
            RequestFingerprint.Compute("POST", Path, "[2,1]"));
    }

    [Fact]
    public void NonJsonBody_IsHashedAsText()
    {
        Assert.Equal(
            RequestFingerprint.Compute("POST", Path, "not json "),
            RequestFingerprint.Compute("POST", Path, "not json"));
    }
}
