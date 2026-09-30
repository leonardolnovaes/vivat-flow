using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Quotes;
using Tsdt.Api.Services;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Quotes;

[Trait("Category", "Integration")]
public sealed class QuoteApiTests
{
    [Fact]
    public async Task Customer_lookup_accepts_formatted_and_normalized_cnpj_and_duplicate_conflict_identifies_existing_customer()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        var customer = await Send<CustomerDetailResponse>(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("CNPJ Lookup", "Lookup", "07.646.779/0001-66", null));
        foreach (var search in new[] { "07.646.779/0001-66", "07646779000166" })
        {
            var response = await admin.GetFromJsonAsync<CustomerListResponse>($"/api/customers?search={search}");
            Assert.Contains(response!.Items, item => item.Id == customer.Id);
        }
        var duplicate = await SendResponse(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Duplicate", null, "07646779000166", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var json = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(customer.Id, json.RootElement.GetProperty("existingCustomer").GetProperty("id").GetGuid());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Customers.CountAsync(x => x.Cnpj == "07646779000166"));
    }

    [Fact]
    public async Task Approval_aggregates_commercial_and_customer_completeness_errors_without_transitioning_draft()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        var customer = await Customer(admin);
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [], null, null, null, null));
        var response = await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(quote.Version));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = json.RootElement.GetProperty("errors");
        foreach (var key in new[] { "items", "totalAmount", "paymentType", "employeeCount", "riskDegree", "serviceUnitId", "contact", "unit" }) Assert.True(errors.TryGetProperty(key, out _));
        var persisted = await admin.GetFromJsonAsync<QuoteDetailResponse>($"/api/quotes/{quote.Id}", QuoteJson);
        Assert.Equal(QuoteStatus.Draft, persisted!.Status);
    }
    [Fact]
    public async Task Admin_creates_draft_with_snapshots_and_transitions_it()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        var customer = await Send<CustomerDetailResponse>(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Quote Customer", null, "04.252.011/0001-10", null));
        var unit = await Send<UnitMutationResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Head Office", "Main Street", "1", null, null, "Sao Paulo", "SP", null, true, customer.Version));
        await Send<ContactMutationResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Commercial contact", null, "contact@example.test", null, true, unit.Version));
        var service = await Send<ServiceDetailResponse>(admin, HttpMethod.Post, "/api/services", new CreateServiceRequest("PGR", "Risk Program", null, 100m, ServiceLineTestData.SstId));
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, service.Id)], null, null, null, null));
        Assert.StartsWith("ORC-", quote.Number); Assert.Equal(QuoteStatus.Draft, quote.Status); Assert.Equal(customer.LegalName, quote.CustomerLegalNameSnapshot); Assert.Equal(service.Name, quote.Items.Single().ServiceNameSnapshot);
        var updated = await Send<QuoteDetailResponse>(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, quote.Items.Select(x => new QuoteItemRequest(x.Id, x.ServiceId)).ToList(), 350m, QuotePaymentType.Cash, null, null, quote.Version, 20, QuoteRiskDegree.Two, unit.Unit.Id));
        var submitted = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(updated.Version));
        Assert.Equal(QuoteStatus.AwaitingApproval, submitted.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/approve", new QuoteVersionRequest(updated.Version))).StatusCode);
        var approved = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/approve", new QuoteVersionRequest(submitted.Version));
        Assert.Equal(QuoteStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Quotes_are_admin_only_and_mutations_require_csrf()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        using var anonymous = IdentityTestClient.Create(factory); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quotes")).StatusCode);
        var user = await CreateUser(factory, IdentityRoles.Manager); using var manager = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(manager, user.Email!, "Userpass1!Password"); Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/quotes")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest(Guid.NewGuid(), [], null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task User_is_forbidden_and_empty_draft_requires_an_active_customer()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        var user = await CreateUser(factory, IdentityRoles.User); using var userClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(userClient, user.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/quotes")).StatusCode);
        var customer = await Customer(admin); var draft = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [], null, null, null, "  draft  "));
        Assert.Empty(draft.Items); Assert.Equal("draft", draft.Notes);
        var inactive = await SendResponse(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/deactivate", new CustomerVersionRequest(customer.Version));
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [], null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Draft_update_preserves_snapshots_and_rejects_stale_or_inactive_new_service_without_audit()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory); var customer = await Customer(admin); var service = await Service(admin, "PGR", "Original Service");
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, service.Id)], 100m, QuotePaymentType.Cash, null, null));
        var changedService = await Send<ServiceDetailResponse>(admin, HttpMethod.Put, $"/api/services/{service.Id}", new UpdateServiceRequest(service.Code, "Changed Service", null, service.BasePrice, ServiceLineTestData.SstId, service.Version));
        var updated = await Send<QuoteDetailResponse>(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, quote.Items.Select(x => new QuoteItemRequest(x.Id, x.ServiceId)).ToList(), 110m, QuotePaymentType.Cash, null, null, quote.Version));
        Assert.Equal("Original Service", updated.Items.Single().ServiceNameSnapshot);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, [], 120m, QuotePaymentType.Cash, null, null, quote.Version))).StatusCode);
        var inactive = await Send<ServiceDetailResponse>(admin, HttpMethod.Post, $"/api/services/{service.Id}/deactivate", new ServiceVersionRequest(changedService.Version));
        var rejected = await SendResponse(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, [new QuoteItemRequest(null, inactive.Id)], 120m, QuotePaymentType.Cash, null, null, updated.Version));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); Assert.Equal(2, await db.QuoteAuditRecords.CountAsync(x => x.QuoteId == quote.Id));
    }

    [Fact]
    public async Task Status_matrix_submission_validation_and_audit_actions_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory); var customer = await Customer(admin); var unit = await Send<UnitMutationResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Head Office", "Main Street", "1", null, null, "Sao Paulo", "SP", null, true, customer.Version)); await Send<ContactMutationResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Commercial contact", null, "contact@example.test", null, true, unit.Version)); var service = await Service(admin, "PCMSO", "PCMSO");
        var empty = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [], null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{empty.Id}/submit", new QuoteVersionRequest(empty.Version))).StatusCode);
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, service.Id)], 200m, QuotePaymentType.Installments, 2, null, 20, QuoteRiskDegree.Two, unit.Unit.Id));
        var submitted = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(quote.Version));
        var reopened = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/reopen", new QuoteVersionRequest(submitted.Version));
        var submittedAgain = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(reopened.Version));
        var cancelled = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/cancel", new QuoteVersionRequest(submittedAgain.Version));
        Assert.Equal(QuoteStatus.Cancelled, cancelled.Status); Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/approve", new QuoteVersionRequest(cancelled.Version))).StatusCode);
        using var scope = factory.Services.CreateScope(); var actions = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().QuoteAuditRecords.Where(x => x.QuoteId == quote.Id).Select(x => x.Action).ToListAsync(); Assert.Equal(["QUOTE_CREATED", "QUOTE_SENT_FOR_APPROVAL", "QUOTE_REOPENED", "QUOTE_SENT_FOR_APPROVAL", "QUOTE_CANCELLED"], actions);
    }

    [Fact]
    public async Task List_defaults_to_open_scope_and_closed_scope_or_status_overrides_it_with_stable_recent_order()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory); var customer = await Customer(admin); var now = DateTimeOffset.UtcNow;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (number, status, updated) in new[] { ("ORC-SCOPE-001", QuoteStatus.Draft, now.AddMinutes(-3)), ("ORC-SCOPE-002", QuoteStatus.AwaitingApproval, now.AddMinutes(-2)), ("ORC-SCOPE-003", QuoteStatus.ChangesRequested, now.AddMinutes(-1)), ("ORC-SCOPE-004", QuoteStatus.Approved, now), ("ORC-SCOPE-005", QuoteStatus.Rejected, now), ("ORC-SCOPE-006", QuoteStatus.Cancelled, now), ("ORC-SCOPE-007", QuoteStatus.Expired, now) }) db.Quotes.Add(new Quote { Id = Guid.NewGuid(), Number = number, CustomerId = customer.Id, CustomerLegalNameSnapshot = customer.LegalName, CustomerCnpjSnapshot = customer.Cnpj, Status = status, CreatedAtUtc = now, UpdatedAtUtc = updated, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var open = await admin.GetFromJsonAsync<QuoteListResponse>("/api/quotes", QuoteJson); Assert.All(open!.Items, item => Assert.Contains(item.Status, new[] { QuoteStatus.Draft, QuoteStatus.AwaitingApproval, QuoteStatus.ChangesRequested })); Assert.Equal(["ORC-SCOPE-003", "ORC-SCOPE-002", "ORC-SCOPE-001"], open.Items.Where(x => x.Number.StartsWith("ORC-SCOPE")).Select(x => x.Number));
        var all = await admin.GetFromJsonAsync<QuoteListResponse>("/api/quotes?scope=All", QuoteJson); Assert.Contains(all!.Items, item => item.Status == QuoteStatus.Approved); Assert.Contains(all.Items, item => item.Status == QuoteStatus.Rejected); Assert.Contains(all.Items, item => item.Status == QuoteStatus.Cancelled); Assert.Contains(all.Items, item => item.Status == QuoteStatus.Expired);
        var closed = await admin.GetFromJsonAsync<QuoteListResponse>("/api/quotes?scope=Closed", QuoteJson); Assert.All(closed!.Items.Where(x => x.Number.StartsWith("ORC-SCOPE")), item => Assert.Contains(item.Status, new[] { QuoteStatus.Approved, QuoteStatus.Rejected, QuoteStatus.Cancelled, QuoteStatus.Expired }));
        var approved = await admin.GetFromJsonAsync<QuoteListResponse>("/api/quotes?status=Approved", QuoteJson); Assert.Contains(approved!.Items, item => item.Number == "ORC-SCOPE-004");
    }

    [Fact]
    public async Task Eligible_professionals_assignment_and_visit_lifecycle_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdmin(factory);
        var manager = await CreateUser(factory, IdentityRoles.Manager); var user = await CreateUser(factory, IdentityRoles.User); var inactiveAdmin = await CreateUser(factory, IdentityRoles.Admin); inactiveAdmin.IsActive = false;
        using (var scope = factory.Services.CreateScope()) { var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var persistedInactiveAdmin = await users.FindByIdAsync(inactiveAdmin.Id); persistedInactiveAdmin!.IsActive = false; Assert.True((await users.UpdateAsync(persistedInactiveAdmin)).Succeeded); }
        var professionals = await admin.GetFromJsonAsync<List<EligibleProfessionalResponse>>("/api/quotes/professionals", QuoteJson);
        Assert.Contains(professionals!, person => person.Id == manager.Id); Assert.DoesNotContain(professionals!, person => person.Id == user.Id); Assert.DoesNotContain(professionals!, person => person.Id == inactiveAdmin.Id);
        var customer = await Customer(admin); var unit = await Send<UnitMutationResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Visit unit", "Street", "1", null, null, "SÃ£o Paulo", "SP", null, true, customer.Version));
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [], null, null, null, null, ResponsibleUserId: manager.Id));
        Assert.Equal(manager.Id, quote.ResponsibleUserId);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, [], null, null, null, null, quote.Version, ResponsibleUserId: user.Id))).StatusCode);
        var start = DateTimeOffset.UtcNow.AddDays(1); var invalid = await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits", new CreateQuoteVisitRequest(user.Id, start, start.AddHours(1), unit.Unit.Id, null)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var scheduled = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits", new CreateQuoteVisitRequest(manager.Id, start, start.AddHours(1), unit.Unit.Id, "Initial")); Assert.Equal(QuoteVisitStatus.Scheduled, scheduled.CurrentVisit!.Status);
        var rescheduled = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits/{scheduled.CurrentVisit.Id}/reschedule", new RescheduleQuoteVisitRequest(start.AddDays(1), start.AddDays(1).AddHours(1), "Changed")); Assert.Equal(start.AddDays(1), rescheduled.CurrentVisit!.ScheduledStart);
        var completed = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits/{scheduled.CurrentVisit.Id}/complete", new { }); Assert.Equal(QuoteVisitStatus.Completed, completed.CurrentVisit!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits/{scheduled.CurrentVisit.Id}/cancel", new { })).StatusCode);
        var second = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits", new CreateQuoteVisitRequest(manager.Id, start.AddDays(2), start.AddDays(2).AddHours(1), unit.Unit.Id, null));
        var cancelled = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/visits/{second.CurrentVisit!.Id}/cancel", new { }); Assert.Equal(QuoteVisitStatus.Cancelled, cancelled.CurrentVisit!.Status);
    }

    private static async Task<HttpClient> ReadyAdmin(IdentityWebApplicationFactory factory) { var client = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(client); (await SendResponse(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode(); await ServiceLineTestData.EnableSstForBootstrapOrganizationAsync(factory); return client; }
    private static Task<CustomerDetailResponse> Customer(HttpClient client) => Send<CustomerDetailResponse>(client, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Quote Customer", null, "04.252.011/0001-10", null));
    private static Task<ServiceDetailResponse> Service(HttpClient client, string code, string name) => Send<ServiceDetailResponse>(client, HttpMethod.Post, "/api/services", new CreateServiceRequest(code, name, null, 100m, ServiceLineTestData.SstId));
    private static async Task<ApplicationUser> CreateUser(IdentityWebApplicationFactory factory, string role) { using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = new ApplicationUser { FullName = "Quote Role", UserName = $"quote-{role}@test", Email = $"quote-{role}@test", EmailConfirmed = true, IsActive = true, MustChangePassword = false }; Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded); return user; }
    private static readonly JsonSerializerOptions QuoteJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private sealed record UnitMutationResponse(CustomerUnitResponse Unit, Guid Version);
    private sealed record ContactMutationResponse(CustomerContactResponse Contact, Guid Version);
    private static async Task<T> Send<T>(HttpClient client, HttpMethod method, string path, object body) { var response = await SendResponse(client, method, path, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(QuoteJson))!; }
    private static async Task<HttpResponseMessage> SendResponse(HttpClient client, HttpMethod method, string path, object body) { var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request); }
}
