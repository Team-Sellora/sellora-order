using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Application.Orders;

namespace Sellora.OrderService.Api.Controllers;

/// <summary>
/// US-E4-5 agency approval of scheduled deliveries. PUT because the request
/// sets the order's approval decision: repeating the same decision returns
/// the order unchanged. It does not edit lines or totals — orders stay
/// immutable, and OrdersRouteImmutabilityTests allows this one route only.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrderApprovalController : ControllerBase
{
    private readonly IOrderApprovalService _approvals;

    public OrderApprovalController(IOrderApprovalService approvals)
    {
        _approvals = approvals;
    }

    [HttpPut("{orderId:guid}/approval")]
    [Authorize(Policy = RolePolicies.RequireAgencyOperator)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide(
        Guid orderId,
        ApprovalRequestBody body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalDecision>(body.Decision, ignoreCase: true, out var decision) ||
            !Enum.IsDefined(decision))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid decision",
                Detail = "decision is required and must be Approve or Reject."
            });
        }

        var result = await _approvals.DecideAsync(
            orderId, new ApprovalDecisionRequest(decision, body.Reason), cancellationToken);

        return this.ToActionResult(result);
    }
}
