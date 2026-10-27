namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// A priced order line. Since US-E4-1b, <see cref="ProductName"/> and
/// <see cref="UnitPrice"/> always come from Catalog's resolve endpoint,
/// never from the client.
/// </summary>
public sealed record NewOrderLine(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
