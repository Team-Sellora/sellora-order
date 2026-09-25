using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Infrastructure.Orders;

/// <summary>Maps a broken decision rule to the outcome the API turns into a status code.</summary>
internal static class OrderDecisionErrors
{
    public static OrderDecisionResult From(OrderDecisionRuleException exception, DateTimeOffset now)
    {
        var outcome = exception.Failure switch
        {
            OrderDecisionFailure.InvalidRequest => OrderDecisionOutcome.InvalidRequest,
            OrderDecisionFailure.ReasonRequired => OrderDecisionOutcome.ReasonRequired,
            OrderDecisionFailure.NotVisible => OrderDecisionOutcome.OrderNotFound,
            OrderDecisionFailure.CancellationWindowClosed => OrderDecisionOutcome.CancellationWindowClosed,
            _ => OrderDecisionOutcome.Conflict
        };

        var window = outcome == OrderDecisionOutcome.CancellationWindowClosed
            ? new CancellationWindowClosedDetail(
                exception.ConfirmedAt, exception.WindowClosedAt, exception.WindowLength, now)
            : null;

        return new OrderDecisionResult(outcome, Message: exception.Message, WindowClosed: window);
    }

    /// <summary>Another request changed the order between our read and our save.</summary>
    public static OrderDecisionResult ChangedMeanwhile(Guid orderId) =>
        OrderDecisionResult.Failed(
            OrderDecisionOutcome.Conflict,
            $"Order {orderId} was changed by someone else a moment ago. Reload it and try again.");
}
