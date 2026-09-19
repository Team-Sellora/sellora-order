namespace Sellora.OrderService.Application.Orders;

public enum CreateOrderOutcome
{
    Created,
    InvalidRequest,
    TenantNotAvailable,
    CallerNotSalesRep
}

public sealed record CreateOrderResult(
    CreateOrderOutcome Outcome,
    OrderResponse? Order,
    string? Message)
{
    public static CreateOrderResult Created(OrderResponse order) =>
        new(CreateOrderOutcome.Created, order, null);

    public static CreateOrderResult Failed(CreateOrderOutcome outcome, string message) =>
        new(outcome, null, message);
}
