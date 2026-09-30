using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Services;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Services;

[Trait("Category", "Unit")]
public sealed class ServiceLineApiTests
{
    [Fact]
    public async Task Tenant_service_lines_are_isolated_and_support_multiple_enabled_lines()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var tenantA = await ReadyTenantAdminAsync(factory);
        var organizationB = await CreateOrganizationAsync(factory, "Organization B");
        var tenantBUser = await CreateTenantUserAsync(factory, organizationB.Id, "service-line-b@example.test", IdentityRoles.Manager);
        using var tenantB = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(tenantB, tenantBUser.Email!, "Userpass1!Password")).EnsureSuccessStatusCode();
        var (lineA, lineB, inactive) = await CreateLinesAsync(factory);
        var organizationAId = await BootstrapOrganizationIdAsync(factory);
        await EnableAsync(factory, organizationAId, lineA.Id);
        await EnableAsync(factory, organizationAId, lineB.Id);
        await EnableAsync(factory, organizationAId, inactive.Id);
        await EnableAsync(factory, organizationB.Id, lineB.Id);

        var linesA = await tenantA.GetFromJsonAsync<List<TenantServiceLineResponse>>("/api/service-lines");
        Assert.Equal([lineA.Id, lineB.Id], linesA!.Select(line => line.Id));
        var linesB = await tenantB.GetFromJsonAsync<List<TenantServiceLineResponse>>("/api/service-lines");
        Assert.Equal([lineB.Id], linesB!.Select(line => line.Id));
    }

    [Fact]
    public async Task Service_create_and_update_require_an_active_enabled_line_for_the_current_tenant()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var tenantA = await ReadyTenantAdminAsync(factory);
        var organizationAId = await BootstrapOrganizationIdAsync(factory);
        var organizationB = await CreateOrganizationAsync(factory, "Organization B");
        var tenantBUser = await CreateTenantUserAsync(factory, organizationB.Id, "service-line-update-b@example.test", IdentityRoles.Manager);
        var (lineA, lineB, inactive) = await CreateLinesAsync(factory);
        await EnableAsync(factory, organizationAId, lineA.Id);
        await EnableAsync(factory, organizationB.Id, lineB.Id);

        var created = await SendAsync(tenantA, HttpMethod.Post, "/api/services", new CreateServiceRequest("SST-001", "Avaliação", null, null, lineA.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var service = (await created.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
        Assert.Equal(lineA.Name, service.ServiceLineName);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(tenantA, HttpMethod.Post, "/api/services", new CreateServiceRequest("SST-000", "Inexistente", null, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(tenantA, HttpMethod.Post, "/api/services", new CreateServiceRequest("SST-002", "Bloqueado", null, null, lineB.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(tenantA, HttpMethod.Post, "/api/services", new CreateServiceRequest("SST-003", "Inativo", null, null, inactive.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(tenantA, HttpMethod.Put, $"/api/services/{service.Id}", new UpdateServiceRequest(service.Code, service.Name, null, null, lineB.Id, service.Version))).StatusCode);
        using var tenantB = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(tenantB, tenantBUser.Email!, "Userpass1!Password")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(tenantB, HttpMethod.Put, $"/api/services/{service.Id}", new UpdateServiceRequest(service.Code, service.Name, null, null, lineB.Id, service.Version))).StatusCode);
    }

    [Fact]
    public async Task Only_platform_administrators_manage_organization_service_lines()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var tenantAdmin = await ReadyTenantAdminAsync(factory);
        var organizationId = await BootstrapOrganizationIdAsync(factory);
        var (lineA, _, _) = await CreateLinesAsync(factory);
        var platform = await CreatePlatformAdministratorAsync(factory);
        using var platformClient = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(platformClient, platform.Email!, "Platform1!Password")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(tenantAdmin, HttpMethod.Post, $"/api/platform/organizations/{organizationId}/service-lines/{lineA.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platformClient, HttpMethod.Post, $"/api/platform/organizations/{organizationId}/service-lines/{lineA.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(platformClient, HttpMethod.Post, $"/api/platform/organizations/{organizationId}/service-lines/{lineA.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platformClient, HttpMethod.Delete, $"/api/platform/organizations/{organizationId}/service-lines/{lineA.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platformClient.GetAsync("/api/services")).StatusCode);
    }

    [Fact]
    public async Task Tenant_roles_and_anonymous_users_cannot_read_platform_service_line_configuration()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organizationId = await BootstrapOrganizationIdAsync(factory);
        var manager = await CreateTenantUserAsync(factory, organizationId, "service-line-manager@example.test", IdentityRoles.Manager);
        var user = await CreateTenantUserAsync(factory, organizationId, "service-line-user@example.test", IdentityRoles.User);
        using var managerClient = IdentityTestClient.Create(factory);
        using var userClient = IdentityTestClient.Create(factory);
        using var anonymousClient = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(managerClient, manager.Email!, "Userpass1!Password")).EnsureSuccessStatusCode();
        (await IdentityTestClient.LoginAsync(userClient, user.Email!, "Userpass1!Password")).EnsureSuccessStatusCode();

        var path = $"/api/platform/organizations/{organizationId}/service-lines";
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Platform_reads_global_lines_with_the_organization_enablement_state()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organizationId = await BootstrapOrganizationIdAsync(factory);
        var (enabled, disabled, inactive) = await CreateLinesAsync(factory);
        await EnableAsync(factory, organizationId, enabled.Id);
        var platform = await CreatePlatformAdministratorAsync(factory);
        using var client = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(client, platform.Email!, "Platform1!Password")).EnsureSuccessStatusCode();

        var response = await client.GetFromJsonAsync<List<PlatformOrganizationServiceLineResponse>>($"/api/platform/organizations/{organizationId}/service-lines");
        Assert.NotNull(response);

        Assert.True(response.Single(item => item.Id == enabled.Id).IsEnabled);
        Assert.False(response.Single(item => item.Id == disabled.Id).IsEnabled);
        Assert.False(response.Single(item => item.Id == inactive.Id).IsActive);
    }

    [Fact]
    public async Task Disabling_a_line_keeps_existing_services_intact()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var tenant = await ReadyTenantAdminAsync(factory);
        var organizationId = await BootstrapOrganizationIdAsync(factory);
        var (line, _, _) = await CreateLinesAsync(factory);
        await EnableAsync(factory, organizationId, line.Id);
        var created = await SendAsync(tenant, HttpMethod.Post, "/api/services", new CreateServiceRequest("LINE-001", "Serviço existente", null, null, line.Id));
        var service = (await created.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
        var platform = await CreatePlatformAdministratorAsync(factory);
        using var platformClient = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(platformClient, platform.Email!, "Platform1!Password")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(platformClient, HttpMethod.Delete, $"/api/platform/organizations/{organizationId}/service-lines/{line.Id}", new { })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.Services.SingleAsync(item => item.Id == service.Id);
        Assert.Equal(line.Id, persisted.ServiceLineId);
    }

    private static async Task<HttpClient> ReadyTenantAdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(client)).EnsureSuccessStatusCode();
        (await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<Guid> BootstrapOrganizationIdAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(user => user.Email == "admin@example.test").Select(user => user.OrganizationId).SingleAsync() ?? throw new InvalidOperationException();
    }
    private static async Task<Organization> CreateOrganizationAsync(IdentityWebApplicationFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organization = new Organization { Name = name, Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}" }; db.Organizations.Add(organization); await db.SaveChangesAsync(); return organization;
    }
    private static async Task<ApplicationUser> CreateTenantUserAsync(IdentityWebApplicationFactory factory, Guid organizationId, string email, string role)
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Tenant Manager", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, OrganizationId = organizationId };
        Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded); return user;
    }
    private static async Task<ApplicationUser> CreatePlatformAdministratorAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Platform Administrator", UserName = "platform@example.test", Email = "platform@example.test", EmailConfirmed = true, IsActive = true, IsPlatformAdministrator = true };
        Assert.True((await manager.CreateAsync(user, "Platform1!Password")).Succeeded); return user;
    }
    private static async Task<(ServiceLine ActiveA, ServiceLine ActiveB, ServiceLine Inactive)> CreateLinesAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var activeA = new ServiceLine { Code = $"A-{Guid.NewGuid():N}", Name = "Linha A" }; var activeB = new ServiceLine { Code = $"B-{Guid.NewGuid():N}", Name = "Linha B" }; var inactive = new ServiceLine { Code = $"I-{Guid.NewGuid():N}", Name = "Linha inativa", IsActive = false };
        db.ServiceLines.AddRange(activeA, activeB, inactive); await db.SaveChangesAsync(); return (activeA, activeB, inactive);
    }
    private static async Task EnableAsync(IdentityWebApplicationFactory factory, Guid organizationId, Guid lineId)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.OrganizationServiceLines.Add(new OrganizationServiceLine { OrganizationId = organizationId, ServiceLineId = lineId }); await db.SaveChangesAsync();
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request);
    }
}
