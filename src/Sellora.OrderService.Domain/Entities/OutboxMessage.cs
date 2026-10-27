using Sellora.OrderService.Domain.Tenancy;

namespace Sellora.OrderService.Domain.Entities;

/// <summary>
/// An event waiting to be published, written in the same transaction as
/// the order change it describes. Copied from sellora-organization's proven
/// outbox, plus <see cref="MessageKey"/> and <see cref="Ordinal"/> so events
/// for one order are published in order.
/// </summary>
public class OutboxMessage : ITenantScoped
{
    public Guid OutboxId { get; set; }

    /// <summary>
    /// Database-generated, strictly increasing insertion order. OccurredAt
    /// alone cannot order events across separate transactions — two events
    /// written moments apart can share a timestamp at clock resolution.
    /// This is the true global order events were written in; the relay and
    /// any reader that needs cross-order ordering use this, not OccurredAt.
    /// </summary>
    public long Sequence { get; set; }

    public Guid CompanyId { get; set; }

    public string AggregateType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    /// <summary>Kafka message key: the order reference, so per-order ordering holds.</summary>
    public string MessageKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    // Mapped to PostgreSQL jsonb through Fluent configuration.
    public string Payload { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Position among events written in the same transaction (e.g.
    /// OrderConfirmed = 0, PaymentRecorded = 1), which share OccurredAt.
    /// </summary>
    public int Ordinal { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public Guid? LeaseId { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public string SchemaVersion { get; set; } = "1.0";
}
