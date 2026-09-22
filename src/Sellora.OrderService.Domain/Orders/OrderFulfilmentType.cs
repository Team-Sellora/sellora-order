namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// How the shop takes the goods. Chosen by the rep at order creation and
/// never changeable afterwards (US-E4-2-T1).
/// </summary>
public enum OrderFulfilmentType
{
    /// <summary>
    /// The shop pays now and the rep hands the goods over from their own van.
    /// Goes to <see cref="OrderStatus.AwaitingCheckout"/> for the GPS-gated
    /// payment in US-E4-3.
    /// </summary>
    ImmediateCashSale = 1,

    /// <summary>
    /// The shop takes the order on credit; the agency delivers later.
    /// Confirmed as soon as verification passes.
    /// </summary>
    ScheduledDelivery = 2
}
