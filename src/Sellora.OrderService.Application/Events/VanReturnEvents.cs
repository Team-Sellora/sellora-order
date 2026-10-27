namespace Sellora.OrderService.Application.Events;

/// <summary>
/// US-E4-6: published on the order topic when the agency accepts a van
/// return. Inventory moves each line's <see cref="VanStockReturnedLine.AcceptedQuantity"/>
/// from the rep's van owner to the agency's owner. Declared quantity and
/// variance ride along for Notification and Audit.
/// </summary>
public sealed record VanStockReturnedEvent
{
    public const string Type = "VanStockReturned";

    public required Guid EventId { get; init; }

    public string EventType { get; init; } = Type;

    public string SchemaVersion { get; init; } = "1.0";

    public required Guid CompanyId { get; init; }

    /// <summary>The van return ID (matches Inventory's envelope name).</summary>
    public required Guid EntityId { get; init; }

    public required Guid VanReturnId { get; init; }

    /// <summary>Also the Kafka key.</summary>
    public required string ReturnReference { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public required Guid SalesRepId { get; init; }

    public string? SalesRepName { get; init; }

    public required Guid AgencyId { get; init; }

    /// <summary>Inventory owner debited (the rep's van).</summary>
    public required Guid VanInventoryOwnerId { get; init; }

    public required DateTimeOffset DeclaredAt { get; init; }

    public required DateTimeOffset AcceptedAt { get; init; }

    public required EventActor AcceptedBy { get; init; }

    public string? AcceptanceNote { get; init; }

    public required IReadOnlyList<VanStockReturnedLine> Lines { get; init; }

    public required int TotalDeclared { get; init; }

    public required int TotalAccepted { get; init; }

    public required int TotalVariance { get; init; }
}

/// <param name="AcceptedQuantity">What the agency counted — the only quantity that moves.</param>
/// <param name="Variance">Declared minus accepted.</param>
public sealed record VanStockReturnedLine(
    Guid ProductId,
    string? ProductName,
    int DeclaredQuantity,
    int AcceptedQuantity,
    int Variance);

public interface IVanReturnEventOutbox
{
    void VanStockReturned(
        Sellora.OrderService.Domain.Entities.VanReturn vanReturn,
        string acceptedByRole,
        DateTimeOffset occurredAt);
}
