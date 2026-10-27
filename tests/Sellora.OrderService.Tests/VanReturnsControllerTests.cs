using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sellora.OrderService.Api.Contracts;
using Sellora.OrderService.Api.Controllers;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Application.VanReturns;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-6: each van-return outcome maps to its HTTP problem.</summary>
public sealed class VanReturnsControllerTests
{
    [Theory]
    [InlineData(VanReturnOutcome.InvalidRequest, 400)]
    [InlineData(VanReturnOutcome.TenantNotAvailable, 401)]
    [InlineData(VanReturnOutcome.CallerNotPermitted, 403)]
    [InlineData(VanReturnOutcome.NotFound, 404)]
    [InlineData(VanReturnOutcome.Conflict, 409)]
    [InlineData(VanReturnOutcome.NoVanStock, 422)]
    public async Task Failures_map_to_the_right_status(VanReturnOutcome outcome, int status)
    {
        var controller = Controller(VanReturnResult.Failed(outcome, "specific reason"));

        var result = await controller.Declare(new DeclareVanReturnRequestBody(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        Assert.Equal("specific reason", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }

    [Fact]
    public async Task Exceeding_van_stock_is_422_with_the_shortages()
    {
        var shortages = new[] { new VanStockShortage(Guid.NewGuid(), "Soap", 10, 4) };
        var controller = Controller(new VanReturnResult(
            VanReturnOutcome.ExceedsVanStock, Message: "Your van holds 4 Soap", Shortages: shortages));

        var result = await controller.Accept(Guid.NewGuid(), new AcceptVanReturnRequestBody(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(422, objectResult.StatusCode);
        Assert.Same(shortages, Assert.IsType<ProblemDetails>(objectResult.Value).Extensions["shortages"]);
    }

    [Fact]
    public async Task Unavailable_dependency_is_503_with_its_name_and_retry_after()
    {
        var controller = Controller(new VanReturnResult(
            VanReturnOutcome.DependencyUnavailable, Message: "Inventory is down", Dependency: "Inventory"));

        var result = await controller.Declare(new DeclareVanReturnRequestBody(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("Inventory", Assert.IsType<ProblemDetails>(objectResult.Value).Extensions["dependency"]);
        Assert.Equal("30", controller.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task A_missing_van_return_is_404()
    {
        var result = await Controller(VanReturnResult.Failed(VanReturnOutcome.NotFound, "unused"))
            .Get(Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Van return not found", Assert.IsType<ProblemDetails>(notFound.Value).Title);
    }

    private static VanReturnsController Controller(VanReturnResult result) => new(new VanReturnServiceStub(result))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class VanReturnServiceStub(VanReturnResult result) : IVanReturnService
    {
        public Task<VanReturnResult> DeclareAsync(DeclareVanReturnRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task<VanReturnResult> AcceptAsync(
            Guid vanReturnId,
            AcceptVanReturnRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public Task<PagedResponse<VanReturnSummaryResponse>> ListAsync(
            VanReturnListQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VanReturnResponse?> GetAsync(Guid vanReturnId, CancellationToken cancellationToken) =>
            Task.FromResult<VanReturnResponse?>(null);
    }
}
