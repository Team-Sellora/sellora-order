using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sellora.OrderService.Api.Identity;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Tests;

/// <summary>Scope resolution from Organization, instead of token claims.</summary>
public sealed class CallerScopeMiddlewareTests
{
    private readonly FakeOrganization _organization = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private async Task<(HttpContext Context, bool NextCalled)> RunAsync(string role, string sub = "user-1")
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("sub", sub), new Claim("roles", role), new Claim("companyId", "c-1") },
                "Test", "sub", "roles"))
        };
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CallerScopeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(
            context, _organization, _cache,
            Options.Create(new CallerScopeOptions()), NullLogger<CallerScopeMiddleware>.Instance);

        return (context, nextCalled);
    }

    [Fact]
    public async Task Resolves_the_scope_and_caches_it_per_user()
    {
        var rep = Guid.NewGuid();
        _organization.Scope = new CallerScope(rep, Guid.NewGuid(), null, Array.Empty<Guid>());

        var (first, _) = await RunAsync("SalesRep");
        var (second, nextCalled) = await RunAsync("SalesRep");

        Assert.True(nextCalled);
        Assert.Equal(rep, ((CallerScope)first.Items[CallerScopeMiddleware.ItemKey]!).SalesRepId);
        Assert.Equal(rep, ((CallerScope)second.Items[CallerScopeMiddleware.ItemKey]!).SalesRepId);
        Assert.Equal(1, _organization.ScopeCalls);
    }

    [Fact]
    public async Task No_profile_resolves_to_an_empty_scope()
    {
        _organization.Scope = null;

        var (context, nextCalled) = await RunAsync("SalesRep");

        Assert.True(nextCalled);
        Assert.Same(CallerScope.Empty, context.Items[CallerScopeMiddleware.ItemKey]);
    }

    [Fact]
    public async Task Company_admins_skip_the_lookup_and_keep_working_when_organization_is_down()
    {
        _organization.Unavailable = Dependency.Organization;

        var (_, nextCalled) = await RunAsync("CompanyAdmin");

        Assert.True(nextCalled);
        Assert.Equal(0, _organization.ScopeCalls);
    }

    [Fact]
    public async Task Organization_down_is_503_for_scoped_roles()
    {
        _organization.Unavailable = Dependency.Organization;

        var (context, nextCalled) = await RunAsync("SalesRep");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("30", context.Response.Headers.RetryAfter.ToString());
    }
}
