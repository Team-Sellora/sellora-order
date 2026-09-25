using System.Text.Json;
using Sellora.OrderService.Application.Events;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Infrastructure.Outbox;

/// <summary>
/// Builds the order events from the aggregate and enqueues them. One
/// instance per request (scoped), so events written in one transaction get
/// increasing ordinals and are published in the order they were written.
/// </summary>
public sealed class OrderEventOutbox(IOutboxWriter writer, ICorrelationIdAccessor correlation) : IOrderEventOutbox
{
    public const string AggregateType = "Order";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private int _ordinal;

    public void OrderPlaced(Order order, DateTimeOffset occurredAt) =>
        Enqueue(order, occurredAt, context => new OrderPlacedEvent
        {
            EventId = context.EventId,
            EventType = OrderEventTypes.OrderPlaced,
            CompanyId = order.CompanyId,
            EntityId = order.OrderId,
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            ReservationId = order.ReservationId,
            OccurredAt = occurredAt,
            CorrelationId = context.CorrelationId,
            FulfilmentType = order.FulfilmentType.ToString(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Shop = Shop(order),
            Agency = Agency(order),
            TerritoryId = order.TerritoryId,
            ProvinceId = order.ProvinceId,
            SalesRep = Rep(order),
            Lines = Lines(order),
            Subtotal = order.Subtotal,
            Total = order.Total,
            CheckoutLocation = CheckoutLocation(order)
        });

    public void OrderConfirmed(Order order, DateTimeOffset occurredAt) =>
        Enqueue(order, occurredAt, context => new OrderConfirmedEvent
        {
            EventId = context.EventId,
            EventType = OrderEventTypes.OrderConfirmed,
            CompanyId = order.CompanyId,
            EntityId = order.OrderId,
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            ReservationId = order.ReservationId,
            OccurredAt = occurredAt,
            CorrelationId = context.CorrelationId,
            FulfilmentType = order.FulfilmentType.ToString(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Shop = Shop(order),
            Agency = Agency(order),
            TerritoryId = order.TerritoryId,
            ProvinceId = order.ProvinceId,
            SalesRep = Rep(order),
            Lines = Lines(order),
            Subtotal = order.Subtotal,
            Total = order.Total,
            CheckoutLocation = CheckoutLocation(order),
            ConfirmedAt = occurredAt
        });

    public void PaymentRecorded(Order order, Payment payment, DateTimeOffset occurredAt)
    {
        var checkIn = order.CheckIns.SingleOrDefault(candidate => candidate.OrderCheckInId == payment.CheckInId);

        Enqueue(order, occurredAt, context => new PaymentRecordedEvent
        {
            EventId = context.EventId,
            EventType = OrderEventTypes.PaymentRecorded,
            CompanyId = order.CompanyId,
            EntityId = order.OrderId,
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            ReservationId = order.ReservationId,
            OccurredAt = occurredAt,
            CorrelationId = context.CorrelationId,
            FulfilmentType = order.FulfilmentType.ToString(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Shop = Shop(order),
            Agency = Agency(order),
            TerritoryId = order.TerritoryId,
            ProvinceId = order.ProvinceId,
            SalesRep = Rep(order),
            Lines = Lines(order),
            Subtotal = order.Subtotal,
            Total = order.Total,
            CheckoutLocation = CheckoutLocation(order),
            Payment = new EventPayment(
                payment.PaymentId,
                payment.Amount,
                payment.Method.ToString(),
                payment.RecordedAt,
                payment.CheckInId),
            CheckInLocation = new EventLocation(
                payment.Latitude,
                payment.Longitude,
                payment.DistanceMeters,
                checkIn?.AccuracyMeters,
                checkIn?.RecordedAt ?? payment.RecordedAt)
        });
    }

    public void OrderCancelled(Order order, DateTimeOffset occurredAt)
    {
        var (source, cancelledBy) = Cancellation(order);

        Enqueue(order, occurredAt, context => new OrderCancelledEvent
        {
            EventId = context.EventId,
            EventType = OrderEventTypes.OrderCancelled,
            CompanyId = order.CompanyId,
            EntityId = order.OrderId,
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            ReservationId = order.ReservationId,
            OccurredAt = occurredAt,
            CorrelationId = context.CorrelationId,
            FulfilmentType = order.FulfilmentType.ToString(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Shop = Shop(order),
            Agency = Agency(order),
            TerritoryId = order.TerritoryId,
            ProvinceId = order.ProvinceId,
            SalesRep = Rep(order),
            Lines = Lines(order),
            Subtotal = order.Subtotal,
            Total = order.Total,
            CheckoutLocation = CheckoutLocation(order),
            CancelledAt = order.CancelledAt ?? occurredAt,
            Reason = order.CancellationReason,
            Source = source,
            CancelledBy = cancelledBy
        });
    }

    public void OrderApproved(Order order, OrderDecision approval, DateTimeOffset occurredAt) =>
        Enqueue(order, occurredAt, context => new OrderApprovedEvent
        {
            EventId = context.EventId,
            EventType = OrderEventTypes.OrderApproved,
            CompanyId = order.CompanyId,
            EntityId = order.OrderId,
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            ReservationId = order.ReservationId,
            OccurredAt = occurredAt,
            CorrelationId = context.CorrelationId,
            FulfilmentType = order.FulfilmentType.ToString(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Shop = Shop(order),
            Agency = Agency(order),
            TerritoryId = order.TerritoryId,
            ProvinceId = order.ProvinceId,
            SalesRep = Rep(order),
            Lines = Lines(order),
            Subtotal = order.Subtotal,
            Total = order.Total,
            CheckoutLocation = CheckoutLocation(order),
            ApprovedAt = approval.DecidedAt,
            ApprovedBy = new EventActor(approval.ActorUserId, approval.ActorRole)
        });

    /// <summary>
    /// Who cancelled, read from the latest rejection or shop cancellation.
    /// No such decision means the system cancelled it (expired stock hold).
    /// </summary>
    private static (string Source, EventActor? CancelledBy) Cancellation(Order order)
    {
        var decision = order.Decisions
            .Where(candidate => candidate.Kind is OrderDecisionKind.Rejected or OrderDecisionKind.CancelledByShop)
            .OrderByDescending(candidate => candidate.DecidedAt)
            .FirstOrDefault();

        if (decision is null)
        {
            return (OrderCancellationSources.StockHoldExpired, null);
        }

        var source = decision.Kind == OrderDecisionKind.Rejected
            ? OrderCancellationSources.AgencyRejection
            : OrderCancellationSources.ShopCancellation;

        return (source, new EventActor(decision.ActorUserId, decision.ActorRole));
    }

    private void Enqueue<TEvent>(
        Order order,
        DateTimeOffset occurredAt,
        Func<(Guid EventId, string CorrelationId), TEvent> build)
        where TEvent : OrderEventBase
    {
        var eventId = Guid.NewGuid();
        var correlationId = correlation.GetCorrelationId();
        var @event = build((eventId, correlationId));

        writer.Enqueue(new NewOutboxMessage(
            eventId,
            order.CompanyId,
            AggregateType,
            order.OrderId,
            order.OrderReference,
            @event.EventType,
            @event.SchemaVersion,
            JsonSerializer.Serialize(@event, @event.GetType(), Json),
            correlationId,
            occurredAt,
            _ordinal++));
    }

    private static EventShop Shop(Order order) =>
        new(order.ShopId, order.ShopName, order.ShopOwnerName, order.ShopOwnerEmail);

    private static EventAgency Agency(Order order) =>
        new(order.AgencyId, order.AgencyName, order.AgencyEmail);

    private static EventSalesRep Rep(Order order) =>
        new(order.SalesRepId, order.SalesRepName);

    private static IReadOnlyList<EventOrderLine> Lines(Order order) =>
        order.Lines
            .Select(line => new EventOrderLine(
                line.ProductId,
                line.ProductNameSnapshot,
                line.Quantity,
                line.UnitPriceSnapshot,
                line.LineTotal))
            .ToList();

    private static EventLocation? CheckoutLocation(Order order)
    {
        if (order.CheckoutLatitude is not { } latitude || order.CheckoutLongitude is not { } longitude)
        {
            return null;
        }

        var checkIn = order.Payment is { } payment
            ? order.CheckIns.SingleOrDefault(candidate => candidate.OrderCheckInId == payment.CheckInId)
            : null;

        return new EventLocation(
            latitude,
            longitude,
            checkIn?.DistanceMeters ?? order.Payment?.DistanceMeters ?? 0,
            checkIn?.AccuracyMeters,
            order.CheckedOutAt ?? checkIn?.RecordedAt ?? DateTimeOffset.MinValue);
    }
}
