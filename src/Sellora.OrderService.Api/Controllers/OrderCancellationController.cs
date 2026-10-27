using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Application.Orders;

namespace Sellora.OrderService.Api.Controllers;

/// <summary>
/// US-E4-5 shop cancellation. POST creates a cancellation of the order;
/// the order's lines and totals are never edited.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrderCancellationController : ControllerBase
{
    private readonly IOrderCancellationService _cancellations;

    public OrderCancellationController(IOrderCancellationService cancellations)
    {
        _cancellations = cancellations;
    }

    /// <summary>
    /// Only the shop owner of the order's own shop, only inside the window
    /// (default one hour from confirmation, computed on the server). A closed
    /// window is a 409 stating how long ago it closed.
    /// </summary>
    [HttpPost("{orderId:guid}/cancellation")]
    [Authorize(Policy = RolePolicies.RequireShopOwner)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid orderId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancellationRequestBody? body,
        CancellationToken cancellationToken)
    {
        var result = await _cancellations.CancelAsync(
            orderId, new CancelOrderRequest(body?.Reason), cancellationToken);

        return this.ToActionResult(result);
    }
}
