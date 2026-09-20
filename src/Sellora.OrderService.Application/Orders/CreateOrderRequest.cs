using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Orders;

/// <summary>
/// What a rep submits: a shop and product quantities. Company and rep come
/// from the token; prices, names, credit and placement come from the
/// owning services during verification (US-E4-1b).
/// </summary>
public sealed record CreateOrderRequest(
    Guid ShopId,
    IReadOnlyCollection<BasketLine> Lines);
