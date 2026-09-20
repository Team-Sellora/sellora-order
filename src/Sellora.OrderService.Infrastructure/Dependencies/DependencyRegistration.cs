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
    /// a clear rejection, not a spinner. A normal order makes four sequential
    /// calls, so the worst case stays around 8s.
    /// </summary>
    public double TimeoutSeconds { get; set; } = 2;

    /// <summary>
    /// Catalog only: shared secret for its /internal/* routes, sent as
    /// X-Internal-Api-Key. Must equal Catalog's InternalApi:ApiKey.
    /// Keep it out of git — set it via App Service settings or user-secrets.
    /// </summary>
    public string InternalApiKey { get; set; } = string.Empty;
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
    /// starts and reports healthy even when they are unreachable.
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

        // Catalog's /internal routes take a shared key, not the user's token.
        services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
            {
                client.BaseAddress = BaseAddress(options.Catalog, "Catalog");

                if (!string.IsNullOrWhiteSpace(options.Catalog.InternalApiKey))
                {
                    client.DefaultRequestHeaders.Add(
                        CatalogClient.InternalApiKeyHeader, options.Catalog.InternalApiKey);
                }
            })
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
