using BuildingBlocks.Idempotency;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Payment.Domain.Payments;
using PaymentEntity = Payment.Domain.Payments.Payment;

namespace Payment.Infrastructure.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    public DbSet<Refund> Refunds => Set<Refund>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<UpperSnakeEnumConverter<PaymentStatus>>().HaveMaxLength(32);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEntity>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.FailureReason).HasMaxLength(200);
            e.HasIndex(x => x.BookingId);
            e.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.PaymentId);
            e.Navigation(x => x.Refunds).HasField("_refunds").AutoInclude();
            e.Ignore(x => x.RefundableMinor);
            e.ToTable(t => t.HasCheckConstraint("ck_payments_refund_within_amount", "refunded_minor >= 0 AND refunded_minor <= amount_minor"));
        });

        modelBuilder.Entity<Refund>(e => e.Property(x => x.Id).ValueGeneratedNever());
        modelBuilder.MapIdempotencyRecords();
    }
}

/// <summary>Used only by <c>dotnet ef</c> to create migrations; never connects.</summary>
public sealed class PaymentDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>();
        options.UseServiceDefaults("Host=localhost;Database=payment_db;Username=payment_user");
        return new PaymentDbContext(options.Options);
    }
}
