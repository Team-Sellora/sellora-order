namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Input for one order line. <see cref="UnitPrice"/> and
/// <see cref="ProductName"/> are provisional client values until US-E4-1b
/// replaces them with Catalog's resolved values.
/// </summary>
public sealed record NewOrderLine(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
