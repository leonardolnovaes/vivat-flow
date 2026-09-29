using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tsdt.Api.Identity;

namespace Tsdt.Api.Customers;

public static partial class CustomerEndpoints
{
    private const string StaleMessage = "Este cliente foi alterado por outro usuário. Atualize os dados e tente novamente.";
    private const string PrimaryContactMessage = "Já existe um contato principal ativo para este cliente.";
    private const string PrimaryUnitMessage = "Já existe uma unidade principal ativa para este cliente.";
    private static readonly HashSet<string> BrazilianStates = ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    public static void MapCustomerEndpoints(this WebApplication app)
    {
        var customers = app.MapGroup("/api/customers").RequireAuthorization();
        customers.MapGet("", ListAsync);
        customers.MapGet("/{id:guid}", GetAsync);
        customers.MapPost("", CreateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{id:guid}/activate", ActivateCustomerAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{id:guid}/deactivate", DeactivateCustomerAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/contacts", CreateContactAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPut("/{customerId:guid}/contacts/{contactId:guid}", UpdateContactAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/contacts/{contactId:guid}/activate", ActivateContactAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/contacts/{contactId:guid}/deactivate", DeactivateContactAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/units", CreateUnitAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPut("/{customerId:guid}/units/{unitId:guid}", UpdateUnitAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/units/{unitId:guid}/activate", ActivateUnitAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
        customers.MapPost("/{customerId:guid}/units/{unitId:guid}/deactivate", DeactivateUnitAsync).RequireAuthorization(policy => policy.RequireRole(IdentityRoles.Admin, IdentityRoles.Manager));
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, string? search, bool? isActive, string? sort, string? direction, HttpContext context, ClaimsPrincipal user, ApplicationDbContext db)
    {
        if (search?.Length > 200)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["search"] = ["A busca deve ter no máximo 200 caracteres."] });
        var requestedPage = Math.Max(page ?? 1, 1);
        var requestedPageSize = Math.Clamp(pageSize ?? 25, 1, 100);
        var isReadOnlyUser = user.IsInRole(IdentityRoles.User);
        var organizationId = TenantContext.OrganizationId(context);
        IQueryable<Customer> query = db.Customers.AsNoTracking().Where(customer => customer.OrganizationId == organizationId);
        if (isReadOnlyUser) query = query.Where(customer => customer.IsActive);
        else if (isActive.HasValue) query = query.Where(customer => customer.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            var cnpjSearch = DigitsOnly(search);
            query = query.Where(customer => customer.LegalName.ToLower().Contains(normalizedSearch)
                || (customer.TradeName != null && customer.TradeName.ToLower().Contains(normalizedSearch))
                || (cnpjSearch.Length > 0 && customer.Cnpj.Contains(cnpjSearch))
                || customer.Contacts.Any(contact => contact.Name.ToLower().Contains(normalizedSearch)
                    || (contact.Email != null && contact.Email.Contains(normalizedSearch))));
        }
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sort?.ToLowerInvariant(), descending) switch
        {
            ("tradename", false) => query.OrderBy(customer => customer.TradeName).ThenBy(customer => customer.Id),
            ("tradename", true) => query.OrderByDescending(customer => customer.TradeName).ThenByDescending(customer => customer.Id),
            ("createdat", false) => query.OrderBy(customer => customer.CreatedAtUtc).ThenBy(customer => customer.Id),
            ("createdat", true) => query.OrderByDescending(customer => customer.CreatedAtUtc).ThenByDescending(customer => customer.Id),
            ("updatedat", false) => query.OrderBy(customer => customer.UpdatedAtUtc).ThenBy(customer => customer.Id),
            ("updatedat", true) => query.OrderByDescending(customer => customer.UpdatedAtUtc).ThenByDescending(customer => customer.Id),
            (_, true) => query.OrderByDescending(customer => customer.LegalName).ThenByDescending(customer => customer.Id),
            _ => query.OrderBy(customer => customer.LegalName).ThenBy(customer => customer.Id)
        };
        var total = await query.CountAsync();
        var offset = ((long)requestedPage - 1) * requestedPageSize;
        if (offset > int.MaxValue) return Results.Ok(new CustomerListResponse([], requestedPage, requestedPageSize, total));
        var customers = await query.Skip((int)offset).Take(requestedPageSize)
            .Include(customer => customer.Contacts).Include(customer => customer.Units).ToListAsync();
        var items = customers.Select(ToSummary).ToList();
        return Results.Ok(new CustomerListResponse(items, requestedPage, requestedPageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, ClaimsPrincipal user, ApplicationDbContext db)
    {
        var organizationId = TenantContext.OrganizationId(context);
        var query = db.Customers.AsNoTracking().Include(customer => customer.Contacts).Include(customer => customer.Units).Where(customer => customer.Id == id && customer.OrganizationId == organizationId);
        if (user.IsInRole(IdentityRoles.User)) query = query.Where(customer => customer.IsActive);
        var customer = await query.SingleOrDefaultAsync();
        return customer is null ? Results.NotFound() : Results.Ok(ToDetail(customer));
    }

    private static async Task<IResult> CreateAsync(CreateCustomerRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateCustomer(request.LegalName, request.TradeName, request.Cnpj, request.Notes);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var organizationId = TenantContext.OrganizationId(context);
        var existing = await db.Customers.Include(customer => customer.Contacts).Include(customer => customer.Units).SingleOrDefaultAsync(customer => customer.OrganizationId == organizationId && customer.Cnpj == input.Cnpj);
        if (existing is not null) return DuplicateCnpj(existing);
        var now = DateTimeOffset.UtcNow;
        var actor = GetActor(context);
        var customer = new Customer { Id = Guid.NewGuid(), OrganizationId = organizationId, LegalName = input.LegalName!, TradeName = input.TradeName, Cnpj = input.Cnpj!, Notes = input.Notes, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actor, UpdatedByUserId = actor, Version = Guid.NewGuid() };
        db.Customers.Add(customer);
        AddAudit(db, customer.Id, actor, "CUSTOMER_CREATED", "LegalName,Cnpj");
        return await SaveAsync(db, () => Results.Created($"/api/customers/{customer.Id}", ToDetail(customer)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCustomerRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateCustomer(request.LegalName, request.TradeName, request.Cnpj, request.Notes);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var organizationId = TenantContext.OrganizationId(context);
        var customer = await db.Customers.Include(customer => customer.Contacts).Include(customer => customer.Units).SingleOrDefaultAsync(customer => customer.Id == id && customer.OrganizationId == organizationId);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        if (customer.Cnpj != input.Cnpj)
        {
            var existing = await db.Customers.Include(other => other.Contacts).Include(other => other.Units).SingleOrDefaultAsync(other => other.OrganizationId == organizationId && other.Id != id && other.Cnpj == input.Cnpj);
            if (existing is not null) return DuplicateCnpj(existing);
        }
        customer.LegalName = input.LegalName!; customer.TradeName = input.TradeName; customer.Cnpj = input.Cnpj!; customer.Notes = input.Notes;
        Touch(customer, GetActor(context));
        AddAudit(db, id, customer.UpdatedByUserId, "CUSTOMER_UPDATED", "LegalName,TradeName,Cnpj,Notes");
        return await SaveAsync(db, () => Results.Ok(ToDetail(customer)));
    }

    private static Task<IResult> ActivateCustomerAsync(Guid id, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeCustomerStatusAsync(id, request, true, context, antiforgery, db);
    private static Task<IResult> DeactivateCustomerAsync(Guid id, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeCustomerStatusAsync(id, request, false, context, antiforgery, db);
    private static async Task<IResult> ChangeCustomerStatusAsync(Guid id, CustomerVersionRequest request, bool active, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var organizationId = TenantContext.OrganizationId(context);
        var customer = await db.Customers.Include(customer => customer.Contacts).Include(customer => customer.Units).SingleOrDefaultAsync(customer => customer.Id == id && customer.OrganizationId == organizationId);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        if (customer.IsActive != active)
        {
            customer.IsActive = active; Touch(customer, GetActor(context));
            AddAudit(db, id, customer.UpdatedByUserId, active ? "CUSTOMER_ACTIVATED" : "CUSTOMER_DEACTIVATED", "IsActive");
            return await SaveAsync(db, () => Results.Ok(ToDetail(customer)));
        }
        return Results.Ok(ToDetail(customer));
    }

    private static async Task<IResult> CreateContactAsync(Guid customerId, CreateContactRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateContact(request.Name, request.RoleOrDepartment, request.Email, request.Phone);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        if (request.IsPrimary && await HasPrimaryContactAsync(db, customerId, null)) return PrimaryContactConflict();
        var now = DateTimeOffset.UtcNow;
        var contact = new CustomerContact { Id = Guid.NewGuid(), CustomerId = customerId, Name = input.Name!, RoleOrDepartment = input.RoleOrDepartment, Email = input.Email, Phone = input.Phone, IsPrimary = request.IsPrimary, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.CustomerContacts.Add(contact); Touch(customer, GetActor(context));
        AddAudit(db, customerId, customer.UpdatedByUserId, "CONTACT_CREATED", "Contact");
        return await SaveAsync(db, () => Results.Created($"/api/customers/{customerId}/contacts/{contact.Id}", new { contact = ToResponse(contact), version = customer.Version }));
    }

    private static async Task<IResult> UpdateContactAsync(Guid customerId, Guid contactId, UpdateContactRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateContact(request.Name, request.RoleOrDepartment, request.Email, request.Phone);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        var contact = await db.CustomerContacts.SingleOrDefaultAsync(item => item.Id == contactId && item.CustomerId == customerId);
        if (contact is null) return Results.NotFound();
        if (request.IsPrimary && contact.IsActive && await HasPrimaryContactAsync(db, customerId, contactId)) return PrimaryContactConflict();
        contact.Name = input.Name!; contact.RoleOrDepartment = input.RoleOrDepartment; contact.Email = input.Email; contact.Phone = input.Phone; contact.IsPrimary = request.IsPrimary; contact.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Touch(customer, GetActor(context)); AddAudit(db, customerId, customer.UpdatedByUserId, "CONTACT_UPDATED", "Contact");
        return await SaveAsync(db, () => Results.Ok(new { contact = ToResponse(contact), version = customer.Version }));
    }

    private static Task<IResult> ActivateContactAsync(Guid customerId, Guid contactId, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeContactStatusAsync(customerId, contactId, request, true, context, antiforgery, db);
    private static Task<IResult> DeactivateContactAsync(Guid customerId, Guid contactId, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeContactStatusAsync(customerId, contactId, request, false, context, antiforgery, db);
    private static async Task<IResult> ChangeContactStatusAsync(Guid customerId, Guid contactId, CustomerVersionRequest request, bool active, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        var contact = await db.CustomerContacts.SingleOrDefaultAsync(item => item.Id == contactId && item.CustomerId == customerId);
        if (contact is null) return Results.NotFound();
        if (contact.IsActive != active)
        {
            if (active && contact.IsPrimary && await HasPrimaryContactAsync(db, customerId, contactId)) return PrimaryContactConflict();
            contact.IsActive = active; contact.UpdatedAtUtc = DateTimeOffset.UtcNow; Touch(customer, GetActor(context));
            AddAudit(db, customerId, customer.UpdatedByUserId, active ? "CONTACT_ACTIVATED" : "CONTACT_DEACTIVATED", "IsActive");
            return await SaveAsync(db, () => Results.Ok(new { contact = ToResponse(contact), version = customer.Version }));
        }
        return Results.Ok(new { contact = ToResponse(contact), version = customer.Version });
    }

    private static async Task<IResult> CreateUnitAsync(Guid customerId, CreateUnitRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateUnit(request);
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        if (request.IsPrimary && await HasPrimaryUnitAsync(db, customerId, null)) return PrimaryUnitConflict();
        var now = DateTimeOffset.UtcNow;
        var unit = new CustomerUnit { Id = Guid.NewGuid(), CustomerId = customerId, Name = input.Name!, Street = input.Street!, Number = input.Number!, Complement = input.Complement, District = input.District, City = input.City!, StateCode = input.StateCode!, PostalCode = input.PostalCode, IsPrimary = request.IsPrimary, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.CustomerUnits.Add(unit); Touch(customer, GetActor(context)); AddAudit(db, customerId, customer.UpdatedByUserId, "UNIT_CREATED", "Unit");
        return await SaveAsync(db, () => Results.Created($"/api/customers/{customerId}/units/{unit.Id}", new { unit = ToResponse(unit), version = customer.Version }));
    }

    private static async Task<IResult> UpdateUnitAsync(Guid customerId, Guid unitId, UpdateUnitRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var input = ValidateUnit(new CreateUnitRequest(request.Name, request.Street, request.Number, request.Complement, request.District, request.City, request.StateCode, request.PostalCode, request.IsPrimary, request.ExpectedVersion));
        if (input.Errors is not null) return Results.ValidationProblem(input.Errors);
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        var unit = await db.CustomerUnits.SingleOrDefaultAsync(item => item.Id == unitId && item.CustomerId == customerId);
        if (unit is null) return Results.NotFound();
        if (request.IsPrimary && unit.IsActive && await HasPrimaryUnitAsync(db, customerId, unitId)) return PrimaryUnitConflict();
        unit.Name = input.Name!; unit.Street = input.Street!; unit.Number = input.Number!; unit.Complement = input.Complement; unit.District = input.District; unit.City = input.City!; unit.StateCode = input.StateCode!; unit.PostalCode = input.PostalCode; unit.IsPrimary = request.IsPrimary; unit.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Touch(customer, GetActor(context)); AddAudit(db, customerId, customer.UpdatedByUserId, "UNIT_UPDATED", "Unit");
        return await SaveAsync(db, () => Results.Ok(new { unit = ToResponse(unit), version = customer.Version }));
    }

    private static Task<IResult> ActivateUnitAsync(Guid customerId, Guid unitId, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeUnitStatusAsync(customerId, unitId, request, true, context, antiforgery, db);
    private static Task<IResult> DeactivateUnitAsync(Guid customerId, Guid unitId, CustomerVersionRequest request, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db) => ChangeUnitStatusAsync(customerId, unitId, request, false, context, antiforgery, db);
    private static async Task<IResult> ChangeUnitStatusAsync(Guid customerId, Guid unitId, CustomerVersionRequest request, bool active, HttpContext context, IAntiforgery antiforgery, ApplicationDbContext db)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery)) return CsrfFailure();
        var customer = await TenantCustomerAsync(db, customerId, context);
        if (customer is null) return Results.NotFound();
        if (customer.Version != request.ExpectedVersion) return Stale();
        var unit = await db.CustomerUnits.SingleOrDefaultAsync(item => item.Id == unitId && item.CustomerId == customerId);
        if (unit is null) return Results.NotFound();
        if (unit.IsActive != active)
        {
            if (active && unit.IsPrimary && await HasPrimaryUnitAsync(db, customerId, unitId)) return PrimaryUnitConflict();
            unit.IsActive = active; unit.UpdatedAtUtc = DateTimeOffset.UtcNow; Touch(customer, GetActor(context));
            AddAudit(db, customerId, customer.UpdatedByUserId, active ? "UNIT_ACTIVATED" : "UNIT_DEACTIVATED", "IsActive");
            return await SaveAsync(db, () => Results.Ok(new { unit = ToResponse(unit), version = customer.Version }));
        }
        return Results.Ok(new { unit = ToResponse(unit), version = customer.Version });
    }

    private static async Task<IResult> SaveAsync(ApplicationDbContext db, Func<IResult> success)
    {
        try { await db.SaveChangesAsync(); return success(); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_Customers_Cnpj")) { return DuplicateCnpj(); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_CustomerContacts_CustomerId_IsPrimary")) { return PrimaryContactConflict(); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, "IX_CustomerUnits_CustomerId_IsPrimary")) { return PrimaryUnitConflict(); }
    }

    private static bool IsUniqueViolation(DbUpdateException exception, string constraint) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: var name } && name == constraint;
    private static IResult DuplicateCnpj(Customer? existing = null) => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["cnpj"] = ["Já existe um cliente com este CNPJ."] }, existingCustomer = existing is null ? null : ToSummary(existing) });
    private static IResult PrimaryContactConflict() => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["isPrimary"] = [PrimaryContactMessage] } });
    private static IResult PrimaryUnitConflict() => Results.Conflict(new { errors = new Dictionary<string, string[]> { ["isPrimary"] = [PrimaryUnitMessage] } });
    private static IResult Stale() => Results.Conflict(new { error = StaleMessage });
    private static IResult CsrfFailure() => Results.BadRequest(new { error = "Não foi possível validar a solicitação. Atualize a página e tente novamente." });
    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery) { try { await antiforgery.ValidateRequestAsync(context); return true; } catch (AntiforgeryValidationException) { return false; } }
    private static string GetActor(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private static void AddAudit(ApplicationDbContext db, Guid customerId, string actor, string action, string? changedFields) => db.CustomerAuditRecords.Add(new CustomerAuditRecord { Id = Guid.NewGuid(), CustomerId = customerId, ActorUserId = actor, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow, ChangedFields = changedFields });
    private static void Touch(Customer customer, string actor) { customer.UpdatedAtUtc = DateTimeOffset.UtcNow; customer.UpdatedByUserId = actor; customer.Version = Guid.NewGuid(); }
    private static Task<bool> HasPrimaryContactAsync(ApplicationDbContext db, Guid customerId, Guid? exceptId) => db.CustomerContacts.AnyAsync(contact => contact.CustomerId == customerId && contact.IsActive && contact.IsPrimary && (!exceptId.HasValue || contact.Id != exceptId));
    private static Task<Customer?> TenantCustomerAsync(ApplicationDbContext db, Guid customerId, HttpContext context) => db.Customers.SingleOrDefaultAsync(customer => customer.Id == customerId && customer.OrganizationId == TenantContext.OrganizationId(context));
    private static Task<bool> HasPrimaryUnitAsync(ApplicationDbContext db, Guid customerId, Guid? exceptId) => db.CustomerUnits.AnyAsync(unit => unit.CustomerId == customerId && unit.IsActive && unit.IsPrimary && (!exceptId.HasValue || unit.Id != exceptId));
    private static CustomerSummaryResponse ToSummary(Customer customer)
    {
        var completion = Completion(customer);
        return new(customer.Id, customer.LegalName, customer.TradeName, customer.Cnpj, customer.IsActive, completion.IsComplete, completion.MissingFields, customer.CreatedAtUtc, customer.UpdatedAtUtc);
    }
    private static CustomerDetailResponse ToDetail(Customer customer)
    {
        var completion = Completion(customer);
        return new(customer.Id, customer.LegalName, customer.TradeName, customer.Cnpj, customer.Notes, customer.IsActive, completion.IsComplete, completion.MissingFields, customer.CreatedAtUtc, customer.UpdatedAtUtc, customer.CreatedByUserId, customer.UpdatedByUserId, customer.Version, customer.Contacts.OrderBy(contact => contact.Name).Select(ToResponse).ToList(), customer.Units.OrderBy(unit => unit.Name).Select(ToResponse).ToList());
    }
    private static CustomerCompletion Completion(Customer customer)
    {
        var missing = new List<string>();
        if (!customer.Contacts.Any(contact => contact.IsActive)) missing.Add("contact");
        if (!customer.Units.Any(unit => unit.IsActive)) missing.Add("unit");
        return new CustomerCompletion(missing.Count == 0, missing);
    }
    private static CustomerContactResponse ToResponse(CustomerContact contact) => new(contact.Id, contact.CustomerId, contact.Name, contact.RoleOrDepartment, contact.Email, contact.Phone, contact.IsPrimary, contact.IsActive, contact.CreatedAtUtc, contact.UpdatedAtUtc);
    private static CustomerUnitResponse ToResponse(CustomerUnit unit) => new(unit.Id, unit.CustomerId, unit.Name, unit.Street, unit.Number, unit.Complement, unit.District, unit.City, unit.StateCode, unit.PostalCode, unit.IsPrimary, unit.IsActive, unit.CreatedAtUtc, unit.UpdatedAtUtc);

    private static CustomerInput ValidateCustomer(string? legalName, string? tradeName, string? cnpj, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        legalName = TrimToNull(legalName); tradeName = TrimToNull(tradeName); notes = TrimToNull(notes); var normalizedCnpj = NormalizeCnpj(cnpj);
        RequireRange(errors, "legalName", legalName, 2, 200, "Informe a razão social.", "A razão social deve ter entre 2 e 200 caracteres.");
        OptionalRange(errors, "tradeName", tradeName, 2, 200, "O nome fantasia deve ter entre 2 e 200 caracteres.");
        if (notes?.Length > 2000) errors["notes"] = ["As observações devem ter no máximo 2.000 caracteres."];
        if (normalizedCnpj is null || !IsValidCnpj(normalizedCnpj)) errors["cnpj"] = ["Informe um CNPJ válido."];
        return new CustomerInput(legalName, tradeName, normalizedCnpj, notes, errors.Count == 0 ? null : errors);
    }

    private static ContactInput ValidateContact(string? name, string? roleOrDepartment, string? email, string? phone)
    {
        var errors = new Dictionary<string, string[]>();
        name = TrimToNull(name); roleOrDepartment = TrimToNull(roleOrDepartment); email = TrimToNull(email)?.ToLowerInvariant(); phone = TrimToNull(phone); var normalizedPhone = NormalizePhone(phone);
        RequireRange(errors, "name", name, 2, 120, "Informe o nome do contato.", "O nome do contato deve ter entre 2 e 120 caracteres.");
        if (roleOrDepartment?.Length > 120) errors["roleOrDepartment"] = ["A função ou departamento deve ter no máximo 120 caracteres."];
        if (email is not null && (email.Length > 254 || !MailAddress.TryCreate(email, out _))) errors["email"] = ["Informe um e-mail válido com no máximo 254 caracteres."];
        if (phone is not null && normalizedPhone is null) errors["phone"] = ["Informe um telefone brasileiro válido."];
        if (email is null && normalizedPhone is null) errors["contact"] = ["Informe e-mail ou telefone para o contato."];
        return new ContactInput(name, roleOrDepartment, email, normalizedPhone, errors.Count == 0 ? null : errors);
    }

    private static UnitInput ValidateUnit(CreateUnitRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = TrimToNull(request.Name); var street = TrimToNull(request.Street); var number = TrimToNull(request.Number); var complement = TrimToNull(request.Complement); var district = TrimToNull(request.District); var city = TrimToNull(request.City); var stateCode = TrimToNull(request.StateCode)?.ToUpperInvariant(); var postalCode = NormalizePostalCode(request.PostalCode);
        RequireRange(errors, "name", name, 2, 160, "Informe o nome da unidade.", "O nome da unidade deve ter entre 2 e 160 caracteres.");
        RequireRange(errors, "street", street, 1, 160, "Informe o logradouro.", "O logradouro deve ter no máximo 160 caracteres.");
        RequireRange(errors, "number", number, 1, 30, "Informe o número.", "O número deve ter no máximo 30 caracteres.");
        OptionalMax(errors, "complement", complement, 120, "O complemento deve ter no máximo 120 caracteres.");
        OptionalMax(errors, "district", district, 120, "O bairro deve ter no máximo 120 caracteres.");
        RequireRange(errors, "city", city, 1, 120, "Informe a cidade.", "A cidade deve ter no máximo 120 caracteres.");
        if (stateCode is null || !BrazilianStates.Contains(stateCode)) errors["stateCode"] = ["Informe uma UF brasileira válida."];
        if (TrimToNull(request.PostalCode) is not null && postalCode is null) errors["postalCode"] = ["Informe um CEP válido."];
        return new UnitInput(name, street, number, complement, district, city, stateCode, postalCode, errors.Count == 0 ? null : errors);
    }

    private static void RequireRange(Dictionary<string, string[]> errors, string field, string? value, int min, int max, string required, string invalid) { if (value is null) errors[field] = [required]; else if (value.Length < min || value.Length > max) errors[field] = [invalid]; }
    private static void OptionalRange(Dictionary<string, string[]> errors, string field, string? value, int min, int max, string invalid) { if (value is not null && (value.Length < min || value.Length > max)) errors[field] = [invalid]; }
    private static void OptionalMax(Dictionary<string, string[]> errors, string field, string? value, int max, string invalid) { if (value?.Length > max) errors[field] = [invalid]; }
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string DigitsOnly(string? value) => value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());
    private static string? NormalizeCnpj(string? value) { if (value is null || value.Any(character => !char.IsAsciiDigit(character) && character is not '.' and not '/' and not '-' and not ' ')) return null; var digits = DigitsOnly(value); return digits.Length == 14 ? digits : null; }
    private static string? NormalizePhone(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; if (value.Any(character => !char.IsAsciiDigit(character) && character is not '+' and not '(' and not ')' and not '-' and not ' ' and not '.')) return null; var digits = DigitsOnly(value); if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal)) digits = digits[2..]; return digits.Length is 10 or 11 ? digits : null; }
    private static string? NormalizePostalCode(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; if (value.Any(character => !char.IsAsciiDigit(character) && character is not '-' and not ' ' and not '.')) return null; var digits = DigitsOnly(value); return digits.Length == 8 ? digits : null; }
    private static bool IsValidCnpj(string value) { if (value.Length != 14 || value.All(character => character == value[0])) return false; var weights1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }; var weights2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }; var first = CnpjDigit(value[..12], weights1); var second = CnpjDigit(value[..12] + first, weights2); return value[12] - '0' == first && value[13] - '0' == second; }
    private static int CnpjDigit(string source, int[] weights) { var total = source.Select((character, index) => (character - '0') * weights[index]).Sum(); var remainder = total % 11; return remainder < 2 ? 0 : 11 - remainder; }
    private sealed record CustomerInput(string? LegalName, string? TradeName, string? Cnpj, string? Notes, Dictionary<string, string[]>? Errors);
    private sealed record ContactInput(string? Name, string? RoleOrDepartment, string? Email, string? Phone, Dictionary<string, string[]>? Errors);
    private sealed record UnitInput(string? Name, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode, string? PostalCode, Dictionary<string, string[]>? Errors);
    private sealed record CustomerCompletion(bool IsComplete, IReadOnlyList<string> MissingFields);
}
