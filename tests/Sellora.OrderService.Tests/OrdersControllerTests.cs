using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Api.Controllers;
using Sellora.OrderService.Application.Orders;

namespace Sellora.OrderService.Tests;

public sealed class OrdersControllerTests
{
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

        // Same JSON options as MVC: unknown "total" is silently dropped.
        var body = JsonSerializer.Deserialize<CreateOrderRequestBody>(
            """{ "shopId": "11111111-1111-1111-1111-111111111111", "total": 1, "subtotal": 1, "lines": [] }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(body);
    }

    [Theory]
    [InlineData(CreateOrderOutcome.InvalidRequest, 400)]
    [InlineData(CreateOrderOutcome.TenantNotAvailable, 401)]
    [InlineData(CreateOrderOutcome.CallerNotSalesRep, 403)]
    public async Task Failures_map_to_the_right_status(CreateOrderOutcome outcome, int status)
    {
        var controller = new OrdersController(
            new CreationSpy(CreateOrderResult.Failed(outcome, "specific reason")), null!);

        var result = await controller.Create(new CreateOrderRequestBody(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        Assert.Equal("specific reason", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }

    [Fact]
    public async Task Success_returns_201_pointing_at_the_detail_route()
    {
        var order = new OrderResponse(Guid.NewGuid(), "ORD-260918-ABCDEF", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Submitted", DateTimeOffset.UtcNow, 10m, 10m,
            Array.Empty<OrderLineResponse>());

        var controller = new OrdersController(new CreationSpy(CreateOrderResult.Created(order)), null!);

        var result = await controller.Create(new CreateOrderRequestBody(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(OrdersController.GetById), created.ActionName);
        Assert.Same(order, created.Value);
    }
}
