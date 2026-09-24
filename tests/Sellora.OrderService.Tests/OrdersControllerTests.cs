using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Api.Controllers;
using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Application.Orders;

namespace Sellora.OrderService.Tests;

public sealed class OrdersControllerTests
{
    private static readonly CreateOrderRequestBody ValidBody = new()
    {
        ShopId = Guid.NewGuid(),
        FulfilmentType = "ScheduledDelivery",
        Lines = new[] { new CreateOrderLineRequestBody { ProductId = Guid.NewGuid(), Quantity = 1 } }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Whenever")]
    public async Task Unknown_fulfilment_type_is_a_400(string? fulfilmentType)
    {
        var controller = Controller(CreateOrderResult.Created(null!));

        var result = await controller.Create(
            new CreateOrderRequestBody { ShopId = Guid.NewGuid(), FulfilmentType = fulfilmentType },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        Assert.Contains("ImmediateCashSale", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }

    private static OrdersController Controller(CreateOrderResult result) => new(new CreationSpy(result), null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task Verification_failure_is_422_naming_the_step_and_specifics()
    {
        var rejection = new OrderRejection(
            "StockReservation",
            "Soap: requested 10, only 4 available (short by 6).",
            new[] { new StepOutcome("StockReservation", false, "short") },
            Shortages: new[] { new StockShortage(Guid.NewGuid(), "Soap", 10, 4, 6) });

        var result = await Controller(CreateOrderResult.Rejected(CreateOrderOutcome.VerificationFailed, rejection))
            .Create(ValidBody, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(422, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("StockReservation", problem.Extensions["failedStep"]);
        Assert.True(problem.Extensions.ContainsKey("shortages"));
        Assert.False(problem.Extensions.ContainsKey("credit"));
    }

    [Fact]
    public async Task Unavailable_dependency_is_503_with_its_name_and_retry_after()
    {
        var rejection = new OrderRejection(
            "StockReservation",
            "Inventory service is currently unavailable, please retry shortly.",
            Array.Empty<StepOutcome>(),
            Dependency: "Inventory");
        var controller = Controller(CreateOrderResult.Rejected(CreateOrderOutcome.DependencyUnavailable, rejection));

        var result = await controller.Create(ValidBody, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("Inventory", Assert.IsType<ProblemDetails>(objectResult.Value).Extensions["dependency"]);
        Assert.Equal("30", controller.Response.Headers.RetryAfter.ToString());
    }

    private sealed class CreationSpy(CreateOrderResult result) : IOrderCreationService
    {
        public CreateOrderRequest? Received { get; private set; }

        public Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
        {
            Received = request;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public void Client_supplied_totals_have_nowhere_to_bind()
    {
        Assert.Null(typeof(CreateOrderRequestBody).GetProperty("Total"));
        Assert.Null(typeof(CreateOrderRequestBody).GetProperty("Subtotal"));
        Assert.Null(typeof(CreateOrderLineRequestBody).GetProperty("LineTotal"));
        // US-E4-1b: prices come from Catalog only.
        Assert.Null(typeof(CreateOrderLineRequestBody).GetProperty("UnitPrice"));
        Assert.Null(typeof(CreateOrderLineRequestBody).GetProperty("ProductName"));

        // Same JSON options as MVC: unknown "total" is silently dropped.
        var body = JsonSerializer.Deserialize<CreateOrderRequestBody>(
            """{ "shopId": "11111111-1111-1111-1111-111111111111", "total": 1, "subtotal": 1, "lines": [{ "productId": "22222222-2222-2222-2222-222222222222", "quantity": 2, "unitPrice": 1 }] }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(body);
        Assert.Equal(2, Assert.Single(body!.Lines!).Quantity);
    }

    [Theory]
    [InlineData(CreateOrderOutcome.InvalidRequest, 400)]
    [InlineData(CreateOrderOutcome.TenantNotAvailable, 401)]
    [InlineData(CreateOrderOutcome.CallerNotSalesRep, 403)]
    public async Task Failures_map_to_the_right_status(CreateOrderOutcome outcome, int status)
    {
        var controller = new OrdersController(
            new CreationSpy(CreateOrderResult.Failed(outcome, "specific reason")), null!, null!);

        var result = await controller.Create(ValidBody, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        Assert.Equal("specific reason", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }

    [Fact]
    public async Task Success_returns_201_pointing_at_the_detail_route()
    {
        var order = new OrderResponse(Guid.NewGuid(), "ORD-260918-ABCDEF", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ScheduledDelivery", "Confirmed",
            DateTimeOffset.UtcNow, 10m, 10m,
            Guid.NewGuid(), Array.Empty<OrderLineResponse>(), Array.Empty<OrderVerificationStepResponse>());

        var controller = new OrdersController(new CreationSpy(CreateOrderResult.Created(order)), null!, null!);

        var result = await controller.Create(ValidBody, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(OrdersController.GetById), created.ActionName);
        Assert.Same(order, created.Value);
    }

    private sealed class CheckoutStub(CheckInResult checkIn, PaymentResult payment) : ICheckoutService
    {
        public Task<CheckInResult> CheckInAsync(Guid orderId, CheckInRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(checkIn);

        public Task<PaymentResult> RecordPaymentAsync(Guid orderId, PaymentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(payment);
    }

    private static OrdersController CheckoutController(CheckInResult? checkIn = null, PaymentResult? payment = null) =>
        new(null!, null!, new CheckoutStub(
            checkIn ?? CheckInResult.Failed(CheckoutOutcome.InvalidRequest, "x"),
            payment ?? PaymentResult.Failed(CheckoutOutcome.InvalidRequest, "x")))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public async Task Rejected_check_in_is_403_with_the_measured_distance()
    {
        var rejected = new CheckInResponse(Guid.NewGuid(), Guid.NewGuid(), false, 342.5, 300, 6.9, 79.8, DateTimeOffset.UtcNow, null);
        var controller = CheckoutController(checkIn: CheckInResult.From(rejected));

        var result = await controller.CheckIn(
            Guid.NewGuid(),
            new CheckInRequestBody { Latitude = 6.9, Longitude = 79.8, CapturedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(342.5, problem.Extensions["distanceMeters"]);
        Assert.Contains("343 m", problem.Detail);
    }

    [Fact]
    public async Task Missing_coordinates_are_a_400()
    {
        var result = await CheckoutController().CheckIn(
            Guid.NewGuid(), new CheckInRequestBody { Latitude = 6.9 }, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(CheckoutOutcome.CheckInRequired, 403)]
    [InlineData(CheckoutOutcome.CheckInExpired, 403)]
    [InlineData(CheckoutOutcome.OrderNotFound, 404)]
    [InlineData(CheckoutOutcome.NotAwaitingCheckout, 409)]
    [InlineData(CheckoutOutcome.ReservationExpired, 409)]
    [InlineData(CheckoutOutcome.AmountMismatch, 422)]
    public async Task Payment_failures_map_to_the_right_status(CheckoutOutcome outcome, int status)
    {
        var controller = CheckoutController(payment: PaymentResult.Failed(outcome, "reason", 12_500m));

        var result = await controller.RecordPayment(
            Guid.NewGuid(), new PaymentRequestBody { Amount = 12_000m, Method = "Cash" }, CancellationToken.None);

        Assert.Equal(status, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Only_cash_is_accepted()
    {
        var result = await CheckoutController().RecordPayment(
            Guid.NewGuid(), new PaymentRequestBody { Amount = 10m, Method = "Card" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
        Assert.Contains("Cash", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }
}
