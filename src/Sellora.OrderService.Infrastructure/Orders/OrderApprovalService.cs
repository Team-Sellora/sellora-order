using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sellora.OrderService.Application.Events;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Orders;

/// <summary>
/// US-E4-5: the agency approves or rejects a scheduled delivery. The
/// decision, the status change and the events commit in one SaveChanges:
///   approve → OrderApproved, then OrderConfirmed (the order is binding now)
///   reject  → OrderCancelled with the reason (Inventory returns the stock)
/// Inventory is not called over HTTP here: the event is the one path that
/// commits atomically with the decision (see docs/US-E4-5.md).
/// </summary>
public sealed class OrderApprovalService : IOrderApprovalService
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly TimeProvider _clock;
    private readonly IOrderEventOutbox _events;
    private readonly ILogger<OrderApprovalService> _logger;

    public OrderApprovalService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        TimeProvider clock,
        IOrderEventOutbox events,
        ILogger<OrderApprovalService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _clock = clock;
        _events = events;
        _logger = logger;
    }

    public async Task<OrderDecisionResult> DecideAsync(
        Guid orderId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is null)
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.TenantNotAvailable,
                "A valid company identifier was not found in the access token.");
        }

        if (_caller.AgencyId is not { } agencyId || string.IsNullOrWhiteSpace(_caller.Subject))
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.CallerNotPermitted,
                "The access token does not identify an agency operator.");
        }

        // Scoped to the caller's agency in the query itself: another agency's
        // order is indistinguishable from a missing one.
        var order = await _db.Orders
            .Include(candidate => candidate.Lines)
            .Include(candidate => candidate.Decisions)
            .SingleOrDefaultAsync(
                candidate => candidate.OrderId == orderId && candidate.AgencyId == agencyId,
                cancellationToken);

        if (order is null)
        {
            return OrderDecisionResult.Failed(
                OrderDecisionOutcome.OrderNotFound, $"No order {orderId} is visible to your agency.");
        }

        var now = _clock.GetUtcNow();
        OrderDecision? decision;

        try
        {
            decision = request.Decision == ApprovalDecision.Approve
                ? order.Approve(agencyId, _caller.Subject!, SelloraRoles.AgencyOperator, now)
                : order.Reject(agencyId, _caller.Subject!, SelloraRoles.AgencyOperator, request.Reason, now);
        }
        catch (OrderDecisionRuleException exception)
        {
            return OrderDecisionErrors.From(exception, now);
        }

        if (decision is null)
        {
            // Same decision already recorded: PUT is idempotent.
            return OrderDecisionResult.Done(OrderResponse.From(order), changed: false);
        }

        if (decision.Kind == OrderDecisionKind.Approved)
        {
            _events.OrderApproved(order, decision, now);
            _events.OrderConfirmed(order, now);
        }
        else
        {
            _events.OrderCancelled(order, now);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OrderDecisionErrors.ChangedMeanwhile(orderId);
        }

        _logger.LogInformation(
            "Order {OrderReference} {Decision} by agency {AgencyId} (user {ActorUserId}); reason: {Reason}",
            order.OrderReference, decision.Kind, agencyId, decision.ActorUserId, decision.Reason ?? "-");

        return OrderDecisionResult.Done(OrderResponse.From(order), changed: true);
    }
}
