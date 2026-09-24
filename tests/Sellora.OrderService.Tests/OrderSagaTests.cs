using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Orders;
using Sellora.OrderService.Infrastructure.Outbox;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-1b: the four-step verification saga against a real database.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OrderSagaTests
{
    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _repId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly FakeOrganization _organization = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeInventory _inventory = new();

    public OrderSagaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _organization.Shop = new ShopPlacement(
            _place.ShopId, "Perera Stores", 10_000m, _place.TerritoryId, _place.AgencyId, _place.ProvinceId,
            6.8960m, 79.8556m);
    }

    private Task<CreateOrderResult> SubmitAsync(params BasketLine[] lines) =>
        SubmitAsync(OrderFulfilmentType.ScheduledDelivery, lines);

    private async Task<CreateOrderResult> SubmitAsync(
        OrderFulfilmentType fulfilmentType,
        params BasketLine[] lines)
    {
        await using var db = _fixture.CreateContext(_companyId);
        var service = new OrderCreationService(
            db,
            new TenantStub(_companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = _repId },
            _organization,
            _catalog,
            _inventory,
            TimeProvider.System,
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation("saga-test-correlation")),
            NullLogger<OrderCreationService>.Instance);

        return await service.CreateAsync(
            new CreateOrderRequest(_place.ShopId, fulfilmentType, lines), CancellationToken.None);
    }

    private async Task<int> OrderCountAsync()
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.Orders.CountAsync(order => order.ShopId == _place.ShopId);
    }

    [Fact]
    public async Task Valid_order_passes_all_four_steps_with_catalogue_prices_and_a_reservation()
    {
        var soap = _catalog.Add("Sunlight Soap 100g", 120.50m);
        var milk = _catalog.Add("Anchor Milk 400g", 1150.00m);

        var result = await SubmitAsync(new BasketLine(soap.ProductId, 3), new BasketLine(milk.ProductId, 2));

        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        var order = result.Order!;
        Assert.Equal(2661.50m, order.Total);
        Assert.Equal(_inventory.LastReservationId, order.ReservationId);
        Assert.Equal(_place.AgencyId, order.AgencyId);
        Assert.Equal("ScheduledDelivery", order.FulfilmentType);
        Assert.Equal(
            new[] { "RepShopRelationship", "PriceResolution", "CreditLimit", "StockReservation" },
            order.VerificationSteps.Select(step => step.Step));
        Assert.All(order.VerificationSteps, step => Assert.True(step.Passed));

        await using var db = _fixture.CreateContext(_companyId);
        var stored = await db.Orders.Include(o => o.Lines).Include(o => o.VerificationSteps)
            .SingleAsync(o => o.OrderId == order.OrderId);
        Assert.Equal(120.50m, stored.Lines.Single(l => l.ProductId == soap.ProductId).UnitPriceSnapshot);
        Assert.Equal(4, stored.VerificationSteps.Count);
        Assert.Empty(_inventory.Released);
    }

    [Fact]
    public async Task Rep_not_assigned_to_shop_is_rejected_before_any_other_call()
    {
        _organization.Verification = new VerifyRepShopRelationshipResponse(false, "repNotAssignedToShopTerritory");
        var soap = _catalog.Add("Soap", 100m);

        var result = await SubmitAsync(new BasketLine(soap.ProductId, 1));

        Assert.Equal(CreateOrderOutcome.VerificationFailed, result.Outcome);
        Assert.Equal("RepShopRelationship", result.Rejection!.FailedStep);
        Assert.Contains("not currently assigned", result.Rejection.Reason);
        Assert.Equal(0, _catalog.Calls);
        Assert.Equal(0, _inventory.ReserveCalls);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Unknown_or_inactive_products_are_named_and_nothing_is_reserved()
    {
        var unknown = Guid.NewGuid();
        var inactive = _catalog.Add("Old Soap", 50m, available: false);

        var result = await SubmitAsync(new BasketLine(unknown, 1), new BasketLine(inactive.ProductId, 1));

        Assert.Equal("PriceResolution", result.Rejection!.FailedStep);
        Assert.Equal(
            new[] { unknown, inactive.ProductId },
            result.Rejection.UnresolvedProducts!.Select(product => product.ProductId));
        Assert.Equal(0, _inventory.ReserveCalls);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Credit_limit_overrun_names_the_limit_and_the_amount()
    {
        // Existing unpaid order of 2 x 3,000 = 6,000 against a 10,000 limit.
        await using (var db = _fixture.CreateContext(_companyId))
        {
            await TestOrders.SeedAsync(db, _companyId, _repId, _place, unitPrice: 3_000m);
        }

        var tv = _catalog.Add("TV", 5_000m);

        var result = await SubmitAsync(new BasketLine(tv.ProductId, 1));

        Assert.Equal("CreditLimit", result.Rejection!.FailedStep);
        var credit = result.Rejection.Credit!;
        Assert.Equal(10_000m, credit.CreditLimit);
        Assert.Equal(6_000m, credit.OutstandingBalance);
        Assert.Equal(5_000m, credit.OrderTotal);
        Assert.Equal(1_000m, credit.ExceededBy);
        Assert.Equal(0, _inventory.ReserveCalls);
        Assert.Equal(1, await OrderCountAsync());
    }

    [Fact]
    public async Task Stock_shortage_names_the_product_and_quantity_and_leaves_nothing_behind()
    {
        var soap = _catalog.Add("Sunlight Soap 100g", 100m);
        _inventory.Mode = StockReservationStatus.InsufficientStock;
        _inventory.Shortages = new[] { new ReservationShortage(soap.ProductId, null, 10, 4) };

        var result = await SubmitAsync(new BasketLine(soap.ProductId, 10));

        Assert.Equal(CreateOrderOutcome.VerificationFailed, result.Outcome);
        Assert.Equal("StockReservation", result.Rejection!.FailedStep);
        var shortage = Assert.Single(result.Rejection.Shortages!);
        Assert.Equal("Sunlight Soap 100g", shortage.ProductName);
        Assert.Equal(6, shortage.ShortBy);
        Assert.Contains("short by 6", result.Rejection.Reason);
        Assert.Equal(new[] { true, true, true, false }, result.Rejection.Steps.Select(step => step.Passed));
        Assert.Empty(_inventory.Released);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Failure_after_reservation_releases_it()
    {
        var soap = _catalog.Add("Soap", 100m);

        // Simulate a concurrent writer taking our reference after stock is
        // reserved, so the order's own save fails.
        _inventory.OnReserved = async reference =>
        {
            await using var other = _fixture.CreateContext(_companyId);
            await TestOrders.SeedAsync(other, _companyId, Guid.NewGuid(), Placement.New(), reference: reference);
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => SubmitAsync(new BasketLine(soap.ProductId, 1)));

        Assert.Equal(new[] { _inventory.LastReservationId!.Value }, _inventory.Released);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Unavailable_dependency_is_named_and_creates_nothing()
    {
        _catalog.Unavailable = true;

        var result = await SubmitAsync(new BasketLine(Guid.NewGuid(), 1));

        Assert.Equal(CreateOrderOutcome.DependencyUnavailable, result.Outcome);
        Assert.Equal("Catalog", result.Rejection!.Dependency);
        Assert.Equal("PriceResolution", result.Rejection.FailedStep);
        Assert.Contains("Catalog service is currently unavailable", result.Rejection.Reason);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Inventory_refusal_is_reported_at_the_stock_step()
    {
        var soap = _catalog.Add("Soap", 100m);
        _inventory.Mode = StockReservationStatus.Rejected;

        var result = await SubmitAsync(new BasketLine(soap.ProductId, 1));

        Assert.Equal("StockReservation", result.Rejection!.FailedStep);
        Assert.Contains("403", result.Rejection.Reason);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Scheduled_delivery_is_confirmed_and_its_stock_is_sold()
    {
        var soap = _catalog.Add("Soap", 100m);

        var result = await SubmitAsync(OrderFulfilmentType.ScheduledDelivery, new BasketLine(soap.ProductId, 2));

        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        Assert.Equal("ScheduledDelivery", result.Order!.FulfilmentType);
        Assert.Equal("Confirmed", result.Order.Status);
        // Agency stock, and the reservation is turned into a sale now.
        Assert.Equal(new[] { _inventory.LastReservationId!.Value }, _inventory.Confirmed);
        Assert.Null(_inventory.LastReservedOwnerId);
    }

    [Fact]
    public async Task Immediate_cash_sale_reserves_van_stock_and_waits_for_checkout()
    {
        var soap = _catalog.Add("Soap", 100m);
        var vanOwnerId = Guid.NewGuid();
        _inventory.VanOwnerId = vanOwnerId;

        var result = await SubmitAsync(OrderFulfilmentType.ImmediateCashSale, new BasketLine(soap.ProductId, 2));

        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        Assert.Equal("ImmediateCashSale", result.Order!.FulfilmentType);
        Assert.Equal("AwaitingCheckout", result.Order.Status);
        Assert.Equal(vanOwnerId, _inventory.LastReservedOwnerId);
        // The stock stays held until the rep checks in and takes payment.
        Assert.Empty(_inventory.Confirmed);
    }

    [Fact]
    public async Task Cash_sale_without_van_stock_is_rejected_and_offered_scheduled_delivery()
    {
        var soap = _catalog.Add("Soap", 100m);
        _inventory.VanOwnerId = null;

        var result = await SubmitAsync(OrderFulfilmentType.ImmediateCashSale, new BasketLine(soap.ProductId, 1));

        Assert.Equal(CreateOrderOutcome.VerificationFailed, result.Outcome);
        Assert.Equal("StockReservation", result.Rejection!.FailedStep);
        Assert.Contains("no van stock", result.Rejection.Reason);
        Assert.Contains("scheduled delivery", result.Rejection.Reason);
        Assert.Equal(0, _inventory.ReserveCalls);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Cash_sale_short_on_van_stock_points_at_scheduled_delivery()
    {
        var soap = _catalog.Add("Soap", 100m);
        _inventory.VanOwnerId = Guid.NewGuid();
        _inventory.Mode = StockReservationStatus.InsufficientStock;
        _inventory.Shortages = new[] { new ReservationShortage(soap.ProductId, null, 10, 3) };

        var result = await SubmitAsync(OrderFulfilmentType.ImmediateCashSale, new BasketLine(soap.ProductId, 10));

        Assert.Contains("short by 7", result.Rejection!.Reason);
        Assert.Contains("Your van is short", result.Rejection.Reason);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task A_confirmed_order_keeps_its_reservation_even_if_confirmation_fails()
    {
        var soap = _catalog.Add("Soap", 100m);
        _inventory.ConfirmOutcome = ReservationConfirmOutcome.Rejected;

        var result = await SubmitAsync(OrderFulfilmentType.ScheduledDelivery, new BasketLine(soap.ProductId, 1));

        // The order is valid and recorded; the stock is held, never oversold.
        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        Assert.Equal(1, await OrderCountAsync());
        Assert.Empty(_inventory.Released);
    }

    [Fact]
    public async Task Bad_basket_is_rejected_before_any_dependency_call()
    {
        var productId = Guid.NewGuid();

        var result = await SubmitAsync(new BasketLine(productId, 1), new BasketLine(productId, 2));

        Assert.Equal(CreateOrderOutcome.InvalidRequest, result.Outcome);
        Assert.Equal(0, _catalog.Calls);
    }
}
