namespace Sellora.OrderService.Application.Orders;

/// <summary>
/// US-E4-5: how long a shop owner has to cancel a confirmed order. Bound to
/// the <c>Cancellation</c> configuration section; one hour by default
/// (resolved open issue #4).
/// </summary>
public sealed class CancellationOptions
{
    public const string Section = "Cancellation";

    public const int MinimumMinutes = 1;

    public const int MaximumMinutes = 24 * 60;

    public int WindowMinutes { get; set; } = 60;

    /// <summary>The configured window, kept within 1 minute and 24 hours.</summary>
    public TimeSpan Window => TimeSpan.FromMinutes(Math.Clamp(WindowMinutes, MinimumMinutes, MaximumMinutes));
}

/// <param name="Reason">Optional; a default reason is recorded when blank.</param>
public sealed record CancelOrderRequest(string? Reason);

public interface IOrderCancellationService
{
    /// <summary>
    /// Cancels the caller's own shop's order if the window allows, records
    /// who and why, and publishes OrderCancelled so Inventory returns the
    /// stock.
    /// </summary>
    Task<OrderDecisionResult> CancelAsync(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken);
}
