using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Platform;

public static class ServiceLineEndpoints
{
    public static void MapServiceLineEndpoints(this WebApplication app)
    {
        app.MapGet("/api/service-lines", async (HttpContext context, ApplicationDbContext db) =>
        {
            var organizationId = TenantContext.OrganizationId(context);
            return Results.Ok(await db.OrganizationServiceLines.AsNoTracking().Where(item => item.OrganizationId == organizationId && item.ServiceLine.IsActive).OrderBy(item => item.ServiceLine.Name).Select(item => new TenantServiceLineResponse(item.ServiceLine.Id, item.ServiceLine.Code, item.ServiceLine.Name)).ToListAsync());
        }).RequireAuthorization();

        var platform = app.MapGroup("/api/platform/organizations/{organizationId:guid}/service-lines").RequireAuthorization(AuthorizationPolicies.PlatformAdministrator);
        platform.MapGet("", async (Guid organizationId, ApplicationDbContext db) =>
        {
            if (!await db.Organizations.AnyAsync(item => item.Id == organizationId)) return Results.NotFound();

            return Results.Ok(await db.ServiceLines.AsNoTracking()
                .OrderBy(item => item.Name).ThenBy(item => item.Code)
                .Select(item => new PlatformOrganizationServiceLineResponse(
                    item.Id,
                    item.Code,
                    item.Name,
                    item.IsActive,
                    db.OrganizationServiceLines.Any(configuration => configuration.OrganizationId == organizationId && configuration.ServiceLineId == item.Id)))
                .ToListAsync());
        });
        platform.MapPost("/{serviceLineId:guid}", async (Guid organizationId, Guid serviceLineId, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            if (!await db.Organizations.AnyAsync(item => item.Id == organizationId) || !await db.ServiceLines.AnyAsync(item => item.Id == serviceLineId)) return Results.NotFound();
            if (await db.OrganizationServiceLines.AnyAsync(item => item.OrganizationId == organizationId && item.ServiceLineId == serviceLineId)) return Results.Conflict();
            db.OrganizationServiceLines.Add(new OrganizationServiceLine { OrganizationId = organizationId, ServiceLineId = serviceLineId }); await db.SaveChangesAsync(); return Results.NoContent();
        });
        platform.MapDelete("/{serviceLineId:guid}", async (Guid organizationId, Guid serviceLineId, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var item = await db.OrganizationServiceLines.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.ServiceLineId == serviceLineId); if (item is null) return Results.NotFound(); db.OrganizationServiceLines.Remove(item); await db.SaveChangesAsync(); return Results.NoContent();
        });
    }
}
public sealed record TenantServiceLineResponse(Guid Id, string Code, string Name);
public sealed record PlatformOrganizationServiceLineResponse(Guid Id, string Code, string Name, bool IsActive, bool IsEnabled);
