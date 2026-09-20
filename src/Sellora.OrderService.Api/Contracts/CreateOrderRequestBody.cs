namespace Sellora.OrderService.Api.Contracts;

/// <summary>
/// POST /api/orders body. There is deliberately no subtotal/total field:
/// any totals a client sends have nowhere to bind and are discarded.
/// </summary>
public sealed class CreateOrderRequestBody
{
    public Guid ShopId { get; init; }

    // PROVISIONAL (US-E4-1b): derived from rep-shop verification later.
    public Guid AgencyId { get; init; }

    public Guid TerritoryId { get; init; }

    public Guid ProvinceId { get; init; }

    public IReadOnlyList<CreateOrderLineRequestBody>? Lines { get; init; }
}

public sealed class CreateOrderLineRequestBody
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }

    // PROVISIONAL (US-E4-1b): replaced by Catalog's resolved name and price.
    public string? ProductName { get; init; }

    public decimal UnitPrice { get; init; }
}
