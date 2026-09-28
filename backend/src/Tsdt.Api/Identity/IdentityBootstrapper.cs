using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tsdt.Api.Identity;

public sealed class IdentityBootstrapper(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<BootstrapAdminOptions> options,
    ILogger<IdentityBootstrapper> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in IdentityRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var createRoleResult = await roleManager.CreateAsync(new ApplicationRole { Name = roleName });
                if (!createRoleResult.Succeeded)
                {
                    throw new InvalidOperationException($"Could not create Identity role '{roleName}'.");
                }
            }
        }

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var bootstrap = options.Value;
        if (string.IsNullOrWhiteSpace(bootstrap.Email) || string.IsNullOrWhiteSpace(bootstrap.FullName) || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            logger.LogWarning("No bootstrap administrator was created because BootstrapAdmin configuration is incomplete.");
            return;
        }

        var user = new ApplicationUser
        {
            FullName = bootstrap.FullName.Trim(),
            UserName = bootstrap.Email,
            Email = bootstrap.Email,
            EmailConfirmed = true,
            IsActive = true,
            MustChangePassword = true
        };
        var result = await userManager.CreateAsync(user, bootstrap.Password);
        if (!result.Succeeded) logger.LogWarning("No bootstrap administrator was created because the configured credentials do not meet Identity validation requirements.");
        else
        {
            var roleResult = await userManager.AddToRoleAsync(user, IdentityRoles.Admin);
            if (!roleResult.Succeeded) throw new InvalidOperationException("Could not assign the ADMIN role to the bootstrap administrator.");
            logger.LogInformation("Bootstrap administrator was created.");
        }
    }
}
