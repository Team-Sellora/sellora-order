using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sellora.OrderService.Domain.Entities;

namespace Sellora.OrderService.Infrastructure.Persistence.Configurations;

/// <summary>Copied from sellora-organization, plus message_key and ordinal.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");

        builder.HasKey(message => message.OutboxId).HasName("pk_outbox_message");

        builder.Property(message => message.OutboxId)
            .HasColumnName("outbox_id").HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(message => message.CompanyId)
            .HasColumnName("company_id").HasColumnType("uuid").IsRequired();
        builder.Property(message => message.AggregateType)
            .HasColumnName("aggregate_type").HasMaxLength(80).IsRequired();
        builder.Property(message => message.AggregateId)
            .HasColumnName("aggregate_id").HasColumnType("uuid").IsRequired();
        builder.Property(message => message.MessageKey)
            .HasColumnName("message_key").HasMaxLength(100).IsRequired();
        builder.Property(message => message.EventType)
            .HasColumnName("event_type").HasMaxLength(120).IsRequired();
        builder.Property(message => message.SchemaVersion)
            .HasColumnName("schema_version").HasMaxLength(16).IsRequired();
        builder.Property(message => message.Payload)
            .HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CorrelationId)
            .HasColumnName("correlation_id").HasColumnType("text").HasMaxLength(128).IsRequired();
        builder.Property(message => message.OccurredAt)
            .HasColumnName("occurred_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.Ordinal)
            .HasColumnName("ordinal").IsRequired();
        builder.Property(message => message.PublishedAt)
            .HasColumnName("published_at").HasColumnType("timestamp with time zone");
        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count").IsRequired();
        builder.Property(message => message.LastError)
            .HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(message => message.NextAttemptAt)
            .HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(message => message.LeaseId)
            .HasColumnName("lease_id").HasColumnType("uuid");
        builder.Property(message => message.LeaseExpiresAt)
            .HasColumnName("lease_expires_at").HasColumnType("timestamp with time zone");

        builder.HasIndex(message => new { message.PublishedAt, message.NextAttemptAt, message.LeaseExpiresAt })
            .HasDatabaseName("ix_outbox_message_pending_relay");

        // "Is there an earlier unpublished event for this order?" — checked
        // for every candidate, so it needs its own index.
        builder.HasIndex(message => new { message.MessageKey, message.OccurredAt, message.Ordinal })
            .HasDatabaseName("ix_outbox_message_key_order");
    }
}
