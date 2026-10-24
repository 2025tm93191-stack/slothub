using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Scheduling.Domain.Catalog;
using Scheduling.Domain.Holds;
using Scheduling.Domain.Slots;

namespace Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public const string SlotCapacityCheck = "ck_slots_reserved_within_capacity";
    public const string SlotNoOverlapConstraint = "ex_slots_resource_no_overlap";
    public const string HoldBookingUniqueIndex = "ux_holds_booking_id";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<ServiceOffering> Offerings => Set<ServiceOffering>();

    public DbSet<Slot> Slots => Set<Slot>();

    public DbSet<Hold> Holds => Set<Hold>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<HoldStatus>().HaveConversion<UpperSnakeEnumConverter<HoldStatus>>().HaveMaxLength(32);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TimeZone).HasMaxLength(64);
        });

        modelBuilder.Entity<Resource>(e =>
        {
            e.ToTable("resources");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasMaxLength(50);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<ServiceOffering>(e =>
        {
            e.ToTable("service_offerings");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });

        modelBuilder.Entity<Slot>(e =>
        {
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne<Resource>().WithMany().HasForeignKey(x => x.ResourceId);
            e.HasOne<ServiceOffering>().WithMany().HasForeignKey(x => x.OfferingId);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.ResourceId, x.StartAt });
            e.HasIndex(x => new { x.TenantId, x.StartAt });
            // BR1 second line of defence: the database itself refuses over-allocation.
            e.ToTable(t => t.HasCheckConstraint(SlotCapacityCheck, "reserved_count >= 0 AND reserved_count <= capacity"));
            // The exclusion constraint preventing overlapping slots is created in the migration (needs btree_gist).
        });

        modelBuilder.Entity<Hold>(e =>
        {
            e.HasOne<Slot>().WithMany().HasForeignKey(x => x.SlotId);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName(HoldBookingUniqueIndex);
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.Ignore(x => x.IsActive);
        });
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; never connects.</summary>
public sealed class SchedulingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchedulingDbContext>();
        options.UseServiceDefaults("Host=localhost;Database=scheduling_db;Username=scheduling_user");
        return new SchedulingDbContext(options.Options);
    }
}
