using Serilog.Context;

namespace Sellora.OrderService.Api.Middleware;

/// <summary>
/// Copied from sellora-organization. Assigns each request a correlation ID —
/// the incoming X-Correlation-ID header if present, otherwise a new one —
/// returns it on the response, and pushes it into every log line. Order
/// events carry it, and calls to Organization, Catalog and Inventory
/// forward it, so one order can be traced across services.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string ItemKey = "Sellora.CorrelationId";
    public const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();

        // Bounded and printable: this value goes into logs, headers and the
        // outbox, so a caller must not be able to inject anything odd.
        var correlationId = !string.IsNullOrWhiteSpace(incoming) &&
                            incoming.Length <= MaxLength &&
                            incoming.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            ? incoming
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;
        context.Items[ItemKey] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
