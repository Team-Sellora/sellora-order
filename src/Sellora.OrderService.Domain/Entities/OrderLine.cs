namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// One product on an order. Name and price are snapshots taken when the
/// order was placed; later catalogue changes must never rewrite them.
/// </summary>
public sealed class OrderLine
{
    private OrderLine()
    {
    }

    internal OrderLine(
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        int quantity,
        decimal unitPriceSnapshot)
    {
        OrderLineId = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        ProductNameSnapshot = productNameSnapshot;
        Quantity = quantity;
        UnitPriceSnapshot = unitPriceSnapshot;
        LineTotal = Math.Round(
            quantity * unitPriceSnapshot,
            2,
            MidpointRounding.AwayFromZero);
    }

    public Guid OrderLineId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductNameSnapshot { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public decimal UnitPriceSnapshot { get; private set; }

    public decimal LineTotal { get; private set; }
}
