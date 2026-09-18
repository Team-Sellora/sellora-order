using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sellora.OrderService.Api.Identity;

namespace Sellora.OrderService.Tests;

public sealed class HttpCurrentUserContextTests
{
    private static HttpCurrentUserContext With(params Claim[] claims) => new(
        new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", "sub", "roles"))
            }
        });

    [Fact]
    public void Reads_raw_wso2_claim_names_and_picks_broadest_role()
    {
        var province1 = Guid.NewGuid();
        var province2 = Guid.NewGuid();
        var rep = Guid.NewGuid();

        var caller = With(
            new Claim("sub", "user-1"),
            new Claim("roles", "SalesRep"),
            new Claim("roles", "AreaManager"),
            new Claim("salesRepId", rep.ToString()),
            new Claim("provinceId", province1.ToString()),
            new Claim("provinceId", province2.ToString()),
            new Claim("provinceId", "not-a-guid"));

        Assert.Equal("user-1", caller.Subject);
        Assert.Equal("AreaManager", caller.Role);
        Assert.Equal(rep, caller.SalesRepId);
        Assert.Equal(new[] { province1, province2 }, caller.ProvinceIds);
        Assert.Null(caller.AgencyId);
    }

    [Fact]
    public void Unknown_role_and_empty_guid_are_ignored()
    {
        var caller = With(new Claim("roles", "Everyone"), new Claim("shopId", Guid.Empty.ToString()));

        Assert.Null(caller.Role);
        Assert.Null(caller.ShopId);
    }
}
