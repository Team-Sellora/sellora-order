using Sellora.OrderService.Application.Outbox;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// US-E4-4: sends the current request's correlation ID to Organization,
/// Catalog and Inventory (X-Correlation-ID), so their logs for one order
/// share the ID that its events carry.
/// </summary>
public sealed class ForwardCorrelationIdHandler(ICorrelationIdAccessor correlation) : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-ID";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(HeaderName);
        request.Headers.TryAddWithoutValidation(HeaderName, correlation.GetCorrelationId());

        return base.SendAsync(request, cancellationToken);
    }
}
