using System.Net.Http.Json;
using Sellora.OrderService.Application.Dependencies;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// sellora-catalog's internal resolve endpoint is [AllowAnonymous] (an
/// internal network call), so no token is forwarded.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _http;

    public CatalogClient(HttpClient http) => _http = http;

    public async Task<ProductResolutionResponse> ResolveProductsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "internal/catalog/products/resolve")
        {
            // Matches Catalog's ResolveProductsRequestBody { CompanyId, ProductIds }.
            Content = JsonContent.Create(
                new { CompanyId = companyId, ProductIds = productIds },
                options: DependencyHttp.Json)
        };

        using var response = await DependencyHttp.SendAsync(
            _http, request, Dependency.Catalog, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyRejectedException(
                Dependency.Catalog,
                response.StatusCode,
                await DependencyHttp.ReadErrorAsync(response, cancellationToken));
        }

        return await DependencyHttp.ReadAsync<ProductResolutionResponse>(
            response, Dependency.Catalog, cancellationToken);
    }
}
