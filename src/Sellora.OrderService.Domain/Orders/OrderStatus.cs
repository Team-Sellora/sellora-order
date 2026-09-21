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
    /// US-E4-2: a scheduled delivery order, confirmed as soon as it is
    /// verified, and an immediate cash sale after payment is recorded
    /// (US-E4-3). Its stock reservation is confirmed (held becomes sold).
    /// </summary>
    Confirmed = 2,

    /// <summary>US-E4-5: cancelled by the shop or rejected on approval; stock released.</summary>
    Cancelled = 3,

    /// <summary>US-E4-5: held for an approver before it can be confirmed.</summary>
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
