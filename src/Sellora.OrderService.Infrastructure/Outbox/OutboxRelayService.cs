using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Application.Outbox;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.Outbox;

public sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";

    public int BatchSize { get; init; } = 50;

    public int PollingIntervalSeconds { get; init; } = 5;

    public int LeaseSeconds { get; init; } = 30;

    public int RetryDelaySeconds { get; init; } = 15;

    /// <summary>
    /// Rounds per poll. Each round publishes at most one event per order,
    /// so an order's OrderConfirmed and PaymentRecorded go out in the same
    /// poll, one round after the other.
    /// </summary>
    public int MaxRoundsPerPoll { get; init; } = 10;
}

/// <summary>
/// Publishes committed outbox rows to Kafka. The lease-and-publish logic is
/// sellora-organization's, unchanged. One addition: a row is only picked
/// when no earlier event for the same order (same message key) is still
/// unpublished. Without it, a failed OrderConfirmed could be overtaken by
/// its PaymentRecorded — or a second relay instance could publish them out
/// of order. With it, per-order ordering holds across retries and instances.
/// </summary>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<OutboxRelayOptions> options,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private readonly OutboxRelayOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Outbox relay polling failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }
    }

    /// <returns>How many events were published.</returns>
    public async Task<int> ProcessPendingMessagesAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;

        for (var round = 0; round < _options.MaxRoundsPerPoll; round++)
        {
            var published = await RelayRoundAsync(cancellationToken);
            total += published;

            if (published == 0)
            {
                break;
            }
        }

        return total;
    }

    private async Task<int> RelayRoundAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.AddSeconds(_options.LeaseSeconds);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        // Ignore tenant filters: this system process relays every tenant.
        var outbox = db.OutboxMessages.IgnoreQueryFilters();

        var candidateIds = await outbox
            .Where(message =>
                message.PublishedAt == null &&
                message.NextAttemptAt <= now &&
                (message.LeaseExpiresAt == null || message.LeaseExpiresAt < now) &&
                !outbox.Any(earlier =>
                    earlier.MessageKey == message.MessageKey &&
                    earlier.PublishedAt == null &&
                    (earlier.OccurredAt < message.OccurredAt ||
                     (earlier.OccurredAt == message.OccurredAt && earlier.Ordinal < message.Ordinal))))
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Ordinal)
            .Select(message => message.OutboxId)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var outboxId in candidateIds)
        {
            // Conditional update means only one relay instance can lease a row.
            await outbox
                .Where(message =>
                    message.OutboxId == outboxId &&
                    message.PublishedAt == null &&
                    (message.LeaseExpiresAt == null || message.LeaseExpiresAt < now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.LeaseId, leaseId)
                        .SetProperty(message => message.LeaseExpiresAt, leaseExpiresAt),
                    cancellationToken);
        }

        var leasedMessages = await outbox
            .AsNoTracking()
            .Where(message => message.LeaseId == leaseId)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Ordinal)
            .ToListAsync(cancellationToken);

        var published = 0;

        foreach (var message in leasedMessages)
        {
            try
            {
                await publisher.PublishAsync(
                    new OutboxMessageToPublish(
                        message.OutboxId,
                        message.EventType,
                        message.SchemaVersion,
                        message.CompanyId,
                        message.AggregateId,
                        message.MessageKey,
                        message.Payload,
                        message.CorrelationId,
                        message.OccurredAt),
                    cancellationToken);

                await outbox
                    .Where(item =>
                        item.OutboxId == message.OutboxId &&
                        item.LeaseId == leaseId &&
                        item.PublishedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.PublishedAt, DateTimeOffset.UtcNow)
                            .SetProperty(item => item.LeaseId, (Guid?)null)
                            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                            .SetProperty(item => item.LastError, (string?)null),
                        cancellationToken);

                published++;

                logger.LogInformation(
                    "Published {EventType} {EventId} for {MessageKey} (correlation {CorrelationId})",
                    message.EventType, message.OutboxId, message.MessageKey, message.CorrelationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Outbox message {OutboxId} could not be published; it will retry.",
                    message.OutboxId);

                var lastError = exception.Message.Length > 2000
                    ? exception.Message[..2000]
                    : exception.Message;

                await outbox
                    .Where(item =>
                        item.OutboxId == message.OutboxId &&
                        item.LeaseId == leaseId &&
                        item.PublishedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                            .SetProperty(item => item.LastError, lastError)
                            .SetProperty(item => item.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(_options.RetryDelaySeconds))
                            .SetProperty(item => item.LeaseId, (Guid?)null)
                            .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null),
                        cancellationToken);
            }
        }

        return published;
    }
}
