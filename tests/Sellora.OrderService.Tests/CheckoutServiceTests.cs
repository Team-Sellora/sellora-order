using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Checkout;
using Sellora.OrderService.Infrastructure.Outbox;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-3 end to end against a real database, with fake dependencies.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class CheckoutServiceTests
{
    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _repId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly FakeOrganization _organization = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
    private const string CorrelationId = "checkout-test-correlation";

    public CheckoutServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _organization.Shop = new ShopPlacement(
            _place.ShopId, "Perera Stores", 1_000_000m, _place.TerritoryId, _place.AgencyId, _place.ProvinceId,
            (decimal)GeoTestPoints.Shop.Latitude, (decimal)GeoTestPoints.Shop.Longitude);
    }

    // Seeded order: 2 × 100 = 200.00.
    private async Task<Order> SeedAsync(OrderFulfilmentType type = OrderFulfilmentType.ImmediateCashSale)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await TestOrders.SeedAsync(db, _companyId, _repId, _place, fulfilmentType: type);
    }

    private async Task<T> AsAsync<T>(Func<CheckoutService, Task<T>> action, Guid? repId = null)
    {
        await using var db = _fixture.CreateContext(_companyId);
        var service = new CheckoutService(
            db,
            new TenantStub(_companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = repId ?? _repId },
            _organization,
            _inventory,
            _clock,
            Options.Create(new CheckInOptions()),
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(CorrelationId)),
            NullLogger<CheckoutService>.Instance);
        return await action(service);
    }

    private Task<CheckInResult> CheckInAsync(Guid orderId, double meters, Guid? repId = null) =>
        AsAsync(service =>
        {
            var point = GeoTestPoints.NorthOf(GeoTestPoints.Shop, meters);
            return service.CheckInAsync(
                orderId, new CheckInRequest(point.Latitude, point.Longitude, _clock.Now.AddSeconds(-3), 6), CancellationToken.None);
        }, repId);

    private Task<PaymentResult> PayAsync(Guid orderId, decimal amount, Guid? repId = null) =>
        AsAsync(service => service.RecordPaymentAsync(
            orderId, new PaymentRequest(amount, PaymentMethod.Cash), CancellationToken.None), repId);

    private async Task<Order> ReloadAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.Orders.Include(o => o.CheckIns).Include(o => o.Payment).SingleAsync(o => o.OrderId == orderId);
    }

    [Fact]
    public async Task Check_in_within_radius_is_accepted_and_stored()
    {
        var order = await SeedAsync();

        var result = await CheckInAsync(order.OrderId, 120);

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.True(result.CheckIn!.Accepted);
        Assert.Equal(120, result.CheckIn.DistanceMeters);

        var stored = await ReloadAsync(order.OrderId);
        var checkIn = Assert.Single(stored.CheckIns);
        Assert.Equal(6, checkIn.AccuracyMeters);
        Assert.Equal(OrderStatus.AwaitingCheckout, stored.Status);
        Assert.Empty(_inventory.Confirmed);
    }

    // Acceptance scenario 2 and US-E4-3-T4.
    [Fact]
    public async Task Check_in_450m_away_is_refused_with_the_distance_and_holds_the_reservation()
    {
        var order = await SeedAsync();

        var result = await CheckInAsync(order.OrderId, 450);

        Assert.Equal(CheckoutOutcome.OutsideRadius, result.Outcome);
        Assert.Equal(450, result.CheckIn!.DistanceMeters);
        Assert.Contains("450 m", result.Message);

        var stored = await ReloadAsync(order.OrderId);
        Assert.False(Assert.Single(stored.CheckIns).Accepted);
        Assert.Equal(OrderStatus.AwaitingCheckout, stored.Status);
        Assert.Empty(_inventory.Released);
        Assert.Empty(_inventory.Confirmed);

        // Checkout for that shop remains blocked.
        Assert.Equal(CheckoutOutcome.CheckInRequired, (await PayAsync(order.OrderId, 200m)).Outcome);
    }

    // Acceptance scenario 3 and US-E4-3-Q4.
    [Fact]
    public async Task Payment_without_check_in_is_blocked_and_confirms_nothing()
    {
        var order = await SeedAsync();

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.CheckInRequired, result.Outcome);
        Assert.Empty(_inventory.Confirmed);
        var stored = await ReloadAsync(order.OrderId);
        Assert.Null(stored.Payment);
        Assert.Equal(OrderStatus.AwaitingCheckout, stored.Status);
    }

    // Acceptance scenario 4 and US-E4-3-Q5.
    [Fact]
    public async Task Payment_amount_mismatch_is_rejected_before_stock_is_confirmed()
    {
        var order = await SeedAsync();
        await CheckInAsync(order.OrderId, 50);

        var result = await PayAsync(order.OrderId, 150m);

        Assert.Equal(CheckoutOutcome.AmountMismatch, result.Outcome);
        Assert.Equal(200m, result.ExpectedAmount);
        Assert.Empty(_inventory.Confirmed);
        Assert.Null((await ReloadAsync(order.OrderId)).Payment);
    }

    // Acceptance scenario 1 and US-E4-3-Q6.
    [Fact]
    public async Task Successful_checkout_confirms_the_reservation_and_records_payment_with_location()
    {
        var order = await SeedAsync();
        var checkIn = (await CheckInAsync(order.OrderId, 120)).CheckIn!;

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { order.ReservationId }, _inventory.Confirmed);

        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.Confirmed, stored.Status);
        Assert.Equal(checkIn.Latitude, stored.CheckoutLatitude);
        Assert.Equal(checkIn.Longitude, stored.CheckoutLongitude);
        Assert.NotNull(stored.CheckedOutAt);

        var payment = stored.Payment!;
        Assert.Equal(200m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(_repId, payment.SalesRepId);
        Assert.Equal(checkIn.CheckInId, payment.CheckInId);
        Assert.Equal(checkIn.Latitude, payment.Latitude);
    }

    [Fact]
    public async Task An_expired_check_in_is_refused()
    {
        var order = await SeedAsync();
        await CheckInAsync(order.OrderId, 50);
        _clock.Now = _clock.Now.AddMinutes(11);

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.CheckInExpired, result.Outcome);
        Assert.Empty(_inventory.Confirmed);
    }

    [Fact]
    public async Task Another_reps_order_looks_missing()
    {
        var order = await SeedAsync();
        var otherRep = Guid.NewGuid();

        Assert.Equal(CheckoutOutcome.OrderNotFound, (await CheckInAsync(order.OrderId, 50, otherRep)).Outcome);
        Assert.Equal(CheckoutOutcome.OrderNotFound, (await PayAsync(order.OrderId, 200m, otherRep)).Outcome);
    }

    [Fact]
    public async Task An_expired_stock_hold_cancels_the_order_and_records_no_payment()
    {
        var order = await SeedAsync();
        await CheckInAsync(order.OrderId, 50);
        _inventory.ConfirmOutcome = ReservationConfirmOutcome.NoLongerActive;

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.ReservationExpired, result.Outcome);
        var stored = await ReloadAsync(order.OrderId);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
        Assert.Null(stored.Payment);
    }

    [Fact]
    public async Task A_retry_after_stock_was_already_confirmed_completes_the_checkout()
    {
        var order = await SeedAsync();
        await CheckInAsync(order.OrderId, 50);
        _inventory.ConfirmOutcome = ReservationConfirmOutcome.AlreadyConfirmed;

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.Succeeded, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(order.OrderId)).Status);
    }

    [Fact]
    public async Task Inventory_being_down_records_nothing()
    {
        var order = await SeedAsync();
        await CheckInAsync(order.OrderId, 50);
        _inventory.Unavailable = true;

        var result = await PayAsync(order.OrderId, 200m);

        Assert.Equal(CheckoutOutcome.DependencyUnavailable, result.Outcome);
        Assert.Equal("Inventory", result.Dependency);
        Assert.Null((await ReloadAsync(order.OrderId)).Payment);
    }

    [Fact]
    public async Task Scheduled_deliveries_are_not_checked_out()
    {
        var order = await SeedAsync(OrderFulfilmentType.ScheduledDelivery);

        Assert.Equal(CheckoutOutcome.NotAwaitingCheckout, (await CheckInAsync(order.OrderId, 50)).Outcome);
    }

    [Fact]
    public async Task A_shop_the_rep_cannot_see_blocks_the_check_in()
    {
        var order = await SeedAsync();
        _organization.Shop = null;

        Assert.Equal(CheckoutOutcome.ShopLocationUnavailable, (await CheckInAsync(order.OrderId, 50)).Outcome);
    }

    [Fact]
    public async Task Out_of_range_coordinates_and_future_timestamps_are_refused()
    {
        var order = await SeedAsync();

        var badCoordinates = await AsAsync(service => service.CheckInAsync(
            order.OrderId, new CheckInRequest(95, 79.8, _clock.Now, null), CancellationToken.None));
        var future = await AsAsync(service => service.CheckInAsync(
            order.OrderId, new CheckInRequest(6.896, 79.8556, _clock.Now.AddHours(1), null), CancellationToken.None));

        Assert.Equal(CheckoutOutcome.InvalidRequest, badCoordinates.Outcome);
        Assert.Equal(CheckoutOutcome.InvalidRequest, future.Outcome);
        Assert.Empty((await ReloadAsync(order.OrderId)).CheckIns);
    }
}
