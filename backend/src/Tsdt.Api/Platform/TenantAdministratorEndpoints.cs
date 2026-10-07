using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Platform;

internal static class TenantAdministratorEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/administrators", async (Guid id, ApplicationDbContext db) =>
        {
            if (!await db.Organizations.AsNoTracking().AnyAsync(item => item.Id == id)) return Results.NotFound();

            var administrators = await (
                from user in db.Users.AsNoTracking()
                join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where user.OrganizationId == id && !user.IsPlatformAdministrator && role.Name == IdentityRoles.Admin
                orderby user.FullName, user.Id
                select new TenantAdministratorResponse(user.Id, user.FullName, user.Email!, user.IsActive, user.MustChangePassword))
                .ToListAsync();

            return Results.Ok(administrators);
        });

        group.MapPost("/{id:guid}/administrators", async (
            Guid id,
            CreateTenantAdministratorRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            ApplicationDbContext db,
            UserManager<ApplicationUser> users,
            TemporaryPasswordGenerator passwordGenerator) =>
        {
            if (!await OrganizationEndpoints.IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            var fullName = request.FullName?.Trim();
            var email = UserAdministrationSupport.NormalizeEmail(request.Email);
            if (UserAdministrationSupport.ValidateInput(fullName, email, IdentityRoles.Admin, validateRole: false) is { } validation) return validation;
            if (await users.FindByEmailAsync(email!) is not null) return DuplicateEmail();

            await using var transaction = await db.Database.BeginTransactionAsync();
            var organization = await OrganizationEndpoints.OrganizationForUpdateQuery(db, id).SingleOrDefaultAsync();
            if (organization is null) return Results.NotFound();
            if (!OrganizationRules.CanProvisionTenantAdministrator(organization.Status)) return OrganizationNotEligible(organization.Status);

            var temporaryPassword = passwordGenerator.Generate();
            var user = new ApplicationUser
            {
                FullName = fullName!,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
                MustChangePassword = true,
                OrganizationId = organization.Id
            };
            var creation = await users.CreateAsync(user, temporaryPassword);
            if (!creation.Succeeded)
            {
                if (creation.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")) return DuplicateEmail();
                return UserAdministrationSupport.ValidationProblem(creation);
            }

            var assignment = await users.AddToRoleAsync(user, IdentityRoles.Admin);
            if (!assignment.Succeeded) return UserAdministrationSupport.ValidationProblem(assignment);

            OrganizationEndpoints.AddAudit(db, organization.Id, await OrganizationEndpoints.Actor(context, users), "TENANT_ADMINISTRATOR_CREATED", targetUserId: user.Id, targetUserNameSnapshot: user.FullName, targetUserEmailSnapshot: user.Email);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var userResponse = await UserAdministrationSupport.CreateResponseAsync(user, users);
            return Results.Created($"/api/platform/organizations/{organization.Id}/administrators", new CreateUserResponse(userResponse, temporaryPassword));
        });

        group.MapGet("/{id:guid}/audit", async (Guid id, ApplicationDbContext db) =>
        {
            if (!await db.Organizations.AsNoTracking().AnyAsync(item => item.Id == id)) return Results.NotFound();

            var history = await (
                from record in db.OrganizationAuditRecords.AsNoTracking()
                where record.OrganizationId == id
                join actor in db.Users.AsNoTracking() on record.ActorUserId equals actor.Id
                orderby record.OccurredAtUtc descending, record.Id
                select new OrganizationAuditResponse(record.Id, record.Action, actor.FullName, record.TargetUserNameSnapshot, record.TargetUserEmailSnapshot, record.FeatureKey, record.OccurredAtUtc))
                .Take(100)
                .ToListAsync();

            return Results.Ok(history);
        });
    }

    private static IResult DuplicateEmail() => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["email"] = ["Já existe um usuário com este e-mail."] } });

    private static IResult OrganizationNotEligible(OrganizationStatus status) => Results.Conflict(new
    {
        error = status == OrganizationStatus.Suspended
            ? "Não é possível provisionar administradores enquanto a organização estiver suspensa."
            : "Não é possível provisionar administradores para uma organização desativada."
    });
}
