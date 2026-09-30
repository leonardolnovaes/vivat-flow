using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class AuthenticationTests
{
    [Fact]
    public async Task Login_requires_csrf_and_accepts_valid_credentials()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        var missingToken = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@example.test", "Bootstrap1!Pass"));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        var login = await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Authentication_mutations_reject_missing_or_invalid_csrf_tokens()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);

        var invalidLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin@example.test", "Bootstrap1!Pass")) };
        invalidLogin.Headers.Add("X-CSRF-TOKEN", "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidLogin)).StatusCode);

        await IdentityTestClient.LoginAsync(client);
        var invalidLogout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        invalidLogout.Headers.Add("X-CSRF-TOKEN", "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidLogout)).StatusCode);

        var invalidChange = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password") { Content = JsonContent.Create(new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password")) };
        invalidChange.Headers.Add("X-CSRF-TOKEN", "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidChange)).StatusCode);
    }

    [Fact]
    public async Task Login_rejects_invalid_or_inactive_users_without_details()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        var invalid = await IdentityTestClient.LoginAsync(client, password: "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.True(string.IsNullOrEmpty(await invalid.Content.ReadAsStringAsync()));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();
        var inactive = await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
    }

    [Fact]
    public async Task Me_requires_authentication_and_returns_only_safe_profile()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client)).StatusCode);

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("mustChangePassword", json);
        Assert.Contains("organization", json);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(profile?.Organization);
        Assert.Equal("Organização inicial", profile.Organization.Name);
    }

    [Fact]
    public async Task Platform_administrator_session_does_not_expose_a_tenant_organization()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var platformAdministrator = new ApplicationUser { FullName = "Platform Administrator", UserName = "platform@example.test", Email = "platform@example.test", EmailConfirmed = true, IsActive = true, MustChangePassword = false, IsPlatformAdministrator = true };
        Assert.True((await userManager.CreateAsync(platformAdministrator, "Platform1!Password")).Succeeded);

        using var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(client, platformAdministrator.Email!, "Platform1!Password")).StatusCode);

        var profile = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.True(profile!.IsPlatformAdministrator);
        Assert.Null(profile.Organization);
    }

    [Fact]
    public async Task Logout_requires_csrf_and_invalidates_the_session()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
