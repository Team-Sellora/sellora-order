using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Infrastructure.Outbox;
using Testcontainers.Kafka;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-4 T5 against a real broker (Testcontainers, so no local Kafka is
/// needed): an event committed while Kafka is unreachable is relayed exactly
/// once when it comes back, keyed by order reference and carrying the
/// correlation ID header.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class KafkaOutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();

    public KafkaOutboxIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _kafka.StartAsync();

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();

    [Fact]
    public async Task Event_committed_during_an_outage_is_published_once_after_recovery()
    {
        var topic = $"sellora.order.test.{Guid.NewGuid():N}";
        var reference = $"ORD-KAFKA-{Guid.NewGuid():N}"[..24];
        var eventId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(companyId))
        {
            // Other tests in this collection leave unpublished rows behind;
            // with the broker down each would cost a full send timeout.
            await db.OutboxMessages.IgnoreQueryFilters()
                .Where(message => message.PublishedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.PublishedAt, DateTimeOffset.UtcNow));

            new EntityFrameworkOutboxWriter(db).Enqueue(new NewOutboxMessage(
                eventId, companyId, "Order", Guid.NewGuid(), reference, "OrderConfirmed", "1.0",
                $$"""{"eventId":"{{eventId}}","eventType":"OrderConfirmed","orderReference":"{{reference}}"}""",
                "kafka-outage-correlation", DateTimeOffset.UtcNow.AddSeconds(-1), 0));
            await db.SaveChangesAsync();
        }

        // 1. Broker unreachable: the event stays in the outbox, not lost.
        using (var down = new KafkaEventPublisher(Options.Create(new KafkaOptions
        {
            BootstrapServers = "localhost:1",
            OrderTopic = topic,
            MessageTimeoutMs = 2_000
        })))
        {
            await OutboxRelayTests.Relay(_fixture, down).ProcessPendingMessagesAsync();
        }

        // 2. Broker back: the relay publishes it.
        using (var up = new KafkaEventPublisher(Options.Create(new KafkaOptions
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            OrderTopic = topic
        })))
        {
            var relay = OutboxRelayTests.Relay(_fixture, up);
            await relay.ProcessPendingMessagesAsync();
            await relay.ProcessPendingMessagesAsync(); // a second poll must not re-publish
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(topic);

        var received = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var record = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (record is not null)
            {
                received.Add(record);
            }
            else if (received.Count > 0)
            {
                break;
            }
        }

        var message = Assert.Single(received, record => record.Message.Key == reference);
        Assert.Contains(eventId.ToString(), message.Message.Value);
        Assert.Equal(
            "kafka-outage-correlation",
            Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes("correlation-id")));
    }
}
