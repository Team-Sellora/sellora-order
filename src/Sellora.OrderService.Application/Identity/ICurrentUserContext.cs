namespace Sellora.OrderService.Application.Identity;

/// <summary>
/// Who is calling, read only from the validated access token.
/// Scope identifiers use the same claim names Inventory already reads
/// (salesRepId, agencyId) plus provinceId and shopId for the two roles
/// Order has to scope that Inventory does not.
/// </summary>
public interface ICurrentUserContext
{
    string? Subject { get; }

    /// <summary>The broadest Sellora role the caller holds, or null.</summary>
    string? Role { get; }

    Guid? SalesRepId { get; }

    Guid? AgencyId { get; }

    Guid? ShopId { get; }

    IReadOnlyCollection<Guid> ProvinceIds { get; }

    /// <summary>The caller's display name from Organization, for order snapshots and events.</summary>
    string? DisplayName { get; }
}
