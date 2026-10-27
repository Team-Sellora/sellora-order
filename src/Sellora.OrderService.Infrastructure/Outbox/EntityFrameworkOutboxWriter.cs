using Microsoft.EntityFrameworkCore;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Infrastructure.Persistence;
using Sellora.OrderService.Infrastructure.Persistence.Configurations;

namespace Sellora.OrderService.Infrastructure.Outbox;

public sealed class EntityFrameworkOutboxWriter(OrderDbContext db) : IOutboxWriter
{
    public void Enqueue(NewOutboxMessage message)
    {
        // The sequence is fetched here, in Enqueue order, rather than letting
        // the database identity column assign it. EF Core does not preserve
        // insertion order when several rows are saved in one SaveChanges, so
        // a database-generated value cannot order events written in the same
        // transaction (OrderConfirmed must come before PaymentRecorded).
        var sequence = db.Database
            .SqlQueryRaw<long>($"SELECT nextval('{OutboxMessageConfiguration.SequenceName}') AS \"Value\"")
            .AsEnumerable()
            .Single();

        db.OutboxMessages.Add(new OutboxMessage
        {
            OutboxId = message.EventId,
            Sequence = sequence,
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
