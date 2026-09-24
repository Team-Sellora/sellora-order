namespace Sellora.OrderService.Application.Identity;

/// <summary>
/// The caller's place in the hierarchy, from Organization's
/// GET /api/me/scope — the single source of truth. Replaces reading
/// salesRepId / agencyId / shopId / provinceId claims from the token.
/// </summary>
public sealed record CallerScope(
    Guid? SalesRepId,
    Guid? AgencyId,
    Guid? ShopId,
    IReadOnlyList<Guid> ProvinceIds)
{
    /// <summary>No profile in Organization: the caller sees nothing.</summary>
    public static CallerScope Empty { get; } = new(null, null, null, Array.Empty<Guid>());
}
