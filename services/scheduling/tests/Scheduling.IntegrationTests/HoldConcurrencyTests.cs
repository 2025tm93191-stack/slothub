using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scheduling.Domain;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;
using Scheduling.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Scheduling.IntegrationTests;

/// <summary>Real PostgreSQL in a container, migrated with the production migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public SchedulingDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>();
        options.UseServiceDefaults(_container.GetConnectionString());
        return new SchedulingDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public class HoldConcurrencyTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private async Task<(Slot Slot, ServiceOffering Offering)> SeedSlotAsync(int capacity)
    {
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create("Test clinic", "Asia/Kolkata", now);
        var resource = Resource.Create(tenant.Id, "Dr. Test", "person", now);
        var offering = ServiceOffering.Create(tenant.Id, "Consultation", 30, 50_000, "INR", now);
        var slot = Slot.Create(resource, offering, now.AddDays(1), capacity, now);

        await using var db = fixture.NewContext();
        db.AddRange(tenant, resource, offering, slot);
        await db.SaveChangesAsync();
        return (slot, offering);
    }

    private async Task<ReserveOutcome> ReserveAsync(Slot slot, ServiceOffering offering, DateTimeOffset now, TimeSpan ttl)
    {
        await using var db = fixture.NewContext();
        var repository = new HoldRepository(db, NullLogger<HoldRepository>.Instance);
        return await repository.ReserveAsync(Hold.Create(slot, offering, Guid.NewGuid(), ttl, now), now, CancellationToken.None);
    }

    private async Task<(int Reserved, Dictionary<HoldStatus, int> Holds)> StateAsync(Guid slotId)
    {
        await using var db = fixture.NewContext();
        var reserved = await db.Slots.Where(s => s.Id == slotId).Select(s => s.ReservedCount).SingleAsync();
        var holds = await db.Holds.Where(h => h.SlotId == slotId).GroupBy(h => h.Status).ToDictionaryAsync(g => g.Key, g => g.Count());
        return (reserved, holds);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(5, 50)]
    public async Task BR1_ParallelHolds_NeverExceedCapacity(int capacity, int requests)
    {
        var (slot, offering) = await SeedSlotAsync(capacity);
        var now = DateTimeOffset.UtcNow;

        var outcomes = await Task.WhenAll(Enumerable.Range(0, requests).Select(_ => Task.Run(() => ReserveAsync(slot, offering, now, Ttl))));

        Assert.Equal(capacity, outcomes.Count(o => o == ReserveOutcome.Reserved));
        Assert.Equal(requests - capacity, outcomes.Count(o => o == ReserveOutcome.SlotUnavailable));
        var (reserved, holds) = await StateAsync(slot.Id);
        Assert.Equal(capacity, reserved);
        Assert.Equal(capacity, holds[HoldStatus.Held]);
    }

    [Fact]
    public async Task BR2_ExpirySweep_ReleasesDueHolds_Once_AndNeverTouchesConfirmed()
    {
        var (slot, offering) = await SeedSlotAsync(capacity: 3);
        var past = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.Equal(ReserveOutcome.Reserved, await ReserveAsync(slot, offering, past, TimeSpan.FromSeconds(1))); // will expire
        Assert.Equal(ReserveOutcome.Reserved, await ReserveAsync(slot, offering, past, TimeSpan.FromSeconds(1))); // confirmed first
        Assert.Equal(ReserveOutcome.Reserved, await ReserveAsync(slot, offering, DateTimeOffset.UtcNow, Ttl));    // not yet due

        await using (var db = fixture.NewContext())
        {
            var toConfirm = await db.Holds.Where(h => h.SlotId == slot.Id).OrderBy(h => h.CreatedAt).Skip(1).FirstAsync();
            await db.Holds.Where(h => h.Id == toConfirm.Id).ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Confirmed));
        }

        int first, second;
        await using (var db = fixture.NewContext())
        {
            first = await new HoldRepository(db, NullLogger<HoldRepository>.Instance).ExpireDueAsync(DateTimeOffset.UtcNow, 100, CancellationToken.None);
        }

        await using (var db = fixture.NewContext())
        {
            second = await new HoldRepository(db, NullLogger<HoldRepository>.Instance).ExpireDueAsync(DateTimeOffset.UtcNow, 100, CancellationToken.None);
        }

        var (reserved, holds) = await StateAsync(slot.Id);
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(2, reserved);
        Assert.Equal(1, holds[HoldStatus.Expired]);
        Assert.Equal(1, holds[HoldStatus.Confirmed]);
        Assert.Equal(1, holds[HoldStatus.Held]);
    }

    [Fact]
    public async Task BR2_ConcurrentSweepers_ReleaseEachHoldExactlyOnce()
    {
        var (slot, offering) = await SeedSlotAsync(capacity: 10);
        var past = DateTimeOffset.UtcNow.AddMinutes(-5);
        for (var i = 0; i < 10; i++)
        {
            await ReserveAsync(slot, offering, past, TimeSpan.FromSeconds(1));
        }

        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.NewContext();
            return await new HoldRepository(db, NullLogger<HoldRepository>.Instance).ExpireDueAsync(DateTimeOffset.UtcNow, 100, CancellationToken.None);
        })));

        var (reserved, holds) = await StateAsync(slot.Id);
        Assert.Equal(10, counts.Sum());
        Assert.Equal(0, reserved);
        Assert.Equal(10, holds[HoldStatus.Expired]);
    }
}
