using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tsdt.Api.Contracts;
using Tsdt.Api.Identity;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderEndpoints
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/work-orders").RequireAuthorization(AuthorizationPolicies.WorkOrderExecution);
        orders.MapGet("", ListAsync);
        orders.MapGet("/eligible-assignees", EligibleAssigneesAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapGet("/{id:guid}/history", HistoryAsync);
        orders.MapPost("/from-contract", CreateAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPut("/{id:guid}/planning", PlanningAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPost("/{id:guid}/schedule", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Scheduled, null, context, antiforgery, db)).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPost("/{id:guid}/start", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.InProgress, null, context, antiforgery, db));
        orders.MapPost("/{id:guid}/complete", (Guid id, CompleteWorkOrderRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.AwaitingClosure, request.CompletionNotes, context, antiforgery, db));
        orders.MapPost("/{id:guid}/close", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Closed, null, context, antiforgery, db)).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPost("/{id:guid}/cancel", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Cancelled, null, context, antiforgery, db)).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
    }

    private static bool IsManagement(HttpContext context) => context.User.IsInRole(IdentityRoles.Admin) || context.User.IsInRole(IdentityRoles.Manager);
    private static string Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private static IQueryable<WorkOrder> Visible(ApplicationDbContext db, HttpContext context) =>
        db.WorkOrders.Where(order => order.OrganizationId == TenantContext.OrganizationId(context) && (IsManagement(context) || order.AssignedUserId == Actor(context)));

    private static async Task<IResult> ListAsync(int? page, int? pageSize, WorkOrderStatus? status, string? assignedUserId, Guid? quoteId, Guid? contractId, HttpContext context, ApplicationDbContext db)
    {
        if (status.HasValue && !Enum.IsDefined(status.Value)) return Error("status", "Informe um status válido.");
        var currentPage = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var query = Visible(db, context).AsNoTracking();
        if (status.HasValue) query = query.Where(order => order.Status == status);
        if (IsManagement(context) && !string.IsNullOrWhiteSpace(assignedUserId)) query = query.Where(order => order.AssignedUserId == assignedUserId);
        if (quoteId.HasValue) query = query.Where(order => order.QuoteId == quoteId);
        if (contractId.HasValue) query = query.Where(order => order.ContractId == contractId);
        var total = await query.CountAsync();
        var offset = ((long)currentPage - 1) * size;
        if (offset > int.MaxValue) return Results.Ok(new WorkOrderListResponse([], currentPage, size, total));
        // SQLite used by unit tests does not translate DateTimeOffset ordering.
        var items = db.Database.ProviderName?.Contains("Sqlite") == true
            ? (await query.ToListAsync()).OrderByDescending(order => order.UpdatedAtUtc).ThenByDescending(order => order.Id).Skip((int)offset).Take(size).ToList()
            : await query.OrderByDescending(order => order.UpdatedAtUtc).ThenByDescending(order => order.Id).Skip((int)offset).Take(size).ToListAsync();
        return Results.Ok(new WorkOrderListResponse(items.Select(Summary).ToList(), currentPage, size, total));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, ApplicationDbContext db)
    {
        var order = await Visible(db, context).AsNoTracking().Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == id);
        return order is null ? Results.NotFound() : Results.Ok(Detail(order));
    }

    private static async Task<IResult> HistoryAsync(Guid id, HttpContext context, ApplicationDbContext db)
    {
        if (!await Visible(db, context).AnyAsync(item => item.Id == id)) return Results.NotFound();
        var query = db.WorkOrderAuditRecords.AsNoTracking().Where(item => item.WorkOrderId == id);
        var history = db.Database.ProviderName?.Contains("Sqlite") == true
            ? (await query.ToListAsync()).OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id).ToList()
            : await query.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id).ToListAsync();
        return Results.Ok(history.Select(item => new WorkOrderAuditResponse(item.Action, item.ActorUserId, item.OccurredAtUtc, item.ChangedFields)));
    }

    private static async Task<IResult> EligibleAssigneesAsync(HttpContext context, ApplicationDbContext db)
    {
        var organizationId = TenantContext.OrganizationId(context);
        var users = await (from user in db.Users.AsNoTracking()
                           join membership in db.UserRoles on user.Id equals membership.UserId
                           join role in db.Roles on membership.RoleId equals role.Id
                           where user.OrganizationId == organizationId && user.IsActive && !user.IsPlatformAdministrator &&
                                 (role.Name == IdentityRoles.Admin || role.Name == IdentityRoles.Manager || role.Name == IdentityRoles.User)
                           orderby user.FullName
                           select new EligibleProfessionalResponse(user.Id, user.FullName, user.Email!, role.Name!)).ToListAsync();
        return Results.Ok(users);
    }

    private static async Task<ApplicationUser?> AssigneeAsync(ApplicationDbContext db, Guid organizationId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return await (from user in db.Users
                      join membership in db.UserRoles on user.Id equals membership.UserId
                      join role in db.Roles on membership.RoleId equals role.Id
                      where user.Id == userId && user.OrganizationId == organizationId && user.IsActive && !user.IsPlatformAdministrator &&
                            (role.Name == IdentityRoles.Admin || role.Name == IdentityRoles.Manager || role.Name == IdentityRoles.User)
                      select user).SingleOrDefaultAsync();
    }

    private static async Task<IResult> CreateAsync(CreateWorkOrderRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var errors = ValidatePlanning(request.ScheduledStart, request.ScheduledEnd, request.OperationalNotes);
        if (errors is not null) return Results.ValidationProblem(errors);
        var organizationId = TenantContext.OrganizationId(context);
        var contract = await db.Contracts.AsNoTracking().Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == request.SourceId && item.OrganizationId == organizationId);
        if (contract is null) return Results.NotFound();
        if (contract.Status != ContractStatus.Active) return StateConflict("Somente contratos ativos podem originar uma OS.");
        var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(item => item.Id == contract.QuoteId && item.OrganizationId == organizationId);
        if (quote is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(quote.ServiceAddressSnapshot)) return Error("sourceId", "O orçamento de origem precisa ter um endereço de serviço válido.");
        if (contract.Items.Count == 0)
            return Error("sourceId", "A origem precisa conter pelo menos um serviço operacional.");
        if (await db.WorkOrders.AnyAsync(item => item.OrganizationId == organizationId && item.QuoteId == quote.Id && item.Status != WorkOrderStatus.Cancelled)) return Duplicate();
        var assignee = await AssigneeAsync(db, organizationId, request.AssignedUserId);
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId) && assignee is null) return Error("assignedUserId", "Selecione um profissional ativo e elegível desta organização.");

        var now = DateTimeOffset.UtcNow; var actor = Actor(context);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var number = await AllocateNumberAsync(db, organizationId, TimeZoneInfo.ConvertTime(now, SaoPaulo).Year);
        if (number is null) return StateConflict("A numeração anual de OS atingiu o limite.");
        var order = new WorkOrder
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Number = number, SourceType = WorkOrderSourceType.Contract, QuoteId = quote.Id, ContractId = contract.Id,
            CustomerId = quote.CustomerId, CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, ServiceAddressSnapshot = quote.ServiceAddressSnapshot.Trim(),
            AssignedUserId = assignee?.Id, AssignedUserNameSnapshot = assignee?.FullName, AssignedUserEmailSnapshot = assignee?.Email,
            ScheduledStart = request.ScheduledStart, ScheduledEnd = request.ScheduledEnd, OperationalNotes = Trim(request.OperationalNotes),
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor, Version = Guid.NewGuid()
        };
        order.Items = contract.Items.Select(item => new WorkOrderItem { Id = Guid.NewGuid(), WorkOrderId = order.Id, QuoteItemId = item.QuoteItemId, ContractItemId = item.Id, ServiceId = item.ServiceId, ServiceCodeSnapshot = item.ServiceCodeSnapshot, ServiceNameSnapshot = item.ServiceNameSnapshot, ServiceLineIdSnapshot = item.ServiceLineId, ServiceLineCodeSnapshot = item.ServiceLineCodeSnapshot, ServiceLineNameSnapshot = item.ServiceLineNameSnapshot, DisplayOrder = item.DisplayOrder }).ToList();
        db.WorkOrders.Add(order);
        Audit(db, order, actor, "WORK_ORDER_CREATED_FROM_CONTRACT", null);
        try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
        catch (DbUpdateException) { return Duplicate(); }
        return Results.Created($"/api/work-orders/{order.Id}", Detail(order));
    }

    private static async Task<IResult> PlanningAsync(Guid id, UpdateWorkOrderPlanningRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var errors = ValidatePlanning(request.ScheduledStart, request.ScheduledEnd, request.OperationalNotes);
        if (errors is not null) return Results.ValidationProblem(errors);
        var order = await Visible(db, context).Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == id);
        if (order is null) return Results.NotFound();
        if (order.Version != request.ExpectedVersion) return Stale();
        if (!WorkOrderRules.CanPlan(order.Status)) return InvalidState();
        var assignee = await AssigneeAsync(db, order.OrganizationId, request.AssignedUserId);
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId) && assignee is null) return Error("assignedUserId", "Selecione um profissional ativo e elegível desta organização.");
        if (order.Status == WorkOrderStatus.Scheduled && (assignee is null || request.ScheduledStart is null || request.ScheduledEnd is null))
            return Error("assignedUserId", "Uma OS agendada precisa manter profissional, início e término.");
        var fields = new List<string>();
        if (order.AssignedUserId != assignee?.Id) { order.AssignedUserId = assignee?.Id; order.AssignedUserNameSnapshot = assignee?.FullName; order.AssignedUserEmailSnapshot = assignee?.Email; fields.Add("AssignedUserId"); }
        if (order.ScheduledStart != request.ScheduledStart) { order.ScheduledStart = request.ScheduledStart; fields.Add("ScheduledStart"); }
        if (order.ScheduledEnd != request.ScheduledEnd) { order.ScheduledEnd = request.ScheduledEnd; fields.Add("ScheduledEnd"); }
        var notes = Trim(request.OperationalNotes); if (order.OperationalNotes != notes) { order.OperationalNotes = notes; fields.Add("OperationalNotes"); }
        if (fields.Count == 0) return Results.Ok(Detail(order));
        Touch(order, Actor(context)); Audit(db, order, Actor(context), "WORK_ORDER_PLANNING_UPDATED", string.Join(',', fields));
        try { await db.SaveChangesAsync(); return Results.Ok(Detail(order)); } catch (DbUpdateConcurrencyException) { return Stale(); }
    }

    private static async Task<IResult> TransitionAsync(Guid id, Guid expectedVersion, WorkOrderStatus target, string? completionNotes, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var order = await Visible(db, context).Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == id);
        if (order is null) return Results.NotFound();
        if (order.Version != expectedVersion) return Stale();
        if (!WorkOrderRules.CanTransition(order.Status, target)) return InvalidState();
        if (target == WorkOrderStatus.Scheduled)
        {
            if (order.AssignedUserId is null || await AssigneeAsync(db, order.OrganizationId, order.AssignedUserId) is null) return Error("assignedUserId", "Atribua um profissional ativo e elegível antes de agendar.");
            if (order.ScheduledStart is null || order.ScheduledEnd is null || !WorkOrderRules.HasValidSchedule(order.ScheduledStart, order.ScheduledEnd)) return Error("scheduledStart", "Informe início e término válidos antes de agendar.");
            if (string.IsNullOrWhiteSpace(order.ServiceAddressSnapshot)) return Error("serviceAddress", "A OS precisa ter um endereço de serviço válido.");
            if (order.Items.Count == 0) return Error("items", "A OS precisa conter pelo menos um serviço.");
        }
        if (target == WorkOrderStatus.AwaitingClosure && completionNotes?.Trim().Length > 2000) return Error("completionNotes", "As observações de conclusão devem ter no máximo 2.000 caracteres.");
        var now = DateTimeOffset.UtcNow;
        order.Status = target;
        if (target == WorkOrderStatus.InProgress) order.StartedAtUtc = now;
        if (target == WorkOrderStatus.AwaitingClosure) { order.ExecutionCompletedAtUtc = now; order.CompletionNotes = Trim(completionNotes); }
        if (target == WorkOrderStatus.Closed) order.ClosedAtUtc = now;
        if (target == WorkOrderStatus.Cancelled) order.CancelledAtUtc = now;
        Touch(order, Actor(context));
        var action = target switch { WorkOrderStatus.Scheduled => "WORK_ORDER_SCHEDULED", WorkOrderStatus.InProgress => "WORK_ORDER_STARTED", WorkOrderStatus.AwaitingClosure => "WORK_ORDER_EXECUTION_COMPLETED", WorkOrderStatus.Closed => "WORK_ORDER_CLOSED", _ => "WORK_ORDER_CANCELLED" };
        Audit(db, order, Actor(context), action, target == WorkOrderStatus.AwaitingClosure && order.CompletionNotes is not null ? "CompletionNotes" : null);
        try { await db.SaveChangesAsync(); return Results.Ok(Detail(order)); } catch (DbUpdateConcurrencyException) { return Stale(); }
    }

    private static async Task<string?> AllocateNumberAsync(ApplicationDbContext db, Guid organizationId, int year)
    {
        if (db.Database.ProviderName?.Contains("Npgsql") != true)
        {
            var counter = await db.WorkOrderNumberCounters.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Year == year);
            if (counter is null) { counter = new WorkOrderNumberCounter { OrganizationId = organizationId, Year = year, LastNumber = 1 }; db.WorkOrderNumberCounters.Add(counter); }
            else if (counter.LastNumber < 999999) counter.LastNumber++;
            else return null;
            return $"OS-{year}-{counter.LastNumber:000000}";
        }
        var connection = db.Database.GetDbConnection(); if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "INSERT INTO \"WorkOrderNumberCounters\" (\"OrganizationId\", \"Year\", \"LastNumber\") VALUES (@organization, @year, 1) ON CONFLICT (\"OrganizationId\", \"Year\") DO UPDATE SET \"LastNumber\" = \"WorkOrderNumberCounters\".\"LastNumber\" + 1 WHERE \"WorkOrderNumberCounters\".\"LastNumber\" < 999999 RETURNING \"LastNumber\";";
        var organization = command.CreateParameter(); organization.ParameterName = "organization"; organization.Value = organizationId; command.Parameters.Add(organization);
        var yearParameter = command.CreateParameter(); yearParameter.ParameterName = "year"; yearParameter.Value = year; command.Parameters.Add(yearParameter);
        var value = await command.ExecuteScalarAsync(); return value is null ? null : $"OS-{year}-{Convert.ToInt32(value):000000}";
    }

    private static Dictionary<string, string[]>? ValidatePlanning(DateTimeOffset? start, DateTimeOffset? end, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WorkOrderRules.HasValidSchedule(start, end)) errors["scheduledEnd"] = ["O término deve ser posterior ao início."];
        if (notes?.Trim().Length > 2000) errors["operationalNotes"] = ["As observações operacionais devem ter no máximo 2.000 caracteres."];
        return errors.Count == 0 ? null : errors;
    }
    private static WorkOrderSummaryResponse Summary(WorkOrder order) => new(order.Id, order.Number, order.SourceType, order.QuoteId, order.ContractId, order.CustomerId, order.CustomerLegalNameSnapshot, order.Status, order.AssignedUserId, order.AssignedUserNameSnapshot, order.ScheduledStart, order.ScheduledEnd, order.UpdatedAtUtc);
    private static WorkOrderDetailResponse Detail(WorkOrder order) => new(order.Id, order.Number, order.SourceType, order.QuoteId, order.ContractId, order.CustomerId, order.CustomerLegalNameSnapshot, order.ServiceAddressSnapshot, order.Status, order.AssignedUserId, order.AssignedUserNameSnapshot, order.AssignedUserEmailSnapshot, order.ScheduledStart, order.ScheduledEnd, order.StartedAtUtc, order.ExecutionCompletedAtUtc, order.ClosedAtUtc, order.CancelledAtUtc, order.OperationalNotes, order.CompletionNotes, order.CreatedAtUtc, order.UpdatedAtUtc, order.Version, order.Items.OrderBy(item => item.DisplayOrder).Select(item => new WorkOrderItemResponse(item.Id, item.QuoteItemId, item.ContractItemId, item.ServiceId, item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineIdSnapshot, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder)).ToList());
    private static void Touch(WorkOrder order, string actor) { order.UpdatedAtUtc = DateTimeOffset.UtcNow; order.UpdatedByUserId = actor; order.Version = Guid.NewGuid(); }
    private static void Audit(ApplicationDbContext db, WorkOrder order, string actor, string action, string? fields) => db.WorkOrderAuditRecords.Add(new WorkOrderAuditRecord { Id = Guid.NewGuid(), WorkOrderId = order.Id, ActorUserId = actor, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, ChangedFields = fields });
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static async Task<bool> Csrf(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static IResult CsrfFailure() => Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." });
    private static IResult Stale() => Results.Conflict(new { error = "Esta OS foi alterada por outro usuário. Atualize os dados e tente novamente." });
    private static IResult InvalidState() => StateConflict("Esta ação não é permitida para o status atual da OS.");
    private static IResult StateConflict(string message) => Results.Conflict(new { error = message });
    private static IResult Duplicate() => StateConflict("Já existe uma OS não cancelada para este escopo.");
    private static IResult Error(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
