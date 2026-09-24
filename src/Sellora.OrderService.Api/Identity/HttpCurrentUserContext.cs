using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Api.Identity;

/// <summary>
/// Who is calling. Subject and role come from the validated token (raw WSO2
/// claim names, MapInboundClaims = false). Hierarchy IDs come from
/// Organization via <see cref="CallerScopeMiddleware"/> — never from token
/// claims, so nobody has to copy database IDs into the identity provider.
/// </summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor)
    : ICurrentUserContext
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    private CallerScope Scope =>
        accessor.HttpContext?.Items[CallerScopeMiddleware.ItemKey] as CallerScope ?? CallerScope.Empty;

    public string? Subject => User?.FindFirst("sub")?.Value;

    public string? Role => SelloraRoles.ByBreadth
        .FirstOrDefault(role => User?.HasClaim("roles", role) == true);

    public Guid? SalesRepId => Scope.SalesRepId;

    public Guid? AgencyId => Scope.AgencyId;

    public Guid? ShopId => Scope.ShopId;

    public IReadOnlyCollection<Guid> ProvinceIds => Scope.ProvinceIds;
}
