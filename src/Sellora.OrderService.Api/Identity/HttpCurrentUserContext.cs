using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Api.Identity;

/// <summary>
/// Reads caller identity from the JWT. MapInboundClaims = false, so claim
/// names arrive exactly as WSO2 emits them ("sub", "roles", ...).
/// </summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor accessor)
    : ICurrentUserContext
{
    private System.Security.Claims.ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string? Subject => User?.FindFirst("sub")?.Value;

    public string? Role => SelloraRoles.ByBreadth
        .FirstOrDefault(role => User?.HasClaim("roles", role) == true);

    public Guid? SalesRepId => ReadGuid("salesRepId");

    public Guid? AgencyId => ReadGuid("agencyId");

    public Guid? ShopId => ReadGuid("shopId");

    public IReadOnlyCollection<Guid> ProvinceIds =>
        User?.FindAll("provinceId")
            .Select(claim => Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList()
        ?? new List<Guid>();

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(User?.FindFirst(claimType)?.Value, out var value) && value != Guid.Empty
            ? value
            : null;
}
