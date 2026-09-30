using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Contracts;
using Tsdt.Api.Identity;
using Tsdt.Api.Quotes;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderSourceEndpoints
{
    public static void Map(RouteGroupBuilder orders)
    {
        var sources = orders.MapGroup("/sources").RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        sources.MapGet("", ListAsync);
        sources.MapGet("/quote/{id:guid}", (Guid id, HttpContext context, ApplicationDbContext db) => GetAsync(WorkOrderSourceType.Quote, id, context, db));
        sources.MapGet("/contract/{id:guid}", (Guid id, HttpContext context, ApplicationDbContext db) => GetAsync(WorkOrderSourceType.Contract, id, context, db));
    }

    private sealed record SourceRow(Guid Id, WorkOrderSourceType SourceType, Guid QuoteId, Guid? ContractId, string Reference, string CustomerLegalNameSnapshot, string? ServiceAddressSnapshot, string Status, int ServiceCount);
    private sealed record RelatedContract(Guid Id, Guid QuoteId, ContractStatus Status);
    private sealed record RelatedOrder(Guid Id, Guid QuoteId, string Number);

    private static async Task<IResult> ListAsync(WorkOrderSourceType? sourceType, string? search, int? page, int? pageSize, HttpContext context, ApplicationDbContext db)
    {
        if (sourceType is not (WorkOrderSourceType.Quote or WorkOrderSourceType.Contract))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sourceType"] = ["Selecione um tipo de origem válido."] });
        var organizationId = TenantContext.OrganizationId(context);
        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var term = search?.Trim().ToLowerInvariant();
        if (term?.Length > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["search"] = ["Use no máximo 120 caracteres na busca."] });
        var offset = ((long)currentPage - 1) * size;
        if (sourceType == WorkOrderSourceType.Quote)
        {
            var query = db.Quotes.AsNoTracking().Where(item => item.OrganizationId == organizationId && item.Status == QuoteStatus.Approved);
            if (!string.IsNullOrWhiteSpace(term)) query = query.Where(item => item.Number.ToLower().Contains(term) || item.CustomerLegalNameSnapshot.ToLower().Contains(term));
            var total = await query.CountAsync();
            if (offset > int.MaxValue) return Results.Ok(new WorkOrderSourceListResponse([], currentPage, size, total));
            var rows = await query.OrderBy(item => item.CustomerLegalNameSnapshot).ThenBy(item => item.Number).ThenBy(item => item.Id)
                .Skip((int)offset).Take(size)
                .Select(item => new SourceRow(item.Id, WorkOrderSourceType.Quote, item.Id, null, item.Number, item.CustomerLegalNameSnapshot, item.ServiceAddressSnapshot, item.Status.ToString(), item.Items.Count))
                .ToListAsync();
            return Results.Ok(new WorkOrderSourceListResponse(await DecorateAsync(rows, organizationId, db), currentPage, size, total));
        }
        else
        {
            var query = from contract in db.Contracts.AsNoTracking()
                        join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
                        where contract.OrganizationId == organizationId && quote.OrganizationId == organizationId && contract.Status == ContractStatus.Active
                        select new { contract, quote };
            if (!string.IsNullOrWhiteSpace(term)) query = query.Where(item => item.contract.CustomerLegalNameSnapshot.ToLower().Contains(term) || item.quote.Number.ToLower().Contains(term));
            var total = await query.CountAsync();
            if (offset > int.MaxValue) return Results.Ok(new WorkOrderSourceListResponse([], currentPage, size, total));
            var rows = await query.OrderBy(item => item.contract.CustomerLegalNameSnapshot).ThenBy(item => item.quote.Number).ThenBy(item => item.contract.Id)
                .Skip((int)offset).Take(size)
                .Select(item => new SourceRow(item.contract.Id, WorkOrderSourceType.Contract, item.quote.Id, item.contract.Id, item.quote.Number, item.contract.CustomerLegalNameSnapshot, item.quote.ServiceAddressSnapshot, item.contract.Status.ToString(), item.contract.Items.Count))
                .ToListAsync();
            return Results.Ok(new WorkOrderSourceListResponse(await DecorateAsync(rows, organizationId, db), currentPage, size, total));
        }
    }

    private static async Task<IResult> GetAsync(WorkOrderSourceType type, Guid id, HttpContext context, ApplicationDbContext db)
    {
        var organizationId = TenantContext.OrganizationId(context);
        SourceRow? row;
        IReadOnlyList<WorkOrderSourceItemResponse> items;
        if (type == WorkOrderSourceType.Quote)
        {
            row = await db.Quotes.AsNoTracking().Where(item => item.Id == id && item.OrganizationId == organizationId && item.Status == QuoteStatus.Approved)
                .Select(item => new SourceRow(item.Id, type, item.Id, null, item.Number, item.CustomerLegalNameSnapshot, item.ServiceAddressSnapshot, item.Status.ToString(), item.Items.Count))
                .SingleOrDefaultAsync();
            if (row is null) return Results.NotFound();
            items = await db.QuoteItems.AsNoTracking().Where(item => item.QuoteId == id)
                .OrderBy(item => item.DisplayOrder).Select(item => new WorkOrderSourceItemResponse(item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder)).ToListAsync();
        }
        else
        {
            row = await (from contract in db.Contracts.AsNoTracking()
                         join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
                         where contract.Id == id && contract.OrganizationId == organizationId && quote.OrganizationId == organizationId && contract.Status == ContractStatus.Active
                         select new SourceRow(contract.Id, type, quote.Id, contract.Id, quote.Number, contract.CustomerLegalNameSnapshot, quote.ServiceAddressSnapshot, contract.Status.ToString(), contract.Items.Count)).SingleOrDefaultAsync();
            if (row is null) return Results.NotFound();
            items = await db.ContractItems.AsNoTracking().Where(item => item.ContractId == id)
                .OrderBy(item => item.DisplayOrder).Select(item => new WorkOrderSourceItemResponse(item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder)).ToListAsync();
        }
        return Results.Ok(new WorkOrderSourceDetailResponse((await DecorateAsync([row], organizationId, db)).Single(), items));
    }

    private static async Task<IReadOnlyList<WorkOrderSourceSummaryResponse>> DecorateAsync(IReadOnlyList<SourceRow> rows, Guid organizationId, ApplicationDbContext db)
    {
        if (rows.Count == 0) return [];
        var quoteIds = rows.Select(item => item.QuoteId).Distinct().ToArray();
        var contracts = await db.Contracts.AsNoTracking().Where(item => item.OrganizationId == organizationId && quoteIds.Contains(item.QuoteId) && (item.Status == ContractStatus.Draft || item.Status == ContractStatus.Active))
            .Select(item => new RelatedContract(item.Id, item.QuoteId, item.Status)).ToListAsync();
        var orders = await db.WorkOrders.AsNoTracking().Where(item => item.OrganizationId == organizationId && quoteIds.Contains(item.QuoteId) && item.Status != WorkOrderStatus.Cancelled)
            .Select(item => new RelatedOrder(item.Id, item.QuoteId, item.Number)).ToListAsync();
        var governing = contracts.GroupBy(item => item.QuoteId).ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Status == ContractStatus.Active).ThenBy(item => item.Id).First());
        var current = orders.GroupBy(item => item.QuoteId).ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id).First());
        return rows.Select(row =>
        {
            governing.TryGetValue(row.QuoteId, out var contract);
            current.TryGetValue(row.QuoteId, out var order);
            var eligibleStatus = row.SourceType == WorkOrderSourceType.Quote ? row.Status == nameof(QuoteStatus.Approved) : row.Status == nameof(ContractStatus.Active);
            var canCreate = eligibleStatus && (row.SourceType == WorkOrderSourceType.Contract || contract is null) && order is null && !string.IsNullOrWhiteSpace(row.ServiceAddressSnapshot) && row.ServiceCount > 0;
            return new WorkOrderSourceSummaryResponse(row.Id, row.SourceType, row.QuoteId, row.ContractId, row.Reference, row.CustomerLegalNameSnapshot, row.ServiceAddressSnapshot, row.Status, canCreate,
                row.SourceType == WorkOrderSourceType.Quote ? contract?.Id : null, row.SourceType == WorkOrderSourceType.Quote ? contract?.Status.ToString() : null, order?.Id, order?.Number);
        }).ToList();
    }
}
