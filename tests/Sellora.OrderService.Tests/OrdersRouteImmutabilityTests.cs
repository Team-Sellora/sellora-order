using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sellora.OrderService.Api.Controllers;

namespace Sellora.OrderService.Tests;

/// <summary>
/// US-E4-1a-T5: immutability is enforced by the absence of edit routes.
/// If someone adds PUT/PATCH to orders out of habit, CI fails here.
/// </summary>
public sealed class OrdersRouteImmutabilityTests
{
    private static readonly string[] EditVerbs = { "PUT", "PATCH" };

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

                    if (route.Contains("api/orders") &&
                        attribute.HttpMethods.Any(verb => EditVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase)))
                    {
                        offenders.Add($"{controller.Name}.{method.Name} -> {route}");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }
}
