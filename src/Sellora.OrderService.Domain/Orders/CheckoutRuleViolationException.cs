namespace Sellora.OrderService.Domain.Orders;

public enum CheckoutFailure
{
    NotAwaitingCheckout,
    WrongSalesRep,
    InvalidCoordinates,
    CapturedInFuture,
    CaptureTooOld,
    CheckInRequired,
    CheckInExpired,
    AmountMismatch,
    UnsupportedPaymentMethod
}

/// <summary>
/// A check-in or checkout broke a rule. <see cref="Failure"/> lets the API
/// choose the status code; the message is shown to the rep as-is.
/// </summary>
public sealed class CheckoutRuleViolationException : Exception
{
    public CheckoutRuleViolationException(CheckoutFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public CheckoutFailure Failure { get; }
}
