using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Services;

public static partial class ServiceEndpoints
{
    private const string StaleMessage = "Este servi\u00e7o foi alterado por outro usu\u00e1rio. Atualize os dados e tente novamente.";

    public static void MapServiceEndpoints(this WebApplication app)
    {
        var services = app.MapGroup("/api/services").RequireAuthorization();
        services.MapGet("", ListAsync);
        services.MapGet("/{id:guid}", GetAsync);
        services.MapPost("", CreateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        services.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        services.MapPost("/{id:guid}/activate", ActivateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        services.MapPost("/{id:guid}/deactivate", DeactivateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, string? search, bool? isActive, string? sort, string? direction, ClaimsPrincipal user, ApplicationDbContext db)
    {
        if (search?.Length > 200) return Results.ValidationProblem(new Dictionary<string, string[]> { ["search"] = ["A busca deve ter no m\u00e1ximo 200 caracteres."] });
        var requestedPage = Math.Max(page ?? 1, 1);
        var requestedPageSize = Math.Clamp(pageSize ?? 25, 1, 100);
        IQueryable<Service> query = db.Services.AsNoTracking();
        if (user.IsInRole(IdentityRoles.User)) query = query.Where(service => service.IsActive);
        else if (isActive.HasValue) query = query.Where(service => service.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim().ToUpperInvariant();
            query = query.Where(service => service.Code.Contains(normalized) || service.Name.ToUpper().Contains(normalized));
        }
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sort?.ToLowerInvariant(), descending) switch
        {
            ("name", false) => query.OrderBy(service => service.Name).ThenBy(service => service.Id),
            ("name", true) => query.OrderByDescending(service => service.Name).ThenByDescending(service => service.Id),
            ("createdat", false) => query.OrderBy(service => service.CreatedAtUtc).ThenBy(service => service.Id),
            ("createdat", true) => query.OrderByDescending(service => service.CreatedAtUtc).ThenByDescending(service => service.Id),
            ("updatedat", false) => query.OrderBy(service => service.UpdatedAtUtc).ThenBy(service => service.Id),
            ("updatedat", true) => query.OrderByDescending(service => service.UpdatedAtUtc).ThenByDescending(service => service.Id),
            (_, true) => query.OrderByDescending(service => service.Code).ThenByDescending(service => service.Id),
            _ => query.OrderBy(service => service.Code).ThenBy(service => service.Id)
        };
        var total = await query.CountAsync();
        var offset = ((long)requestedPage - 1) * requestedPageSize;
        if (offset > int.MaxValue) return Results.Ok(new ServiceListResponse([], requestedPage, requestedPageSize, total));
        var items = await query.Skip((int)offset).Take(requestedPageSize).Select(service => ToSummary(service)).ToListAsync();
        return Results.Ok(new ServiceListResponse(items, requestedPage, requestedPageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ApplicationDbContext db)
    {
        IQueryable<Service> query = db.Services.AsNoTracking().Where(service => service.Id == id);
        if (user.IsInRole(IdentityRoles.User)) query = query.Where(service => service.IsActive);
        var service = await query.SingleOrDefaultAsync();
        return service is null ? Results.NotFound() : Results.Ok(ToDetail(service));
    }

    private static async Task<IResult> CreateAsync(CreateServiceRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = Validate(request.Code, request.Name, request.Description, request.BasePrice);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        if (await db.Services.AnyAsync(service => service.Code == input.Code)) return DuplicateCode();
        var now = DateTimeOffset.UtcNow; var actor = GetActor(context);
        var service = new Service { Id = Guid.NewGuid(), Code = input.Code!, Name = input.Name!, Description = input.Description, BasePrice = input.BasePrice, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor, Version = Guid.NewGuid() };
        db.Services.Add(service); AddAudit(db, service.Id, actor, "SERVICE_CREATED", "Code,Name,Description,BasePrice");
        return await SaveAsync(db, () => Results.Created($"/api/services/{service.Id}", ToDetail(service)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateServiceRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = Validate(request.Code, request.Name, request.Description, request.BasePrice);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var service = await db.Services.SingleOrDefaultAsync(service => service.Id == id);
        if (service is null) return Results.NotFound();
        if (service.Version != request.ExpectedVersion) return Stale();
        if (service.Code != input.Code && await db.Services.AnyAsync(other => other.Id != id && other.Code == input.Code)) return DuplicateCode();
        service.Code = input.Code!; service.Name = input.Name!; service.Description = input.Description; service.BasePrice = input.BasePrice;
        Touch(service, GetActor(context)); AddAudit(db, service.Id, service.UpdatedByUserId, "SERVICE_UPDATED", "Code,Name,Description,BasePrice");
        return await SaveAsync(db, () => Results.Ok(ToDetail(service)));
    }

    private static Task<IResult> ActivateAsync(Guid id, ServiceVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeStatusAsync(id, request, true, context, antiforgery, db);
    private static Task<IResult> DeactivateAsync(Guid id, ServiceVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeStatusAsync(id, request, false, context, antiforgery, db);
    private static async Task<IResult> ChangeStatusAsync(Guid id, ServiceVersionRequest request, bool active, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var service = await db.Services.SingleOrDefaultAsync(service => service.Id == id);
        if (service is null) return Results.NotFound();
        if (service.Version != request.ExpectedVersion) return Stale();
        if (service.IsActive == active) return Results.Ok(ToDetail(service));
        service.IsActive = active; Touch(service, GetActor(context)); AddAudit(db, id, service.UpdatedByUserId, active ? "SERVICE_ACTIVATED" : "SERVICE_DEACTIVATED", "IsActive");
        return await SaveAsync(db, () => Results.Ok(ToDetail(service)));
    }

    private static async Task<IResult> SaveAsync(ApplicationDbContext db, Func<IResult> success)
    {
        try { await db.SaveChangesAsync(); return success(); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_Services_Code")) { return DuplicateCode(); }
    }
    private static bool IsUniqueViolation(DbUpdateException exception, string constraint) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: var name } && name == constraint;
    private static IResult DuplicateCode() => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["code"] = ["J\u00e1 existe um servi\u00e7o com este c\u00f3digo."] } });
    private static IResult Stale() => Results.Conflict(new { error = StaleMessage });
    private static IResult CsrfFailure() => Results.BadRequest(new { error = "N\u00e3o foi poss\u00edvel validar a solicita\u00e7\u00e3o. Atualize a p\u00e1gina e tente novamente." });
    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static string GetActor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private static void AddAudit(ApplicationDbContext db, Guid serviceId, string actor, string action, string? changedFields) => db.ServiceAuditRecords.Add(new ServiceAuditRecord { Id = Guid.NewGuid(), ServiceId = serviceId, ActorUserId = actor, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, ChangedFields = changedFields });
    private static void Touch(Service service, string actor) { service.UpdatedAtUtc = DateTimeOffset.UtcNow; service.UpdatedByUserId = actor; service.Version = Guid.NewGuid(); }
    private static ServiceSummaryResponse ToSummary(Service service) => new(service.Id, service.Code, service.Name, service.BasePrice, service.IsActive, service.CreatedAtUtc, service.UpdatedAtUtc);
    private static ServiceDetailResponse ToDetail(Service service) => new(service.Id, service.Code, service.Name, service.Description, service.BasePrice, service.IsActive, service.CreatedAtUtc, service.UpdatedAtUtc, service.CreatedByUserId, service.UpdatedByUserId, service.Version);
    private static ServiceInput Validate(string? code, string? name, string? description, decimal? basePrice)
    {
        var errors = new Dictionary<string, string[]>();
        code = TrimToNull(code)?.ToUpperInvariant(); name = TrimToNull(name); description = TrimToNull(description);
        if (code is null || code.Length is < 2 or > 50 || !ServiceCodePattern().IsMatch(code)) errors["code"] = ["Informe um c\u00f3digo entre 2 e 50 caracteres usando letras, n\u00fameros, ponto, barra, h\u00edfen ou sublinhado."];
        if (name is null || name.Length is < 2 or > 160) errors["name"] = ["Informe o nome entre 2 e 160 caracteres."];
        if (description?.Length > 2000) errors["description"] = ["A descri\u00e7\u00e3o deve ter no m\u00e1ximo 2.000 caracteres."];
        if (basePrice < 0 || (basePrice.HasValue && decimal.Round(basePrice.Value, 2) != basePrice.Value)) errors["basePrice"] = ["Informe um pre\u00e7o base n\u00e3o negativo com no m\u00e1ximo duas casas decimais."];
        return new ServiceInput(code, name, description, basePrice, errors.Count == 0 ? null : errors);
    }
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    [GeneratedRegex("^[A-Z0-9][A-Z0-9._/-]*$")]
    private static partial Regex ServiceCodePattern();
    private sealed record ServiceInput(string? Code, string? Name, string? Description, decimal? BasePrice, Dictionary<string, string[]>? Errors);
}
