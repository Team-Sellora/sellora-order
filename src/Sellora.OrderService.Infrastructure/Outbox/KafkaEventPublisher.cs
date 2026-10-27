using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Infrastructure.Kafka;

namespace Sellora.OrderService.Infrastructure.Outbox;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";

    /// <summary>The order events topic Inventory already consumes.</summary>
    public string OrderTopic { get; init; } = "sellora.order.v1";

    public int MessageTimeoutMs { get; init; } = 10_000;

    /// <summary>Confluent Cloud API key. Empty for a local broker.</summary>
    public string? SaslUsername { get; init; }

    /// <summary>Confluent Cloud API secret.</summary>
    public string? SaslPassword { get; init; }
}

/// <summary>
/// Publishes outbox rows to Kafka. Idempotent producer with acks=all, as in
/// sellora-organization, so a retried send cannot duplicate within a session.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        _options = options.Value;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = _options.MessageTimeoutMs
        };

        KafkaSaslConfigurator.Apply(config, _options.SaslUsername, _options.SaslPassword);

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(
        OutboxMessageToPublish message,
        CancellationToken cancellationToken = default)
    {
        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(message.OutboxId.ToString()) },
            { "event-type", Encoding.UTF8.GetBytes(message.EventType) },
            { "schema-version", Encoding.UTF8.GetBytes(message.SchemaVersion) },
            { "company-id", Encoding.UTF8.GetBytes(message.CompanyId.ToString()) },
            { "correlation-id", Encoding.UTF8.GetBytes(message.CorrelationId) }
        };

        await _producer.ProduceAsync(
            _options.OrderTopic,
            new Message<string, string>
            {
                // Order reference: every event of one order lands on the same
                // partition, so consumers see them in the order they happened.
                Key = message.MessageKey,
                Value = message.Payload,
                Headers = headers
            },
            cancellationToken);
    }

    public void Dispose() => _producer.Dispose();
}
