using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Controllers;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-6 / DoD 1: only a rep declares, only an agency operator accepts.</summary>
public sealed class VanReturnAuthorizationTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorizationCore(options => options.AddSelloraRolePolicies())
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static async Task<bool> AllowedAsync(string action, string role)
    {
        var policy = typeof(VanReturnsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("roles", role) }, "test"));
        return (await Authorization.AuthorizeAsync(user, null, policy)).Succeeded;
    }

    [Theory]
    [InlineData("SalesRep", true)]
    [InlineData("AgencyOperator", false)]
    [InlineData("ShopOwner", false)]
    [InlineData("CompanyAdmin", false)]
    public async Task Only_a_sales_rep_can_declare(string role, bool allowed) =>
        Assert.Equal(allowed, await AllowedAsync(nameof(VanReturnsController.Declare), role));

    [Theory]
    [InlineData("AgencyOperator", true)]
    [InlineData("SalesRep", false)]
    [InlineData("ShopOwner", false)]
    [InlineData("CompanyAdmin", false)]
    public async Task Only_an_agency_operator_can_accept(string role, bool allowed) =>
        Assert.Equal(allowed, await AllowedAsync(nameof(VanReturnsController.Accept), role));

    [Fact]
    public void Van_returns_do_not_live_under_the_immutable_orders_routes()
    {
        var route = typeof(VanReturnsController).GetCustomAttribute<Microsoft.AspNetCore.Mvc.RouteAttribute>()!.Template;

        Assert.Equal("api/van-returns", route);
    }
}
