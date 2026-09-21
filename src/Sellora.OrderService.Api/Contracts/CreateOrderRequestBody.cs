namespace Sellora.OrderService.Api.Contracts;

/// <summary>
/// POST /api/orders body: a shop and product quantities only.
/// Prices, names, totals and placement are resolved server-side (US-E4-1b);
/// any such fields a client still sends have nowhere to bind and are ignored.
/// </summary>
public sealed class CreateOrderRequestBody
{
    public Guid ShopId { get; init; }

    /// <summary>"ImmediateCashSale" or "ScheduledDelivery" (US-E4-2).</summary>
    public string? FulfilmentType { get; init; }

    public IReadOnlyList<CreateOrderLineRequestBody>? Lines { get; init; }
}

public sealed class CreateOrderLineRequestBody
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}
