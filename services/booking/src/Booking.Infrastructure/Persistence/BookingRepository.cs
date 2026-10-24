using System.Text.Json;
using System.Text.Json.Serialization;
using Booking.Domain.Bookings;
using BuildingBlocks.Correlation;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Booking.Domain.Bookings.Booking;

namespace Booking.Infrastructure.Persistence;

internal sealed class BookingRepository(BookingDbContext db, IHttpContextAccessor http, TimeProvider clock) : IBookingRepository
{
    internal static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public async Task AddAsync(BookingEntity booking, CancellationToken ct)
    {
        db.Bookings.Add(booking);
        await SaveAsync(booking, ct);
    }

    public async Task SaveAsync(BookingEntity booking, CancellationToken ct)
    {
        foreach (var notification in booking.PendingNotifications)
        {
            db.Outbox.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = JsonNamingPolicy.SnakeCaseUpper.ConvertName(notification.EventType.ToString()),
                Payload = JsonSerializer.Serialize(notification, PayloadJson),
                CreatedAt = clock.GetUtcNow(),
                NextAttemptAt = clock.GetUtcNow(),
            });
        }

        var correlationId = http.HttpContext is { } context ? CorrelationId.Get(context) : null;
        foreach (var entry in db.ChangeTracker.Entries<BookingStatusChange>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(BookingDbContext.CorrelationIdColumn).CurrentValue = correlationId;
        }

        try
        {
            // Booking row, status history and outbox messages are written in one transaction.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict(ErrorCodes.BookingBusy, $"Booking {booking.Id} was changed by another request. Retry.");
        }

        booking.ClearPendingNotifications();
    }

    public Task<BookingEntity?> FindAsync(Guid id, CancellationToken ct) =>
        db.Bookings.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<BookingEntity?> GetReadOnlyAsync(Guid id, CancellationToken ct) =>
        db.Bookings.AsNoTracking().Include(x => x.History).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<PagedResult<BookingEntity>> ListAsync(BookingQuery query, CancellationToken ct)
    {
        var bookings = db.Bookings.AsNoTracking();
        if (query.CustomerId is { } customerId)
        {
            bookings = bookings.Where(x => x.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            bookings = bookings.Where(x => x.Status == status);
        }

        var total = await bookings.CountAsync(ct);
        var items = await bookings
            .OrderByDescending(x => x.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return new PagedResult<BookingEntity>(items, query.Page, query.PageSize, total);
    }

    public async Task<bool> TryAcquireLeaseAsync(Guid id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct) =>
        await db.Bookings
            .Where(x => x.Id == id
                && (EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil) == null
                    || EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil) < now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil), until), ct) == 1;

    public Task ReleaseLeaseAsync(Guid id) =>
        db.Bookings
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil), (DateTimeOffset?)null));

    public async Task<IReadOnlyList<Guid>> FindExpiryCandidatesAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Where(x => x.Status == BookingStatus.Held && x.HoldExpiresAt <= now
                && (EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil) == null
                    || EF.Property<DateTimeOffset?>(x, BookingDbContext.LockedUntil) < now))
            .OrderBy(x => x.HoldExpiresAt)
            .Select(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);
}
