using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class UserProfileHardeningTests
{
    [Fact]
    public async Task Create_and_update_validate_trim_normalize_unicode_boundaries_and_duplicate_email()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var admin = await CreateReadyAdminClientAsync(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest(" ", "valid@example.test", IdentityRoles.User))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Valid Name", " ", IdentityRoles.User))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest(new string('a', 121), "long-name@example.test", IdentityRoles.User))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Valid Name", $"{new string('a', 243)}@example.test", IdentityRoles.User))).StatusCode);

        var nameAtLimit = $"  {new string('\u00e9', 118)}  ";
        var emailAtLimit = $"{new string('a', 241)}@example.test";
        var createdResponse = await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest(nameAtLimit, $"  {emailAtLimit.ToUpperInvariant()}  ", IdentityRoles.User));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = Assert.IsType<CreateUserResponse>(await createdResponse.Content.ReadFromJsonAsync<CreateUserResponse>());
        Assert.Equal(new string('\u00e9', 118), created.User.FullName);
        Assert.Equal(emailAtLimit, created.User.Email);

        var duplicate = await SendAsync(admin, HttpMethod.Post, "/api/admin/users", new CreateUserRequest("Duplicate", emailAtLimit.ToUpperInvariant(), IdentityRoles.User));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var targetSession = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(targetSession, created.User.Email, created.TemporaryPassword)).StatusCode);
        var updatedResponse = await SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{created.User.Id}", new UpdateUserRequest("  Jos\u00e9 \u00c1rvore  ", "  RENAMED@EXAMPLE.TEST  "));
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = Assert.IsType<UserAdministrationResponse>(await updatedResponse.Content.ReadFromJsonAsync<UserAdministrationResponse>());
        Assert.Equal("Jos\u00e9 \u00c1rvore", updated.FullName);
        Assert.Equal("renamed@example.test", updated.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetSession.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Password_policy_endpoint_and_validation_use_active_identity_password_options_with_portuguese_errors()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var anonymous = IdentityTestClient.Create(factory);
        var policyResponse = await anonymous.GetAsync("/api/auth/password-policy");
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);
        var policy = Assert.IsType<PasswordPolicyResponse>(await policyResponse.Content.ReadFromJsonAsync<PasswordPolicyResponse>());
        using var scope = factory.Services.CreateScope();
        var active = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;
        Assert.Equal(active.RequiredLength, policy.MinimumLength);
        Assert.Equal(active.RequireDigit, policy.RequiresDigit);
        Assert.Equal(active.RequireLowercase, policy.RequiresLowercase);
        Assert.Equal(active.RequireUppercase, policy.RequiresUppercase);
        Assert.Equal(active.RequireNonAlphanumeric, policy.RequiresNonAlphanumeric);

        using var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        var wrongCurrent = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("wrong", "ValidPassword1!"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Contains("N\u00e3o foi poss\u00edvel", await wrongCurrent.Content.ReadAsStringAsync());
        var policyFailure = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "short"));
        Assert.Equal(HttpStatusCode.BadRequest, policyFailure.StatusCode);
        Assert.Contains(policy.Description, await policyFailure.Content.ReadAsStringAsync());
    }

    private static async Task<HttpClient> CreateReadyAdminClientAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }
}
