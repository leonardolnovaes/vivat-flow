using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Quotes;
using Tsdt.Api.Customers;

namespace Tsdt.Api.Contracts;

public static class ContractEndpoints
{
    private const string StaleMessage = "Este contrato foi alterado por outro usuário. Atualize os dados e tente novamente.";

    public static void MapContractEndpoints(this WebApplication app)
    {
        var contracts = app.MapGroup("/api/contracts").RequireAuthorization(AuthorizationPolicies.CommercialAdmin).RequireFeatureEntitlement(FeatureCatalog.Contracts);
        contracts.MapGet("", ListAsync);
        contracts.MapGet("/{id:guid}", GetAsync);
        contracts.MapPost("", CreateFromQuoteAsync);
        contracts.MapPut("/{id:guid}", UpdateDraftAsync);
        contracts.MapPost("/{id:guid}/activate", ActivateAsync);
        contracts.MapPost("/{id:guid}/end", EndAsync);
        contracts.MapPost("/{id:guid}/cancel", CancelAsync);
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, ContractStatus? status, HttpContext context, ApplicationDbContext db)
    {
        var organizationId = TenantContext.OrganizationId(context);
        var currentPage = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var query = db.Contracts.AsNoTracking().Where(contract => contract.OrganizationId == organizationId);
        if (status.HasValue) query = query.Where(contract => contract.Status == status);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(contract => contract.UpdatedAtUtc).ThenByDescending(contract => contract.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync();
        return Results.Ok(new ContractListResponse(items.Select(ToSummary).ToList(), currentPage, size, total));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, ApplicationDbContext db)
    {
        var contract = await TenantContractQuery(db, id, context).AsNoTracking().Include(contract => contract.Items).SingleOrDefaultAsync();
        return contract is null ? Results.NotFound() : Results.Ok(ToDetail(contract));
    }

    private static async Task<IResult> CreateFromQuoteAsync(CreateContractFromQuoteRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var inputErrors = ValidateInput(request.StartDate, request.EndDate, request.PaymentTerms, request.Notes);
        if (inputErrors is not null) return Results.ValidationProblem(inputErrors);
        if (!Enum.IsDefined(request.Kind)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Selecione um tipo de contrato válido."] });
        var organizationId = TenantContext.OrganizationId(context);
        var quote = await db.Quotes.Include(item => item.Items).ThenInclude(item => item.Service).ThenInclude(service => service.ServiceLine).SingleOrDefaultAsync(item => item.Id == request.QuoteId && item.OrganizationId == organizationId);
        if (quote is null) return Results.NotFound();
        if (quote.Status != QuoteStatus.Approved) return Results.Conflict(new { error = "Somente orçamentos aprovados podem ser formalizados em contrato." });
        if (quote.Items.Count == 0 || quote.TotalAmount is null || quote.PaymentType is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["quoteId"] = ["O orçamento aprovado não possui escopo comercial válido para contrato."] });
        if (await db.Contracts.AnyAsync(contract => contract.OrganizationId == organizationId && contract.QuoteId == quote.Id && (contract.Status == ContractStatus.Draft || contract.Status == ContractStatus.Active))) return DuplicateConflict();

        var actor = Actor(context);
        var now = DateTimeOffset.UtcNow;
        var contract = new Contract
        {
            Id = Guid.NewGuid(), QuoteId = quote.Id, CustomerId = quote.CustomerId, CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, Kind = request.Kind,
            ApprovedTotalAmount = quote.TotalAmount.Value, PaymentType = quote.PaymentType.Value, InstallmentCount = quote.InstallmentCount,
            StartDate = request.StartDate, EndDate = request.EndDate, PaymentTerms = Trim(request.PaymentTerms, 2000), Notes = Trim(request.Notes, 2000),
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor, Version = Guid.NewGuid()
        };
        contract.Items = quote.Items.OrderBy(item => item.DisplayOrder).Select(item => new ContractItem
        {
            Id = Guid.NewGuid(), ContractId = contract.Id, QuoteItemId = item.Id, ServiceId = item.ServiceId,
            ServiceCodeSnapshot = item.ServiceCodeSnapshot, ServiceNameSnapshot = item.ServiceNameSnapshot,
            ServiceLineId = item.ServiceLineIdSnapshot, ServiceLineCodeSnapshot = item.ServiceLineCodeSnapshot, ServiceLineNameSnapshot = item.ServiceLineNameSnapshot,
            DisplayOrder = item.DisplayOrder
        }).ToList();
        db.Contracts.Add(contract);
        Audit(db, contract, actor, "CONTRACT_CREATED_FROM_QUOTE", null);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return DuplicateConflict(); }
        return Results.Created($"/api/contracts/{contract.Id}", ToDetail(contract));
    }

    private static async Task<IResult> UpdateDraftAsync(Guid id, UpdateContractDraftRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var errors = ValidateInput(request.StartDate, request.EndDate, request.PaymentTerms, request.Notes);
        if (errors is not null) return Results.ValidationProblem(errors);
        if (!Enum.IsDefined(request.Kind)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Selecione um tipo de contrato válido."] });
        var contract = await TenantContractQuery(db, id, context).Include(item => item.Items).SingleOrDefaultAsync();
        if (contract is null) return Results.NotFound();
        if (contract.Version != request.ExpectedVersion) return Stale();
        if (contract.Status != ContractStatus.Draft) return StateConflict();
        var fields = new List<string>();
        if (contract.Kind != request.Kind) { contract.Kind = request.Kind; fields.Add("Kind"); }
        if (contract.StartDate != request.StartDate) { contract.StartDate = request.StartDate; fields.Add("StartDate"); }
        if (contract.EndDate != request.EndDate) { contract.EndDate = request.EndDate; fields.Add("EndDate"); }
        var paymentTerms = Trim(request.PaymentTerms, 2000); if (contract.PaymentTerms != paymentTerms) { contract.PaymentTerms = paymentTerms; fields.Add("PaymentTerms"); }
        var notes = Trim(request.Notes, 2000); if (contract.Notes != notes) { contract.Notes = notes; fields.Add("Notes"); }
        if (fields.Count == 0) return Results.Ok(ToDetail(contract));
        Touch(contract, Actor(context)); Audit(db, contract, contract.UpdatedByUserId, "CONTRACT_DRAFT_UPDATED", string.Join(',', fields));
        try { await db.SaveChangesAsync(); return Results.Ok(ToDetail(contract)); } catch (DbUpdateConcurrencyException) { return Stale(); }
    }

    private static Task<IResult> ActivateAsync(Guid id, ContractVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request, ContractStatus.Draft, ContractStatus.Active, "CONTRACT_ACTIVATED", true, context, antiforgery, db);
    private static Task<IResult> EndAsync(Guid id, ContractVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request, ContractStatus.Active, ContractStatus.Ended, "CONTRACT_ENDED", false, context, antiforgery, db);
    private static Task<IResult> CancelAsync(Guid id, ContractVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request, null, ContractStatus.Cancelled, "CONTRACT_CANCELLED", false, context, antiforgery, db);

    private static async Task<IResult> TransitionAsync(Guid id, ContractVersionRequest request, ContractStatus? requiredStatus, ContractStatus target, string action, bool requiresStartDate, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var contract = await TenantContractQuery(db, id, context).Include(item => item.Items).SingleOrDefaultAsync();
        if (contract is null) return Results.NotFound();
        if (contract.Version != request.ExpectedVersion) return Stale();
        var allowed = requiredStatus.HasValue ? contract.Status == requiredStatus : contract.Status is ContractStatus.Draft or ContractStatus.Active;
        if (!allowed) return StateConflict();
        if (requiresStartDate && contract.StartDate is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["startDate"] = ["Informe a data de início antes de ativar o contrato."] });
        contract.Status = target; Touch(contract, Actor(context)); Audit(db, contract, contract.UpdatedByUserId, action, null);
        await CustomerActivityService.ReconcileAsync(db, contract.CustomerId, contract.UpdatedByUserId, CustomerActivityService.IsActiveRelationship(target), excludeContractId: contract.Id, cancelledContract: target == ContractStatus.Cancelled);
        try { await db.SaveChangesAsync(); return Results.Ok(ToDetail(contract)); } catch (DbUpdateConcurrencyException) { return Stale(); }
    }

    private static IQueryable<Contract> TenantContractQuery(ApplicationDbContext db, Guid id, HttpContext context) => db.Contracts.Where(contract => contract.Id == id && contract.OrganizationId == TenantContext.OrganizationId(context));
    private static Dictionary<string, string[]>? ValidateInput(DateOnly? startDate, DateOnly? endDate, string? paymentTerms, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (endDate.HasValue && (!startDate.HasValue || endDate < startDate)) errors["endDate"] = ["A data de término deve ser igual ou posterior à data de início."];
        if (paymentTerms?.Trim().Length > 2000) errors["paymentTerms"] = ["As condições comerciais devem ter no máximo 2.000 caracteres."];
        if (notes?.Trim().Length > 2000) errors["notes"] = ["As observações devem ter no máximo 2.000 caracteres."];
        return errors.Count == 0 ? null : errors;
    }
    private static ContractSummaryResponse ToSummary(Contract contract) => new(contract.Id, contract.QuoteId, contract.CustomerId, contract.CustomerLegalNameSnapshot, contract.Status, contract.ApprovedTotalAmount, contract.StartDate, contract.EndDate, contract.UpdatedAtUtc, contract.Kind);
    private static ContractDetailResponse ToDetail(Contract contract) => new(contract.Id, contract.QuoteId, contract.CustomerId, contract.CustomerLegalNameSnapshot, contract.Status, contract.ApprovedTotalAmount, contract.PaymentType, contract.InstallmentCount, contract.StartDate, contract.EndDate, contract.PaymentTerms, contract.Notes, contract.CreatedAtUtc, contract.UpdatedAtUtc, contract.Version, contract.Items.OrderBy(item => item.DisplayOrder).Select(item => new ContractItemResponse(item.Id, item.QuoteItemId, item.ServiceId, item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineId, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder)).ToList(), contract.Kind);
    private static void Touch(Contract contract, string actor) { contract.UpdatedAtUtc = DateTimeOffset.UtcNow; contract.UpdatedByUserId = actor; contract.Version = Guid.NewGuid(); }
    private static void Audit(ApplicationDbContext db, Contract contract, string actor, string action, string? fields) => db.ContractAuditRecords.Add(new ContractAuditRecord { Id = Guid.NewGuid(), ContractId = contract.Id, ActorUserId = actor, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, ChangedFields = fields });
    private static string Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private static async Task<bool> Csrf(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static IResult CsrfFailure() => Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." });
    private static IResult Stale() => Results.Conflict(new { error = StaleMessage });
    private static IResult StateConflict() => Results.Conflict(new { error = "Esta ação não é permitida para o status atual do contrato." });
    private static IResult DuplicateConflict() => Results.Conflict(new { error = "Já existe um contrato em rascunho ou ativo para este orçamento." });
    private static string? Trim(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
