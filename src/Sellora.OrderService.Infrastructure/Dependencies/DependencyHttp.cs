using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sellora.OrderService.Application.Dependencies;

namespace Sellora.OrderService.Infrastructure.Dependencies;

/// <summary>
/// A dependency answered, but refused the request in a way that is not an
/// outage (a 4xx the saga did not expect). Reported as that step's failure.
/// </summary>
public sealed class DependencyRejectedException : Exception
{
    public DependencyRejectedException(Dependency dependency, HttpStatusCode status, string? detail)
        : base($"{dependency} rejected the request (HTTP {(int)status}){(string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}")}")
    {
        Dependency = dependency;
        Status = status;
    }

    public Dependency Dependency { get; }

    public HttpStatusCode Status { get; }
}

/// <summary>Shared send/parse helpers so every client fails the same way.</summary>
internal static class DependencyHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sends the request, turning timeouts, an open breaker, network errors
    /// and 5xx responses into <see cref="DependencyUnavailableException"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        Dependency dependency,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is TimeoutRejectedException
                or BrokenCircuitException
                or HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new DependencyUnavailableException(dependency, exception);
        }

        if ((int)response.StatusCode >= 500)
        {
            response.Dispose();
            throw new DependencyUnavailableException(dependency);
        }

        return response;
    }

    public static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        Dependency dependency,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        return body ?? throw new DependencyRejectedException(
            dependency, response.StatusCode, "The response body was empty.");
    }

    /// <summary>Best-effort error text from ProblemDetails or { message } bodies.</summary>
    public static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, cancellationToken);
            return body?.Detail ?? body?.Message ?? body?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ErrorBody(string? Message, string? Title, string? Detail);
}
