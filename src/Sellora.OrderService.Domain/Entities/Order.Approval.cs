using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// US-E4-5 agency approval. A scheduled delivery is taken on credit, so it
/// starts in <see cref="OrderStatus.PendingApproval"/> and the owning agency
/// either approves it (it becomes Confirmed and binding) or rejects it with
/// a reason (it is cancelled and its stock goes back). Cash sales never need
/// approval: the shop pays at the counter.
/// </summary>
public sealed partial class Order
{
    /// <summary>The latest approval or rejection, or null if none was taken.</summary>
    public OrderDecision? ApprovalDecision =>
        _decisions
            .Where(decision => decision.Kind is OrderDecisionKind.Approved or OrderDecisionKind.Rejected)
            .OrderByDescending(decision => decision.DecidedAt)
            .FirstOrDefault();

    /// <summary>
    /// Approves a pending scheduled delivery for the owning agency. Returns
    /// the new decision, or null when this order was already approved (a
    /// repeated PUT changes nothing).
    /// </summary>
    public OrderDecision? Approve(
        Guid agencyId,
        string actorUserId,
        string actorRole,
        DateTimeOffset now)
    {
        EnsureOwningAgency(agencyId);

        // Only a no-op while the approval still stands: an approved order the
        // shop has since cancelled must not report "already approved".
        if (Status == OrderStatus.Confirmed && ApprovalDecision?.Kind == OrderDecisionKind.Approved)
        {
            return null;
        }

        EnsureAwaitingApproval();

        var decision = RecordDecision(
            OrderDecisionKind.Approved, actorUserId, actorRole, null, OrderStatus.Confirmed, now);

        ConfirmedAt = now;
        return decision;
    }

    /// <summary>
    /// Rejects a pending scheduled delivery. The reason is mandatory and is
    /// checked first, so a missing reason leaves the order untouched.
    /// Returns null when this order was already rejected.
    /// </summary>
    public OrderDecision? Reject(
        Guid agencyId,
        string actorUserId,
        string actorRole,
        string? reason,
        DateTimeOffset now)
    {
        var normalised = NormaliseReason(reason)
            ?? throw new OrderDecisionRuleException(
                OrderDecisionFailure.ReasonRequired,
                "A reason is required to reject an order.");

        EnsureOwningAgency(agencyId);

        if (Status == OrderStatus.Cancelled && ApprovalDecision?.Kind == OrderDecisionKind.Rejected)
        {
            return null;
        }

        EnsureAwaitingApproval();

        var decision = RecordDecision(
            OrderDecisionKind.Rejected, actorUserId, actorRole, normalised, OrderStatus.Cancelled, now);

        CancelledAt = now;
        CancelledBy = decision.ActorUserId;
        CancellationReason = normalised;
        return decision;
    }

    private void EnsureOwningAgency(Guid agencyId)
    {
        // Another agency's order looks exactly like a missing one.
        if (agencyId == Guid.Empty || agencyId != AgencyId)
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.NotVisible,
                "The order is not visible to your agency.");
        }
    }

    private void EnsureAwaitingApproval()
    {
        if (FulfilmentType != OrderFulfilmentType.ScheduledDelivery)
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.NotAwaitingApproval,
                "Only scheduled deliveries are approved by the agency; a cash sale is paid at the counter.");
        }

        if (Status != OrderStatus.PendingApproval)
        {
            throw new OrderDecisionRuleException(
                OrderDecisionFailure.NotAwaitingApproval,
                $"This order is {Status} and is no longer awaiting approval.");
        }
    }
}
