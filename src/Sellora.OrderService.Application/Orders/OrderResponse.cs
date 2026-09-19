using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Application.Orders;

public sealed record OrderLineResponse(
    Guid OrderLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    int Quantity,
    decimal UnitPriceSnapshot,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid OrderId,
    string OrderReference,
    Guid ShopId,
    Guid SalesRepId,
    Guid AgencyId,
    Guid TerritoryId,
    Guid ProvinceId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Subtotal,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Lines)
{
    public static OrderResponse From(Order order) => new(
        order.OrderId,
        order.OrderReference,
        order.ShopId,
        order.SalesRepId,
        order.AgencyId,
        order.TerritoryId,
        order.ProvinceId,
        order.Status.ToString(),
        order.OrderDate,
        order.Subtotal,
        order.Total,
        order.Lines
            .OrderBy(line => line.ProductNameSnapshot)
            .Select(line => new OrderLineResponse(
                line.OrderLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.Quantity,
                line.UnitPriceSnapshot,
                line.LineTotal))
            .ToList());
}

public sealed record OrderSummaryResponse(
    Guid OrderId,
    string OrderReference,
    Guid ShopId,
    Guid SalesRepId,
    Guid AgencyId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    int LineCount);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
