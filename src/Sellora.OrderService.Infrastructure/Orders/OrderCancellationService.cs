using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Events;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Orders;

/// <summary>
/// US-E4-5: a shop owner cancels their own order. The window is measured
/// from the stored confirmation time with this server's clock — the request
/// carries no time at all, so there is nothing for a client to adjust.
///
/// The cancellation, its decision record and OrderCancelled commit in one
/// SaveChanges. Inventory returns the stock when it consumes the event; it
/// is not called over HTTP because its reservation endpoints (rightly) do
/// not accept a shop owner's token, and an event written with the change
/// cannot be lost the way a second call after the save can.
/// </summary>
public sealed class OrderCancellationService : IOrderCancellationService
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _window;
    private readonly IOrderEventOutbox _events;
    private readonly ILogger<OrderCancellationService> _logger;

    public OrderCancellationService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        TimeProvider clock,
        IOptions<CancellationOptions> options,
        IOrderEventOutbox events,
        ILogger<OrderCancellationService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _clock = clock;
        _window = options.Value.Window;
        _events = events;
        _logger = logger;
    }

    public async Task<OrderDecisionResult> CancelAsync(
        Guid orderId,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is null)
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.TenantNotAvailable,
                "A valid company identifier was not found in the access token.");
        }

        if (_caller.ShopId is not { } shopId || string.IsNullOrWhiteSpace(_caller.Subject))
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.CallerNotPermitted,
                "The access token does not identify a shop owner.");
        }

        // Another shop's order is indistinguishable from a missing one.
        var order = await _db.Orders
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Decisions)
            .Include(candidate => candidate.CheckIns)
            .Include(candidate => candidate.Payment)
            .SingleOrDefaultAsync(
                candidate => candidate.OrderId == orderId && candidate.ShopId == shopId,
                cancellationToken);

        if (order is null)
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.OrderNotFound, $"No order {orderId} is visible to your shop.");
        }

        var now = _clock.GetUtcNow();

        try
        {
            order.CancelByShop(shopId, _caller.Subject!, SelloraRoles.ShopOwner, request.Reason, now, _window);
        }
        catch (OrderDecisionRuleException exception)
        {
            if (exception.Failure == OrderDecisionFailure.CancellationWindowClosed)
            {
                _logger.LogInformation(
                    "Cancellation of {OrderReference} refused: window closed at {ClosedAt}",
                    order.OrderReference, exception.WindowClosedAt);
            }

            return OrderDecisionErrors.From(exception, now);
        }

        _events.OrderCancelled(order, now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // e.g. the agency approved, or the rep took payment, in between.
            return OrderDecisionErrors.ChangedMeanwhile(orderId);
        }

        _logger.LogInformation(
            "Order {OrderReference} cancelled by shop {ShopId} (user {ActorUserId}) at {CancelledAt}; reservation {ReservationId} to be returned",
            order.OrderReference, shopId, order.CancelledBy, now, order.ReservationId);

        return OrderDecisionResult.Done(OrderResponse.From(order, now, _window), changed: true);
    }
}
