using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Scheduling.Domain.Holds;

namespace Scheduling.Api.Endpoints;

public sealed record CreateHoldRequest(Guid SlotId, Guid BookingId);

public sealed record HoldResponse(
    Guid Id,
    Guid TenantId,
    Guid SlotId,
    Guid BookingId,
    HoldStatus Status,
    long PriceMinor,
    string Currency,
    DateTimeOffset SlotStartAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static HoldResponse From(Hold h) =>
        new(h.Id, h.TenantId, h.SlotId, h.BookingId, h.Status, h.PriceMinor, h.Currency, h.SlotStartAt, h.ExpiresAt, h.CreatedAt, h.UpdatedAt);
}

public sealed record ExpirySweepResponse(int Expired, DateTimeOffset RanAt);

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator()
    {
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public static class HoldEndpoints
{
    public static RouteGroupBuilder MapHoldEndpoints(this RouteGroupBuilder api)
    {
        var holds = api.MapGroup("/holds").WithTags("Holds");

        holds.MapPost("/", async Task<Results<Created<HoldResponse>, Ok<HoldResponse>>> (CreateHoldRequest request, HoldService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request.SlotId, request.BookingId, ct);
                var body = HoldResponse.From(result.Hold);
                return result.Created
                    ? TypedResults.Created($"/api/v1/holds/{body.Id}", body)
                    : TypedResults.Ok(body);
            })
            .Validate<CreateHoldRequest>()
            .WithName("CreateHold")
            .WithSummary("Hold one unit of a slot for a booking (BR1, BR6). Repeating for the same booking returns the existing hold (200).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        holds.MapGet("/{holdId:guid}", async (Guid holdId, HoldService service, CancellationToken ct) =>
                TypedResults.Ok(HoldResponse.From(await service.GetAsync(holdId, ct))))
            .WithName("GetHold")
            .WithSummary("Get a hold")
            .ProducesProblem(StatusCodes.Status404NotFound);

        holds.MapPost("/{holdId:guid}/confirm", async (Guid holdId, HoldService service, CancellationToken ct) =>
                TypedResults.Ok(HoldResponse.From(await service.ConfirmAsync(holdId, ct))))
            .WithName("ConfirmHold")
            .WithSummary("Confirm a hold after payment (BR2/BR3). 409 HOLD_EXPIRED if its TTL has passed.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        holds.MapPost("/{holdId:guid}/release", async (Guid holdId, HoldService service, CancellationToken ct) =>
                TypedResults.Ok(HoldResponse.From(await service.ReleaseAsync(holdId, ct))))
            .WithName("ReleaseHold")
            .WithSummary("Release a held or confirmed hold and return its capacity. Releasing twice is a no-op.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/admin/holds/expire-now", async (HoldService service, TimeProvider clock, CancellationToken ct) =>
                TypedResults.Ok(new ExpirySweepResponse(await service.ExpireDueAsync(ct), clock.GetUtcNow())))
            .WithTags("Admin")
            .WithName("ExpireHoldsNow")
            .WithSummary("Run one hold-expiry sweep now (Challenge 3B demo). Running it twice is safe: the second run expires nothing.");

        return api;
    }
}
