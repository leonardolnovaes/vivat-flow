using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderEndpoints
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/api/work-orders").RequireAuthorization(AuthorizationPolicies.WorkOrderExecution).RequireFeatureEntitlement(FeatureCatalog.WorkOrders);
        orders.MapGet("", ListAsync);
        orders.MapGet("/agenda", AgendaAsync).RequireFeatureEntitlement(FeatureCatalog.Schedule);
        orders.MapGet("/eligible-assignees", EligibleAssigneesAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        WorkOrderSourceEndpoints.Map(orders);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapGet("/{id:guid}/history", HistoryAsync);
        orders.MapPost("/from-contract", CreateAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPut("/{id:guid}/planning", PlanningAsync).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
        orders.MapPost("/{id:guid}/schedule", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Scheduled, null, context, antiforgery, db)).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement).RequireFeatureEntitlement(FeatureCatalog.Schedule);
        orders.MapPost("/{id:guid}/start", (Guid id, WorkOrderVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.InProgress, null, context, antiforgery, db));
        orders.MapPost("/{id:guid}/complete", (Guid id, CompleteWorkOrderRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Completed, request.CompletionNotes, context, antiforgery, db));
        orders.MapPost("/{id:guid}/cancel", (Guid id, CancelWorkOrderRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => TransitionAsync(id, request.ExpectedVersion, WorkOrderStatus.Cancelled, request.CancellationReason, context, antiforgery, db)).RequireAuthorization(AuthorizationPolicies.WorkOrderManagement);
    }

    private static bool IsManagement(HttpContext context) => context.User.IsInRole(IdentityRoles.Admin) || context.User.IsInRole(IdentityRoles.Manager);
    private static string Actor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private static IQueryable<WorkOrder> Visible(ApplicationDbContext db, HttpContext context) =>
        WorkOrderAgendaQuery.Visible(db.WorkOrders, TenantContext.OrganizationId(context), IsManagement(context), Actor(context));

    private static async Task<IResult> AgendaAsync(string? from, string? to, string? assignedUserId, HttpContext context, ApplicationDbContext db)
    {
        if (!WorkOrderAgendaQuery.TryTimestamp(from, out var start)) return Error("from", "Informe o início com data, horário e fuso horário válidos.");
        if (!WorkOrderAgendaQuery.TryTimestamp(to, out var end)) return Error("to", "Informe o término com data, horário e fuso horário válidos.");
        var error = WorkOrderAgendaQuery.RangeError(start, end);
        if (error is not null) return Error("to", error);
        var query = WorkOrderAgendaQuery.InRange(Visible(db, context).AsNoTracking(), start, end, db.WorkOrderAuditRecords);
        query = WorkOrderAgendaQuery.ForAssignee(query, IsManagement(context), assignedUserId);
        var entries = await query.OrderBy(order => order.ScheduledStartDate).ThenBy(order => order.ScheduledStartTime)
            .ThenBy(order => order.ScheduledEndDate).ThenBy(order => order.ScheduledEndTime)
            .ThenBy(order => order.Number).ThenBy(order => order.Id)
            .Select(order => new WorkOrderAgendaResponse(order.Id, order.Number, order.CustomerLegalNameSnapshot,
                order.ServiceAddressSnapshot, order.Status, order.AssignedUserId, order.AssignedUserNameSnapshot,
                order.ScheduledStartDate!.Value, order.ScheduledStartTime, order.ScheduledEndDate, order.ScheduledEndTime,
                order.Items.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Id)
                    .Select(item => new WorkOrderAgendaServiceResponse(item.ServiceCodeSnapshot, item.ServiceNameSnapshot)).ToList()))
            .ToListAsync();
        return Results.Ok(entries);
    }

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
        return Results.Ok(history.Select(item => new WorkOrderAuditResponse(item.Action, item.ActorUserId, item.ActorNameSnapshot ?? (string.IsNullOrEmpty(item.ActorUserId) ? "Sistema" : "Usuário não disponível"), item.OccurredAtUtc, item.ChangedFields, item.CancellationReason)));
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

    private static async Task<IResult> CreateAsync(CreateWorkOrderRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db, FeatureEntitlementService entitlements)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var organizationId = TenantContext.OrganizationId(context);
        var hasSchedule = await entitlements.IsEnabledAsync(organizationId, FeatureCatalog.Schedule, context.RequestAborted);
        if (!WorkOrderSchedulingRules.CanCreateWithSchedule(hasSchedule, request)) return ScheduleForbidden();
        var errors = ValidatePlanning(request.ScheduledStartDate, request.ScheduledEndDate, request.ScheduledStartTime, request.ScheduledEndTime, request.OperationalNotes);
        if (errors is not null) return Results.ValidationProblem(errors);

        var contract = await db.Contracts.AsNoTracking().Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == request.SourceId && item.OrganizationId == organizationId);
        if (contract is null) return Results.NotFound();
        if (contract.Status != ContractStatus.Active) return StateConflict("Somente contratos ativos podem originar uma OS.");

        var quote = await db.Quotes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == contract.QuoteId && item.OrganizationId == organizationId);
        if (quote is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(quote.ServiceAddressSnapshot))
            return Error("sourceId", "O contrato precisa ter um endereço de serviço válido.");
        if (contract.Items.Count == 0)
            return Error("sourceId", "O contrato precisa conter pelo menos um serviço operacional.");

        var previousOrderStatuses = await db.WorkOrders
            .Where(item => item.OrganizationId == organizationId && (item.ContractId == contract.Id || (item.ContractId == null && item.QuoteId == contract.QuoteId)))
            .Select(item => item.Status)
            .ToListAsync();
        if (!WorkOrderRules.CanCreateFromContract(contract.Status, previousOrderStatuses)) return Duplicate();

        var assignee = await AssigneeAsync(db, organizationId, request.AssignedUserId);
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId) && assignee is null)
            return Error("assignedUserId", "Selecione um profissional ativo e elegível desta organização.");

        var now = DateTimeOffset.UtcNow;
        var actor = Actor(context);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var number = await AllocateNumberAsync(db, organizationId, TimeZoneInfo.ConvertTime(now, SaoPaulo).Year);
        if (number is null) return StateConflict("A numeração anual de OS atingiu o limite.");

        var order = new WorkOrder
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Number = number, SourceType = WorkOrderSourceType.Contract,
            QuoteId = quote.Id, ContractId = contract.Id, CustomerId = quote.CustomerId,
            CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, ServiceAddressSnapshot = quote.ServiceAddressSnapshot.Trim(),
            AssignedUserId = assignee?.Id, AssignedUserNameSnapshot = assignee?.FullName, AssignedUserEmailSnapshot = assignee?.Email,
            ScheduledStartDate = request.ScheduledStartDate, ScheduledEndDate = request.ScheduledEndDate, ScheduledStartTime = request.ScheduledStartTime, ScheduledEndTime = request.ScheduledEndTime, OperationalNotes = Trim(request.OperationalNotes),
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor, Version = Guid.NewGuid()
        };
        order.Items = contract.Items.Select(item => new WorkOrderItem
        {
            Id = Guid.NewGuid(), WorkOrderId = order.Id, QuoteItemId = item.QuoteItemId, ContractItemId = item.Id,
            ServiceId = item.ServiceId, ServiceCodeSnapshot = item.ServiceCodeSnapshot, ServiceNameSnapshot = item.ServiceNameSnapshot,
            ServiceLineIdSnapshot = item.ServiceLineId, ServiceLineCodeSnapshot = item.ServiceLineCodeSnapshot,
            ServiceLineNameSnapshot = item.ServiceLineNameSnapshot, DisplayOrder = item.DisplayOrder
        }).ToList();

        db.WorkOrders.Add(order);
        await AuditAsync(db, order, actor, "WORK_ORDER_CREATED_FROM_CONTRACT", null);
        await CustomerActivityService.ReconcileAsync(db, order.CustomerId, actor, true, excludeWorkOrderId: order.Id);
        try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
        catch (DbUpdateException) { return Duplicate(); }
        return Results.Created($"/api/work-orders/{order.Id}", Detail(order));
    }

    private static async Task<IResult> PlanningAsync(Guid id, UpdateWorkOrderPlanningRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db, FeatureEntitlementService entitlements)
    {
        if (!await Csrf(context, antiforgery)) return CsrfFailure();
        var order = await Visible(db, context).Include(item => item.Items).SingleOrDefaultAsync(item => item.Id == id);
        if (order is null) return Results.NotFound();
        if (order.Version != request.ExpectedVersion) return Stale();
        if (!WorkOrderRules.CanPlan(order.Status)) return InvalidState();
        var hasSchedule = await entitlements.IsEnabledAsync(TenantContext.OrganizationId(context), FeatureCatalog.Schedule, context.RequestAborted);
        if (!WorkOrderSchedulingRules.CanUpdateSchedule(hasSchedule, order, request)) return ScheduleForbidden();
        var errors = ValidatePlanning(request.ScheduledStartDate, request.ScheduledEndDate, request.ScheduledStartTime, request.ScheduledEndTime, request.OperationalNotes);
        if (errors is not null) return Results.ValidationProblem(errors);
        var assignee = await AssigneeAsync(db, order.OrganizationId, request.AssignedUserId);
        if (!string.IsNullOrWhiteSpace(request.AssignedUserId) && assignee is null) return Error("assignedUserId", "Selecione um profissional ativo e elegível desta organização.");
        if (order.Status == WorkOrderStatus.Scheduled && (assignee is null || request.ScheduledStartDate is null))
            return Error("assignedUserId", "Uma OS agendada precisa manter profissional e data de início.");
        var fields = new List<string>();
        if (order.AssignedUserId != assignee?.Id) { order.AssignedUserId = assignee?.Id; order.AssignedUserNameSnapshot = assignee?.FullName; order.AssignedUserEmailSnapshot = assignee?.Email; fields.Add("AssignedUserId"); }
        if (order.ScheduledStartDate != request.ScheduledStartDate) { order.ScheduledStartDate = request.ScheduledStartDate; fields.Add("ScheduledStartDate"); }
        if (order.ScheduledEndDate != request.ScheduledEndDate) { order.ScheduledEndDate = request.ScheduledEndDate; fields.Add("ScheduledEndDate"); }
        if (order.ScheduledStartTime != request.ScheduledStartTime) { order.ScheduledStartTime = request.ScheduledStartTime; fields.Add("ScheduledStartTime"); }
        if (order.ScheduledEndTime != request.ScheduledEndTime) { order.ScheduledEndTime = request.ScheduledEndTime; fields.Add("ScheduledEndTime"); }
        var notes = Trim(request.OperationalNotes); if (order.OperationalNotes != notes) { order.OperationalNotes = notes; fields.Add("OperationalNotes"); }
        if (fields.Count == 0) return Results.Ok(Detail(order));
        Touch(order, Actor(context)); await AuditAsync(db, order, Actor(context), "WORK_ORDER_PLANNING_UPDATED", string.Join(',', fields));
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
            if (!WorkOrderRules.CanSchedule(order.AssignedUserId, order.ScheduledStartDate) || !WorkOrderRules.HasValidSchedule(order.ScheduledStartDate, order.ScheduledEndDate, order.ScheduledStartTime, order.ScheduledEndTime)) return Error("scheduledStartDate", "Informe a data de início antes de agendar.");
            if (string.IsNullOrWhiteSpace(order.ServiceAddressSnapshot)) return Error("serviceAddress", "A OS precisa ter um endereço de serviço válido.");
            if (order.Items.Count == 0) return Error("items", "A OS precisa conter pelo menos um serviço.");
        }
        if (target == WorkOrderStatus.Completed && completionNotes?.Trim().Length > 2000) return Error("completionNotes", "As observações de conclusão devem ter no máximo 2.000 caracteres.");
        if (target == WorkOrderStatus.Cancelled && completionNotes?.Trim().Length > 500) return Error("cancellationReason", "Use no máximo 500 caracteres para o motivo do cancelamento.");
        var now = DateTimeOffset.UtcNow;
        order.Status = target;
        if (target == WorkOrderStatus.InProgress) order.StartedAtUtc = now;
        if (target == WorkOrderStatus.Completed) { order.ExecutionCompletedAtUtc = now; order.CompletionNotes = Trim(completionNotes); }

        if (target == WorkOrderStatus.Cancelled) order.CancelledAtUtc = now;
        Touch(order, Actor(context));
        var action = target switch { WorkOrderStatus.Scheduled => "WORK_ORDER_SCHEDULED", WorkOrderStatus.InProgress => "WORK_ORDER_STARTED", WorkOrderStatus.Completed => "WORK_ORDER_EXECUTION_COMPLETED", _ => "WORK_ORDER_CANCELLED" };
        await AuditAsync(db, order, Actor(context), action, target == WorkOrderStatus.Completed && order.CompletionNotes is not null ? "CompletionNotes" : null, target == WorkOrderStatus.Cancelled ? Trim(completionNotes) : null);
        await CustomerActivityService.ReconcileAsync(db, order.CustomerId, Actor(context), CustomerActivityService.IsActiveRelationship(target), excludeWorkOrderId: order.Id, cancelledWorkOrder: target == WorkOrderStatus.Cancelled);
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

    private static Dictionary<string, string[]>? ValidatePlanning(DateOnly? start, DateOnly? end, TimeOnly? startTime, TimeOnly? endTime, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WorkOrderRules.HasValidSchedule(start, end, startTime, endTime)) errors["scheduledEndDate"] = ["Revise as datas e horários: informe a data correspondente a cada horário e um término posterior ao início."];
        if (notes?.Trim().Length > 2000) errors["operationalNotes"] = ["As observações operacionais devem ter no máximo 2.000 caracteres."];
        return errors.Count == 0 ? null : errors;
    }
    private static WorkOrderSummaryResponse Summary(WorkOrder order) => new(order.Id, order.Number, order.SourceType, order.QuoteId, order.ContractId, order.CustomerId, order.CustomerLegalNameSnapshot, order.Status, order.AssignedUserId, order.AssignedUserNameSnapshot, order.ScheduledStartDate, order.ScheduledStartTime, order.ScheduledEndDate, order.ScheduledEndTime, order.UpdatedAtUtc);
    private static WorkOrderDetailResponse Detail(WorkOrder order) => new(order.Id, order.Number, order.SourceType, order.QuoteId, order.ContractId, order.CustomerId, order.CustomerLegalNameSnapshot, order.ServiceAddressSnapshot, order.Status, order.AssignedUserId, order.AssignedUserNameSnapshot, order.AssignedUserEmailSnapshot, order.ScheduledStartDate, order.ScheduledStartTime, order.ScheduledEndDate, order.ScheduledEndTime, order.StartedAtUtc, order.ExecutionCompletedAtUtc, order.ClosedAtUtc, order.CancelledAtUtc, order.OperationalNotes, order.CompletionNotes, order.CreatedAtUtc, order.UpdatedAtUtc, order.Version, order.Items.OrderBy(item => item.DisplayOrder).Select(item => new WorkOrderItemResponse(item.Id, item.QuoteItemId, item.ContractItemId, item.ServiceId, item.ServiceCodeSnapshot, item.ServiceNameSnapshot, item.ServiceLineIdSnapshot, item.ServiceLineCodeSnapshot, item.ServiceLineNameSnapshot, item.DisplayOrder)).ToList());
    private static void Touch(WorkOrder order, string actor) { order.UpdatedAtUtc = DateTimeOffset.UtcNow; order.UpdatedByUserId = actor; order.Version = Guid.NewGuid(); }
    private static async Task AuditAsync(ApplicationDbContext db, WorkOrder order, string actor, string action, string? fields, string? cancellationReason = null)
    {
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == actor && user.OrganizationId == order.OrganizationId);
        db.WorkOrderAuditRecords.Add(WorkOrderAudit.Create(order, actor, user, action, fields, cancellationReason));
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static async Task<bool> Csrf(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static IResult CsrfFailure() => Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." });
    private static IResult Stale() => Results.Conflict(new { error = "Esta OS foi alterada por outro usuário. Atualize os dados e tente novamente." });
    private static IResult InvalidState() => StateConflict("Esta ação não é permitida para o status atual da OS.");
    private static IResult ScheduleForbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);
    private static IResult StateConflict(string message) => Results.Conflict(new { error = message });
    private static IResult Duplicate() => StateConflict("Já existe uma OS não cancelada para este escopo.");
    private static IResult Error(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
