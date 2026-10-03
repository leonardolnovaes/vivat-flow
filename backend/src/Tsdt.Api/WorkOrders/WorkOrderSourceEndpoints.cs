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

        if (search?.Trim().Length > 120)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["search"] = ["Use no máximo 120 caracteres na busca."] });
        return Results.Ok(await ListSourcesAsync(db, TenantContext.OrganizationId(context), search, page, pageSize));
    }

    public static async Task<WorkOrderSourceListResponse> ListSourcesAsync(ApplicationDbContext db, Guid organizationId, string? search, int? page, int? pageSize)
    {
        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var term = search?.Trim().ToLowerInvariant();
        var cnpjTerm = term is not null && term.All(character => char.IsDigit(character) || character is '.' or '/' or '-' or ' ')
            ? new string(term.Where(char.IsDigit).ToArray()) : "";
        var offset = ((long)currentPage - 1) * size;

        var query = from contract in db.Contracts.AsNoTracking()
                    join quote in db.Quotes.AsNoTracking() on contract.QuoteId equals quote.Id
                    join customer in db.Customers.AsNoTracking() on contract.CustomerId equals customer.Id
                    where contract.OrganizationId == organizationId && quote.OrganizationId == organizationId
                        && customer.OrganizationId == organizationId && contract.Status == ContractStatus.Active
                    select new { contract, quote, customer };
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(item => item.contract.CustomerLegalNameSnapshot.ToLower().Contains(term)
                || item.customer.LegalName.ToLower().Contains(term)
                || (item.customer.TradeName != null && item.customer.TradeName.ToLower().Contains(term))
                || item.quote.Number.ToLower().Contains(term)
                || (cnpjTerm != "" && item.customer.Cnpj.Contains(cnpjTerm)));

        var total = await query.CountAsync();
        if (offset > int.MaxValue) return new WorkOrderSourceListResponse([], currentPage, size, total);
        var rows = await query.OrderBy(item => item.contract.CustomerLegalNameSnapshot).ThenBy(item => item.quote.Number).ThenBy(item => item.contract.Id)
            .Skip((int)offset).Take(size)
            .Select(item => new SourceRow(item.contract.Id, item.quote.Id, item.quote.Number, item.contract.CustomerLegalNameSnapshot, item.quote.ServiceAddressSnapshot, item.contract.Status.ToString(), item.contract.Items.Count))
            .ToListAsync();
        return new WorkOrderSourceListResponse(await DecorateAsync(rows, organizationId, db), currentPage, size, total);
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
            var reasons = new List<string>();
            if (!WorkOrderRules.CanCreateFromContract(ContractStatus.Active, contractOrders.Select(item => item.Status))) reasons.Add("OpenWorkOrder");
            if (string.IsNullOrWhiteSpace(row.ServiceAddressSnapshot)) reasons.Add("MissingServiceAddress");
            if (row.ServiceCount == 0) reasons.Add("NoOperationalServices");

            return new WorkOrderSourceSummaryResponse(
                row.Id, WorkOrderSourceType.Contract, row.QuoteId, row.Id, row.Reference, row.CustomerLegalNameSnapshot,
                row.ServiceAddressSnapshot, row.Status, reasons.Count == 0, null, null, current?.Id, current?.Number, reasons);
        }).ToList();
    }
}
