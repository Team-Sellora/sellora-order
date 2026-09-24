using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Application.Checkout;
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
    private readonly ICheckoutService _checkout;

    public OrdersController(
        IOrderCreationService creation,
        IOrderReadService reads,
        ICheckoutService checkout)
    {
        _creation = creation;
        _reads = reads;
        _checkout = checkout;
    }

    /// <summary>
    /// US-E4-3 GPS gate. Separate from order creation on purpose (open issue
    /// #7): the gate applies at checkout, when the rep is at the counter.
    /// </summary>
    [HttpPost("{orderId:guid}/checkin")]
    [Authorize(Policy = RolePolicies.RequireSalesRep)]
    [ProducesResponseType(typeof(CheckInResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(
        Guid orderId,
        CheckInRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body.Latitude is not { } latitude || body.Longitude is not { } longitude || body.CapturedAt is not { } capturedAt)
        {
            return ProblemResult(StatusCodes.Status400BadRequest, "Invalid check-in",
                "latitude, longitude and capturedAt are required.");
        }

        var result = await _checkout.CheckInAsync(
            orderId,
            new CheckInRequest(latitude, longitude, capturedAt, body.AccuracyMeters),
            cancellationToken);

        if (result.Outcome == CheckoutOutcome.Succeeded)
        {
            return Ok(result.CheckIn);
        }

        if (result.Outcome == CheckoutOutcome.OutsideRadius)
        {
            // 403 with the measured distance: the rep needs "you are 340 m
            // away", not just "failed", to know to walk closer.
            var problem = BuildProblem(StatusCodes.Status403Forbidden, "Outside the permitted radius", result.Message);
            problem.Extensions["distanceMeters"] = result.CheckIn!.DistanceMeters;
            problem.Extensions["radiusMeters"] = result.CheckIn.RadiusMeters;
            problem.Extensions["checkInId"] = result.CheckIn.CheckInId;
            return StatusCode(StatusCodes.Status403Forbidden, problem);
        }

        return CheckoutProblem(result.Outcome, result.Message, null, null);
    }

    /// <summary>
    /// Records the cash payment. Gated server-side on a stored, unexpired,
    /// accepted check-in by the same rep — without that guard every other
    /// fraud measure is decorative.
    /// </summary>
    [HttpPost("{orderId:guid}/payment")]
    [Authorize(Policy = RolePolicies.RequireSalesRep)]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RecordPayment(
        Guid orderId,
        PaymentRequestBody body,
        CancellationToken cancellationToken)
    {
        if (body.Amount is not { } amount)
        {
            return ProblemResult(StatusCodes.Status400BadRequest, "Invalid payment", "amount is required.");
        }

        if (!Enum.TryParse<PaymentMethod>(body.Method, ignoreCase: true, out var method) || !Enum.IsDefined(method))
        {
            return ProblemResult(StatusCodes.Status400BadRequest, "Invalid payment", "method must be Cash; no other payment method is supported.");
        }

        var result = await _checkout.RecordPaymentAsync(orderId, new PaymentRequest(amount, method), cancellationToken);

        return result.Outcome == CheckoutOutcome.Succeeded
            ? CreatedAtAction(nameof(GetById), new { orderId }, result.Payment)
            : CheckoutProblem(result.Outcome, result.Message, result.ExpectedAmount, result.Dependency, amount);
    }

    private ObjectResult CheckoutProblem(
        CheckoutOutcome outcome,
        string? message,
        decimal? expectedAmount,
        string? dependency,
        decimal? submittedAmount = null)
    {
        var (status, title) = outcome switch
        {
            CheckoutOutcome.InvalidRequest => (StatusCodes.Status400BadRequest, "Invalid request"),
            CheckoutOutcome.TenantNotAvailable => (StatusCodes.Status401Unauthorized, "Tenant not available"),
            CheckoutOutcome.CallerNotSalesRep => (StatusCodes.Status403Forbidden, "Sales rep identity missing"),
            CheckoutOutcome.CheckInRequired => (StatusCodes.Status403Forbidden, "Check-in required"),
            CheckoutOutcome.CheckInExpired => (StatusCodes.Status403Forbidden, "Check-in expired"),
            CheckoutOutcome.OrderNotFound => (StatusCodes.Status404NotFound, "Order not found"),
            CheckoutOutcome.NotAwaitingCheckout => (StatusCodes.Status409Conflict, "Not awaiting checkout"),
            CheckoutOutcome.ReservationExpired => (StatusCodes.Status409Conflict, "Stock hold expired"),
            CheckoutOutcome.AmountMismatch => (StatusCodes.Status422UnprocessableEntity, "Amount does not match"),
            CheckoutOutcome.ShopLocationUnavailable => (StatusCodes.Status422UnprocessableEntity, "Shop location unavailable"),
            CheckoutOutcome.DependencyUnavailable => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Checkout failed")
        };

        var problem = BuildProblem(status, title, message);

        if (expectedAmount is not null)
        {
            problem.Extensions["expectedAmount"] = expectedAmount;
            problem.Extensions["submittedAmount"] = submittedAmount;
        }

        if (dependency is not null)
        {
            problem.Extensions["dependency"] = dependency;
            Response.Headers.RetryAfter = "30";
        }

        return StatusCode(status, problem);
    }

    private static ProblemDetails BuildProblem(int status, string title, string? detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail
    };

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
        // Parsed here so a typo is a clear 400, not a silent default.
        if (!Enum.TryParse<OrderFulfilmentType>(body.FulfilmentType, ignoreCase: true, out var fulfilmentType) ||
            !Enum.IsDefined(fulfilmentType))
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                "Invalid order",
                "fulfilmentType is required and must be ImmediateCashSale or ScheduledDelivery.");
        }

        var request = new CreateOrderRequest(
            body.ShopId,
            fulfilmentType,
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
