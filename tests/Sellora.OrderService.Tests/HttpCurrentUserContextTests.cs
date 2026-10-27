using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sellora.OrderService.Api.Identity;
using Sellora.OrderService.Application.Identity;

namespace Sellora.OrderService.Tests;

public sealed class HttpCurrentUserContextTests
{
    private static HttpCurrentUserContext With(CallerScope? scope, params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", "sub", "roles"))
        };

        if (scope is not null)
        {
            context.Items[CallerScopeMiddleware.ItemKey] = scope;
        }

        return new HttpCurrentUserContext(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void Subject_and_broadest_role_come_from_the_token()
    {
        var caller = With(null,
            new Claim("sub", "user-1"),
            new Claim("roles", "SalesRep"),
            new Claim("roles", "AreaManager"));

        Assert.Equal("user-1", caller.Subject);
        Assert.Equal("AreaManager", caller.Role);
    }

    [Fact]
    public void Hierarchy_ids_come_from_organization_not_from_claims()
    {
        var rep = Guid.NewGuid();
        var province = Guid.NewGuid();

        var caller = With(
            new CallerScope(rep, null, null, new[] { province }),
            new Claim("sub", "user-1"),
            new Claim("roles", "SalesRep"),
            // A stale or forged claim must be ignored.
            new Claim("salesRepId", Guid.NewGuid().ToString()));

        Assert.Equal(rep, caller.SalesRepId);
        Assert.Equal(new[] { province }, caller.ProvinceIds);
        Assert.Null(caller.AgencyId);
    }

    [Fact]
    public void No_resolved_scope_means_no_access()
    {
        var caller = With(null,
            new Claim("roles", "SalesRep"),
            new Claim("salesRepId", Guid.NewGuid().ToString()));

        Assert.Null(caller.SalesRepId);
        Assert.Empty(caller.ProvinceIds);
    }

    [Fact]
    public void Unknown_role_is_ignored()
    {
        Assert.Null(With(null, new Claim("roles", "Everyone")).Role);
    }
}
