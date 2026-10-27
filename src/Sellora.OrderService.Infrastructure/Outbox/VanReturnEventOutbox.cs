using System.Text.Json;
using Sellora.OrderService.Application.Events;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Infrastructure.Outbox;

/// <summary>
/// US-E4-6: writes VanStockReturned into the same outbox as order events,
/// in the same SaveChanges as the acceptance. Keyed by the return
/// reference, so the relay's per-key ordering applies to it too.
/// </summary>
public sealed class VanReturnEventOutbox(IOutboxWriter writer, ICorrelationIdAccessor correlation) : IVanReturnEventOutbox
{
    public const string AggregateType = "VanReturn";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void VanStockReturned(VanReturn vanReturn, string acceptedByRole, DateTimeOffset occurredAt)
    {
        var eventId = Guid.NewGuid();
        var correlationId = correlation.GetCorrelationId();

        var lines = vanReturn.Lines
            .OrderBy(line => line.ProductId)
            .Select(line => new VanStockReturnedLine(
                line.ProductId,
                line.ProductNameSnapshot,
                line.DeclaredQuantity,
                line.CountedQuantity ?? 0,
                line.Variance ?? line.DeclaredQuantity))
            .ToList();

        var @event = new VanStockReturnedEvent
        {
            EventId = eventId,
            CompanyId = vanReturn.CompanyId,
            EntityId = vanReturn.VanReturnId,
            VanReturnId = vanReturn.VanReturnId,
            ReturnReference = vanReturn.ReturnReference,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            SalesRepId = vanReturn.SalesRepId,
            SalesRepName = vanReturn.SalesRepName,
            AgencyId = vanReturn.AgencyId,
            VanInventoryOwnerId = vanReturn.VanInventoryOwnerId,
            DeclaredAt = vanReturn.DeclaredAt,
            AcceptedAt = vanReturn.AcceptedAt ?? occurredAt,
            AcceptedBy = new EventActor(vanReturn.AcceptedBy ?? string.Empty, acceptedByRole),
            AcceptanceNote = vanReturn.AcceptanceNote,
            Lines = lines,
            TotalDeclared = lines.Sum(line => line.DeclaredQuantity),
            TotalAccepted = lines.Sum(line => line.AcceptedQuantity),
            TotalVariance = lines.Sum(line => line.Variance)
        };

        writer.Enqueue(new NewOutboxMessage(
            eventId,
            vanReturn.CompanyId,
            AggregateType,
            vanReturn.VanReturnId,
            vanReturn.ReturnReference,
            @event.EventType,
            @event.SchemaVersion,
            JsonSerializer.Serialize(@event, Json),
            correlationId,
            occurredAt,
            0));
    }
}
