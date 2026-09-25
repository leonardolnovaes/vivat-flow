using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

public sealed class LocalAdminResetSecurityTests
{
    [Theory]
    [InlineData("Production", "admin@example.test")]
    [InlineData("Staging", "admin@example.test")]
    [InlineData(null, "admin@example.test")]
    [InlineData("Development", null)]
    [InlineData("Development", "  ")]
    public void Recovery_rejects_non_development_or_missing_configured_identity(string? environment, string? email)
    {
        Assert.False(LocalAdminResetPolicy.AllowsEnvironment(environment, email));
    }

    [Fact]
    public void Recovery_accepts_only_active_admin_target()
    {
        Assert.True(LocalAdminResetPolicy.AllowsEnvironment("Development", "admin@example.test"));
        Assert.True(LocalAdminResetPolicy.AllowsTarget(isActive: true, isAdmin: true));
        Assert.False(LocalAdminResetPolicy.AllowsTarget(isActive: false, isAdmin: true));
        Assert.False(LocalAdminResetPolicy.AllowsTarget(isActive: true, isAdmin: false)); // USER and MANAGER
    }

    [Fact]
    public void Recovery_has_no_http_route()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = factory.CreateClient();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty).ToList();
        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, route => route.Contains("local-admin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("recover", StringComparison.OrdinalIgnoreCase));
    }
}
