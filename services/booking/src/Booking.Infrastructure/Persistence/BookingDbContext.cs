using Booking.Domain.Bookings;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using BookingEntity = Booking.Domain.Bookings.Booking;

namespace Booking.Infrastructure.Persistence;

/// <summary>A notification waiting to be delivered to the Notification service (transactional outbox).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = null!;

    public string Payload { get; set; } = null!;

    /// <summary>Correlation ID of the request that caused the message, forwarded so the delivery can be traced end to end.</summary>
    public string? CorrelationId { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public const string LockedUntil = "LockedUntil";
    public const string CorrelationIdColumn = "CorrelationId";

    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();

    public DbSet<BookingStatusChange> StatusHistory => Set<BookingStatusChange>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<BookingStatus>().HaveConversion<UpperSnakeEnumConverter<BookingStatus>>().HaveMaxLength(32);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookingEntity>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CustomerId).HasMaxLength(100);
            e.Property(x => x.CustomerPhone).HasMaxLength(20);
            e.Property(x => x.CustomerEmail).HasMaxLength(320);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.FailureReason).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            // Operation lease (see IBookingRepository.TryAcquireLeaseAsync); infrastructure detail, not part of the domain.
            e.Property<DateTimeOffset?>(LockedUntil);
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt });
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.BookingId);
            e.Navigation(x => x.History).HasField("_history");
            e.Ignore(x => x.PendingNotifications);
        });

        modelBuilder.Entity<BookingStatusChange>(e =>
        {
            e.ToTable("booking_status_history");
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property<string?>(CorrelationIdColumn).HasMaxLength(100);
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.Property(x => x.Type).HasMaxLength(64);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
        });

        modelBuilder.MapIdempotencyRecords();
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; never connects.</summary>
public sealed class BookingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>();
        options.UseServiceDefaults("Host=localhost;Database=booking_db;Username=booking_user");
        return new BookingDbContext(options.Options);
    }
}
