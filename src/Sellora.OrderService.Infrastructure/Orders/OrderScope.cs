using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Infrastructure.Orders;

/// <summary>
/// Step two of the two-step pattern: the DbContext query filter already
/// limits rows to the caller's company; this narrows them to the caller's
/// role scope. A role without its scope claim sees nothing (fail closed).
/// </summary>
internal static class OrderScope
{
    public static IQueryable<Order> ApplyCallerScope(
        this IQueryable<Order> orders,
        ICurrentUserContext caller)
    {
        switch (caller.Role)
        {
            case SelloraRoles.CompanyAdmin:
                return orders;

            case SelloraRoles.AreaManager when caller.ProvinceIds.Count > 0:
                var provinceIds = caller.ProvinceIds.ToList();
                return orders.Where(order => provinceIds.Contains(order.ProvinceId));

            case SelloraRoles.AgencyOperator when caller.AgencyId is { } agencyId:
                return orders.Where(order => order.AgencyId == agencyId);

            case SelloraRoles.SalesRep when caller.SalesRepId is { } salesRepId:
                return orders.Where(order => order.SalesRepId == salesRepId);

            case SelloraRoles.ShopOwner when caller.ShopId is { } shopId:
                return orders.Where(order => order.ShopId == shopId);

            default:
                return orders.Where(_ => false);
        }
    }
}
