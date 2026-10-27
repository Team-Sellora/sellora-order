using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Infrastructure.Outbox;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-4 T5 / DoD 4: the relay publishes each committed event once, keeps
/// per-order ordering through failures, and across concurrent relays.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OutboxRelayTests
{
    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();

    public OutboxRelayTests(PostgreSqlFixture fixture) => _fixture = fixture;

    internal sealed class RecordingPublisher : IEventPublisher
    {
        public ConcurrentQueue<OutboxMessageToPublish> Published { get; } = new();

        /// <summary>Event IDs that fail this many more times before succeeding.</summary>
        public ConcurrentDictionary<Guid, int> FailuresLeft { get; } = new();

        public bool BrokerDown { get; set; }

        public async Task PublishAsync(OutboxMessageToPublish message, CancellationToken cancellationToken = default)
        {
            // A little latency so concurrent relays genuinely interleave.
            await Task.Delay(Random.Shared.Next(1, 5), cancellationToken);

            if (BrokerDown)
            {
                throw new InvalidOperationException("Local: Message timed out");
            }

            if (FailuresLeft.TryGetValue(message.OutboxId, out var left) && left > 0)
            {
                FailuresLeft[message.OutboxId] = left - 1;
                throw new InvalidOperationException("Local: Broker transport failure");
            }

            Published.Enqueue(message);
        }
    }

    internal static OutboxRelayService Relay(PostgreSqlFixture fixture, IEventPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext(null));

        return new OutboxRelayService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            publisher,
            Options.Create(new OutboxRelayOptions { RetryDelaySeconds = 0, BatchSize = 200 }),
            NullLogger<OutboxRelayService>.Instance);
    }

    /// <summary>Writes events the way OrderEventOutbox does: same time, increasing ordinal.</summary>
    private async Task<List<Guid>> SeedOrderEventsAsync(string reference, params string[] eventTypes)
    {
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ids = new List<Guid>();

        await using var db = _fixture.CreateContext(_companyId);
        var writer = new EntityFrameworkOutboxWriter(db);

        for (var ordinal = 0; ordinal < eventTypes.Length; ordinal++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            writer.Enqueue(new NewOutboxMessage(
                id, _companyId, "Order", Guid.NewGuid(), reference, eventTypes[ordinal], "1.0",
                $$"""{"eventType":"{{eventTypes[ordinal]}}","orderReference":"{{reference}}"}""",
                "relay-test", occurredAt, ordinal));
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task<OutboxMessage> RowAsync(Guid id)
    {
        await using var db = _fixture.CreateContext(null);
        return await db.OutboxMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync(message => message.OutboxId == id);
    }

    private static string Reference() => $"ORD-TEST-{Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task Each_event_is_published_exactly_once_in_order()
    {
        var reference = Reference();
        var ids = await SeedOrderEventsAsync(reference, "OrderPlaced", "OrderConfirmed", "PaymentRecorded");
        var publisher = new RecordingPublisher();
        var relay = Relay(_fixture, publisher);

        await relay.ProcessPendingMessagesAsync();
        await relay.ProcessPendingMessagesAsync();

        var mine = publisher.Published.Where(message => message.MessageKey == reference).ToList();
        Assert.Equal(ids, mine.Select(message => message.OutboxId));

        foreach (var id in ids)
        {
            Assert.NotNull((await RowAsync(id)).PublishedAt);
        }
    }

    [Fact]
    public async Task A_failed_event_is_not_overtaken_by_a_later_event_of_the_same_order()
    {
        var reference = Reference();
        var ids = await SeedOrderEventsAsync(reference, "OrderConfirmed", "PaymentRecorded");
        var publisher = new RecordingPublisher();
        publisher.FailuresLeft[ids[0]] = 1;
        var relay = Relay(_fixture, publisher);

        await relay.ProcessPendingMessagesAsync();

        // OrderConfirmed failed, so PaymentRecorded must wait for it.
        Assert.DoesNotContain(publisher.Published, message => message.MessageKey == reference);
        Assert.Equal(1, (await RowAsync(ids[0])).AttemptCount);

        await relay.ProcessPendingMessagesAsync();

        Assert.Equal(ids, publisher.Published.Where(m => m.MessageKey == reference).Select(m => m.OutboxId));
    }

    [Fact]
    public async Task Broker_outage_then_recovery_publishes_each_committed_event_once()
    {
        var reference = Reference();
        var ids = await SeedOrderEventsAsync(reference, "OrderConfirmed", "PaymentRecorded");
        var publisher = new RecordingPublisher { BrokerDown = true };
        var relay = Relay(_fixture, publisher);

        await relay.ProcessPendingMessagesAsync();
        await relay.ProcessPendingMessagesAsync();

        Assert.Null((await RowAsync(ids[0])).PublishedAt);
        Assert.True((await RowAsync(ids[0])).AttemptCount >= 1);

        publisher.BrokerDown = false;
        await relay.ProcessPendingMessagesAsync();
        await relay.ProcessPendingMessagesAsync();

        Assert.Equal(ids, publisher.Published.Where(m => m.MessageKey == reference).Select(m => m.OutboxId));
    }

    // DoD 4: ordering holds with two relay instances racing for the same rows.
    [Fact]
    public async Task Concurrent_relays_keep_per_order_ordering_and_publish_once()
    {
        var orders = new Dictionary<string, List<Guid>>();
        for (var i = 0; i < 20; i++)
        {
            var reference = Reference();
            orders[reference] = await SeedOrderEventsAsync(reference, "OrderPlaced", "OrderConfirmed", "PaymentRecorded");
        }

        var publisher = new RecordingPublisher();
        var first = Relay(_fixture, publisher);
        var second = Relay(_fixture, publisher);

        for (var round = 0; round < 5; round++)
        {
            await Task.WhenAll(first.ProcessPendingMessagesAsync(), second.ProcessPendingMessagesAsync());
        }

        foreach (var (reference, ids) in orders)
        {
            Assert.Equal(ids, publisher.Published.Where(m => m.MessageKey == reference).Select(m => m.OutboxId));
        }
    }
}
