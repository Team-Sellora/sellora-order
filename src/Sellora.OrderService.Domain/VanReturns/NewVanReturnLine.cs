namespace Sellora.OrderService.Domain.VanReturns;

/// <summary>A product the rep declares they are handing back.</summary>
public sealed record NewVanReturnLine(Guid ProductId, string? ProductName, int DeclaredQuantity);

/// <summary>What the agency counted for one declared product.</summary>
public sealed record VanReturnCount(Guid ProductId, int CountedQuantity);
