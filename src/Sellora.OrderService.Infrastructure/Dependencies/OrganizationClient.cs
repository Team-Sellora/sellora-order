using Sellora.OrderService.Application.Dependencies;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// sellora-organization. Both endpoints are [Authorize], so the caller's
/// bearer token is forwarded by <see cref="ForwardBearerTokenHandler"/>.
/// </summary>
public sealed class OrganizationClient : IOrganizationClient
{
    private readonly HttpClient _http;

    public OrganizationClient(HttpClient http) => _http = http;

    public async Task<VerifyRepShopRelationshipResponse> VerifyRepShopAsync(
        Guid repId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/rep-shop-relationships/verify?repId={repId}&shopId={shopId}");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Organization, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Organization,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        return await DependencyHttp.ReadAsync<VerifyRepShopRelationshipResponse>(
            response, Dependency.Organization, cancellationToken);
    }

    public async Task<ShopPlacement?> FindShopAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/hierarchy");

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Organization, cancellationToken);

        // Organization returns 404 when the caller has no visible hierarchy.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Organization,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        var tree = await DependencyHttp.ReadAsync<HierarchyTreeResponse>(
            response, Dependency.Organization, cancellationToken);

        // Shops in unassigned territories have no agency, so no fulfilment
        // source; they are deliberately not searched.
        return (
            from province in tree.Provinces
            from agency in province.Agencies
            from territory in agency.Territories
            from shop in territory.Shops
            where shop.ShopId == shopId
            select new ShopPlacement(
                shop.ShopId,
                shop.Name,
                shop.CreditLimit,
                territory.TerritoryId,
                agency.AgencyId,
                province.ProvinceId))
            .FirstOrDefault();
    }

    // sellora-organization: Application/Hierarchy/HierarchyTreeResponse.cs
    private sealed record HierarchyTreeResponse(
        Guid CompanyId,
        string Name,
        IReadOnlyList<ProvinceHierarchyNode> Provinces);

    private sealed record ProvinceHierarchyNode(
        Guid ProvinceId,
        string Code,
        string Name,
        IReadOnlyList<AgencyHierarchyNode> Agencies,
        IReadOnlyList<TerritoryHierarchyNode> UnassignedTerritories);

    private sealed record AgencyHierarchyNode(
        Guid AgencyId,
        string Name,
        IReadOnlyList<TerritoryHierarchyNode> Territories);

    private sealed record TerritoryHierarchyNode(
        Guid TerritoryId,
        string Code,
        string Name,
        IReadOnlyList<ShopHierarchyNode> Shops);

    private sealed record ShopHierarchyNode(
        Guid ShopId,
        string Name,
        string? OwnerName,
        string Address,
        decimal Latitude,
        decimal Longitude,
        decimal CreditLimit);
}
