using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tsdt.Api.Platform;

namespace Tsdt.Api.Identity;

public sealed class IdentityBootstrapper(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<BootstrapAdminOptions> options,
    IOptions<OrganizationBootstrapOptions> organizationOptions,
    IOptions<PlatformBootstrapAdminOptions> platformOptions,
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

        var organization = await EnsureOrganizationAsync(cancellationToken);
        var existingTenantUsers = await dbContext.Users.Where(user => !user.IsPlatformAdministrator && user.OrganizationId == null).ToListAsync(cancellationToken);
        foreach (var existingUser in existingTenantUsers) existingUser.OrganizationId = organization.Id;
        if (existingTenantUsers.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);

        var bootstrap = options.Value;
        if (!await dbContext.Users.AnyAsync(cancellationToken) && !string.IsNullOrWhiteSpace(bootstrap.Email) && !string.IsNullOrWhiteSpace(bootstrap.FullName) && !string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            var user = new ApplicationUser { FullName = bootstrap.FullName.Trim(), UserName = bootstrap.Email, Email = bootstrap.Email, EmailConfirmed = true, IsActive = true, MustChangePassword = true, OrganizationId = organization.Id };
            var result = await userManager.CreateAsync(user, bootstrap.Password);
            if (!result.Succeeded) logger.LogWarning("No bootstrap administrator was created because the configured credentials do not meet Identity validation requirements.");
            else { var roleResult = await userManager.AddToRoleAsync(user, IdentityRoles.Admin); if (!roleResult.Succeeded) throw new InvalidOperationException("Could not assign the ADMIN role to the bootstrap administrator."); logger.LogInformation("Bootstrap administrator was created."); }
        }
        await EnsurePlatformAdministratorAsync(cancellationToken);
    }

    private async Task<Organization> EnsureOrganizationAsync(CancellationToken token)
    { var settings = organizationOptions.Value; var slug = OrganizationRules.NormalizeSlug(settings.Slug); var organization = await dbContext.Organizations.SingleOrDefaultAsync(item => item.Slug == slug, token); if (organization is not null) return organization; organization = new Organization { Name = string.IsNullOrWhiteSpace(settings.Name) ? "Organização inicial" : settings.Name.Trim(), Slug = OrganizationRules.IsValidSlug(slug) ? slug : "organizacao-inicial" }; dbContext.Organizations.Add(organization); await dbContext.SaveChangesAsync(token); return organization; }
    private async Task EnsurePlatformAdministratorAsync(CancellationToken token)
    { var settings = platformOptions.Value; if (string.IsNullOrWhiteSpace(settings.Email) || string.IsNullOrWhiteSpace(settings.FullName) || string.IsNullOrWhiteSpace(settings.Password) || await userManager.FindByEmailAsync(settings.Email.Trim()) is not null) return; var user = new ApplicationUser { FullName = settings.FullName.Trim(), UserName = settings.Email.Trim(), Email = settings.Email.Trim(), EmailConfirmed = true, IsActive = true, MustChangePassword = true, IsPlatformAdministrator = true }; var result = await userManager.CreateAsync(user, settings.Password); if (!result.Succeeded) logger.LogWarning("No platform bootstrap administrator was created because configured credentials do not meet Identity validation requirements."); else logger.LogInformation("Platform bootstrap administrator was created."); }
}
