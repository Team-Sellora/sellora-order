using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-5: orders in the states the approval and cancellation tests start from.</summary>
internal static class DecisionTestOrders
{
    public const string AgencySub = "agency-operator-sub";

    public const string ShopSub = "shop-owner-sub";

    /// <summary>A verified scheduled delivery awaiting approval, not saved.</summary>
    public static Order Pending(Guid companyId, Placement placement, DateTimeOffset placedAt) =>
        New(companyId, placement, placedAt, OrderFulfilmentType.ScheduledDelivery);

    /// <summary>A scheduled delivery the agency approved at <paramref name="approvedAt"/>.</summary>
    public static Order Approved(Guid companyId, Placement placement, DateTimeOffset approvedAt)
    {
        var order = Pending(companyId, placement, approvedAt.AddMinutes(-5));
        order.Approve(placement.AgencyId, AgencySub, "AgencyOperator", approvedAt);
        return order;
    }

    public static Order New(Guid companyId, Placement placement, DateTimeOffset placedAt, OrderFulfilmentType type)
    {
        var order = Order.Create(
            companyId, placement.ShopId, Guid.NewGuid(), placement.AgencyId, placement.TerritoryId,
            placement.ProvinceId, type, OrderReferenceGenerator.Generate(placedAt), placedAt,
            new[] { new NewOrderLine(Guid.NewGuid(), "Sunlight Soap 100g", 2, 100m) });

        order.CompleteVerification(Guid.NewGuid(), Guid.NewGuid(), TestOrders.AllPassed, placedAt);
        return order;
    }
}
