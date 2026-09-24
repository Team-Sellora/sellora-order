using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Checkout;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Infrastructure.Checkout;
using Sellora.OrderService.Infrastructure.Orders;
using Sellora.OrderService.Infrastructure.Outbox;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-4 T2–T4: every order transition writes exactly the right events to
/// the outbox, in the same transaction, with the location and context
/// downstream services need.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OrderEventPublishingTests
{
    private const string Correlation = "req-7f3a-correlation";

    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _repId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly FakeOrganization _organization = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeInventory _inventory = new() { VanOwnerId = Guid.NewGuid() };
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    public OrderEventPublishingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _organization.Shop = new ShopPlacement(
            _place.ShopId, "Perera Stores", 1_000_000m, _place.TerritoryId, _place.AgencyId, _place.ProvinceId,
            (decimal)GeoTestPoints.Shop.Latitude, (decimal)GeoTestPoints.Shop.Longitude,
            OwnerName: "Chaminda Perera",
            OwnerEmail: "owner@pererastores.lk",
            AgencyName: "Colombo Distribution Agency",
            AgencyEmail: "orders@colombo-agency.lk");
    }

    private async Task<CreateOrderResult> PlaceAsync(OrderFulfilmentType type, params BasketLine[] lines)
    {
        await using var db = _fixture.CreateContext(_companyId);
        var service = new OrderCreationService(
            db, new TenantStub(_companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = _repId, DisplayName = "Ruwan Dias" },
            _organization, _catalog, _inventory, TimeProvider.System,
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
            NullLogger<OrderCreationService>.Instance);

        return await service.CreateAsync(new CreateOrderRequest(_place.ShopId, type, lines), CancellationToken.None);
    }

    private async Task<T> CheckoutAsync<T>(Func<CheckoutService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext(_companyId);
        var service = new CheckoutService(
            db, new TenantStub(_companyId),
            new CallerStub { Role = SelloraRoles.SalesRep, SalesRepId = _repId },
            _organization, _inventory, _clock, Options.Create(new CheckInOptions()),
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
            NullLogger<CheckoutService>.Instance);
        return await action(service);
    }

    private Task<CheckInResult> CheckInAsync(Guid orderId, double meters) =>
        CheckoutAsync(service =>
        {
            var point = GeoTestPoints.NorthOf(GeoTestPoints.Shop, meters);
            return service.CheckInAsync(
                orderId, new CheckInRequest(point.Latitude, point.Longitude, _clock.Now.AddSeconds(-2), 7), CancellationToken.None);
        });

    private Task<PaymentResult> PayAsync(Guid orderId, decimal amount) =>
        CheckoutAsync(service => service.RecordPaymentAsync(
            orderId, new PaymentRequest(amount, PaymentMethod.Cash), CancellationToken.None));

    private async Task<List<OutboxMessage>> EventsAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.OutboxMessages
            .Where(message => message.AggregateId == orderId)
            .OrderBy(message => message.Sequence)
            .ToListAsync();
    }

    private static JsonElement Payload(OutboxMessage message) => JsonDocument.Parse(message.Payload).RootElement;

    private async Task<(Guid OrderId, decimal Total)> PlaceCashSaleAsync()
    {
        var soap = _catalog.Add("Sunlight Soap 100g", 120m);
        var result = await PlaceAsync(OrderFulfilmentType.ImmediateCashSale, new BasketLine(soap.ProductId, 3));
        Assert.Equal(CreateOrderOutcome.Created, result.Outcome);
        return (result.Order!.OrderId, result.Order.Total);
    }

    [Fact]
    public async Task Scheduled_delivery_publishes_placed_then_confirmed_in_one_transaction()
    {
        var soap = _catalog.Add("Sunlight Soap 100g", 120m);

        var result = await PlaceAsync(OrderFulfilmentType.ScheduledDelivery, new BasketLine(soap.ProductId, 2));
        var events = await EventsAsync(result.Order!.OrderId);

        Assert.Equal(new[] { "OrderPlaced", "OrderConfirmed" }, events.Select(e => e.EventType));
        Assert.All(events, e =>
        {
            Assert.Equal(result.Order.OrderReference, e.MessageKey);
            Assert.Equal(Correlation, e.CorrelationId);
            Assert.Null(e.PublishedAt);
        });
        Assert.Equal(events[0].OccurredAt, events[1].OccurredAt);
        Assert.True(events[0].Ordinal < events[1].Ordinal);

        // No GPS in the scheduled-delivery flow, so no checkout location.
        Assert.Equal(JsonValueKind.Null, Payload(events[1]).GetProperty("checkoutLocation").ValueKind);
    }

    [Fact]
    public async Task Cash_sale_publishes_only_placed_until_checkout()
    {
        var (orderId, _) = await PlaceCashSaleAsync();

        var events = await EventsAsync(orderId);

        Assert.Equal("OrderPlaced", Assert.Single(events).EventType);
        Assert.Equal("AwaitingCheckout", Payload(events[0]).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_rejected_order_publishes_nothing()
    {
        var soap = _catalog.Add("Soap", 100m);
        _organization.Verification = new VerifyRepShopRelationshipResponse(false, "repNotAssignedToShopTerritory");

        var result = await PlaceAsync(OrderFulfilmentType.ScheduledDelivery, new BasketLine(soap.ProductId, 1));

        Assert.Equal(CreateOrderOutcome.VerificationFailed, result.Outcome);
        await using var db = _fixture.CreateContext(_companyId);
        Assert.False(await db.OutboxMessages.AnyAsync(message => message.CompanyId == _companyId));
    }

    // Acceptance scenario 1.
    [Fact]
    public async Task Checkout_publishes_confirmed_and_payment_with_the_checkout_coordinates()
    {
        var (orderId, total) = await PlaceCashSaleAsync();
        var checkIn = (await CheckInAsync(orderId, 120)).CheckIn!;

        Assert.Equal(CheckoutOutcome.Succeeded, (await PayAsync(orderId, total)).Outcome);

        var events = await EventsAsync(orderId);
        Assert.Equal(new[] { "OrderPlaced", "OrderConfirmed", "PaymentRecorded" }, events.Select(e => e.EventType));

        var confirmed = Payload(events[1]);
        var payment = Payload(events[2]);

        foreach (var payload in new[] { confirmed, payment })
        {
            Assert.Equal(events[0].MessageKey, payload.GetProperty("orderReference").GetString());
            Assert.Equal(_place.ShopId, payload.GetProperty("shop").GetProperty("shopId").GetGuid());
            Assert.Equal(_place.AgencyId, payload.GetProperty("agency").GetProperty("agencyId").GetGuid());
            Assert.Equal(_repId, payload.GetProperty("salesRep").GetProperty("salesRepId").GetGuid());
            Assert.Equal(checkIn.Latitude, payload.GetProperty("checkoutLocation").GetProperty("latitude").GetDouble());
            Assert.Equal(checkIn.Longitude, payload.GetProperty("checkoutLocation").GetProperty("longitude").GetDouble());
        }

        Assert.Equal(total, payment.GetProperty("payment").GetProperty("amount").GetDecimal());
        Assert.Equal("Cash", payment.GetProperty("payment").GetProperty("method").GetString());
        Assert.Equal(120, payment.GetProperty("checkInLocation").GetProperty("distanceMeters").GetDouble());
        Assert.Equal(7, payment.GetProperty("checkInLocation").GetProperty("accuracyMeters").GetDouble());
    }

    // Acceptance scenario 2.
    [Fact]
    public async Task A_checkout_that_fails_validation_publishes_nothing()
    {
        var (orderId, total) = await PlaceCashSaleAsync();
        await CheckInAsync(orderId, 50);

        var result = await PayAsync(orderId, total - 1);

        Assert.Equal(CheckoutOutcome.AmountMismatch, result.Outcome);
        Assert.Equal("OrderPlaced", Assert.Single(await EventsAsync(orderId)).EventType);
    }

    [Fact]
    public async Task A_rejected_check_in_publishes_nothing()
    {
        var (orderId, _) = await PlaceCashSaleAsync();

        await CheckInAsync(orderId, 450);

        Assert.Single(await EventsAsync(orderId));
    }

    [Fact]
    public async Task An_expired_stock_hold_publishes_order_cancelled_with_the_reason()
    {
        var (orderId, total) = await PlaceCashSaleAsync();
        await CheckInAsync(orderId, 50);
        _inventory.ConfirmOutcome = ReservationConfirmOutcome.NoLongerActive;

        await PayAsync(orderId, total);

        var cancelled = (await EventsAsync(orderId)).Last();
        Assert.Equal("OrderCancelled", cancelled.EventType);
        var payload = Payload(cancelled);
        Assert.Equal("Cancelled", payload.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("reason").GetString()));
    }

    // Inventory's consumer contract, copied from sellora-inventory
    // (Application/Events/OrderConfirmedEvent.cs, OrderCancelledEvent.cs)
    // with the same validation its OrderEventHandler applies.
    private sealed record InventoryOrderConfirmed(
        Guid EventId, string EventType, string SchemaVersion, Guid CompanyId, Guid EntityId,
        Guid ReservationId, string OrderReference, DateTimeOffset ConfirmedAt, string CorrelationId);

    private sealed record InventoryOrderCancelled(
        Guid EventId, string EventType, string SchemaVersion, Guid CompanyId, Guid EntityId,
        Guid ReservationId, string OrderReference, DateTimeOffset CancelledAt, string CorrelationId);

    private static readonly JsonSerializerOptions InventoryJson = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Order_confirmed_and_cancelled_match_inventorys_consumer_contract()
    {
        var (orderId, total) = await PlaceCashSaleAsync();
        await CheckInAsync(orderId, 50);
        await PayAsync(orderId, total);
        var (cancelledOrderId, cancelledTotal) = await PlaceCashSaleAsync();
        await CheckInAsync(cancelledOrderId, 50);
        _inventory.ConfirmOutcome = ReservationConfirmOutcome.NoLongerActive;
        await PayAsync(cancelledOrderId, cancelledTotal);

        var confirmedRow = (await EventsAsync(orderId)).Single(e => e.EventType == "OrderConfirmed");
        var cancelledRow = (await EventsAsync(cancelledOrderId)).Single(e => e.EventType == "OrderCancelled");
        var confirmed = JsonSerializer.Deserialize<InventoryOrderConfirmed>(confirmedRow.Payload, InventoryJson)!;
        var cancelled = JsonSerializer.Deserialize<InventoryOrderCancelled>(cancelledRow.Payload, InventoryJson)!;

        Assert.Equal(confirmedRow.OutboxId, confirmed.EventId);
        Assert.Equal("OrderConfirmed", confirmed.EventType);
        Assert.Equal("1.0", confirmed.SchemaVersion);
        Assert.Equal(_companyId, confirmed.CompanyId);
        Assert.NotEqual(Guid.Empty, confirmed.ReservationId);
        Assert.Equal(confirmedRow.MessageKey, confirmed.OrderReference);
        Assert.Equal(Correlation, confirmed.CorrelationId);
        Assert.NotEqual(default, confirmed.ConfirmedAt);

        Assert.Equal("OrderCancelled", cancelled.EventType);
        Assert.Equal("1.0", cancelled.SchemaVersion);
        Assert.NotEqual(Guid.Empty, cancelled.ReservationId);
        Assert.NotEqual(default, cancelled.CancelledAt);
    }

    // Acceptance scenario 3: everything a shop/agency email needs is in the
    // event itself — no call back to Order, Organization or Catalog.
    [Fact]
    public async Task A_notification_can_be_composed_from_payment_recorded_alone()
    {
        var (orderId, total) = await PlaceCashSaleAsync();
        await CheckInAsync(orderId, 80);
        await PayAsync(orderId, total);

        var payload = Payload((await EventsAsync(orderId)).Single(e => e.EventType == "PaymentRecorded"));

        var recipients = new[]
        {
            payload.GetProperty("shop").GetProperty("ownerEmail").GetString(),
            payload.GetProperty("agency").GetProperty("email").GetString()
        };
        var line = payload.GetProperty("lines")[0];
        var location = payload.GetProperty("checkInLocation");
        var body =
            $"{payload.GetProperty("orderReference").GetString()} · {payload.GetProperty("shop").GetProperty("name").GetString()} · " +
            $"rep {payload.GetProperty("salesRep").GetProperty("name").GetString()} · " +
            $"{line.GetProperty("quantity").GetInt32()} × {line.GetProperty("productName").GetString()} · " +
            $"{payload.GetProperty("total").GetDecimal()} {payload.GetProperty("currency").GetString()} " +
            $"{payload.GetProperty("payment").GetProperty("method").GetString()} at " +
            $"{payload.GetProperty("payment").GetProperty("recordedAt").GetDateTimeOffset():u} · " +
            $"https://maps.google.com/?q={location.GetProperty("latitude").GetDouble()},{location.GetProperty("longitude").GetDouble()}";

        Assert.Equal(new[] { "owner@pererastores.lk", "orders@colombo-agency.lk" }, recipients);
        Assert.Contains("Perera Stores", body);
        Assert.Contains("rep Ruwan Dias", body);
        Assert.Contains("3 × Sunlight Soap 100g", body);
        Assert.Contains("Colombo Distribution Agency", payload.GetProperty("agency").GetProperty("name").GetString());
        Assert.Contains("maps.google.com/?q=", body);
    }
}
