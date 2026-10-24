using BuildingBlocks.Domain;
using Microsoft.Extensions.Logging;

namespace Booking.Domain.Bookings;

public sealed class BookingOptions
{
    /// <summary>Refuse to start a payment if the hold expires within this margin (env <c>HOLD_SAFETY_MARGIN_SECONDS</c>).</summary>
    public TimeSpan HoldSafetyMargin { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Upper bound for one pay/cancel/reconcile operation on a booking (env <c>BOOKING_LEASE_SECONDS</c>).</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed record NewBooking(Guid TenantId, Guid SlotId, CustomerContact Customer);

/// <summary>
/// Orchestrates the booking transaction across Scheduling, Payment and (via the outbox) Notification.
/// Booking is the only orchestrator; the other services expose simple, idempotent capabilities.
/// </summary>
public sealed class BookingService(
    IBookingRepository repository,
    ISchedulingClient scheduling,
    IPaymentClient payments,
    BookingOptions options,
    TimeProvider clock,
    ILogger<BookingService> logger)
{
    /// <summary>Step 1: create the booking (PENDING) and hold one unit of the slot → HELD, or REJECTED if unavailable.</summary>
    public async Task<Booking> CreateAsync(NewBooking request, CancellationToken ct)
    {
        var booking = Booking.Create(request.TenantId, request.SlotId, request.Customer, clock.GetUtcNow());
        await repository.AddAsync(booking, ct);

        HoldInfo hold;
        try
        {
            hold = await scheduling.CreateHoldAsync(request.SlotId, booking.Id, ct);
        }
        catch (DomainException ex) when (ex.Kind is ErrorKind.Conflict or ErrorKind.NotFound or ErrorKind.Validation)
        {
            booking.Reject(ex.Code, clock.GetUtcNow());
            await repository.SaveAsync(booking, CancellationToken.None);
            logger.LogInformation("Booking {BookingId} rejected: {Code}", booking.Id, ex.Code);
            throw;
        }
        catch (DomainException ex) when (ex.Kind is ErrorKind.Unavailable or ErrorKind.Timeout)
        {
            // If the hold was created before the failure, BR2 (hold TTL) gives the capacity back.
            booking.Fail(ex.Code, clock.GetUtcNow());
            await repository.SaveAsync(booking, CancellationToken.None);
            logger.LogWarning("Booking {BookingId} failed: Scheduling unavailable ({Code})", booking.Id, ex.Code);
            throw;
        }

        if (hold.TenantId != request.TenantId)
        {
            await TryReleaseHoldAsync(hold.Id);
            booking.Reject(ErrorCodes.TenantMismatch, clock.GetUtcNow());
            await repository.SaveAsync(booking, CancellationToken.None);
            throw DomainException.Conflict(ErrorCodes.TenantMismatch, $"Slot {request.SlotId} does not belong to tenant {request.TenantId}.");
        }

        booking.MarkHeld(hold.Id, hold.PriceMinor, hold.Currency, hold.SlotStartAt, hold.ExpiresAt, clock.GetUtcNow());
        await repository.SaveAsync(booking, CancellationToken.None);
        logger.LogInformation("Booking {BookingId} HELD with hold {HoldId} until {ExpiresAt}", booking.Id, hold.Id, hold.ExpiresAt);
        return booking;
    }

    /// <summary>
    /// Step 2 (BR3): charge, then confirm the hold → CONFIRMED.
    /// Declined → FAILED and the hold is released. Gateway down → stays HELD (retry is safe: same payment key).
    /// Hold expired while paying → payment refunded, booking EXPIRED (compensation).
    /// </summary>
    public Task<Booking> PayAsync(Guid bookingId, CancellationToken ct) =>
        WithLeaseAsync(bookingId, async booking =>
        {
            if (booking.Status == BookingStatus.Confirmed)
            {
                return booking; // already paid (e.g. a retry with a different key): no second charge
            }

            booking.EnsurePayable(clock.GetUtcNow(), options.HoldSafetyMargin);

            if (booking.AmountMinor > 0)
            {
                var payment = await payments.ChargeAsync(booking.Id, booking.AmountMinor, booking.Currency!, ct);
                if (!payment.Approved)
                {
                    booking.Fail(ErrorCodes.PaymentDeclined, clock.GetUtcNow());
                    await repository.SaveAsync(booking, CancellationToken.None);
                    await TryReleaseHoldAsync(booking.HoldId!.Value);
                    logger.LogInformation("Booking {BookingId} FAILED: payment {PaymentId} declined", booking.Id, payment.PaymentId);
                    throw new DomainException(ErrorKind.PaymentRequired, ErrorCodes.PaymentDeclined,
                        $"Payment {payment.PaymentId} was declined ({payment.DeclineReason}). The slot has been released.");
                }

                booking.RecordPayment(payment.PaymentId, clock.GetUtcNow());
                await repository.SaveAsync(booking, CancellationToken.None);
            }

            try
            {
                await scheduling.ConfirmHoldAsync(booking.HoldId!.Value, ct);
            }
            catch (DomainException ex) when (ex.Code is ErrorCodes.HoldExpired or ErrorCodes.InvalidStateTransition)
            {
                var refunded = await TryRefundAsync(booking, booking.AmountMinor);
                booking.Expire(ErrorCodes.HoldExpired, refunded, clock.GetUtcNow());
                await repository.SaveAsync(booking, CancellationToken.None);
                logger.LogWarning("Booking {BookingId} EXPIRED: hold expired before confirmation; refunded {Refunded}", booking.Id, refunded);
                throw DomainException.Conflict(ErrorCodes.HoldExpired,
                    $"The hold expired before the booking could be confirmed. Payment {booking.PaymentId} was refunded ({refunded}).");
            }

            booking.Confirm(clock.GetUtcNow());
            await repository.SaveAsync(booking, CancellationToken.None);
            logger.LogInformation("Booking {BookingId} CONFIRMED with payment {PaymentId}", booking.Id, booking.PaymentId);
            return booking;
        }, ct);

    /// <summary>
    /// Cancels a booking. HELD: release the hold (refund in full if it was already charged).
    /// CONFIRMED: refund per BR5, then release the hold. Cancelling twice returns the cancelled booking.
    /// </summary>
    public Task<Booking> CancelAsync(Guid bookingId, CancellationToken ct) =>
        WithLeaseAsync(bookingId, async booking =>
        {
            if (booking.Status == BookingStatus.Cancelled)
            {
                return booking;
            }

            BookingStateMachine.EnsureCanTransition(booking.Status, BookingStatus.Cancelled);

            var refund = booking.Status == BookingStatus.Confirmed
                ? RefundPolicy.Calculate(booking.AmountMinor, booking.SlotStartAt!.Value, clock.GetUtcNow())
                : booking.PaymentId is null ? 0 : booking.AmountMinor;

            // Refund first: if releasing the slot then fails, the customer has their money and a retry finishes the job.
            if (refund > 0)
            {
                await payments.RefundAsync(booking.PaymentId!.Value, booking.Id, refund, ct);
            }

            await scheduling.ReleaseHoldAsync(booking.HoldId!.Value, ct);
            booking.Cancel(refund, clock.GetUtcNow());
            await repository.SaveAsync(booking, CancellationToken.None);
            logger.LogInformation("Booking {BookingId} CANCELLED; refunded {Refund}", booking.Id, refund);
            return booking;
        }, ct);

    /// <summary>
    /// Brings HELD bookings in line with Scheduling once their hold time has passed. Scheduling is the source of truth:
    /// EXPIRED/RELEASED → booking EXPIRED (refunding any charge); CONFIRMED with a recorded payment → booking CONFIRMED.
    /// </summary>
    public async Task<int> ReconcileExpiredAsync(CancellationToken ct)
    {
        var reconciled = 0;
        foreach (var id in await repository.FindExpiryCandidatesAsync(clock.GetUtcNow(), 50, ct))
        {
            try
            {
                var changed = false;
                await WithLeaseAsync(id, async booking =>
                {
                    if (booking.Status != BookingStatus.Held)
                    {
                        return booking;
                    }

                    var hold = await scheduling.GetHoldAsync(booking.HoldId!.Value, ct);
                    switch (hold.Status)
                    {
                        case HoldInfo.Expired or HoldInfo.Released:
                            var refunded = booking.PaymentId is null ? 0 : await TryRefundAsync(booking, booking.AmountMinor);
                            booking.Expire(ErrorCodes.HoldExpired, refunded, clock.GetUtcNow());
                            changed = true;
                            break;
                        case HoldInfo.Confirmed when booking.PaymentId is not null:
                            booking.Confirm(clock.GetUtcNow());
                            changed = true;
                            break;
                        default:
                            return booking; // Scheduling has not swept it yet; try again next time
                    }

                    await repository.SaveAsync(booking, CancellationToken.None);
                    logger.LogInformation("Booking {BookingId} reconciled to {Status} (hold {HoldStatus})", booking.Id, booking.Status, hold.Status);
                    return booking;
                }, ct);

                reconciled += changed ? 1 : 0;
            }
            catch (DomainException ex)
            {
                logger.LogWarning("Could not reconcile booking {BookingId}: {Code} {Message}", id, ex.Code, ex.Message);
            }
        }

        return reconciled;
    }

    public async Task<Booking> GetAsync(Guid bookingId, CancellationToken ct) =>
        await repository.GetReadOnlyAsync(bookingId, ct) ?? throw NotFound(bookingId);

    public Task<PagedResult<Booking>> ListAsync(BookingQuery query, CancellationToken ct) => repository.ListAsync(query, ct);

    private async Task<Booking> WithLeaseAsync(Guid bookingId, Func<Booking, Task<Booking>> operation, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await repository.TryAcquireLeaseAsync(bookingId, now, now + options.LeaseDuration, ct))
        {
            _ = await repository.GetReadOnlyAsync(bookingId, ct) ?? throw NotFound(bookingId);
            throw DomainException.Conflict(ErrorCodes.BookingBusy, $"Booking {bookingId} is being changed by another request. Retry shortly.");
        }

        try
        {
            var booking = await repository.FindAsync(bookingId, ct) ?? throw NotFound(bookingId);
            return await operation(booking);
        }
        finally
        {
            await repository.ReleaseLeaseAsync(bookingId);
        }
    }

    private async Task<long> TryRefundAsync(Booking booking, long amountMinor)
    {
        if (booking.PaymentId is null || amountMinor == 0)
        {
            return 0;
        }

        try
        {
            await payments.RefundAsync(booking.PaymentId.Value, booking.Id, amountMinor, CancellationToken.None);
            return amountMinor;
        }
        catch (DomainException ex)
        {
            // Known limitation: the refund must then be retried manually (future: move refunds to the outbox).
            logger.LogError("Refund of {Amount} for booking {BookingId} failed: {Code}. Manual follow-up needed.", amountMinor, booking.Id, ex.Code);
            return 0;
        }
    }

    private async Task TryReleaseHoldAsync(Guid holdId)
    {
        try
        {
            await scheduling.ReleaseHoldAsync(holdId, CancellationToken.None);
        }
        catch (DomainException ex)
        {
            logger.LogWarning("Could not release hold {HoldId} ({Code}); it will expire automatically (BR2)", holdId, ex.Code);
        }
    }

    private static DomainException NotFound(Guid bookingId) =>
        DomainException.NotFound(ErrorCodes.BookingNotFound, $"Booking {bookingId} was not found.");
}
