using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// US-E4-5 shop cancellation window. A shop owner can cancel their own
/// order while it is not yet binding (awaiting approval or checkout) and
/// for a configured window after it is confirmed. The window is measured
/// from the stored <see cref="ConfirmedAt"/> using the server's clock.
///
/// Which statuses can be cancelled is an allow-list, not a deny-list: any
/// status added later (for example delivery statuses in E6) is treated as
/// "has entered delivery" and refused until someone decides otherwise.
/// </summary>
public sealed partial class Order
{
    public const string DefaultShopCancellationReason = "Cancelled by the shop owner.";

    /// <summary>
    /// Where the order stands against the cancellation window at
    /// <paramref name="now"/>. Changes nothing, so the order view can show
    /// the remaining time and the cancel endpoint can apply the same rule.
    /// </summary>
    public CancellationWindow GetCancellationWindow(DateTimeOffset now, TimeSpan windowLength)
    {
        if (windowLength <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(windowLength), "The cancellation window must be positive.");
        }

        switch (Status)
        {
            case OrderStatus.Cancelled:
                return Refused(windowLength, "This order has already been cancelled.");

            case OrderStatus.PendingApproval:
            case OrderStatus.AwaitingCheckout:
                // Not binding yet: the window starts at confirmation.
                return new CancellationWindow(true, null, null, windowLength, null, null, null);

            case OrderStatus.Confirmed when FulfilmentType == OrderFulfilmentType.ImmediateCashSale:
                return Refused(
                    windowLength,
                    "A cash sale is paid for and handed over at the counter, so it cannot be cancelled here.");

            case OrderStatus.Confirmed when ConfirmedAt is { } confirmedAt:
                // DateTimeOffset compares instants, so the server's local
                // time zone and the stored offset make no difference.
                var closesAt = confirmedAt + windowLength;

                if (now < closesAt)
                {
                    return new CancellationWindow(true, confirmedAt, closesAt, windowLength, closesAt - now, null, null);
                }

                var closedAgo = now - closesAt;
                return new CancellationWindow(
                    false,
                    confirmedAt,
                    closesAt,
                    windowLength,
                    null,
                    closedAgo,
                    $"The cancellation window closed {DurationText.Describe(closedAgo)} ago. " +
                    $"This order was confirmed {DurationText.Describe(now - confirmedAt)} ago and could only be " +
                    $"cancelled within {DurationText.Describe(windowLength)} of confirmation.");

            case OrderStatus.Confirmed:
                // Fail closed rather than guess a start time.
                return Refused(
                    windowLength,
                    "This order has no recorded confirmation time, so its cancellation window cannot be checked.");

            default:
                return Refused(windowLength, $"This order is {Status} and can no longer be cancelled.");
        }
    }

    /// <summary>
    /// Cancels the order for the shop that owns it, if the window allows.
    /// The reason is optional for the shop; a default is recorded so every
    /// decision still carries one.
    /// </summary>
    public OrderDecision CancelByShop(
        Guid shopId,
        string actorUserId,
        string actorRole,
        string? reason,
        DateTimeOffset now,
        TimeSpan windowLength)
    {
        // Another shop's order looks exactly like a missing one.
        if (shopId == Guid.Empty || shopId != ShopId)
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.NotVisible,
                "The order is not visible to your shop.");
        }

        var normalised = NormaliseReason(reason) ?? DefaultShopCancellationReason;
        var window = GetCancellationWindow(now, windowLength);

        if (!window.CanCancel)
        {
            throw new OrderDecisionRuleException(
                window.ClosedAgo is null
                    ? OrderDecisionFailure.NotCancellable
                    : OrderDecisionFailure.CancellationWindowClosed,
                window.Reason!)
            {
                ConfirmedAt = window.ConfirmedAt,
                WindowClosedAt = window.ClosesAt,
                WindowLength = window.WindowLength
            };
        }

        var decision = RecordDecision(
            OrderDecisionKind.CancelledByShop, actorUserId, actorRole, normalised, OrderStatus.Cancelled, now);

        CancelledAt = now;
        CancelledBy = decision.ActorUserId;
        CancellationReason = normalised;
        return decision;
    }

    private static CancellationWindow Refused(TimeSpan windowLength, string reason) =>
        new(false, null, null, windowLength, null, null, reason);
}
