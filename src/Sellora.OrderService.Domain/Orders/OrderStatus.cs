namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Every status an order can hold, in one place (US-E4-2-T3). Add new
/// statuses here and nowhere else, so nobody has to hunt for status strings
/// scattered across controllers. The column stores the name as text.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// US-E4-2: an immediate cash sale that has passed verification and now
    /// waits for the rep's GPS check-in and payment (US-E4-3). Its stock is
    /// reserved, not yet sold.
    /// </summary>
    AwaitingCheckout = 1,

    /// <summary>
    /// Binding. A scheduled delivery once its agency approves it (US-E4-5),
    /// and an immediate cash sale once payment is recorded (US-E4-3).
    /// <see cref="Entities.Order.ConfirmedAt"/> records when, and the shop's
    /// cancellation window runs from that moment.
    /// </summary>
    Confirmed = 2,

    /// <summary>
    /// Cancelled by the shop, rejected by the agency (US-E4-5), or dropped
    /// because the stock hold expired before checkout (US-E4-3). The
    /// OrderCancelled event tells Inventory to return the stock.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// US-E4-5: a scheduled delivery that passed verification and waits for
    /// its agency to approve or reject it. Its stock is already committed
    /// (sold in Inventory) so the agency cannot oversell while deciding; a
    /// rejection or cancellation puts it back.
    /// </summary>
    PendingApproval = 4
}

public static class OrderStatuses
{
    /// <summary>
    /// Statuses that still owe the shop money, used for the credit check.
    /// A cancelled order stops counting against the limit.
    /// </summary>
    public static readonly OrderStatus[] Outstanding =
    {
        OrderStatus.AwaitingCheckout,
        OrderStatus.Confirmed,
        OrderStatus.PendingApproval
    };
}
