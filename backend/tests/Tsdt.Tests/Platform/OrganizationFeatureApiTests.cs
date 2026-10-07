using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Integration")]
public sealed class OrganizationFeatureApiTests
{
    [Fact]
    public async Task Platform_administrator_manages_only_catalog_features_and_dependency_errors_are_audited()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var organization = await CreateOrganizationAsync(factory);
        var endpoint = $"/api/platform/organizations/{organization.Id}/features";

        var initial = await platform.Client.GetFromJsonAsync<OrganizationFeatureResponse[]>(endpoint);
        Assert.NotNull(initial);
        Assert.Equal(7, initial.Length);
        Assert.All(initial, item => Assert.False(item.Enabled));

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(platform.Client, HttpMethod.Post, $"{endpoint}/{FeatureCatalog.Quotes}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(platform.Client, HttpMethod.Post, $"{endpoint}/invented-feature")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platform.Client, HttpMethod.Post, $"{endpoint}/{FeatureCatalog.Customers}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platform.Client, HttpMethod.Post, $"{endpoint}/{FeatureCatalog.Documents}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(platform.Client, HttpMethod.Delete, $"{endpoint}/{FeatureCatalog.Customers}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platform.Client, HttpMethod.Delete, $"{endpoint}/{FeatureCatalog.Documents}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await db.OrganizationAuditRecords.Where(item => item.OrganizationId == organization.Id).ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.Contains(events, item => item.Action == "ORGANIZATION_FEATURE_ENABLED" && item.FeatureKey == FeatureCatalog.Customers);
        Assert.Contains(events, item => item.Action == "ORGANIZATION_FEATURE_ENABLED" && item.FeatureKey == FeatureCatalog.Documents);
        Assert.Contains(events, item => item.Action == "ORGANIZATION_FEATURE_DISABLED" && item.FeatureKey == FeatureCatalog.Documents);
        Assert.DoesNotContain(events, item => item.FeatureKey == "invented-feature");
    }

    [Fact]
    public async Task Only_platform_administrators_can_manage_organization_features()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organization = await CreateOrganizationAsync(factory);
        var endpoint = $"/api/platform/organizations/{organization.Id}/features";
        using var anonymous = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Post, $"{endpoint}/{FeatureCatalog.Customers}")).StatusCode);

        foreach (var role in IdentityRoles.All)
        {
            using var tenant = await CreateTenantClientAsync(factory, role, organization.Id);
            Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync(endpoint)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(tenant, HttpMethod.Post, $"{endpoint}/{FeatureCatalog.Customers}")).StatusCode);
        }

        using var platform = await CreatePlatformClientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await platform.Client.GetAsync(endpoint)).StatusCode);
    }

    [Fact]
    public async Task Tenant_module_route_groups_enforce_entitlements_and_agenda_remains_separate_from_work_orders()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organization = await CreateOrganizationAsync(factory);
        using var platform = await CreatePlatformClientAsync(factory);
        using var tenant = await CreateTenantClientAsync(factory, IdentityRoles.Admin, organization.Id);
        var customerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var representativePaths = new[]
        {
            "/api/customers",
            "/api/services",
            "/api/service-lines",
            "/api/quotes",
            "/api/contracts",
            "/api/work-orders",
            "/api/work-orders/agenda?from=2026-10-01T00:00:00Z&to=2026-10-02T00:00:00Z",
            $"/api/customers/{customerId}/documents",
            $"/api/documents/{documentId}/download"
        };
        foreach (var path in representativePaths)
            Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync(path)).StatusCode);

        var platformFeatures = $"/api/platform/organizations/{organization.Id}/features";
        foreach (var feature in new[] { FeatureCatalog.Customers, FeatureCatalog.Services, FeatureCatalog.Quotes, FeatureCatalog.Contracts, FeatureCatalog.WorkOrders })
            Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platform.Client, HttpMethod.Post, $"{platformFeatures}/{feature}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/api/work-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync("/api/work-orders/agenda?from=2026-10-01T00:00:00Z&to=2026-10-02T00:00:00Z")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platform.Client, HttpMethod.Post, $"{platformFeatures}/{FeatureCatalog.Schedule}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/api/work-orders/agenda?from=2026-10-01T00:00:00Z&to=2026-10-02T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync($"/api/customers/{customerId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync($"/api/documents/{documentId}/download")).StatusCode);

        var currentUser = await tenant.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.NotNull(currentUser);
        Assert.Contains(FeatureCatalog.Customers, currentUser.Features);
        Assert.Contains(FeatureCatalog.Schedule, currentUser.Features);
        Assert.DoesNotContain(FeatureCatalog.Documents, currentUser.Features);
    }

    private static async Task<PlatformClient> CreatePlatformClientAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Platform Operator", UserName = "features-platform@example.test", Email = "features-platform@example.test", EmailConfirmed = true, IsActive = true, IsPlatformAdministrator = true };
        Assert.True((await users.CreateAsync(user, "Platform1!Password")).Succeeded);
        var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client, user.Email!, "Platform1!Password")).StatusCode);
        return new PlatformClient(client);
    }

    private static async Task<HttpClient> CreateTenantClientAsync(IdentityWebApplicationFactory factory, string role, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"features-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { FullName = $"Tenant {role}", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, OrganizationId = organizationId };
        Assert.True((await users.CreateAsync(user, "Tenant1!Password")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client, email, "Tenant1!Password")).StatusCode);
        return client;
    }

    private static async Task<Organization> CreateOrganizationAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var organization = new Organization { Name = $"Feature Organization {suffix}", Slug = $"feature-organization-{suffix}" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path) { Content = method == HttpMethod.Post ? JsonContent.Create(new { }) : null };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private sealed record PlatformClient(HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
