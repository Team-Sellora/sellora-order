namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// US-E4-5: whether the shop can cancel an order right now, and why not.
/// Computed on the server from the stored confirmation time and the server's
/// clock — never from a time the client sends, since a client clock is
/// exactly what someone would adjust.
/// </summary>
/// <param name="CanCancel">True when a cancellation would be accepted now.</param>
/// <param name="ClosesAt">
/// When the window closes. Null before the order is confirmed (the window has
/// not started, so a pending order can be cancelled any time) and when the
/// order cannot be cancelled for a reason other than time.
/// </param>
/// <param name="Remaining">Time left, only while the window is open.</param>
/// <param name="ClosedAgo">How long ago the window closed, only once it has.</param>
/// <param name="Reason">Why the order cannot be cancelled; null when it can.</param>
public sealed record CancellationWindow(
    bool CanCancel,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ClosesAt,
    TimeSpan WindowLength,
    TimeSpan? Remaining,
    TimeSpan? ClosedAgo,
    string? Reason);
