using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Contracts;
using Tsdt.Api.Identity;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderSourceEndpoints
{
    public static void Map(RouteGroupBuilder orders)
    {
        var sources = orders.MapGroup("/sources").RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        sources.MapGet("", ListAsync);
        sources.MapGet("/contract/{id:guid}", GetAsync);
    }

    private sealed record SourceRow(Guid Id, Guid QuoteId, string Reference, string CustomerLegalNameSnapshot, string? ServiceAddressSnapshot, string Status, int ServiceCount);
    private sealed record RelatedOrder(Guid Id, Guid? ContractId, Guid QuoteId, string Number, WorkOrderStatus Status);

    private static async Task<IResult> ListAsync(WorkOrderSourceType? sourceType, string? search, int? page, int? pageSize, HttpContext context, ApplicationDbContext db)
    {
        if (sourceType != WorkOrderSourceType.Contract)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sourceType"] = ["Selecione um contrato ativo como origem da OS."] });

        var organizationId = TenantContext.OrganizationId(context);
        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var term = search?.Trim().ToLowerInvariant();
        if (term?.Length > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["search"] = ["Use no máximo 120 caracteres na busca."] });
        var offset = ((long)currentPage - 1) * size;

        var query = from contract in db.Contracts.AsNoTracking()
                    join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
                    where contract.OrganizationId == organizationId && quote.OrganizationId == organizationId && contract.Status == ContractStatus.Active
                        && contract.Items.Any() && quote.ServiceAddressSnapshot != null && quote.ServiceAddressSnapshot.Trim() != ""
                        && !db.WorkOrders.Any(order => order.OrganizationId == organizationId &&
                            (order.ContractId == contract.Id || (order.ContractId == null && order.QuoteId == quote.Id)) &&
                            order.Status != WorkOrderStatus.Completed && order.Status != WorkOrderStatus.Cancelled)
                    select new { contract, quote };
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(item => item.contract.CustomerLegalNameSnapshot.ToLower().Contains(term) || item.quote.Number.ToLower().Contains(term));

        var total = await query.CountAsync();
        if (offset > int.MaxValue) return Results.Ok(new WorkOrderSourceListResponse([], currentPage, size, total));
        var rows = await query.OrderBy(item => item.contract.CustomerLegalNameSnapshot).ThenBy(item => item.quote.Number).ThenBy(item => item.contract.Id)
            .Skip((int)offset).Take(size)
            .Select(item => new SourceRow(item.contract.Id, item.quote.Id, item.quote.Number, item.contract.CustomerLegalNameSnapshot, item.quote.ServiceAddressSnapshot, item.contract.Status.ToString(), item.contract.Items.Count))
            .ToListAsync();
        return Results.Ok(new WorkOrderSourceListResponse(await DecorateAsync(rows, organizationId, db), currentPage, size, total));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, ApplicationDbContext db)
    {
        var organizationId = TenantContext.OrganizationId(context);
        var row = await (from contract in db.Contracts.AsNoTracking()
                         join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
                         where contract.Id == id && contract.OrganizationId == organizationId && quote.OrganizationId == organizationId && contract.Status == ContractStatus.Active
                         select new SourceRow(contract.Id, quote.Id, quote.Number, contract.CustomerLegalNameSnapshot, quote.ServiceAddressSnapshot, contract.Status.ToString(), contract.Items.Count))
            .SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();

        var items = await db.ContractItems.AsNoTracking().Where(item => item.ContractId == id)
            .OrderBy(item => item.DisplayOrder)
            .Select(item => new WorkOrderSourceItemResponse(item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder))
            .ToListAsync();
        return Results.Ok(new WorkOrderSourceDetailResponse((await DecorateAsync([row], organizationId, db)).Single(), items));
    }

    private static async Task<IReadOnlyList<WorkOrderSourceSummaryResponse>> DecorateAsync(IReadOnlyList<SourceRow> rows, Guid organizationId, ApplicationDbContext db)
    {
        if (rows.Count == 0) return [];

        var contractIds = rows.Select(item => item.Id).Distinct().ToArray();
        var quoteIds = rows.Select(item => item.QuoteId).Distinct().ToArray();
        var orders = await db.WorkOrders.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && ((item.ContractId.HasValue && contractIds.Contains(item.ContractId.Value)) || (!item.ContractId.HasValue && quoteIds.Contains(item.QuoteId))))
            .Select(item => new RelatedOrder(item.Id, item.ContractId, item.QuoteId, item.Number, item.Status))
            .ToListAsync();

        return rows.Select(row =>
        {
            var contractOrders = orders.Where(item => item.ContractId == row.Id || (item.ContractId is null && item.QuoteId == row.QuoteId)).ToList();
            var current = contractOrders
                .Where(item => item.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Cancelled))
                .OrderBy(item => item.Id)
                .FirstOrDefault();
            var canCreate = row.Status == nameof(ContractStatus.Active)
                && WorkOrderRules.CanCreateFromContract(ContractStatus.Active, contractOrders.Select(item => item.Status))
                && !string.IsNullOrWhiteSpace(row.ServiceAddressSnapshot)
                && row.ServiceCount > 0;

            return new WorkOrderSourceSummaryResponse(
                row.Id, WorkOrderSourceType.Contract, row.QuoteId, row.Id, row.Reference, row.CustomerLegalNameSnapshot,
                row.ServiceAddressSnapshot, row.Status, canCreate, null, null, current?.Id, current?.Number);
        }).ToList();
    }
}
