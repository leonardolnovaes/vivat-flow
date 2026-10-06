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
public sealed class TenantAdministratorApiTests
{
    [Fact]
    public async Task Platform_administrator_creates_and_lists_an_admin_only_for_the_selected_organization_and_records_audit()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var selectedOrganization = await CreateOrganizationAsync(factory);
        var otherOrganization = await CreateOrganizationAsync(factory);
        var otherAdmin = await CreateTenantUserAsync(factory, "other-admin@example.test", IdentityRoles.Admin, otherOrganization.Id);

        var createdResponse = await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{selectedOrganization.Id}/administrators", new CreateTenantAdministratorRequest("João Silva", "  JOAO@Empresa.com "));

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.NotNull(created);
        Assert.Equal("joao@empresa.com", created.User.Email);
        Assert.Equal([IdentityRoles.Admin], created.User.Roles);
        Assert.True(created.User.IsActive);
        Assert.True(created.User.MustChangePassword);
        Assert.True(created.TemporaryPassword.Length >= 12);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var createdUser = await userManager.FindByEmailAsync("joao@empresa.com");
        Assert.NotNull(createdUser);
        Assert.Equal(selectedOrganization.Id, createdUser.OrganizationId);
        Assert.True(await userManager.CheckPasswordAsync(createdUser, created.TemporaryPassword));
        Assert.Equal([IdentityRoles.Admin], await userManager.GetRolesAsync(createdUser));

        var listedResponse = await platform.Client.GetAsync($"/api/platform/organizations/{selectedOrganization.Id}/administrators");
        Assert.Equal(HttpStatusCode.OK, listedResponse.StatusCode);
        var administrators = await listedResponse.Content.ReadFromJsonAsync<TenantAdministratorResponse[]>();
        var listedAdmin = Assert.Single(administrators!);
        Assert.Equal(created.User.Id, listedAdmin.Id);
        Assert.DoesNotContain(administrators!, item => item.Id == otherAdmin.Id);

        var audit = await db.OrganizationAuditRecords.SingleAsync(record => record.Action == "TENANT_ADMINISTRATOR_CREATED");
        Assert.Equal(selectedOrganization.Id, audit.OrganizationId);
        Assert.Equal(platform.User.Id, audit.ActorUserId);
        Assert.Equal(created.User.Id, audit.TargetUserId);
        Assert.NotEqual(default, audit.OccurredAtUtc);
    }

    [Fact]
    public async Task Provisioned_admin_uses_the_existing_forced_password_change_and_tenant_user_management_flow()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var organization = await CreateOrganizationAsync(factory);
        var response = await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{organization.Id}/administrators", new CreateTenantAdministratorRequest("Initial Admin", "initial-admin@example.test"));
        var created = await response.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        using var tenant = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(tenant, created.User.Email, created.TemporaryPassword)).StatusCode);
        var session = await tenant.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.True(session!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.GetAsync("/api/admin/users")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(tenant, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest(created.TemporaryPassword, "Permanent1!Password"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenant.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Only_platform_administrators_can_read_or_create_tenant_administrators()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organization = await CreateOrganizationAsync(factory);
        var endpoint = $"/api/platform/organizations/{organization.Id}/administrators";
        using var anonymous = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, HttpMethod.Post, endpoint, new CreateTenantAdministratorRequest("New Admin", "new-admin@example.test"))).StatusCode);

        foreach (var role in IdentityRoles.All)
        {
            using var tenant = await CreateTenantClientAsync(factory, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await tenant.Client.GetAsync(endpoint)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(tenant.Client, HttpMethod.Post, endpoint, new CreateTenantAdministratorRequest("New Admin", $"{role.ToLowerInvariant()}-admin@example.test"))).StatusCode);
        }

        using var platform = await CreatePlatformClientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await platform.Client.GetAsync(endpoint)).StatusCode);
    }

    [Theory]
    [InlineData(OrganizationStatus.Active, HttpStatusCode.Created)]
    [InlineData(OrganizationStatus.Suspended, HttpStatusCode.Conflict)]
    [InlineData(OrganizationStatus.Deactivated, HttpStatusCode.Conflict)]
    public async Task Provisioning_respects_organization_status(OrganizationStatus status, HttpStatusCode expected)
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var organization = await CreateOrganizationAsync(factory, status);

        var response = await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{organization.Id}/administrators", new CreateTenantAdministratorRequest("Status Admin", $"{status.ToString().ToLowerInvariant()}@example.test"));

        Assert.Equal(expected, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdUsers = await db.Users.CountAsync(user => user.OrganizationId == organization.Id && user.Email != null && user.Email.EndsWith("@example.test"));
        Assert.Equal(status == OrganizationStatus.Active ? 1 : 0, createdUsers);
    }

    [Fact]
    public async Task Provisioning_rejects_unknown_organizations_and_duplicate_emails_without_leaking_existing_tenant_details()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var organization = await CreateOrganizationAsync(factory);
        var otherOrganization = await CreateOrganizationAsync(factory);
        await CreateTenantUserAsync(factory, "already-used@example.test", IdentityRoles.User, otherOrganization.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{Guid.NewGuid()}/administrators", new CreateTenantAdministratorRequest("Unknown", "unknown@example.test"))).StatusCode);
        var duplicate = await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{organization.Id}/administrators", new CreateTenantAdministratorRequest("Duplicate", "already-used@example.test"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var body = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("Já existe um usuário com este e-mail.", body);
        Assert.DoesNotContain(otherOrganization.Id.ToString(), body);
        Assert.DoesNotContain("USER", body);
    }

    [Fact]
    public async Task Organization_audit_endpoint_returns_only_the_selected_organizations_events()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var platform = await CreatePlatformClientAsync(factory);
        var organization = await CreateOrganizationAsync(factory);
        var otherOrganization = await CreateOrganizationAsync(factory);
        var createdResponse = await SendAsync(platform.Client, HttpMethod.Post, $"/api/platform/organizations/{organization.Id}/administrators", new CreateTenantAdministratorRequest("Audit Target", "audit-target@example.test"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        OrganizationEndpointsAudit(factory, otherOrganization.Id, platform.User.Id);

        var response = await platform.Client.GetFromJsonAsync<OrganizationAuditResponse[]>($"/api/platform/organizations/{organization.Id}/audit");

        var entry = Assert.Single(response!);
        Assert.Equal("TENANT_ADMINISTRATOR_CREATED", entry.Action);
        Assert.Equal(platform.User.FullName, entry.ActorName);
        Assert.Equal("Audit Target", entry.TargetUserName);
        Assert.Equal("audit-target@example.test", entry.TargetUserEmail);
        Assert.NotEqual(default, entry.OccurredAtUtc);
        Assert.DoesNotContain(response!, item => item.Action == "ORGANIZATION_UPDATED");
    }

    private static async Task<PlatformClient> CreatePlatformClientAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Platform Operator", UserName = "platform@example.test", Email = "platform@example.test", EmailConfirmed = true, IsActive = true, IsPlatformAdministrator = true };
        Assert.True((await users.CreateAsync(user, "Platform1!Password")).Succeeded);
        var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client, user.Email!, "Platform1!Password")).StatusCode);
        return new PlatformClient(client, user);
    }

    private static async Task<TenantClient> CreateTenantClientAsync(IdentityWebApplicationFactory factory, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = await db.Organizations.Select(item => item.Id).FirstAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = $"Tenant {role}", UserName = $"tenant-{role.ToLowerInvariant()}@example.test", Email = $"tenant-{role.ToLowerInvariant()}@example.test", EmailConfirmed = true, IsActive = true, OrganizationId = organizationId };
        Assert.True((await users.CreateAsync(user, "Tenant1!Password")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client, user.Email!, "Tenant1!Password")).StatusCode);
        return new TenantClient(client);
    }

    private static async Task<Organization> CreateOrganizationAsync(IdentityWebApplicationFactory factory, OrganizationStatus status = OrganizationStatus.Active)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var organization = new Organization { Name = $"Organization {suffix}", Slug = $"organization-{suffix}", Status = status };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization;
    }

    private static async Task<ApplicationUser> CreateTenantUserAsync(IdentityWebApplicationFactory factory, string email, string role, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = email, UserName = email, Email = email, EmailConfirmed = true, IsActive = true, OrganizationId = organizationId };
        Assert.True((await users.CreateAsync(user, "Tenant1!Password")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static void OrganizationEndpointsAudit(IdentityWebApplicationFactory factory, Guid organizationId, string actorId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OrganizationAuditRecords.Add(new OrganizationAuditRecord { OrganizationId = organizationId, ActorUserId = actorId, Action = "ORGANIZATION_UPDATED" });
        db.SaveChanges();
    }

    private sealed record PlatformClient(HttpClient Client, ApplicationUser User) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record TenantClient(HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}
