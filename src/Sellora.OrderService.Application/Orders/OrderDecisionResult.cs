namespace Sellora.OrderService.Application.Orders;

/// <summary>US-E4-5: how an approval or cancellation request ended.</summary>
public enum OrderDecisionOutcome
{
    Succeeded,
    InvalidRequest,
    ReasonRequired,
    TenantNotAvailable,
    CallerNotPermitted,
    OrderNotFound,
    Conflict,
    CancellationWindowClosed
}

/// <summary>Timing detail returned with a 409 when the window has closed.</summary>
public sealed record CancellationWindowClosedDetail(
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? WindowClosedAt,
    TimeSpan? WindowLength,
    DateTimeOffset CheckedAt)
{
    public TimeSpan? ElapsedSinceConfirmation => ConfirmedAt is { } confirmed ? CheckedAt - confirmed : null;

    public TimeSpan? ClosedAgo => WindowClosedAt is { } closed ? CheckedAt - closed : null;
}

/// <param name="Changed">False when a repeated request found the decision already in place.</param>
public sealed record OrderDecisionResult(
    OrderDecisionOutcome Outcome,
    OrderResponse? Order = null,
    string? Message = null,
    bool Changed = false,
    CancellationWindowClosedDetail? WindowClosed = null)
{
    public static OrderDecisionResult Failed(OrderDecisionOutcome outcome, string message) =>
        new(outcome, Message: message);

    public static OrderDecisionResult Done(OrderResponse order, bool changed) =>
        new(OrderDecisionOutcome.Succeeded, order, Changed: changed);
}
