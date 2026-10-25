using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Notification.Domain.Messages;

namespace Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public const string MessageDedupIndex = "ux_messages_booking_event_channel";

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<EventType>().HaveConversion<UpperSnakeEnumConverter<EventType>>().HaveMaxLength(32);
        configurationBuilder.Properties<Channel>().HaveConversion<UpperSnakeEnumConverter<Channel>>().HaveMaxLength(16);
        configurationBuilder.Properties<MessageStatus>().HaveConversion<UpperSnakeEnumConverter<MessageStatus>>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Recipient).HasMaxLength(320);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.LastError).HasMaxLength(500);
            // Duplicate suppression: one message per booking, event and channel.
            e.HasIndex(x => new { x.BookingId, x.EventType, x.Channel }).IsUnique().HasDatabaseName(MessageDedupIndex);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasMany(x => x.DeliveryAttempts).WithOne().HasForeignKey(x => x.MessageId);
            e.Navigation(x => x.DeliveryAttempts).HasField("_attempts");
            e.Ignore(x => x.IsFinal);
        });

        modelBuilder.Entity<DeliveryAttempt>(e => e.Property(x => x.Error).HasMaxLength(500));
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; never connects.</summary>
public sealed class NotificationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>();
        options.UseServiceDefaults("Host=localhost;Database=notification_db;Username=notification_user");
        return new NotificationDbContext(options.Options);
    }
}
