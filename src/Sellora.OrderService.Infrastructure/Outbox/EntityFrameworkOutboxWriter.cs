using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Outbox;

public sealed class EntityFrameworkOutboxWriter(OrderDbContext db) : IOutboxWriter
{
    public void Enqueue(NewOutboxMessage message)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = message.EventId,
            CompanyId = message.CompanyId,
            AggregateType = message.AggregateType,
            AggregateId = message.AggregateId,
            MessageKey = message.MessageKey,
            EventType = message.EventType,
            SchemaVersion = message.SchemaVersion,
            Payload = message.Payload,
            CorrelationId = message.CorrelationId,
            OccurredAt = message.OccurredAt,
            Ordinal = message.Ordinal,
            NextAttemptAt = message.OccurredAt,
            AttemptCount = 0
        });
    }
}
