namespace Sellora.OrderService.Domain.Orders;

/// <summary>What the rep asks for: a product and a quantity. Nothing else.</summary>
public sealed record BasketLine(Guid ProductId, int Quantity);
