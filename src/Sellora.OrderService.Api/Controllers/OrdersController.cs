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
    public async Task<IActionResult> Create(
        CreateOrderRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new CreateOrderRequest(
            body.ShopId,
            body.AgencyId,
            body.TerritoryId,
            body.ProvinceId,
            (body.Lines ?? Array.Empty<CreateOrderLineRequestBody>())
                .Select(line => new NewOrderLine(
                    line.ProductId,
                    line.ProductName ?? string.Empty,
                    line.Quantity,
                    line.UnitPrice))
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

    private ObjectResult ProblemResult(int status, string title, string? detail) =>
        StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        });
}
