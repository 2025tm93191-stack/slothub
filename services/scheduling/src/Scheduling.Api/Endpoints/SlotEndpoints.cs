using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Domain;
using Scheduling.Domain.Slots;

namespace Scheduling.Api.Endpoints;

public sealed record SlotItem(DateTimeOffset StartAt, int Capacity);

public sealed record CreateSlotsRequest(Guid ResourceId, Guid OfferingId, IReadOnlyList<SlotItem> Slots);

public sealed record SlotSearchParameters(
    [FromQuery] Guid? TenantId,
    [FromQuery] Guid? ResourceId,
    [FromQuery] DateTimeOffset? From,
    [FromQuery] DateTimeOffset? To,
    [FromQuery] bool? Available,
    [FromQuery] int? Page,
    [FromQuery] int? PageSize);

public sealed record SlotResponse(
    Guid Id,
    Guid TenantId,
    Guid ResourceId,
    Guid OfferingId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int Capacity,
    int Reserved,
    int Remaining)
{
    public static SlotResponse From(Slot s) =>
        new(s.Id, s.TenantId, s.ResourceId, s.OfferingId, s.StartAt, s.EndAt, s.Capacity, s.ReservedCount, s.Remaining);
}

public sealed class CreateSlotsRequestValidator : AbstractValidator<CreateSlotsRequest>
{
    public CreateSlotsRequestValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.OfferingId).NotEmpty();
        RuleFor(x => x.Slots).NotEmpty().Must(s => s.Count <= 200).WithMessage("At most 200 slots per request.");
        RuleForEach(x => x.Slots).ChildRules(slot =>
        {
            slot.RuleFor(s => s.StartAt).NotEmpty();
            slot.RuleFor(s => s.Capacity).InclusiveBetween(1, 10_000);
        });
    }
}

public sealed class SlotSearchParametersValidator : AbstractValidator<SlotSearchParameters>
{
    public SlotSearchParametersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From is not null && x.To is not null);
    }
}

public static class SlotEndpoints
{
    public static RouteGroupBuilder MapSlotEndpoints(this RouteGroupBuilder api)
    {
        var slots = api.MapGroup("/slots").WithTags("Slots");

        slots.MapPost("/", async (CreateSlotsRequest request, SlotService service, CancellationToken ct) =>
            {
                var created = await service.CreateBatchAsync(
                    request.ResourceId,
                    request.OfferingId,
                    request.Slots.Select(s => new NewSlot(s.StartAt, s.Capacity)).ToList(),
                    ct);
                return TypedResults.Created("/api/v1/slots", created.Select(SlotResponse.From).ToList());
            })
            .Validate<CreateSlotsRequest>()
            .WithName("CreateSlots")
            .WithSummary("Create a batch of slots for one resource and offering")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        slots.MapGet("/", async ([AsParameters] SlotSearchParameters query, SlotService service, CancellationToken ct) =>
            {
                var result = await service.SearchAsync(
                    new SlotQuery(query.TenantId, query.ResourceId, query.From, query.To, query.Available ?? false, query.Page ?? 1, query.PageSize ?? 20),
                    ct);
                return TypedResults.Ok(result.Map(SlotResponse.From));
            })
            .Validate<SlotSearchParameters>()
            .WithName("SearchSlots")
            .WithSummary("Search slots; available=true returns only future slots with capacity left");

        slots.MapGet("/{slotId:guid}", async (Guid slotId, SlotService service, CancellationToken ct) =>
                TypedResults.Ok(SlotResponse.From(await service.GetAsync(slotId, ct))))
            .WithName("GetSlot")
            .WithSummary("Get a slot including remaining capacity")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
