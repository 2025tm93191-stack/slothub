using BuildingBlocks.Validation;
using FluentValidation;
using Scheduling.Domain.Catalog;

namespace Scheduling.Api.Endpoints;

public sealed record CreateTenantRequest(string Name, string TimeZone);

public sealed record TenantResponse(Guid Id, string Name, string TimeZone, DateTimeOffset CreatedAt)
{
    public static TenantResponse From(Tenant t) => new(t.Id, t.Name, t.TimeZone, t.CreatedAt);
}

public sealed record CreateResourceRequest(string Name, string Type);

public sealed record ResourceResponse(Guid Id, Guid TenantId, string Name, string Type, DateTimeOffset CreatedAt)
{
    public static ResourceResponse From(Resource r) => new(r.Id, r.TenantId, r.Name, r.Type, r.CreatedAt);
}

public sealed record CreateOfferingRequest(string Name, int DurationMinutes, long PriceMinor, string Currency);

public sealed record OfferingResponse(Guid Id, Guid TenantId, string Name, int DurationMinutes, long PriceMinor, string Currency, DateTimeOffset CreatedAt)
{
    public static OfferingResponse From(ServiceOffering o) => new(o.Id, o.TenantId, o.Name, o.DurationMinutes, o.PriceMinor, o.Currency, o.CreatedAt);
}

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TimeZone).NotEmpty()
            .Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("TimeZone must be a valid IANA time zone, e.g. Asia/Kolkata.");
    }
}

public sealed class CreateResourceRequestValidator : AbstractValidator<CreateResourceRequest>
{
    public CreateResourceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(50);
    }
}

public sealed class CreateOfferingRequestValidator : AbstractValidator<CreateOfferingRequest>
{
    public CreateOfferingRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 24 * 60);
        RuleFor(x => x.PriceMinor).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$").WithMessage("Currency must be an ISO-4217 code, e.g. INR.");
    }
}

public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var tenants = api.MapGroup("/tenants").WithTags("Catalog");

        tenants.MapPost("/", async (CreateTenantRequest request, CatalogService catalog, CancellationToken ct) =>
            {
                var tenant = await catalog.CreateTenantAsync(request.Name, request.TimeZone, ct);
                return TypedResults.Created($"/api/v1/tenants/{tenant.Id}", TenantResponse.From(tenant));
            })
            .Validate<CreateTenantRequest>()
            .WithName("CreateTenant")
            .WithSummary("Create a tenant (a business using SlotHub)");

        tenants.MapGet("/{tenantId:guid}", async (Guid tenantId, CatalogService catalog, CancellationToken ct) =>
                TypedResults.Ok(TenantResponse.From(await catalog.GetTenantAsync(tenantId, ct))))
            .WithName("GetTenant")
            .WithSummary("Get a tenant")
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenants.MapPost("/{tenantId:guid}/resources", async (Guid tenantId, CreateResourceRequest request, CatalogService catalog, CancellationToken ct) =>
            {
                var resource = await catalog.AddResourceAsync(tenantId, request.Name, request.Type, ct);
                return TypedResults.Created($"/api/v1/tenants/{tenantId}/resources/{resource.Id}", ResourceResponse.From(resource));
            })
            .Validate<CreateResourceRequest>()
            .WithName("CreateResource")
            .WithSummary("Add a bookable resource (doctor, court, room...)")
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenants.MapGet("/{tenantId:guid}/resources", async (Guid tenantId, CatalogService catalog, CancellationToken ct) =>
                TypedResults.Ok((await catalog.ListResourcesAsync(tenantId, ct)).Select(ResourceResponse.From)))
            .WithName("ListResources")
            .WithSummary("List a tenant's resources")
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenants.MapPost("/{tenantId:guid}/offerings", async (Guid tenantId, CreateOfferingRequest request, CatalogService catalog, CancellationToken ct) =>
            {
                var offering = await catalog.AddOfferingAsync(tenantId, request.Name, request.DurationMinutes, request.PriceMinor, request.Currency, ct);
                return TypedResults.Created($"/api/v1/tenants/{tenantId}/offerings/{offering.Id}", OfferingResponse.From(offering));
            })
            .Validate<CreateOfferingRequest>()
            .WithName("CreateOffering")
            .WithSummary("Add a service offering with duration and price")
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenants.MapGet("/{tenantId:guid}/offerings", async (Guid tenantId, CatalogService catalog, CancellationToken ct) =>
                TypedResults.Ok((await catalog.ListOfferingsAsync(tenantId, ct)).Select(OfferingResponse.From)))
            .WithName("ListOfferings")
            .WithSummary("List a tenant's service offerings")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
