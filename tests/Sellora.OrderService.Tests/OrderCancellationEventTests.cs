using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Infrastructure.Orders;
using Sellora.OrderService.Infrastructure.Outbox;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-5-T2 / DoD 3: a cancellation releases the stock by publishing
/// OrderCancelled in the same transaction, carrying exactly what
/// Inventory's consumer needs to find and return the reservation.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OrderCancellationEventTests
{
    private const string Correlation = "cancel-event-correlation";

    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Placement _place = Placement.New();
    private readonly DateTimeOffset _confirmedAt = new(2026, 9, 25, 4, 0, 0, TimeSpan.Zero);

    public OrderCancellationEventTests(PostgreSqlFixture fixture) => _fixture = fixture;

    // Inventory's consumer record, copied from sellora-inventory
    // Application/Events/OrderCancelledEvent.cs.
    private sealed record InventoryOrderCancelled(
        Guid EventId, string EventType, string SchemaVersion, Guid CompanyId, Guid EntityId,
        Guid ReservationId, string OrderReference, DateTimeOffset CancelledAt, string CorrelationId);

    private static readonly JsonSerializerOptions InventoryJson = new() { PropertyNameCaseInsensitive = true };

    private async Task<Order> CancelConfirmedOrderAsync()
    {
        var order = DecisionTestOrders.Approved(_companyId, _place, _confirmedAt);
        await using (var seed = _fixture.CreateContext(_companyId))
        {
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        await using var db = _fixture.CreateContext(_companyId);
        var service = new OrderCancellationService(
            db, new TenantStub(_companyId),
            new CallerStub { Subject = DecisionTestOrders.ShopSub, Role = SelloraRoles.ShopOwner, ShopId = _place.ShopId },
            new FakeClock(_confirmedAt.AddMinutes(20)), Options.Create(new CancellationOptions()),
            new OrderEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
            NullLogger<OrderCancellationService>.Instance);

        var result = await service.CancelAsync(order.OrderId, new CancelOrderRequest(null), CancellationToken.None);
        Assert.Equal(OrderDecisionOutcome.Succeeded, result.Outcome);
        return order;
    }

    private async Task<OutboxMessage> CancelledEventAsync(Guid orderId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.OutboxMessages.SingleAsync(message =>
            message.AggregateId == orderId && message.EventType == "OrderCancelled");
    }

    [Fact]
    public async Task Shop_cancellation_publishes_order_cancelled_for_the_orders_reservation()
    {
        var order = await CancelConfirmedOrderAsync();

        var message = await CancelledEventAsync(order.OrderId);
        var payload = JsonDocument.Parse(message.Payload).RootElement;

        Assert.Equal(order.OrderReference, message.MessageKey);
        Assert.Equal(Correlation, message.CorrelationId);
        Assert.Equal(order.ReservationId, payload.GetProperty("reservationId").GetGuid());
        Assert.Equal("ShopCancellation", payload.GetProperty("source").GetString());
        Assert.Equal(DecisionTestOrders.ShopSub, payload.GetProperty("cancelledBy").GetProperty("userId").GetString());
        Assert.Equal("ShopOwner", payload.GetProperty("cancelledBy").GetProperty("role").GetString());
        Assert.Equal("Cancelled", payload.GetProperty("status").GetString());
        Assert.Equal(Order.DefaultShopCancellationReason, payload.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Shop_cancellation_matches_inventorys_consumer_contract()
    {
        var order = await CancelConfirmedOrderAsync();

        var message = await CancelledEventAsync(order.OrderId);
        var cancelled = JsonSerializer.Deserialize<InventoryOrderCancelled>(message.Payload, InventoryJson)!;

        // The same checks Inventory's OrderEventHandler applies before releasing.
        Assert.Equal(message.OutboxId, cancelled.EventId);
        Assert.Equal("OrderCancelled", cancelled.EventType);
        Assert.Equal("1.0", cancelled.SchemaVersion);
        Assert.Equal(_companyId, cancelled.CompanyId);
        Assert.Equal(order.ReservationId, cancelled.ReservationId);
        Assert.Equal(order.OrderReference, cancelled.OrderReference);
        Assert.Equal(_confirmedAt.AddMinutes(20), cancelled.CancelledAt);
    }
}
