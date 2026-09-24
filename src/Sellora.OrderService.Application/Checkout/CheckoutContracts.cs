using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Application.Checkout;

/// <summary>The rep's device position. Company and rep come from the token.</summary>
public sealed record CheckInRequest(
    double Latitude,
    double Longitude,
    DateTimeOffset CapturedAt,
    double? AccuracyMeters);

public sealed record PaymentRequest(decimal Amount, PaymentMethod Method);

public enum CheckoutOutcome
{
    Succeeded,
    OrderNotFound,
    InvalidRequest,
    NotAwaitingCheckout,
    OutsideRadius,
    CheckInRequired,
    CheckInExpired,
    AmountMismatch,
    ReservationExpired,
    ShopLocationUnavailable,
    TenantNotAvailable,
    CallerNotSalesRep,
    DependencyUnavailable
}

public sealed record CheckInResponse(
    Guid CheckInId,
    Guid OrderId,
    bool Accepted,
    double DistanceMeters,
    double RadiusMeters,
    double Latitude,
    double Longitude,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidUntil);

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Method,
    Guid SalesRepId,
    Guid CheckInId,
    double Latitude,
    double Longitude,
    double DistanceMeters,
    DateTimeOffset RecordedAt);

public sealed record CheckInResult(
    CheckoutOutcome Outcome,
    CheckInResponse? CheckIn,
    string? Message)
{
    /// <summary>Creates a new CheckInResult based on the provided CheckInResponse.</summary>
    public static CheckInResult From(CheckInResponse checkIn) => new(
        checkIn.Accepted ? CheckoutOutcome.Succeeded : CheckoutOutcome.OutsideRadius,
        checkIn,
        checkIn.Accepted
            ? null
            : FormattableString.Invariant(
                $"You are {Math.Round(checkIn.DistanceMeters, MidpointRounding.AwayFromZero):N0} m from the shop; check-in is allowed within {Math.Round(checkIn.RadiusMeters, MidpointRounding.AwayFromZero):N0} m. Move closer and try again."));

    public static CheckInResult Failed(CheckoutOutcome outcome, string message) => new(outcome, null, message);
}

public sealed record PaymentResult(
    CheckoutOutcome Outcome,
    PaymentResponse? Payment,
    string? Message,
    decimal? ExpectedAmount = null,
    string? Dependency = null)
{
    public static PaymentResult Succeeded(PaymentResponse payment) => new(CheckoutOutcome.Succeeded, payment, null);

    public static PaymentResult Failed(CheckoutOutcome outcome, string message, decimal? expectedAmount = null) =>
        new(outcome, null, message, expectedAmount);
}

public interface ICheckoutService
{
    /// <summary>POST /api/orders/{id}/checkin</summary>
    Task<CheckInResult> CheckInAsync(Guid orderId, CheckInRequest request, CancellationToken cancellationToken);

    /// <summary>POST /api/orders/{id}/payment</summary>
    Task<PaymentResult> RecordPaymentAsync(Guid orderId, PaymentRequest request, CancellationToken cancellationToken);
}
