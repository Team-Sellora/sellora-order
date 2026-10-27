namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Raised when an order would break one of its creation invariants.
/// The message names the exact problem so the API can return it as a 400.
/// </summary>
public sealed class OrderRuleViolationException : Exception
{
    public OrderRuleViolationException(string message)
        : base(message)
    {
    }
}
