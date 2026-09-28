using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;
using Tsdt.Api.Services;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Services;

[Trait("Category", "Integration")]
public sealed class ServiceApiTests
{
    [Fact]
    public async Task Service_creation_normalizes_validates_and_audits()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var service = await CreateServiceAsync(client, new CreateServiceRequest(" pgr-001 ", "  PGR  ", "  Programa  ", 250.50m));
        Assert.Equal("PGR-001", service.Code); Assert.Equal("PGR", service.Name); Assert.Equal("Programa", service.Description); Assert.Equal(250.50m, service.BasePrice);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, "/api/services", new CreateServiceRequest("pgr-001", "Outro", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, "/api/services", new CreateServiceRequest("?", "Outro", null, null))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var audit = Assert.Single(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ServiceAuditRecords);
        Assert.Equal("SERVICE_CREATED", audit.Action);
    }

    [Fact]
    public async Task Service_status_requires_current_version_even_when_target_matches()
    {
        using var factory = new IdentityWebApplicationFactory(); using var client = await CreateReadyAdminAsync(factory);
        var created = await CreateServiceAsync(client);
        var updatedResponse = await SendAsync(client, HttpMethod.Put, $"/api/services/{created.Id}", new UpdateServiceRequest(created.Code, "PGR atualizado", null, null, created.Version));
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, $"/api/services/{created.Id}/activate", new ServiceVersionRequest(created.Version))).StatusCode);
        var currentState = await SendAsync(client, HttpMethod.Post, $"/api/services/{created.Id}/activate", new ServiceVersionRequest(updated.Version));
        var sameState = (await currentState.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
        Assert.Equal(HttpStatusCode.OK, currentState.StatusCode); Assert.Equal(updated.Version, sameState.Version);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ServiceAuditRecords.Count());
    }

    [Fact]
    public async Task Service_authorization_active_visibility_and_csrf_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await CreateReadyAdminAsync(factory);
        var service = await CreateServiceAsync(admin);
        var inactive = await SendAsync(admin, HttpMethod.Post, $"/api/services/{service.Id}/deactivate", new ServiceVersionRequest(service.Version));
        var inactiveService = (await inactive.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
        var user = await CreateUserAsync(factory, "service-user@example.test", IdentityRoles.User);
        using var userClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(userClient, user.Email!, "Userpass1!Password")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userClient.GetAsync($"/api/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.PostAsJsonAsync("/api/services", new CreateServiceRequest("USR", "Blocked", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/services/{service.Id}/activate", new ServiceVersionRequest(inactiveService.Version))).StatusCode);
    }

    private static async Task<ServiceDetailResponse> CreateServiceAsync(HttpClient client, CreateServiceRequest? request = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/services", request ?? new CreateServiceRequest("PGR", "Programa de Gerenciamento de Riscos", null, null));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ServiceDetailResponse>())!;
    }
    private static async Task<HttpClient> CreateReadyAdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory); Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client)).StatusCode);
        (await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<ApplicationUser> CreateUserAsync(IdentityWebApplicationFactory factory, string email, string role)
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Service Reader", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = false };
        Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded); return user;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request);
    }
}
