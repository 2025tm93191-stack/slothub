namespace Scheduling.Domain.Catalog;

/// <summary>A business using SlotHub (clinic, salon, turf...). Every other record belongs to one tenant.</summary>
public sealed class Tenant
{
    private Tenant()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string TimeZone { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(string name, string timeZone, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), Name = name.Trim(), TimeZone = timeZone, CreatedAt = now };
}

/// <summary>Something that can be booked: a doctor, a court, a room, a class.</summary>
public sealed class Resource
{
    private Resource()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Type { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Resource Create(Guid tenantId, string name, string type, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), TenantId = tenantId, Name = name.Trim(), Type = type.Trim().ToUpperInvariant(), CreatedAt = now };
}

/// <summary>What the customer pays for, e.g. "Teeth cleaning, 30 min, ₹500". Prices are in minor units (paise).</summary>
public sealed class ServiceOffering
{
    private ServiceOffering()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public int DurationMinutes { get; private set; }

    public long PriceMinor { get; private set; }

    public string Currency { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static ServiceOffering Create(Guid tenantId, string name, int durationMinutes, long priceMinor, string currency, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = name.Trim(),
            DurationMinutes = durationMinutes,
            PriceMinor = priceMinor,
            Currency = currency.ToUpperInvariant(),
            CreatedAt = now,
        };
}
