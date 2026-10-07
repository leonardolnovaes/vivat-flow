using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Platform;

public static class OrganizationEndpoints
{
    public static void MapPlatformOrganizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/organizations").RequireAuthorization(AuthorizationPolicies.PlatformAdministrator);
        TenantAdministratorEndpoints.Map(group);
        OrganizationFeatureEndpoints.Map(group);
        group.MapGet("", async (ApplicationDbContext db) => Results.Ok(await db.Organizations.AsNoTracking().OrderBy(item => item.Name).Select(item => ToResponse(item)).ToListAsync()));
        group.MapGet("/summary", async (ApplicationDbContext db) => Results.Ok(new OrganizationDashboardResponse(await db.Organizations.CountAsync(), await db.Organizations.CountAsync(item => item.Status == OrganizationStatus.Active), await db.Organizations.CountAsync(item => item.Status == OrganizationStatus.Suspended), await db.Organizations.CountAsync(item => item.Status == OrganizationStatus.Deactivated))));
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db) => await db.Organizations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id) is { } organization ? Results.Ok(ToResponse(organization)) : Results.NotFound());

        group.MapPost("", async (OrganizationRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db, UserManager<ApplicationUser> users) =>
        {
            if (!await IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            if (Validate(request) is { } validation) return validation;
            var slug = OrganizationRules.NormalizeSlug(request.Slug);
            if (await db.Organizations.AnyAsync(item => item.Slug == slug)) return DuplicateSlug();
            var item = new Organization { Name = request.Name!.Trim(), Slug = slug };
            db.Organizations.Add(item);
            AddAudit(db, item.Id, await Actor(context, users), "ORGANIZATION_CREATED");
            return await SaveAsync(db, () => Results.Created($"/api/platform/organizations/{item.Id}", ToResponse(item)));
        });

        group.MapPut("/{id:guid}", async (Guid id, OrganizationRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db, UserManager<ApplicationUser> users) =>
        {
            if (!await IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            if (Validate(request) is { } validation) return validation;
            var item = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id);
            if (item is null) return Results.NotFound();
            var slug = OrganizationRules.NormalizeSlug(request.Slug);
            if (await db.Organizations.AnyAsync(x => x.Id != id && x.Slug == slug)) return DuplicateSlug();
            item.Name = request.Name!.Trim(); item.Slug = slug; item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, id, await Actor(context, users), "ORGANIZATION_UPDATED");
            return await SaveAsync(db, () => Results.Ok(ToResponse(item)));
        });

        MapTransition("activate", OrganizationStatus.Active, "ORGANIZATION_ACTIVATED");
        MapTransition("suspend", OrganizationStatus.Suspended, "ORGANIZATION_SUSPENDED");
        MapTransition("deactivate", OrganizationStatus.Deactivated, "ORGANIZATION_DEACTIVATED");

        void MapTransition(string route, OrganizationStatus target, string action) => group.MapPost($"/{{id:guid}}/{route}", async (Guid id, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db, UserManager<ApplicationUser> users) =>
        {
            if (!await IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            var item = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id);
            if (item is null) return Results.NotFound();
            if (!OrganizationLifecycle.CanTransition(item.Status, target)) return InvalidTransition();
            if (item.Status == target) return Results.Ok(ToResponse(item));
            item.Status = target; item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(db, id, await Actor(context, users), action);
            return await SaveAsync(db, () => Results.Ok(ToResponse(item)));
        });
    }

    internal static IQueryable<Organization> OrganizationForUpdateQuery(ApplicationDbContext db, Guid id) =>
        db.Database.IsNpgsql()
            ? db.Organizations.FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {id} FOR UPDATE")
            : db.Organizations.Where(item => item.Id == id);

    private static OrganizationResponse ToResponse(Organization item) => new(item.Id, item.Name, item.Slug, item.Status, item.CreatedAtUtc, item.UpdatedAtUtc);
    private static IResult? Validate(OrganizationRequest request)
    {
        var errors = new Dictionary<string, string[]>(); var name = request.Name?.Trim(); var slug = OrganizationRules.NormalizeSlug(request.Slug);
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Nome da organização é obrigatório."];
        else if (name.Length > 200) errors["name"] = ["O nome deve ter no máximo 200 caracteres."];
        if (string.IsNullOrWhiteSpace(slug)) errors["slug"] = ["Identificador é obrigatório."];
        else if (!OrganizationRules.IsValidSlug(slug)) errors["slug"] = ["Use apenas letras minúsculas, números e hífens."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }
    private static IResult DuplicateSlug() => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["slug"] = ["Este identificador já está em uso."] } });
    private static IResult InvalidTransition() => Results.Conflict(new { error = "Esta alteração de status não é permitida." });
    internal static async Task<bool> IsValidCsrf(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    internal static async Task<string> Actor(HttpContext context, UserManager<ApplicationUser> users) => (await users.GetUserAsync(context.User))?.Id ?? throw new UnauthorizedAccessException();
    internal static void AddAudit(ApplicationDbContext db, Guid organizationId, string actorId, string action, string? targetUserId = null, string? targetUserNameSnapshot = null, string? targetUserEmailSnapshot = null, string? featureKey = null) => db.OrganizationAuditRecords.Add(new OrganizationAuditRecord { OrganizationId = organizationId, ActorUserId = actorId, TargetUserId = targetUserId, TargetUserNameSnapshot = targetUserNameSnapshot, TargetUserEmailSnapshot = targetUserEmailSnapshot, FeatureKey = featureKey, Action = action });
    private static async Task<IResult> SaveAsync(ApplicationDbContext db, Func<IResult> result) { try { await db.SaveChangesAsync(); return result(); } catch (DbUpdateException) { return DuplicateSlug(); } }
}
