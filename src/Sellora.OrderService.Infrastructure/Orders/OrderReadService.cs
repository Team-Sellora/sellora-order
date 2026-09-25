using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Orders;

public sealed class OrderReadService : IOrderReadService
{
    private readonly OrderDbContext _db;
    private readonly ICurrentUserContext _caller;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _cancellationWindow;

    public OrderReadService(
        OrderDbContext db,
        ICurrentUserContext caller,
        TimeProvider clock,
        IOptions<CancellationOptions> cancellation)
    {
        _db = db;
        _caller = caller;
        _clock = clock;
        _cancellationWindow = cancellation.Value.Window;
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
                order.FulfilmentType.ToString(),
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
            .Include(order => order.VerificationSteps)
            .Include(order => order.CheckIns)
            .Include(order => order.Payment)
            .Include(order => order.Decisions)
            .ApplyCallerScope(_caller)
            .SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);

        // US-E4-5: the window as this server sees it now, so the shop owner's
        // view shows the remaining time without trusting the device clock.
        return order is null ? null : OrderResponse.From(order, _clock.GetUtcNow(), _cancellationWindow);
    }
}
