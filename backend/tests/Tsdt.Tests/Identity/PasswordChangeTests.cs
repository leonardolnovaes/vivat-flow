using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class PasswordChangeTests
{
    [Fact]
    public async Task Bootstrapped_user_can_only_change_password_or_logout_until_password_is_changed()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendChangePasswordAsync(client, "Bootstrap1!Pass", "Changed1!Password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>().FindByEmailAsync("admin@example.test");
        Assert.False(user!.MustChangePassword);
    }

    [Fact]
    public async Task Change_password_requires_authentication_current_password_policy_and_csrf()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).StatusCode);
        await IdentityTestClient.LoginAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendChangePasswordAsync(client, "incorrect", "Changed1!Password")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendChangePasswordAsync(client, "Bootstrap1!Pass", "short")).StatusCode);
    }

    private static async Task<HttpResponseMessage> SendChangePasswordAsync(HttpClient client, string currentPassword, string newPassword)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password") { Content = JsonContent.Create(new ChangePasswordRequest(currentPassword, newPassword)) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }
}
