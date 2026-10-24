using Booking.Domain.Bookings;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using BookingEntity = Booking.Domain.Bookings.Booking;

namespace Booking.Api.Endpoints;

public sealed record CustomerRequest(string Id, string? Phone, string? Email);

public sealed record CreateBookingRequest(Guid TenantId, Guid SlotId, CustomerRequest Customer);

public sealed record BookingSearchParameters(
    [FromQuery] string? CustomerId,
    [FromQuery] BookingStatus? Status,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize);

public sealed record StatusChangeResponse(BookingStatus? From, BookingStatus To, string Reason, DateTimeOffset At);

public sealed record BookingResponse(
    Guid Id,
    Guid TenantId,
    Guid SlotId,
    BookingStatus Status,
    CustomerRequest Customer,
    Guid? HoldId,
    Guid? PaymentId,
    long AmountMinor,
    string? Currency,
    DateTimeOffset? SlotStartAt,
    DateTimeOffset? HoldExpiresAt,
    long RefundedMinor,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StatusChangeResponse> History)
{
    public static BookingResponse From(BookingEntity b) =>
        new(b.Id, b.TenantId, b.SlotId, b.Status, new CustomerRequest(b.CustomerId, b.CustomerPhone, b.CustomerEmail),
            b.HoldId, b.PaymentId, b.AmountMinor, b.Currency, b.SlotStartAt, b.HoldExpiresAt, b.RefundedMinor, b.FailureReason,
            b.CreatedAt, b.UpdatedAt,
            b.History.OrderBy(h => h.At).ThenBy(h => h.Id).Select(h => new StatusChangeResponse(h.FromStatus, h.ToStatus, h.Reason, h.At)).ToList());
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.Customer).NotNull().ChildRules(customer =>
        {
            customer.RuleFor(c => c.Id).NotEmpty().MaximumLength(100);
            customer.RuleFor(c => c.Phone).Matches(@"^\+[1-9]\d{7,14}$").WithMessage("Phone must be E.164, e.g. +919800000001.")
                .When(c => !string.IsNullOrWhiteSpace(c.Phone));
            customer.RuleFor(c => c.Email).EmailAddress().MaximumLength(320).When(c => !string.IsNullOrWhiteSpace(c.Email));
            customer.RuleFor(c => c).Must(c => !string.IsNullOrWhiteSpace(c.Phone) || !string.IsNullOrWhiteSpace(c.Email))
                .WithName("Customer").WithMessage("Provide a phone or an email so the customer can be notified.");
        });
    }
}

public sealed class BookingSearchParametersValidator : AbstractValidator<BookingSearchParameters>
{
    public BookingSearchParametersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public static class BookingEndpoints
{
    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder api)
    {
        var bookings = api.MapGroup("/bookings").WithTags("Bookings");

        bookings.MapPost("/", async (CreateBookingRequest request, BookingService service, CancellationToken ct) =>
            {
                var booking = await service.CreateAsync(
                    new NewBooking(request.TenantId, request.SlotId, new CustomerContact(request.Customer.Id, request.Customer.Phone, request.Customer.Email)),
                    ct);
                return TypedResults.Created($"/api/v1/bookings/{booking.Id}", BookingResponse.From(booking));
            })
            .Validate<CreateBookingRequest>()
            .RequireIdempotencyKey()
            .WithName("CreateBooking")
            .WithSummary("Step 1: create a booking and hold the slot (HELD until holdExpiresAt). 409 SLOT_UNAVAILABLE if full.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bookings.MapGet("/{bookingId:guid}", async (Guid bookingId, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(BookingResponse.From(await service.GetAsync(bookingId, ct))))
            .WithName("GetBooking")
            .WithSummary("Get a booking with its status history")
            .ProducesProblem(StatusCodes.Status404NotFound);

        bookings.MapGet("/", async ([AsParameters] BookingSearchParameters query, BookingService service, CancellationToken ct) =>
                TypedResults.Ok((await service.ListAsync(new BookingQuery(query.CustomerId, query.Status, query.Page ?? 1, query.PageSize ?? 20), ct))
                    .Map(BookingResponse.From)))
            .Validate<BookingSearchParameters>()
            .WithName("ListBookings")
            .WithSummary("List bookings, optionally by customer and status");

        bookings.MapPost("/{bookingId:guid}/payment", async (Guid bookingId, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(BookingResponse.From(await service.PayAsync(bookingId, ct))))
            .RequireIdempotencyKey()
            .WithName("PayBooking")
            .WithSummary("Step 2: charge and confirm (BR3). 402 declined (slot released), 409 HOLD_EXPIRED (refunded), 503 gateway down (still HELD, retry safe).")
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bookings.MapPost("/{bookingId:guid}/cancel", async (Guid bookingId, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(BookingResponse.From(await service.CancelAsync(bookingId, ct))))
            .RequireIdempotencyKey()
            .WithName("CancelBooking")
            .WithSummary("Cancel a booking. Refund per BR5: >= 24 h before start 100 %, < 24 h 50 %, after start not allowed.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }
}
