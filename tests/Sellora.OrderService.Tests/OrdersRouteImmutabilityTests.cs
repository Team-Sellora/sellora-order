using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sellora.OrderService.Api.Controllers;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-1a-T5: immutability is enforced by the absence of edit routes.
/// If someone adds PUT/PATCH to orders out of habit, CI fails here.
///
/// US-E4-5 adds exactly one PUT, the agency's approval decision. It sets a
/// decision on the order and never touches lines or totals, so it is
/// allow-listed by exact route; any other PUT, and every PATCH, still fails.
/// </summary>
public sealed class OrdersRouteImmutabilityTests
{
    private static readonly string[] EditVerbs = { "PUT", "PATCH" };

    private static readonly string[] AllowedPutRoutes =
    {
        "api/orders/{orderid:guid}/approval"
    };

    [Fact]
    public void OrdersController_exposes_no_put_or_patch_action()
    {
        var offenders = typeof(OrdersController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Any(attribute => attribute.HttpMethods.Any(verb => EditVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase))))
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_controller_in_the_api_maps_put_or_patch_onto_orders()
    {
        var controllers = typeof(OrdersController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        var offenders = new List<string>();

        foreach (var controller in controllers)
        {
            var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;

            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var route = $"{classRoute}/{attribute.Template}".ToLowerInvariant();

                    var isAllowedPut =
                        AllowedPutRoutes.Contains(route) &&
                        attribute.HttpMethods.All(verb => string.Equals(verb, "PUT", StringComparison.OrdinalIgnoreCase));

                    if (route.Contains("api/orders") && !isAllowedPut &&
                        attribute.HttpMethods.Any(verb => EditVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase)))
                    {
                        offenders.Add($"{controller.Name}.{method.Name} -> {route}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_only_put_on_orders_is_the_approval_decision_and_it_changes_no_lines()
    {
        var puts = typeof(OrdersController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                    .Where(attribute => attribute.HttpMethods.Contains("PUT", StringComparer.OrdinalIgnoreCase))
                    .Select(attribute => $"{controller.GetCustomAttribute<RouteAttribute>()?.Template}/{attribute.Template}".ToLowerInvariant())))
            .Where(route => route.Contains("api/orders"))
            .ToList();

        Assert.Equal(AllowedPutRoutes, puts);

        // The approval body can only carry a decision and a reason.
        var bodyProperties = typeof(Sellora.OrderService.Api.Contracts.ApprovalRequestBody)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name);

        Assert.Equal(new[] { "Decision", "Reason" }, bodyProperties);
    }
}
