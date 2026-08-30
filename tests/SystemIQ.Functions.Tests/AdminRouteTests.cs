using Microsoft.Azure.Functions.Worker;
using SystemIQ.Functions.Functions;

namespace SystemIQ.Functions.Tests;

public sealed class AdminRouteTests
{
    [Fact]
    public void CuratorEndpointsDoNotUseTheReservedAdminPrefix()
    {
        var routes = typeof(AdminFunctions)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .SelectMany(parameter => parameter.GetCustomAttributes(typeof(HttpTriggerAttribute), false))
            .Cast<HttpTriggerAttribute>()
            .Select(trigger => trigger.Route)
            .Where(route => route is not null)
            .ToArray();

        Assert.NotEmpty(routes);
        Assert.All(
            routes,
            route => Assert.False(
                route!.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                route.StartsWith("admin/", StringComparison.OrdinalIgnoreCase),
                $"The route '{route}' uses Azure Functions' reserved admin prefix."));
    }
}
