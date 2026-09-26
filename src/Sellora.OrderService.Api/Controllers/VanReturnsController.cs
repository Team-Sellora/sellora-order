using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Application.VanReturns;

namespace Sellora.OrderService.Api.Controllers;

/// <summary>
/// US-E4-6 end-of-route van returns. Deliberately not under /api/orders:
/// a van return is not an order and never touches one, and orders' routes
/// are guarded as immutable (OrdersRouteImmutabilityTests).
/// </summary>
[ApiController]
[Route("api/van-returns")]
public sealed class VanReturnsController : ControllerBase
{
    private readonly IVanReturnService _vanReturns;

    public VanReturnsController(IVanReturnService vanReturns)
    {
        _vanReturns = vanReturns;
    }

    /// <summary>The rep declares unsold van stock; rejected if the van does not hold it.</summary>
    [HttpPost]
    [Authorize(Policy = RolePolicies.RequireSalesRep)]
    [ProducesResponseType(typeof(VanReturnResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Declare(DeclareVanReturnRequestBody body, CancellationToken cancellationToken)
    {
        var request = new DeclareVanReturnRequest(
            (body.Lines ?? Array.Empty<DeclareVanReturnLineBody>())
                .Select(line => new DeclareVanReturnLine(line.ProductId, line.Quantity))
                .ToList());

        var result = await _vanReturns.DeclareAsync(request, cancellationToken);

        return result.Outcome == VanReturnOutcome.Succeeded
            ? CreatedAtAction(nameof(Get), new { vanReturnId = result.VanReturn!.VanReturnId }, result.VanReturn)
            : ToProblem(result);
    }

    /// <summary>
    /// The rep's own agency operator records the counted quantity per line.
    /// Counted may be lower than declared; both and the variance are kept,
    /// and only the counted stock moves. Repeating the same counts is a no-op.
    /// </summary>
    [HttpPut("{vanReturnId:guid}/acceptance")]
    [Authorize(Policy = RolePolicies.RequireAgencyOperator)]
    [ProducesResponseType(typeof(VanReturnResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Accept(
        Guid vanReturnId,
        AcceptVanReturnRequestBody body,
        CancellationToken cancellationToken)
    {
        var request = new AcceptVanReturnRequest(
            (body.Lines ?? Array.Empty<AcceptVanReturnLineBody>())
                .Select(line => new AcceptVanReturnLine(line.ProductId, line.CountedQuantity))
                .ToList(),
            body.Note);

        var result = await _vanReturns.AcceptAsync(vanReturnId, request, cancellationToken);

        return result.Outcome == VanReturnOutcome.Succeeded ? Ok(result.VanReturn) : ToProblem(result);
    }

    /// <summary>?status=Declared gives the agency's pending-acceptance list.</summary>
    [HttpGet]
    [Authorize(Policy = RolePolicies.RequireOrderReader)]
    [ProducesResponseType(typeof(PagedResponse<VanReturnSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await _vanReturns.ListAsync(new VanReturnListQuery(page, pageSize, status), cancellationToken));

    [HttpGet("{vanReturnId:guid}")]
    [Authorize(Policy = RolePolicies.RequireOrderReader)]
    [ProducesResponseType(typeof(VanReturnResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid vanReturnId, CancellationToken cancellationToken)
    {
        var vanReturn = await _vanReturns.GetAsync(vanReturnId, cancellationToken);

        return vanReturn is null
            ? NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Van return not found",
                Detail = $"No van return {vanReturnId} is visible to you."
            })
            : Ok(vanReturn);
    }

    private IActionResult ToProblem(VanReturnResult result)
    {
        var (status, title) = result.Outcome switch
        {
            VanReturnOutcome.InvalidRequest => (StatusCodes.Status400BadRequest, "Invalid van return"),
            VanReturnOutcome.TenantNotAvailable => (StatusCodes.Status401Unauthorized, "Tenant not available"),
            VanReturnOutcome.CallerNotPermitted => (StatusCodes.Status403Forbidden, "Caller scope missing"),
            VanReturnOutcome.NotFound => (StatusCodes.Status404NotFound, "Van return not found"),
            VanReturnOutcome.Conflict => (StatusCodes.Status409Conflict, "Van return already accepted"),
            VanReturnOutcome.NoVanStock => (StatusCodes.Status422UnprocessableEntity, "No van stock"),
            VanReturnOutcome.ExceedsVanStock => (StatusCodes.Status422UnprocessableEntity, "More than the van holds"),
            VanReturnOutcome.DependencyUnavailable => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Van return failed")
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = result.Message };

        if (result.Shortages is { Count: > 0 } shortages)
        {
            problem.Extensions["shortages"] = shortages;
        }

        if (result.Dependency is not null)
        {
            problem.Extensions["dependency"] = result.Dependency;
            Response.Headers.RetryAfter = "30";
        }

        return StatusCode(status, problem);
    }
}
