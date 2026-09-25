namespace Sellora.OrderService.Application.Orders;

/// <summary>US-E4-5: the agency's decision on a scheduled delivery.</summary>
public enum ApprovalDecision
{
    Approve = 1,
    Reject = 2
}

/// <param name="Reason">Mandatory for <see cref="ApprovalDecision.Reject"/>.</param>
public sealed record ApprovalDecisionRequest(ApprovalDecision Decision, string? Reason);

public interface IOrderApprovalService
{
    /// <summary>
    /// Approves or rejects a scheduled delivery awaiting approval, for the
    /// caller's own agency only. Repeating the decision already taken
    /// succeeds without changing anything (PUT is idempotent).
    /// </summary>
    Task<OrderDecisionResult> DecideAsync(
        Guid orderId,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken);
}
