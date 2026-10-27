namespace Sellora.OrderService.Application.Outbox;

/// <param name="EventId">Also written into the payload, so consumers can deduplicate on it.</param>
public sealed record NewOutboxMessage(
    Guid EventId,
    Guid CompanyId,
    string AggregateType,
    Guid AggregateId,
    string MessageKey,
    string EventType,
    string SchemaVersion,
    string Payload,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    int Ordinal);

public interface ICorrelationIdAccessor
{
    string GetCorrelationId();
}

public interface IOutboxWriter
{
    /// <summary>
    /// Adds the message to the current DbContext. It is saved by the same
    /// SaveChanges as the order change, so both commit or neither does.
    /// </summary>
    void Enqueue(NewOutboxMessage message);
}

public sealed record OutboxMessageToPublish(
    Guid OutboxId,
    string EventType,
    string SchemaVersion,
    Guid CompanyId,
    Guid AggregateId,
    string MessageKey,
    string Payload,
    string CorrelationId,
    DateTimeOffset OccurredAt);

public interface IEventPublisher
{
    Task PublishAsync(
        OutboxMessageToPublish message,
        CancellationToken cancellationToken = default);
}
