using BuildingBlocks.Domain;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Payment.Domain.Payments;
using PaymentEntity = Payment.Domain.Payments.Payment;

namespace Payment.Api.Endpoints;

public sealed record ChargeRequest(Guid BookingId, long AmountMinor, string Currency);

public sealed record RefundRequest(long AmountMinor);

public sealed record RefundResponse(Guid Id, Guid PaymentId, long AmountMinor, DateTimeOffset CreatedAt)
{
    public static RefundResponse From(Refund r) => new(r.Id, r.PaymentId, r.AmountMinor, r.CreatedAt);
}

public sealed record PaymentResponse(
    Guid Id,
    Guid BookingId,
    long AmountMinor,
    string Currency,
    PaymentStatus Status,
    long RefundedMinor,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RefundResponse> Refunds)
{
    public static PaymentResponse From(PaymentEntity p) =>
        new(p.Id, p.BookingId, p.AmountMinor, p.Currency, p.Status, p.RefundedMinor, p.FailureReason, p.CreatedAt, p.UpdatedAt,
            p.Refunds.Select(RefundResponse.From).ToList());
}

public sealed record RefundResultResponse(RefundResponse Refund, PaymentResponse Payment);

public sealed class ChargeRequestValidator : AbstractValidator<ChargeRequest>
{
    public ChargeRequestValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.AmountMinor).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be an ISO-4217 code, e.g. INR.");
    }
}

public sealed class RefundRequestValidator : AbstractValidator<RefundRequest>
{
    public RefundRequestValidator() => RuleFor(x => x.AmountMinor).GreaterThan(0);
}

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        var payments = api.MapGroup("/payments").WithTags("Payments");

        payments.MapPost("/", async Task<Results<Created<PaymentResponse>, ProblemHttpResult>> (ChargeRequest request, PaymentService service, CancellationToken ct) =>
            {
                var payment = await service.ChargeAsync(request.BookingId, request.AmountMinor, request.Currency, ct);
                var body = PaymentResponse.From(payment);
                if (payment.Status == PaymentStatus.Failed)
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status402PaymentRequired,
                        detail: $"Payment {payment.Id} was declined: {payment.FailureReason}.",
                        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.PaymentDeclined, ["paymentId"] = payment.Id });
                }

                return TypedResults.Created($"/api/v1/payments/{payment.Id}", body);
            })
            .Validate<ChargeRequest>()
            .RequireIdempotencyKey()
            .WithName("Charge")
            .WithSummary("Charge a booking. 402 PAYMENT_DECLINED if declined, 503 if the gateway is down (nothing stored).")
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        payments.MapGet("/{paymentId:guid}", async (Guid paymentId, PaymentService service, CancellationToken ct) =>
                TypedResults.Ok(PaymentResponse.From(await service.GetAsync(paymentId, ct))))
            .WithName("GetPayment")
            .WithSummary("Get a payment with its refunds")
            .ProducesProblem(StatusCodes.Status404NotFound);

        payments.MapGet("/", async (
                [FromQuery] Guid bookingId,
                PaymentService service,
                CancellationToken ct,
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 20) =>
                TypedResults.Ok((await service.ListByBookingAsync(bookingId, page, pageSize, ct))
                    .Select(PaymentResponse.From).ToList()))
            .WithName("ListPaymentsByBooking")
            .WithSummary("List payments for a booking in creation order; page starts at 1, pageSize is 1-100 (default 20).")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        payments.MapPost("/{paymentId:guid}/refunds", async (Guid paymentId, RefundRequest request, PaymentService service, CancellationToken ct) =>
            {
                var (payment, refund) = await service.RefundAsync(paymentId, request.AmountMinor, ct);
                return TypedResults.Created(
                    $"/api/v1/payments/{paymentId}",
                    new RefundResultResponse(RefundResponse.From(refund), PaymentResponse.From(payment)));
            })
            .Validate<RefundRequest>()
            .RequireIdempotencyKey()
            .WithName("Refund")
            .WithSummary("Refund part or all of a payment. Total refunds can never exceed the charged amount.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }
}
