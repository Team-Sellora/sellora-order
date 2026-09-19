using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sellora.OrderService.Application.Dependencies;

namespace Sellora.OrderService.Infrastructure.Dependencies;

public sealed class DependencyOptions
{
    public const string Section = "Dependencies";

    public EndpointOptions Organization { get; set; } = new();

    public EndpointOptions Catalog { get; set; } = new();

    public EndpointOptions Inventory { get; set; } = new();

    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
}

public sealed class EndpointOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Per-call budget. Short on purpose: a rep at a shop counter should get
    /// a clear rejection, not a spinner. Four sequential calls stay under ~12s.
    /// </summary>
    public double TimeoutSeconds { get; set; } = 3;
}

public sealed class CircuitBreakerSettings
{
    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 5;

    public double SamplingDurationSeconds { get; set; } = 30;

    public double BreakDurationSeconds { get; set; } = 30;
}

public static class DependencyRegistration
{
    /// <summary>
    /// Registers the three typed clients, each with its own timeout and
    /// circuit breaker. Nothing here contacts the dependencies, so the app
    /// starts and reports healthy even when they are unreachable (US-E4-1b-D2).
    /// </summary>
    public static IServiceCollection AddOrderDependencies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(DependencyOptions.Section).Get<DependencyOptions>()
            ?? new DependencyOptions();

        services.AddTransient<ForwardBearerTokenHandler>();

        services.AddHttpClient<IOrganizationClient, OrganizationClient>(client =>
                client.BaseAddress = BaseAddress(options.Organization, "Organization"))
            .AddResilience("organization", options.Organization, options.CircuitBreaker)
            .AddHttpMessageHandler<ForwardBearerTokenHandler>();

        // Anonymous internal endpoint: no token handler.
        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
                client.BaseAddress = BaseAddress(options.Catalog, "Catalog"))
            .AddResilience("catalog", options.Catalog, options.CircuitBreaker);

        services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
                client.BaseAddress = BaseAddress(options.Inventory, "Inventory"))
            .AddResilience("inventory", options.Inventory, options.CircuitBreaker)
            .AddHttpMessageHandler<ForwardBearerTokenHandler>();

        return services;
    }

    private static IHttpClientBuilder AddResilience(
        this IHttpClientBuilder builder,
        string name,
        EndpointOptions endpoint,
        CircuitBreakerSettings breaker)
    {
        builder.AddResilienceHandler($"{name}-pipeline", pipeline =>
        {
            // Outer: the breaker counts timeouts, network errors and 5xx
            // (the default ShouldHandle), then fails fast while open.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(Math.Max(
                    breaker.SamplingDurationSeconds, endpoint.TimeoutSeconds * 2)),
                BreakDuration = TimeSpan.FromSeconds(breaker.BreakDurationSeconds)
            });

            // Inner: each attempt gets its own time budget.
            pipeline.AddTimeout(TimeSpan.FromSeconds(endpoint.TimeoutSeconds));
        });

        return builder;
    }

    private static Uri BaseAddress(EndpointOptions endpoint, string name)
    {
        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Dependencies:{name}:BaseUrl must be an absolute URL (got '{endpoint.BaseUrl}').");
        }

        // A trailing slash keeps relative paths like "api/hierarchy" under any gateway prefix.
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
