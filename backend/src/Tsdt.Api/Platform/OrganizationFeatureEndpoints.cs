using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Platform;

internal static class OrganizationFeatureEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/features", async (Guid id, ApplicationDbContext db) =>
        {
            if (!await db.Organizations.AsNoTracking().AnyAsync(item => item.Id == id)) return Results.NotFound();
            var enabled = await db.OrganizationFeatures.AsNoTracking().Where(item => item.OrganizationId == id)
                .Select(item => item.FeatureKey).ToListAsync();
            var keys = enabled.ToHashSet(StringComparer.Ordinal);
            return Results.Ok(FeatureCatalog.All.Select(feature => new OrganizationFeatureResponse(
                feature.Key, feature.DisplayName, keys.Contains(feature.Key), feature.Dependencies)).ToArray());
        });

        group.MapPost("/{id:guid}/features/{key}", async (
            Guid id, string key, HttpContext context, IAntiforgery antiforgery,
            ApplicationDbContext db, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users) =>
        {
            if (!await OrganizationEndpoints.IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            if (!FeatureCatalog.TryGet(key, out var feature)) return Results.NotFound();
            if (!await db.Organizations.AnyAsync(item => item.Id == id)) return Results.NotFound();

            var enabled = await db.OrganizationFeatures.Where(item => item.OrganizationId == id)
                .Select(item => item.FeatureKey).ToListAsync();
            var keys = enabled.ToHashSet(StringComparer.Ordinal);
            if (keys.Contains(key)) return Results.NoContent();

            var missing = FeatureEntitlementRules.MissingDependencies(key, keys);
            if (missing.Count > 0)
            {
                var names = missing.Select(dependency => FeatureCatalog.TryGet(dependency, out var item) ? item.DisplayName : dependency);
                return Results.Conflict(new { error = $"Não é possível habilitar {feature.DisplayName}. Habilite primeiro: {string.Join(", ", names)}." });
            }

            db.OrganizationFeatures.Add(new OrganizationFeature { OrganizationId = id, FeatureKey = key });
            OrganizationEndpoints.AddAudit(db, id, await OrganizationEndpoints.Actor(context, users), "ORGANIZATION_FEATURE_ENABLED", featureKey: key);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}/features/{key}", async (
            Guid id, string key, HttpContext context, IAntiforgery antiforgery,
            ApplicationDbContext db, Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> users) =>
        {
            if (!await OrganizationEndpoints.IsValidCsrf(context, antiforgery)) return Results.BadRequest();
            if (!FeatureCatalog.TryGet(key, out var feature)) return Results.NotFound();
            if (!await db.Organizations.AnyAsync(item => item.Id == id)) return Results.NotFound();

            var enabled = await db.OrganizationFeatures.Where(item => item.OrganizationId == id).ToListAsync();
            var grant = enabled.SingleOrDefault(item => item.FeatureKey == key);
            if (grant is null) return Results.NoContent();

            var keys = enabled.Select(item => item.FeatureKey).ToHashSet(StringComparer.Ordinal);
            var dependents = FeatureEntitlementRules.EnabledDependents(key, keys);
            if (dependents.Count > 0)
            {
                var names = dependents.Select(dependent => FeatureCatalog.TryGet(dependent, out var item) ? item.DisplayName : dependent);
                return Results.Conflict(new { error = $"Antes de desabilitar {feature.DisplayName}, desabilite também: {string.Join(", ", names)}." });
            }

            db.OrganizationFeatures.Remove(grant);
            OrganizationEndpoints.AddAudit(db, id, await OrganizationEndpoints.Actor(context, users), "ORGANIZATION_FEATURE_DISABLED", featureKey: key);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

public sealed record OrganizationFeatureResponse(string Key, string DisplayName, bool Enabled, IReadOnlyList<string> Dependencies);
