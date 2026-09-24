namespace Sellora.OrderService.Application.Events;

/// <summary>
/// Order event contracts, schema version 1.0, published on the order topic
/// (<c>sellora.order.v1</c>) keyed by order reference. Documented in
/// docs/contracts/order-events.v1.md.
///
/// Payloads are flat JSON (camelCase). The top-level fields are exactly the
/// ones Inventory's consumer already reads for OrderConfirmed and
/// OrderCancelled (eventId, eventType, schemaVersion, companyId, entityId,
/// reservationId, orderReference, confirmedAt / cancelledAt, correlationId);
/// everything else is additive, so Inventory keeps working unchanged.
/// </summary>
public static class OrderEventTypes
{
    public const string SchemaVersion = "1.0";

    public const string OrderPlaced = "OrderPlaced";

    public const string OrderConfirmed = "OrderConfirmed";

    public const string PaymentRecorded = "PaymentRecorded";

    public const string OrderCancelled = "OrderCancelled";
}

/// <summary>Where the rep verifiably was — the evidence the fraud control rests on.</summary>
public sealed record EventLocation(
    double Latitude,
    double Longitude,
    double DistanceMeters,
    double? AccuracyMeters,
    DateTimeOffset CheckedInAt);

public sealed record EventShop(Guid ShopId, string? Name, string? OwnerName, string? OwnerEmail);

public sealed record EventAgency(Guid AgencyId, string? Name, string? Email);

public sealed record EventSalesRep(Guid SalesRepId, string? Name);

public sealed record EventOrderLine(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record EventPayment(
    Guid PaymentId,
    decimal Amount,
    string Method,
    DateTimeOffset RecordedAt,
    Guid CheckInId);

/// <summary>
/// Fields every order event carries: enough denormalised context for the
/// Notification service to compose an email to the shop and the agency
/// without calling Order, Organization or Catalog.
/// </summary>
public abstract record OrderEventBase
{
    public required Guid EventId { get; init; }

    public required string EventType { get; init; }

    public string SchemaVersion { get; init; } = OrderEventTypes.SchemaVersion;

    public required Guid CompanyId { get; init; }

    /// <summary>The order ID; named entityId to match Inventory's envelope.</summary>
    public required Guid EntityId { get; init; }

    public required Guid OrderId { get; init; }

    public required string OrderReference { get; init; }

    public required Guid ReservationId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public required string FulfilmentType { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset OrderDate { get; init; }

    public required EventShop Shop { get; init; }

    public required EventAgency Agency { get; init; }

    public required Guid TerritoryId { get; init; }

    public required Guid ProvinceId { get; init; }

    public required EventSalesRep SalesRep { get; init; }

    public required IReadOnlyList<EventOrderLine> Lines { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal Total { get; init; }

    public string Currency { get; init; } = "LKR";

    /// <summary>
    /// Where checkout happened. Null until a cash sale is checked out, and
    /// always null for a scheduled delivery (no GPS check-in in that flow).
    /// </summary>
    public EventLocation? CheckoutLocation { get; init; }
}

public sealed record OrderPlacedEvent : OrderEventBase;

public sealed record OrderConfirmedEvent : OrderEventBase
{
    public required DateTimeOffset ConfirmedAt { get; init; }
}

public sealed record PaymentRecordedEvent : OrderEventBase
{
    public required EventPayment Payment { get; init; }

    /// <summary>The accepted check-in that permitted the payment.</summary>
    public required EventLocation CheckInLocation { get; init; }
}

public sealed record OrderCancelledEvent : OrderEventBase
{
    public required DateTimeOffset CancelledAt { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Adds order events to the outbox in the current unit of work. They
/// commit with the order change or not at all — a confirmed order can
/// never exist without its event.
/// </summary>
public interface IOrderEventOutbox
{
    void OrderPlaced(Sellora.OrderService.Domain.Entities.Order order, DateTimeOffset occurredAt);

    void OrderConfirmed(Sellora.OrderService.Domain.Entities.Order order, DateTimeOffset occurredAt);

    void PaymentRecorded(
        Sellora.OrderService.Domain.Entities.Order order,
        Sellora.OrderService.Domain.Entities.Payment payment,
        DateTimeOffset occurredAt);

    void OrderCancelled(Sellora.OrderService.Domain.Entities.Order order, DateTimeOffset occurredAt);
}
