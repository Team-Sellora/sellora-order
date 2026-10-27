namespace Sellora.OrderService.Application.Orders;

public enum CreateOrderOutcome
{
    Created,
    InvalidRequest,
    TenantNotAvailable,
    CallerNotSalesRep,
    VerificationFailed,
    DependencyUnavailable
}

public sealed record StepOutcome(string Step, bool Passed, string Detail);

public sealed record StockShortage(
    Guid ProductId,
    string? ProductName,
    int RequestedQuantity,
    int AvailableQuantity,
    int ShortBy);

public sealed record CreditCheckDetail(
    decimal CreditLimit,
    decimal OutstandingBalance,
    decimal OrderTotal,
    decimal ExceededBy);

public sealed record UnresolvedProduct(Guid ProductId, string Reason);

/// <summary>
/// Why an order was not accepted: which step failed, why, and the specifics
/// a rep can act on at the shop counter.
/// </summary>
public sealed record OrderRejection(
    string FailedStep,
    string Reason,
    IReadOnlyList<StepOutcome> Steps,
    IReadOnlyList<StockShortage>? Shortages = null,
    CreditCheckDetail? Credit = null,
    IReadOnlyList<UnresolvedProduct>? UnresolvedProducts = null,
    string? Dependency = null);

public sealed record CreateOrderResult(
    CreateOrderOutcome Outcome,
    OrderResponse? Order,
    string? Message,
    OrderRejection? Rejection = null)
{
    public static CreateOrderResult Created(OrderResponse order) =>
        new(CreateOrderOutcome.Created, order, null);

    public static CreateOrderResult Failed(CreateOrderOutcome outcome, string message) =>
        new(outcome, null, message);

    public static CreateOrderResult Rejected(CreateOrderOutcome outcome, OrderRejection rejection) =>
        new(outcome, null, rejection.Reason, rejection);
}
