using BuildingBlocks.Domain;

namespace Scheduling.Domain.Catalog;

/// <summary>Tenants, resources and service offerings: what a business offers.</summary>
public sealed class CatalogService(ICatalogRepository repository, TimeProvider clock)
{
    public async Task<Tenant> CreateTenantAsync(string name, string timeZone, CancellationToken ct)
    {
        var tenant = Tenant.Create(name, timeZone, clock.GetUtcNow());
        await repository.AddAsync(tenant, ct);
        return tenant;
    }

    public async Task<Tenant> GetTenantAsync(Guid tenantId, CancellationToken ct) =>
        await repository.FindTenantAsync(tenantId, ct)
        ?? throw DomainException.NotFound(ErrorCodes.TenantNotFound, $"Tenant {tenantId} was not found.");

    public async Task<Resource> AddResourceAsync(Guid tenantId, string name, string type, CancellationToken ct)
    {
        await GetTenantAsync(tenantId, ct);
        var resource = Resource.Create(tenantId, name, type, clock.GetUtcNow());
        await repository.AddAsync(resource, ct);
        return resource;
    }

    public async Task<IReadOnlyList<Resource>> ListResourcesAsync(Guid tenantId, CancellationToken ct)
    {
        await GetTenantAsync(tenantId, ct);
        return await repository.ListResourcesAsync(tenantId, ct);
    }

    public async Task<ServiceOffering> AddOfferingAsync(
        Guid tenantId, string name, int durationMinutes, long priceMinor, string currency, CancellationToken ct)
    {
        await GetTenantAsync(tenantId, ct);
        var offering = ServiceOffering.Create(tenantId, name, durationMinutes, priceMinor, currency, clock.GetUtcNow());
        await repository.AddAsync(offering, ct);
        return offering;
    }

    public async Task<IReadOnlyList<ServiceOffering>> ListOfferingsAsync(Guid tenantId, CancellationToken ct)
    {
        await GetTenantAsync(tenantId, ct);
        return await repository.ListOfferingsAsync(tenantId, ct);
    }
}
