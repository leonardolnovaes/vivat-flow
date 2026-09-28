using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class BootstrapAdminTests
{
    [Fact]
    public async Task Startup_creates_one_active_admin_that_must_change_password()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = await db.Users.SingleAsync();

        Assert.Equal("admin@example.test", user.Email);
        Assert.Equal("Bootstrap Administrator", user.FullName);
        Assert.True(user.IsActive);
        Assert.True(user.MustChangePassword);
        Assert.True(await userManager.IsInRoleAsync(user, IdentityRoles.Admin));
        Assert.Equal(IdentityRoles.All, await db.Roles.Select(role => role.Name!).OrderBy(name => name).ToArrayAsync());
    }

    [Fact]
    public async Task Bootstrapper_is_idempotent_and_does_not_reset_existing_user()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = await db.Users.ToListAsync();
        var user = Assert.Single(users);
        var originalHash = user.PasswordHash;

        await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().InitializeAsync();

        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(originalHash, (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Missing_bootstrap_configuration_creates_no_user()
    {
        using var factory = new IdentityWebApplicationFactory(bootstrapEnabled: false);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(await db.Users.ToListAsync());
        Assert.Equal(IdentityRoles.All.Length, await db.Roles.CountAsync());
    }

    [Fact]
    public void Identity_schema_declares_active_and_password_change_columns()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model.FindEntityType(typeof(ApplicationUser));

        Assert.NotNull(entity?.FindProperty(nameof(ApplicationUser.IsActive)));
        Assert.NotNull(entity?.FindProperty(nameof(ApplicationUser.FullName)));
        Assert.NotNull(entity?.FindProperty(nameof(ApplicationUser.MustChangePassword)));
    }
}
