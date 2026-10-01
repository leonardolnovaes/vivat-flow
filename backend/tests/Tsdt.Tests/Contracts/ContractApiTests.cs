using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Quotes;
using Tsdt.Api.Services;
using Tsdt.Api.Platform;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Contracts;

public sealed class ContractApiTests
{
    private static int fixtureSequence;
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Tenant_and_platform_users_cannot_access_foreign_contract_operations()
    {
        using var factory = new IdentityWebApplicationFactory();
        var organizationB = await CreateOrganizationAsync(factory, "Contract tenant B");
        var tenantBUser = await CreateTenantAdminAsync(factory, organizationB.Id, "contract-b@example.test");
        var (quoteB, contractB) = await CreateForeignCommercialDataAsync(factory, organizationB.Id);
        using var tenantA = await ReadyAdminAsync(factory); using var tenantB = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(tenantB, tenantBUser.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.GetAsync($"/api/contracts/{contractB.Id}")).StatusCode);
        foreach (var path in new[] { $"/api/contracts/{contractB.Id}", $"/api/contracts/{contractB.Id}/activate", $"/api/contracts/{contractB.Id}/end", $"/api/contracts/{contractB.Id}/cancel" })
            Assert.Equal(HttpStatusCode.NotFound, (await SendResponse(tenantA, path.EndsWith(contractB.Id.ToString()) ? HttpMethod.Put : HttpMethod.Post, path, path.EndsWith(contractB.Id.ToString()) ? new UpdateContractDraftRequest(null, null, null, null, Guid.NewGuid()) : new ContractVersionRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendResponse(tenantA, HttpMethod.Post, "/api/contracts", new CreateContractFromQuoteRequest(quoteB.Id, null, null, null, null, ContractType.Pontual))).StatusCode);
        var platformUser = await CreatePlatformAdministratorAsync(factory); using var platform = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(platform, platformUser.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/contracts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync($"/api/contracts/{contractB.Id}")).StatusCode);
        foreach (var path in new[] { "/api/contracts", $"/api/contracts/{contractB.Id}", $"/api/contracts/{contractB.Id}/activate", $"/api/contracts/{contractB.Id}/end", $"/api/contracts/{contractB.Id}/cancel" })
            Assert.Equal(HttpStatusCode.Forbidden, (await SendResponse(platform, path == "/api/contracts" ? HttpMethod.Post : path.EndsWith(contractB.Id.ToString()) ? HttpMethod.Put : HttpMethod.Post, path, path == "/api/contracts" ? new CreateContractFromQuoteRequest(quoteB.Id, null, null, null, null, ContractType.Pontual) : path.EndsWith(contractB.Id.ToString()) ? new UpdateContractDraftRequest(null, null, null, null, Guid.NewGuid()) : new ContractVersionRequest(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Contract_dates_lifecycle_and_stale_version_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdminAsync(factory); var contract = await CreateApprovedContractAsync(factory, admin, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/contracts/{contract.Id}", new UpdateContractDraftRequest(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1), null, null, contract.Version))).StatusCode);
        var changed = await Send<ContractDetailResponse>(admin, HttpMethod.Put, $"/api/contracts/{contract.Id}", new UpdateContractDraftRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), "A", null, contract.Version));
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Put, $"/api/contracts/{contract.Id}", new UpdateContractDraftRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), "stale", null, contract.Version))).StatusCode);
        var active = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{contract.Id}/activate", new ContractVersionRequest(changed.Version)); var ended = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{contract.Id}/end", new ContractVersionRequest(active.Version));
        Assert.Equal(ContractStatus.Ended, ended.Status); Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{contract.Id}/activate", new ContractVersionRequest(ended.Version))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{contract.Id}/cancel", new ContractVersionRequest(ended.Version))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Draft_and_active_contracts_cancel_and_reject_repeated_transitions()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await ReadyAdminAsync(factory);
        var draft = await CreateApprovedContractAsync(factory, admin, new DateOnly(2026, 3, 1), null);
        var cancelledDraft = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{draft.Id}/cancel", new ContractVersionRequest(draft.Version));
        Assert.Equal(ContractStatus.Cancelled, cancelledDraft.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{draft.Id}/cancel", new ContractVersionRequest(cancelledDraft.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{draft.Id}/end", new ContractVersionRequest(cancelledDraft.Version))).StatusCode);
        var activeDraft = await CreateApprovedContractAsync(factory, admin, new DateOnly(2026, 4, 1), null);
        var active = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{activeDraft.Id}/activate", new ContractVersionRequest(activeDraft.Version));
        var cancelledActive = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{active.Id}/cancel", new ContractVersionRequest(active.Version));
        Assert.Equal(ContractStatus.Cancelled, cancelledActive.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{active.Id}/activate", new ContractVersionRequest(cancelledActive.Version))).StatusCode);
    }

    private static async Task<HttpClient> ReadyAdminAsync(IdentityWebApplicationFactory factory) { var client = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(client); (await SendResponse(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode(); await ServiceLineTestData.EnableSstForBootstrapOrganizationAsync(factory); return client; }
    private static async Task<ContractDetailResponse> CreateApprovedContractAsync(IdentityWebApplicationFactory factory, HttpClient admin, DateOnly? start, DateOnly? end) { var suffix = Guid.NewGuid().ToString("N"); var cnpj = Interlocked.Increment(ref fixtureSequence) % 2 == 0 ? "07646779000166" : "04252011000110"; var customer = await Send<CustomerDetailResponse>(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Lifecycle", null, cnpj, null)); var unit = await Send<UnitResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Main", "Street", "1", null, null, "São Paulo", "SP", null, true, customer.Version)); await Send<object>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Contact", null, $"{suffix}@example.test", null, true, unit.Version)); var service = await Send<ServiceDetailResponse>(admin, HttpMethod.Post, "/api/services", new CreateServiceRequest(suffix[..12], "Lifecycle", null, 10m, ServiceLineTestData.SstId)); var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, service.Id)], 10m, QuotePaymentType.Cash, null, null, 1, QuoteRiskDegree.One, unit.Unit.Id)); var submitted = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(quote.Version)); var approved = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/approve", new QuoteVersionRequest(submitted.Version)); return await Send<ContractDetailResponse>(admin, HttpMethod.Post, "/api/contracts", new CreateContractFromQuoteRequest(approved.Id, start, end, null, null, ContractType.Pontual)); }
    private static async Task<Organization> CreateOrganizationAsync(IdentityWebApplicationFactory factory, string name) { using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var organization = new Organization { Id = Guid.NewGuid(), Name = name, Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}"[..40], Status = OrganizationStatus.Active }; db.Organizations.Add(organization); await db.SaveChangesAsync(); return organization; }
    private static async Task<ApplicationUser> CreateTenantAdminAsync(IdentityWebApplicationFactory factory, Guid organizationId, string email) { using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = new ApplicationUser { FullName = "Tenant B", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = false, OrganizationId = organizationId }; Assert.True((await users.CreateAsync(user, "Userpass1!Password")).Succeeded); Assert.True((await users.AddToRoleAsync(user, IdentityRoles.Admin)).Succeeded); return user; }
    private static async Task<ApplicationUser> CreatePlatformAdministratorAsync(IdentityWebApplicationFactory factory) { using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = new ApplicationUser { FullName = "Platform", UserName = "contracts-platform@test", Email = "contracts-platform@test", EmailConfirmed = true, IsActive = true, MustChangePassword = false, IsPlatformAdministrator = true }; Assert.True((await users.CreateAsync(user, "Userpass1!Password")).Succeeded); return user; }
    private static async Task<(Quote Quote, Contract Contract)> CreateForeignCommercialDataAsync(IdentityWebApplicationFactory factory, Guid organizationId) { using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var now = DateTimeOffset.UtcNow; if (!await db.ServiceLines.AnyAsync(line => line.Id == ServiceLineTestData.SstId)) db.ServiceLines.Add(new ServiceLine { Id = ServiceLineTestData.SstId, Code = "SST", Name = "SST" }); var customer = new Customer { Id = Guid.NewGuid(), OrganizationId = organizationId, LegalName = "Foreign", Cnpj = "98765432000198", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() }; var service = new Service { Id = Guid.NewGuid(), OrganizationId = organizationId, ServiceLineId = ServiceLineTestData.SstId, Code = "FOREIGN", Name = "Foreign", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() }; var quote = new Quote { Id = Guid.NewGuid(), OrganizationId = organizationId, Number = "ORC-FOREIGN", CustomerId = customer.Id, CustomerLegalNameSnapshot = customer.LegalName, CustomerCnpjSnapshot = customer.Cnpj, Status = QuoteStatus.Approved, TotalAmount = 1m, PaymentType = QuotePaymentType.Cash, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() }; var contract = new Contract { Id = Guid.NewGuid(), OrganizationId = organizationId, QuoteId = quote.Id, CustomerId = customer.Id, CustomerLegalNameSnapshot = customer.LegalName, ApprovedTotalAmount = 1m, PaymentType = QuotePaymentType.Cash, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() }; db.AddRange(customer, service, quote, contract); await db.SaveChangesAsync(); return (quote, contract); }
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Approved_quote_creates_immutable_multi_service_line_draft_and_activates()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var admin = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(admin);
        (await SendResponse(admin, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        await ServiceLineTestData.EnableSstForBootstrapOrganizationAsync(factory);
        var customer = await Send<CustomerDetailResponse>(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Contract Customer", null, "04.252.011/0001-10", null));
        var unit = await Send<UnitResponse>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Main", "Street", "1", null, null, "São Paulo", "SP", null, true, customer.Version));
        await Send<object>(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Contact", null, "contact@example.test", null, true, unit.Version));
        var service = await Send<ServiceDetailResponse>(admin, HttpMethod.Post, "/api/services", new CreateServiceRequest("PGR", "PGR", null, 100m, ServiceLineTestData.SstId));
        var quote = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, service.Id)], 100m, QuotePaymentType.Cash, null, null, 10, QuoteRiskDegree.Two, unit.Unit.Id));
        var submitted = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/submit", new QuoteVersionRequest(quote.Version));
        var approved = await Send<QuoteDetailResponse>(admin, HttpMethod.Post, $"/api/quotes/{quote.Id}/approve", new QuoteVersionRequest(submitted.Version));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Post, "/api/contracts", new CreateContractFromQuoteRequest(approved.Id, null, null, null, null))).StatusCode);
        var draft = await Send<ContractDetailResponse>(admin, HttpMethod.Post, "/api/contracts", new CreateContractFromQuoteRequest(approved.Id, null, null, null, null, ContractType.Pontual));
        Assert.Equal(ContractType.Pontual, draft.Type);
        Assert.Equal(ContractStatus.Draft, draft.Status); Assert.Equal(approved.TotalAmount, draft.ApprovedTotalAmount); Assert.Equal(approved.Items.Single().ServiceNameSnapshot, draft.Items.Single().ServiceNameSnapshot);
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Post, "/api/contracts", new CreateContractFromQuoteRequest(approved.Id, null, null, null, null, ContractType.Pontual))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Post, $"/api/contracts/{draft.Id}/activate", new ContractVersionRequest(draft.Version))).StatusCode);
        var formalized = await Send<ContractDetailResponse>(admin, HttpMethod.Put, $"/api/contracts/{draft.Id}", new UpdateContractDraftRequest(new DateOnly(2026, 10, 1), null, "À vista", null, draft.Version));
        var active = await Send<ContractDetailResponse>(admin, HttpMethod.Post, $"/api/contracts/{draft.Id}/activate", new ContractVersionRequest(formalized.Version));
        Assert.Equal(ContractStatus.Active, active.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Contracts_require_commercial_admin_access()
    {
        using var factory = new IdentityWebApplicationFactory(); using var anonymous = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/contracts")).StatusCode);
        using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var manager = new ApplicationUser { FullName = "Manager", UserName = "manager@test", Email = "manager@test", EmailConfirmed = true, IsActive = true, MustChangePassword = false }; Assert.True((await users.CreateAsync(manager, "Userpass1!Password")).Succeeded); Assert.True((await users.AddToRoleAsync(manager, IdentityRoles.Manager)).Succeeded);
        using var client = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(client, manager.Email!, "Userpass1!Password"); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/contracts")).StatusCode);
    }

    private sealed record UnitResponse(CustomerUnitResponse Unit, Guid Version);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Send<T>(HttpClient client, HttpMethod method, string url, object body) { var response = await SendResponse(client, method, url, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<HttpResponseMessage> SendResponse(HttpClient client, HttpMethod method, string url, object body) { var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request); }
}
