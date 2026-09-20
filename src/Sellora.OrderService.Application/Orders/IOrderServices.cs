namespace Sellora.OrderService.Application.Orders;

public interface IOrderCreationService
{
    Task<CreateOrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken);
}

public interface IOrderReadService
{
    /// <summary>Orders visible to the caller, newest first.</summary>
    Task<PagedResponse<OrderSummaryResponse>> ListAsync(
        OrderListQuery query,
        CancellationToken cancellationToken);

    /// <summary>Null when the order does not exist OR is outside the caller's scope.</summary>
    Task<OrderResponse?> GetAsync(
        Guid orderId,
        CancellationToken cancellationToken);
}
