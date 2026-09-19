using Microsoft.EntityFrameworkCore;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Orders;

public sealed class OrderReadService : IOrderReadService
{
    private readonly OrderDbContext _db;
    private readonly ICurrentUserContext _caller;

    public OrderReadService(OrderDbContext db, ICurrentUserContext caller)
    {
        _db = db;
        _caller = caller;
    }

    public async Task<PagedResponse<OrderSummaryResponse>> ListAsync(
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var scoped = _db.Orders.AsNoTracking().ApplyCallerScope(_caller);

        var totalCount = await scoped.CountAsync(cancellationToken);

        var items = await scoped
            .OrderByDescending(order => order.OrderDate)
            .ThenBy(order => order.OrderReference)
            .Skip((query.SafePage - 1) * query.SafePageSize)
            .Take(query.SafePageSize)
            .Select(order => new OrderSummaryResponse(
                order.OrderId,
                order.OrderReference,
                order.ShopId,
                order.SalesRepId,
                order.AgencyId,
                order.Status.ToString(),
                order.OrderDate,
                order.Total,
                order.Lines.Count))
            .ToListAsync(cancellationToken);

        return new PagedResponse<OrderSummaryResponse>(
            items,
            query.SafePage,
            query.SafePageSize,
            totalCount);
    }

    public async Task<OrderResponse?> GetAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(order => order.Lines)
            .ApplyCallerScope(_caller)
            .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);

        return order is null ? null : OrderResponse.From(order);
    }
}
