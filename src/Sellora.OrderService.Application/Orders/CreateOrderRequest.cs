using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Orders;

/// <summary>
/// Order creation input. Company and sales rep are NOT here: both come
/// from the caller's token. AgencyId, TerritoryId and ProvinceId are
/// provisional until US-E4-1b derives them from verification.
/// </summary>
public sealed record CreateOrderRequest(
    Guid ShopId,
    Guid AgencyId,
    Guid TerritoryId,
    Guid ProvinceId,
    IReadOnlyCollection<NewOrderLine> Lines);
