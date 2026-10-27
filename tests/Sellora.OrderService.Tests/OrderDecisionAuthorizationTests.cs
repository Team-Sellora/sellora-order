using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Sellora.OrderService.Api.Authorization;
using Sellora.OrderService.Api.Controllers;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-5-Q3 / DoD 1: only a Shop Owner token reaches the cancellation
/// endpoint and only an Agency Operator token reaches approval, whatever
/// the window. Evaluates the real policies the attributes name.
/// </summary>
public sealed class OrderDecisionAuthorizationTests
{
    private static readonly IAuthorizationService Authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorizationCore(options => options.AddSelloraRolePolicies())
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static string PolicyOf(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy!;

    private static async Task<bool> AllowedAsync(string policy, string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("roles", role) }, "test"));
        return (await Authorization.AuthorizeAsync(user, null, policy)).Succeeded;
    }

    [Theory]
    [InlineData("ShopOwner", true)]
    [InlineData("SalesRep", false)]
    [InlineData("AgencyOperator", false)]
    [InlineData("AreaManager", false)]
    [InlineData("CompanyAdmin", false)]
    public async Task Only_a_shop_owner_can_call_cancellation(string role, bool allowed)
    {
        var policy = PolicyOf(typeof(OrderCancellationController), nameof(OrderCancellationController.Cancel));

        Assert.Equal(allowed, await AllowedAsync(policy, role));
    }

    [Theory]
    [InlineData("AgencyOperator", true)]
    [InlineData("ShopOwner", false)]
    [InlineData("SalesRep", false)]
    [InlineData("AreaManager", false)]
    [InlineData("CompanyAdmin", false)]
    public async Task Only_an_agency_operator_can_decide_approval(string role, bool allowed)
    {
        var policy = PolicyOf(typeof(OrderApprovalController), nameof(OrderApprovalController.Decide));

        Assert.Equal(allowed, await AllowedAsync(policy, role));
    }
}
