using Sellora.OrderService.Application.Outbox;

namespace Sellora.OrderService.Api.Middleware;

public sealed class HttpCorrelationIdAccessor(IHttpContextAccessor accessor) : ICorrelationIdAccessor
{
    public string GetCorrelationId() =>
        accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey] is string { Length: > 0 } correlationId
            ? correlationId
            // Background work has no request; give it its own ID.
            : Guid.NewGuid().ToString();
}
