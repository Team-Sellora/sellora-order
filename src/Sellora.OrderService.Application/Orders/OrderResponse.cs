using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Domain.Entities;

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
    OrderCheckoutResponse? Checkout = null)
{
    public static OrderResponse From(Order order) => new(
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
        OrderCheckoutResponse.From(order));
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
