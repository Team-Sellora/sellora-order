using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Orders;

public sealed record OrderLineResponse(
    Guid OrderLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    int Quantity,
    decimal UnitPriceSnapshot,
    decimal LineTotal);

public sealed record OrderVerificationStepResponse(
    string Step,
    bool Passed,
    string Detail,
    DateTimeOffset RecordedAt);

public sealed record OrderResponse(
    Guid OrderId,
    string OrderReference,
    Guid ShopId,
    Guid SalesRepId,
    Guid AgencyId,
    Guid TerritoryId,
    Guid ProvinceId,
    string FulfilmentType,
    string Status,
    DateTimeOffset OrderDate,
    decimal Subtotal,
    decimal Total,
    Guid ReservationId,
    IReadOnlyList<OrderLineResponse> Lines,
    IReadOnlyList<OrderVerificationStepResponse> VerificationSteps,
    OrderCheckoutResponse? Checkout = null,
    DateTimeOffset? ConfirmedAt = null,
    OrderApprovalResponse? Approval = null,
    OrderCancellationWindowResponse? Cancellation = null,
    IReadOnlyList<OrderDecisionResponse>? Decisions = null)
{
    /// <param name="now">
    /// With <paramref name="cancellationWindow"/>, adds the shop's
    /// cancellation window as the server sees it at this moment (US-E4-5).
    /// </param>
    public static OrderResponse From(
        Order order,
        DateTimeOffset? now = null,
        TimeSpan? cancellationWindow = null) => new(
        order.OrderId,
        order.OrderReference,
        order.ShopId,
        order.SalesRepId,
        order.AgencyId,
        order.TerritoryId,
        order.ProvinceId,
        order.FulfilmentType.ToString(),
        order.Status.ToString(),
        order.OrderDate,
        order.Subtotal,
        order.Total,
        order.ReservationId,
        order.Lines
            .OrderBy(line => line.ProductNameSnapshot)
            .Select(line => new OrderLineResponse(
                line.OrderLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.Quantity,
                line.UnitPriceSnapshot,
                line.LineTotal))
            .ToList(),
        order.VerificationSteps
            .OrderBy(step => step.Step)
            .Select(step => new OrderVerificationStepResponse(
                step.Step.ToString(),
                step.Passed,
                step.Detail,
                step.RecordedAt))
            .ToList(),
        OrderCheckoutResponse.From(order),
        order.ConfirmedAt,
        OrderApprovalResponse.From(order),
        now is { } checkedAt && cancellationWindow is { } window
            ? OrderCancellationWindowResponse.From(order.GetCancellationWindow(checkedAt, window), checkedAt)
            : null,
        order.Decisions
            .OrderBy(decision => decision.DecidedAt)
            .Select(OrderDecisionResponse.From)
            .ToList());
}

/// <summary>US-E4-5: one approval, rejection or cancellation, with who, when and why.</summary>
public sealed record OrderDecisionResponse(
    Guid OrderDecisionId,
    string Decision,
    string ActorUserId,
    string ActorRole,
    string? Reason,
    string StatusBefore,
    string StatusAfter,
    DateTimeOffset DecidedAt)
{
    public static OrderDecisionResponse From(OrderDecision decision) => new(
        decision.OrderDecisionId,
        decision.Kind.ToString(),
        decision.ActorUserId,
        decision.ActorRole,
        decision.Reason,
        decision.StatusBefore.ToString(),
        decision.StatusAfter.ToString(),
        decision.DecidedAt);
}

/// <summary>
/// US-E4-5: where a scheduled delivery stands with its agency. Null for a
/// cash sale, which is never approved.
/// </summary>
/// <param name="State">Pending, Approved, Rejected or NotDecided.</param>
public sealed record OrderApprovalResponse(
    string State,
    string? DecidedBy,
    string? DecidedByRole,
    DateTimeOffset? DecidedAt,
    string? Reason)
{
    public static OrderApprovalResponse? From(Order order)
    {
        if (order.FulfilmentType != OrderFulfilmentType.ScheduledDelivery)
        {
            return null;
        }

        if (order.ApprovalDecision is { } decision)
        {
            return new OrderApprovalResponse(
                decision.Kind.ToString(), decision.ActorUserId, decision.ActorRole, decision.DecidedAt, decision.Reason);
        }

        // NotDecided: withdrawn by the shop before the agency decided, or
        // confirmed before approvals existed.
        return new OrderApprovalResponse(
            order.Status == OrderStatus.PendingApproval ? "Pending" : "NotDecided", null, null, null, null);
    }
}

/// <summary>
/// US-E4-5: the shop's cancellation window, computed by the server at
/// <see cref="CheckedAt"/>. A client counts <see cref="RemainingSeconds"/>
/// down from when it received the response, so its own clock never decides
/// anything; the cancel endpoint re-checks on the server regardless.
/// </summary>
public sealed record OrderCancellationWindowResponse(
    bool CanCancel,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ClosesAt,
    int WindowMinutes,
    long? RemainingSeconds,
    long? ClosedSecondsAgo,
    string? Reason,
    DateTimeOffset CheckedAt)
{
    public static OrderCancellationWindowResponse From(CancellationWindow window, DateTimeOffset checkedAt) => new(
        window.CanCancel,
        window.ConfirmedAt,
        window.ClosesAt,
        (int)window.WindowLength.TotalMinutes,
        window.Remaining is { } remaining ? (long)Math.Floor(remaining.TotalSeconds) : null,
        window.ClosedAgo is { } closedAgo ? (long)Math.Floor(closedAgo.TotalSeconds) : null,
        window.Reason,
        checkedAt);
}

/// <summary>
/// Checkout state for a cash sale (US-E4-3). Null for orders that never
/// had a check-in, payment or cancellation.
/// </summary>
public sealed record OrderCheckoutResponse(
    double? CheckoutLatitude,
    double? CheckoutLongitude,
    DateTimeOffset? CheckedOutAt,
    PaymentResponse? Payment,
    CheckInResponse? LatestCheckIn,
    DateTimeOffset? CancelledAt,
    string? CancellationReason)
{
    public static OrderCheckoutResponse? From(Order order)
    {
        var latest = order.CheckIns.OrderByDescending(checkIn => checkIn.RecordedAt).FirstOrDefault();

        if (latest is null && order.Payment is null && order.CancelledAt is null)
        {
            return null;
        }

        return new OrderCheckoutResponse(
            order.CheckoutLatitude,
            order.CheckoutLongitude,
            order.CheckedOutAt,
            order.Payment is { } payment
                ? new PaymentResponse(
                    payment.PaymentId, payment.OrderId, payment.Amount, payment.Method.ToString(),
                    payment.SalesRepId, payment.CheckInId, payment.Latitude, payment.Longitude,
                    payment.DistanceMeters, payment.RecordedAt)
                : null,
            latest is null
                ? null
                : new CheckInResponse(
                    latest.OrderCheckInId, latest.OrderId, latest.Accepted, latest.DistanceMeters,
                    latest.RadiusMeters, latest.Latitude, latest.Longitude, latest.RecordedAt,
                    latest.Accepted ? latest.ExpiresAt : null),
            order.CancelledAt,
            order.CancellationReason);
    }
}

public sealed record OrderSummaryResponse(
    Guid OrderId,
    string OrderReference,
    Guid ShopId,
    Guid SalesRepId,
    Guid AgencyId,
    string FulfilmentType,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    int LineCount);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
