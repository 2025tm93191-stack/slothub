using BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Payments;
using PaymentEntity = Payment.Domain.Payments.Payment;

namespace Payment.Infrastructure.Persistence;

internal sealed class PaymentRepository(PaymentDbContext db) : IPaymentRepository
{
    public Task<PaymentEntity?> FindAsync(Guid id, CancellationToken ct) =>
        db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<PaymentEntity>> ListByBookingAsync(Guid bookingId, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(x => x.BookingId == bookingId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(PaymentEntity payment, CancellationToken ct)
    {
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<T> ModifyAsync<T>(Guid id, Func<PaymentEntity, T> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Row lock so two concurrent refunds cannot both pass the "refund <= remaining" check.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM payments WHERE id = {id} FOR UPDATE", ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw DomainException.NotFound(ErrorCodes.PaymentNotFound, $"Payment {id} was not found.");

        var result = change(payment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
