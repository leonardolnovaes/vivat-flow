using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Platform;

public static class FeatureCatalog
{
    public const string Customers = "customers";
    public const string Services = "services";
    public const string Quotes = "quotes";
    public const string Contracts = "contracts";
    public const string WorkOrders = "work-orders";
    public const string Schedule = "schedule";
    public const string Documents = "documents";

    private static readonly IReadOnlyList<FeatureCatalogEntry> catalog =
    [
        new(Customers, "Clientes", []),
        new(Services, "Serviços", []),
        new(Quotes, "Orçamentos", [Customers, Services]),
        new(Contracts, "Contratos", [Quotes]),
        new(WorkOrders, "Ordens de Serviço", [Contracts]),
        new(Schedule, "Agenda", [WorkOrders]),
        new(Documents, "Documentos", [Customers])
    ];

    public static IReadOnlyList<FeatureCatalogEntry> All => catalog;
    public static IReadOnlyList<string> AllKeys => catalog.Select(item => item.Key).ToArray();
    public static bool TryGet(string key, out FeatureCatalogEntry entry)
    {
        var match = catalog.FirstOrDefault(item => item.Key == key);
        entry = match!;
        return match is not null;
    }

    public static bool IsValid() =>
        catalog.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() == catalog.Count &&
        catalog.All(item => item.Dependencies.Distinct(StringComparer.Ordinal).Count() == item.Dependencies.Count &&
                            !item.Dependencies.Contains(item.Key, StringComparer.Ordinal) &&
                            item.Dependencies.All(key => catalog.Any(candidate => candidate.Key == key)));
}

public sealed record FeatureCatalogEntry(string Key, string DisplayName, IReadOnlyList<string> Dependencies);

public sealed class OrganizationFeature
{
    public Guid OrganizationId { get; set; }
    public required string FeatureKey { get; set; }
}

public static class FeatureEntitlementRules
{
    public static IReadOnlyList<string> MissingDependencies(string featureKey, IReadOnlySet<string> enabledKeys) =>
        FeatureCatalog.TryGet(featureKey, out var feature)
            ? feature.Dependencies.Where(key => !enabledKeys.Contains(key)).ToArray()
            : [];

    public static IReadOnlyList<string> EnabledDependents(string featureKey, IReadOnlySet<string> enabledKeys) =>
        FeatureCatalog.All.Where(item => enabledKeys.Contains(item.Key) && item.Dependencies.Contains(featureKey, StringComparer.Ordinal))
            .Select(item => item.Key).ToArray();
}

public sealed class FeatureEntitlementService(ApplicationDbContext db)
{
    public Task<bool> IsEnabledAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default) =>
        db.OrganizationFeatures.AsNoTracking().AnyAsync(item => item.OrganizationId == organizationId && item.FeatureKey == featureKey, cancellationToken);
}

public sealed class FeatureEntitlementFilter(string featureKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(TenantContext.OrganizationItemKey, out var value) || value is not Guid organizationId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var entitlements = context.HttpContext.RequestServices.GetRequiredService<FeatureEntitlementService>();
        if (!await entitlements.IsEnabledAsync(organizationId, featureKey, context.HttpContext.RequestAborted))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return await next(context);
    }
}

public static class FeatureEntitlementEndpointExtensions
{
    public static RouteGroupBuilder RequireFeatureEntitlement(this RouteGroupBuilder group, string featureKey)
    {
        group.AddEndpointFilter(new FeatureEntitlementFilter(featureKey));
        return group;
    }

    public static RouteHandlerBuilder RequireFeatureEntitlement(this RouteHandlerBuilder endpoint, string featureKey)
    {
        endpoint.AddEndpointFilter(new FeatureEntitlementFilter(featureKey));
        return endpoint;
    }
}
