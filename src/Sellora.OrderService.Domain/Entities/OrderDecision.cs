using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// US-E4-5: one decision taken on an order — an agency approval or
/// rejection, or a shop owner's cancellation. Append-only: every decision
/// keeps who took it, in which role, when, why, and the status it moved the
/// order from and to, so a disputed order has a trail without reading logs.
/// </summary>
public sealed class OrderDecision : ITenantScoped
{
    public const int MaxActorLength = 200;

    private OrderDecision()
    {
    }

    internal OrderDecision(
        Guid orderId,
        Guid companyId,
        OrderDecisionKind kind,
        string actorUserId,
        string actorRole,
        string? reason,
        OrderStatus statusBefore,
        OrderStatus statusAfter,
        DateTimeOffset decidedAt)
    {
        OrderDecisionId = Guid.NewGuid();
        OrderId = orderId;
        CompanyId = companyId;
        Kind = kind;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        Reason = reason;
        StatusBefore = statusBefore;
        StatusAfter = statusAfter;
        DecidedAt = decidedAt;
    }

    public Guid OrderDecisionId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public OrderDecisionKind Kind { get; private set; }

    /// <summary>The caller's identity-provider user ID (the token's <c>sub</c>).</summary>
    public string ActorUserId { get; private set; } = string.Empty;

    public string ActorRole { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public OrderStatus StatusBefore { get; private set; }

    public OrderStatus StatusAfter { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }
}
