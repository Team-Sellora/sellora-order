using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Infrastructure.Dependencies;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-1b-T3: real Polly pipelines, fake slow dependency.</summary>
public sealed class ResilienceTests
{
    private static ServiceProvider Build(HttpMessageHandler primary, string? catalogKey = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dependencies:Organization:BaseUrl"] = "http://organization.test",
                ["Dependencies:Catalog:BaseUrl"] = "http://catalog.test",
                ["Dependencies:Catalog:TimeoutSeconds"] = "0.2",
                ["Dependencies:Catalog:InternalApiKey"] = catalogKey,
                ["Dependencies:Inventory:BaseUrl"] = "http://inventory.test",
                ["Dependencies:CircuitBreaker:MinimumThroughput"] = "2",
                ["Dependencies:CircuitBreaker:FailureRatio"] = "0.5",
                ["Dependencies:CircuitBreaker:BreakDurationSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessTokenAccessor>(new NoToken());
        services.AddOrderDependencies(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => primary));
        return services.BuildServiceProvider();
    }

    private static StubHttpHandler Slow() =>
        new(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

    private static Task<ProductResolutionResponse> Call(ServiceProvider provider) =>
        provider.GetRequiredService<ICatalogClient>()
            .ResolveProductsAsync(Guid.NewGuid(), new[] { Guid.NewGuid() }, CancellationToken.None);

    [Fact]
    public void Registration_does_not_contact_dependencies()
    {
        // D2: resolving the clients must succeed with nothing listening.
        using var provider = Build(Slow());

        Assert.NotNull(provider.GetRequiredService<IOrganizationClient>());
        Assert.NotNull(provider.GetRequiredService<ICatalogClient>());
        Assert.NotNull(provider.GetRequiredService<IInventoryClient>());
    }

    [Fact]
    public async Task Slow_dependency_is_rejected_within_its_timeout()
    {
        using var provider = Build(Slow());
        var clock = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() => Call(provider));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        Assert.Equal(Dependency.Catalog, exception.Dependency);
        Assert.IsType<TimeoutRejectedException>(exception.InnerException);
    }

    [Fact]
    public async Task Repeated_failures_open_the_breaker_and_later_calls_fail_fast()
    {
        var handler = Slow();
        using var provider = Build(handler);

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => Call(provider));
        await Assert.ThrowsAsync<DependencyUnavailableException>(() => Call(provider));

        var clock = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() => Call(provider));

        Assert.IsType<BrokenCircuitException>(exception.InnerException);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(150), $"took {clock.Elapsed}");
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Catalog_calls_carry_the_internal_api_key_and_no_user_token()
    {
        var handler = StubHttpHandler.Json(HttpStatusCode.OK, """{ "items": [] }""");
        using var provider = Build(handler, catalogKey: "test-internal-key");

        await Call(provider);

        var request = handler.Requests.Single().Request;
        Assert.Equal("test-internal-key", request.Headers.GetValues("X-Internal-Api-Key").Single());
        Assert.Null(request.Headers.Authorization);
    }

    private sealed class NoToken : IAccessTokenAccessor
    {
        public string? GetBearerToken() => null;
    }
}
