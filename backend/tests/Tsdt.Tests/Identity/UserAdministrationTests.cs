using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Audit;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

public sealed class UserAdministrationTests
{
    [Fact]
    public async Task Only_an_authenticated_admin_can_list_users()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var anonymous = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/users")).StatusCode);

        using var managerClient = IdentityTestClient.Create(factory);
        await CreateUserAsync(factory, "manager@example.test", IdentityRoles.Manager);
        await IdentityTestClient.LoginAsync(managerClient, "manager@example.test", "Manager1!Password");
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.GetAsync("/api/admin/users")).StatusCode);

        using var userClient = IdentityTestClient.Create(factory);
        await CreateUserAsync(factory, "plain-user@example.test", IdentityRoles.User);
        await IdentityTestClient.LoginAsync(userClient, "plain-user@example.test", "Manager1!Password");
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/admin/users")).StatusCode);

        using var adminClient = await CreateReadyAdminClientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Admin_creates_user_with_one_role_secure_temporary_password_and_audit_record()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminClientAsync(factory);
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("João da Silva", "joao@example.test", IdentityRoles.User));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.NotNull(created);
        Assert.Equal("João da Silva", created.User.FullName);
        Assert.True(created.User.IsActive);
        Assert.True(created.User.MustChangePassword);
        Assert.Equal([IdentityRoles.User], created.User.Roles);
        Assert.True(created.TemporaryPassword.Length >= 12);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("joao@example.test");
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, created.TemporaryPassword));
        Assert.Equal([IdentityRoles.User], await users.GetRolesAsync(user));
        var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserAdministrationAuditRecords.SingleAsync();
        Assert.Equal("USER_CREATED", audit.Action);
        Assert.DoesNotContain(created.TemporaryPassword, $"{audit.Action}{audit.OldRole}{audit.NewRole}");

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Other", "joao@example.test", IdentityRoles.User));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var invalidRole = await SendAsync(client, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Other", "other@example.test", "OTHER"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRole.StatusCode);
    }

    [Fact]
    public async Task Admin_can_deactivate_and_activate_another_user_but_not_self_or_the_last_admin()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminClientAsync(factory);
        var target = await CreateUserAsync(factory, "user@example.test", IdentityRoles.User);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{target.Id}/deactivate")).StatusCode);
        Assert.False((await GetUserAsync(factory, target.Id)).IsActive);
        using var targetClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IdentityTestClient.LoginAsync(targetClient, "user@example.test", "Manager1!Password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{target.Id}/activate")).StatusCode);

        var admin = await GetUserByEmailAsync(factory, "admin@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{admin.Id}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"/api/admin/users/{admin.Id}/role", new ChangeUserRoleRequest(IdentityRoles.User))).StatusCode);
    }

    [Theory]
    [InlineData(IdentityRoles.User)]
    [InlineData(IdentityRoles.Manager)]
    public async Task Non_admin_roles_cannot_access_any_user_administration_operation(string role)
    {
        using var factory = new IdentityWebApplicationFactory();
        var target = await CreateUserAsync(factory, "target@example.test", IdentityRoles.User);
        await CreateUserAsync(factory, $"{role.ToLowerInvariant()}@example.test", role);
        using var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client, $"{role.ToLowerInvariant()}@example.test", "Manager1!Password");

        foreach (var operation in UserAdministrationMutations(target.Id))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, operation.Method, operation.Path, operation.Body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_clients_cannot_access_any_user_administration_operation()
    {
        using var factory = new IdentityWebApplicationFactory();
        var target = await CreateUserAsync(factory, "target@example.test", IdentityRoles.User);
        using var client = IdentityTestClient.Create(factory);

        foreach (var operation in UserAdministrationMutations(target.Id))
        {
            var request = new HttpRequestMessage(operation.Method, operation.Path) { Content = operation.Body is null ? null : JsonContent.Create(operation.Body) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task A_previously_authenticated_inactive_user_cannot_access_protected_resources()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var adminClient = await CreateReadyAdminClientAsync(factory);
        var target = await CreateUserAsync(factory, "active-user@example.test", IdentityRoles.User);
        using var targetClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(targetClient, target.Email!, "Manager1!Password")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(adminClient, HttpMethod.Post, $"/api/admin/users/{target.Id}/deactivate")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task All_user_administration_actions_create_safe_complete_audit_records()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminClientAsync(factory);
        var admin = await GetUserByEmailAsync(factory, "admin@example.test");
        var createdResponse = await SendAsync(client, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Audit Target", "audit-target@example.test", IdentityRoles.User));
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{created.User.Id}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{created.User.Id}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/admin/users/{created.User.Id}/role", new ChangeUserRoleRequest(IdentityRoles.Manager))).StatusCode);
        var reset = await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{created.User.Id}/reset-password");
        var resetPayload = await reset.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.NotNull(resetPayload);

        using var scope = factory.Services.CreateScope();
        var audits = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserAdministrationAuditRecords.OrderBy(record => record.Id).ToListAsync();
        Assert.Equal(["USER_CREATED", "USER_DEACTIVATED", "USER_ACTIVATED", "USER_ROLE_CHANGED", "USER_PASSWORD_RESET"], audits.Select(record => record.Action));
        foreach (var audit in audits)
        {
            Assert.Equal(admin.Id, audit.ActorUserId);
            Assert.Equal(created.User.Id, audit.TargetUserId);
            Assert.NotEqual(default, audit.OccurredAtUtc);
            Assert.DoesNotContain(created.TemporaryPassword, $"{audit.Action}{audit.OldRole}{audit.NewRole}");
            Assert.DoesNotContain(resetPayload.TemporaryPassword, $"{audit.Action}{audit.OldRole}{audit.NewRole}");
            Assert.DoesNotContain("csrf", $"{audit.Action}{audit.OldRole}{audit.NewRole}", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cookie", $"{audit.Action}{audit.OldRole}{audit.NewRole}", StringComparison.OrdinalIgnoreCase);
        }
        var roleChange = Assert.Single(audits, record => record.Action == "USER_ROLE_CHANGED");
        Assert.Equal(IdentityRoles.User, roleChange.OldRole);
        Assert.Equal(IdentityRoles.Manager, roleChange.NewRole);
    }

    [Fact]
    public async Task Admin_can_change_roles_and_reset_password_which_invalidates_existing_sessions()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminClientAsync(factory);
        var target = await CreateUserAsync(factory, "target@example.test", IdentityRoles.User);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/admin/users/{target.Id}/role", new ChangeUserRoleRequest(IdentityRoles.Manager))).StatusCode);
        Assert.Equal([IdentityRoles.Manager], await GetRolesAsync(factory, target));

        using var targetClient = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(targetClient, "target@example.test", "Manager1!Password");
        var reset = await SendAsync(client, HttpMethod.Post, $"/api/admin/users/{target.Id}/reset-password");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var payload = await reset.Content.ReadFromJsonAsync<CreateUserResponse>();
        Assert.NotNull(payload);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(targetClient, "target@example.test", payload.TemporaryPassword)).StatusCode);
        Assert.True((await GetUserAsync(factory, target.Id)).MustChangePassword);
    }

    private static async Task<HttpClient> CreateReadyAdminClientAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        var response = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ApplicationUser> CreateUserAsync(IdentityWebApplicationFactory factory, string email, string role)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = email, UserName = email, Email = email, EmailConfirmed = true, IsActive = true };
        Assert.True((await users.CreateAsync(user, "Manager1!Password")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static IEnumerable<(HttpMethod Method, string Path, object? Body)> UserAdministrationMutations(string targetId)
    {
        yield return (HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Blocked User", "blocked@example.test", IdentityRoles.User));
        yield return (HttpMethod.Put, $"/api/admin/users/{targetId}/role", new ChangeUserRoleRequest(IdentityRoles.Manager));
        yield return (HttpMethod.Post, $"/api/admin/users/{targetId}/activate", null);
        yield return (HttpMethod.Post, $"/api/admin/users/{targetId}/deactivate", null);
        yield return (HttpMethod.Post, $"/api/admin/users/{targetId}/reset-password", null);
    }

    private static async Task<ApplicationUser> GetUserAsync(IdentityWebApplicationFactory factory, string id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(user => user.Id == id);
    }

    private static async Task<ApplicationUser> GetUserByEmailAsync(IdentityWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
    }

    private static async Task<IList<string>> GetRolesAsync(IdentityWebApplicationFactory factory, ApplicationUser user)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(user);
    }
}
