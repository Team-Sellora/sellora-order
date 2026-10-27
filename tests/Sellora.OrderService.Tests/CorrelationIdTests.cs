using System.Net;
using Microsoft.AspNetCore.Http;
using Sellora.OrderService.Api.Middleware;
using Sellora.OrderService.Infrastructure.Dependencies;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-4 DoD 5: one correlation ID from the HTTP request into logs, calls and events.</summary>
public sealed class CorrelationIdTests
{
    private static async Task<(HttpContext Context, string? SeenByNext)> RunAsync(string? incoming)
    {
        var context = new DefaultHttpContext();
        if (incoming is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;
        }

        string? seen = null;
        var middleware = new CorrelationIdMiddleware(inner =>
        {
            seen = new HttpCorrelationIdAccessor(new HttpContextAccessor { HttpContext = inner }).GetCorrelationId();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, seen);
    }

    [Fact]
    public async Task An_incoming_correlation_id_is_reused_and_returned()
    {
        var (context, seen) = await RunAsync("web-3f9c-42");

        Assert.Equal("web-3f9c-42", seen);
        Assert.Equal("web-3f9c-42", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task A_missing_correlation_id_is_generated()
    {
        var (context, seen) = await RunAsync(null);

        Assert.True(Guid.TryParse(seen, out _));
        Assert.Equal(seen, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData("bad value with spaces")]
    [InlineData("<script>")]
    public async Task An_unsafe_correlation_id_is_replaced(string incoming)
    {
        var (_, seen) = await RunAsync(incoming);

        Assert.NotEqual(incoming, seen);
        Assert.True(Guid.TryParse(seen, out _));
    }

    [Fact]
    public async Task Calls_to_other_services_forward_the_correlation_id()
    {
        var inner = StubHttpHandler.Json(HttpStatusCode.OK, "{}");
        var handler = new ForwardCorrelationIdHandler(new FixedCorrelation("req-123")) { InnerHandler = inner };

        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://organization.test/") };
        await client.GetAsync("api/hierarchy");

        var sent = inner.Requests.Single().Request;
        Assert.Equal("req-123", sent.Headers.GetValues(ForwardCorrelationIdHandler.HeaderName).Single());
    }
}
