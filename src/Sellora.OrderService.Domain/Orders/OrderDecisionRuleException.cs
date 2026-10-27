namespace Sellora.OrderService.Domain.Orders;

public enum OrderDecisionFailure
{
    /// <summary>Actor or reason missing or too long — a 400.</summary>
    InvalidRequest,

    /// <summary>A rejection without a reason — a 400 naming the reason field.</summary>
    ReasonRequired,

    /// <summary>The order belongs to another agency or shop; shown as not found.</summary>
    NotVisible,

    /// <summary>Approval asked for an order that is not waiting for one — a 409.</summary>
    NotAwaitingApproval,

    /// <summary>The order can no longer be cancelled for a reason other than time — a 409.</summary>
    NotCancellable,

    /// <summary>The cancellation window has closed — a 409 with the elapsed time.</summary>
    CancellationWindowClosed
}

/// <summary>
/// An approval or cancellation broke a rule. <see cref="Failure"/> lets the
/// API choose the status code; the message is shown to the caller as-is.
/// </summary>
public sealed class OrderDecisionRuleException : Exception
{
    public OrderDecisionRuleException(OrderDecisionFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public OrderDecisionFailure Failure { get; }

    /// <summary>When the order was confirmed; set when the window was the reason.</summary>
    public DateTimeOffset? ConfirmedAt { get; init; }

    /// <summary>When the window closed; set when the window was the reason.</summary>
    public DateTimeOffset? WindowClosedAt { get; init; }

    /// <summary>How long the window lasts; set when the window was the reason.</summary>
    public TimeSpan? WindowLength { get; init; }
}
