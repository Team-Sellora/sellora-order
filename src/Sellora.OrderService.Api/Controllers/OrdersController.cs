using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Api.Controllers;

/// <summary>
/// Orders are immutable once submitted. There is intentionally no PUT or
/// PATCH action on this controller; OrdersRouteImmutabilityTests fails CI
/// if one is added.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderCreationService _creation;
    private readonly IOrderReadService _reads;

    public OrdersController(
        IOrderCreationService creation,
        IOrderReadService reads)
    {
        _creation = creation;
        _reads = reads;
    }

    [HttpPost]
    [Authorize(Policy = RolePolicies.RequireSalesRep)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(
        CreateOrderRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new CreateOrderRequest(
            body.ShopId,
            (body.Lines ?? Array.Empty<CreateOrderLineRequestBody>())
                .Select(line => new BasketLine(line.ProductId, line.Quantity))
                .ToList());

        var result = await _creation.CreateAsync(request, cancellationToken);

        return result.Outcome switch
        {
            CreateOrderOutcome.Created => CreatedAtAction(
                nameof(GetById),
                new { orderId = result.Order!.OrderId },
                result.Order),

            CreateOrderOutcome.InvalidRequest =>
                ProblemResult(StatusCodes.Status400BadRequest, "Invalid order", result.Message),

            CreateOrderOutcome.TenantNotAvailable =>
                ProblemResult(StatusCodes.Status401Unauthorized, "Tenant not available", result.Message),

            CreateOrderOutcome.CallerNotSalesRep =>
                ProblemResult(StatusCodes.Status403Forbidden, "Sales rep identity missing", result.Message),

            CreateOrderOutcome.VerificationFailed =>
                RejectionResult(StatusCodes.Status422UnprocessableEntity, "Order verification failed", result.Rejection!),

            CreateOrderOutcome.DependencyUnavailable =>
                RejectionResult(StatusCodes.Status503ServiceUnavailable, "Dependency unavailable", result.Rejection!),

            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet]
    [Authorize(Policy = RolePolicies.RequireOrderReader)]
    [ProducesResponseType(typeof(PagedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _reads.ListAsync(
            new OrderListQuery(page, pageSize),
            cancellationToken));
    }

    [HttpGet("{orderId:guid}")]
    [Authorize(Policy = RolePolicies.RequireOrderReader)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _reads.GetAsync(orderId, cancellationToken);

        // Out-of-scope and non-existent look identical, so IDs from another
        // rep, agency or tenant cannot be probed.
        return order is null
            ? ProblemResult(StatusCodes.Status404NotFound, "Order not found",
                $"No order {orderId} is visible to the caller.")
            : Ok(order);
    }

    /// <summary>
    /// A rejection names the failing step and the specifics a rep can act on
    /// (short products, credit overrun, unpriced products, unavailable service).
    /// </summary>
    private ObjectResult RejectionResult(int status, string title, OrderRejection rejection)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = rejection.Reason
        };

        problem.Extensions["failedStep"] = rejection.FailedStep;
        problem.Extensions["steps"] = rejection.Steps;

        if (rejection.Shortages is not null)
        {
            problem.Extensions["shortages"] = rejection.Shortages;
        }

        if (rejection.Credit is not null)
        {
            problem.Extensions["credit"] = rejection.Credit;
        }

        if (rejection.UnresolvedProducts is not null)
        {
            problem.Extensions["unresolvedProducts"] = rejection.UnresolvedProducts;
        }

        if (rejection.Dependency is not null)
        {
            problem.Extensions["dependency"] = rejection.Dependency;
            Response.Headers.RetryAfter = "30";
        }

        return StatusCode(status, problem);
    }

    private ObjectResult ProblemResult(int status, string title, string? detail) =>
        StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        });
}
