namespace Sellora.OrderService.Domain.Orders;

/// <summary>US-E4-5: the decisions a person can take on an order. Stored as text.</summary>
public enum OrderDecisionKind
{
    /// <summary>The agency accepted a scheduled delivery; it is now confirmed.</summary>
    Approved = 1,

    /// <summary>The agency refused a scheduled delivery, with a reason; it is cancelled.</summary>
    Rejected = 2,

    /// <summary>The shop owner cancelled their own order inside the window.</summary>
    CancelledByShop = 3
}
