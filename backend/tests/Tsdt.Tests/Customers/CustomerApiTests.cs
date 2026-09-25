using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Customers;

public sealed class CustomerApiTests
{
    [Fact]
    public async Task Customer_list_rejects_long_search_and_handles_large_page_without_offset_overflow()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        await CreateCustomerAsync(client);
        var longSearch = await client.GetAsync($"/api/customers?search={new string('x', 201)}");
        Assert.Equal(HttpStatusCode.BadRequest, longSearch.StatusCode);
        var largePage = await client.GetFromJsonAsync<CustomerListResponse>("/api/customers?page=2147483647&pageSize=100");
        Assert.NotNull(largePage);
        Assert.Equal(1, largePage.TotalCount);
        Assert.Empty(largePage.Items);
    }

    [Fact]
    public async Task Customer_audit_records_actor_action_and_minimized_context_only_for_successful_mutations()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var customer = await CreateCustomerAsync(client, new CreateCustomerRequest("Audited Company", null, "04.252.011/0001-10", "private notes"));
        var updatedResponse = await SendAsync(client, HttpMethod.Put, $"/api/customers/{customer.Id}",
            new UpdateCustomerRequest("Audited Company Updated", null, customer.Cnpj, "private updated notes", customer.Version));
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;
        var contactResponse = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts",
            new CreateContactRequest("Private Person", null, "private@example.test", "", false, updated.Version));
        Assert.Equal(HttpStatusCode.Created, contactResponse.StatusCode);
        var contact = (await contactResponse.Content.ReadFromJsonAsync<ContactMutationResponse>())!;
        var unitResponse = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/units",
            new CreateUnitRequest("Head Office", "Main Street", "10", null, null, "São Paulo", "SP", null, false, contact.Version));
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        var unit = (await unitResponse.Content.ReadFromJsonAsync<UnitMutationResponse>())!;
        var statusResponse = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/deactivate", new CustomerVersionRequest(unit.Version));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var failedResponse = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/deactivate", new CustomerVersionRequest(customer.Version));
        Assert.Equal(HttpStatusCode.Conflict, failedResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var records = (await db.CustomerAuditRecords.Where(record => record.CustomerId == customer.Id).ToListAsync())
            .OrderBy(record => record.OccurredAtUtc).ToList();
        Assert.Equal(5, records.Count);
        Assert.Equal(new[] { "CUSTOMER_CREATED", "CUSTOMER_UPDATED", "CONTACT_CREATED", "UNIT_CREATED", "CUSTOMER_DEACTIVATED" }, records.Select(record => record.Action));
        Assert.All(records, record =>
        {
            Assert.Equal(customer.CreatedByUserId, record.ActorUserId);
            Assert.NotEqual(default, record.OccurredAtUtc);
            Assert.DoesNotContain("private", record.ChangedFields ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Person", record.ChangedFields ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", record.ChangedFields ?? "", StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Customer_creation_normalizes_validates_and_audits()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var created = await CreateCustomerAsync(client, new CreateCustomerRequest("  Empresa Açúcar LTDA  ", "  Açúcar  ", "04.252.011/0001-10", "  observação  "));
        Assert.Equal("Empresa Açúcar LTDA", created.LegalName);
        Assert.Equal("Açúcar", created.TradeName);
        Assert.Equal("04252011000110", created.Cnpj);
        Assert.Equal("observação", created.Notes);
        Assert.True(created.IsActive);
        Assert.NotEqual(Guid.Empty, created.Version);

        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Outra empresa", null, "04252011000110", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var invalidName = await SendAsync(client, HttpMethod.Post, "/api/customers", new CreateCustomerRequest(" ", null, "04.252.011/0001-10", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
        var invalidCnpj = await SendAsync(client, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Empresa válida", null, "04.252.011/0001-11", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCnpj.StatusCode);

        using var scope = factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CustomerAuditRecords.SingleAsync();
        Assert.Equal("CUSTOMER_CREATED", audit.Action);
        Assert.DoesNotContain("observação", audit.ChangedFields ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Customer_status_update_and_stale_version_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var created = await CreateCustomerAsync(client);
        var updated = await SendAsync(client, HttpMethod.Put, $"/api/customers/{created.Id}", new UpdateCustomerRequest("Empresa Atualizada", null, created.Cnpj, null, created.Version));
        var afterUpdate = await updated.Content.ReadFromJsonAsync<CustomerDetailResponse>();
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(created.Version, afterUpdate!.Version);
        var stale = await SendAsync(client, HttpMethod.Post, $"/api/customers/{created.Id}/deactivate", new CustomerVersionRequest(created.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var deactivated = await SendAsync(client, HttpMethod.Post, $"/api/customers/{created.Id}/deactivate", new CustomerVersionRequest(afterUpdate.Version));
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var current = await deactivated.Content.ReadFromJsonAsync<CustomerDetailResponse>();
        var idempotent = await SendAsync(client, HttpMethod.Post, $"/api/customers/{created.Id}/deactivate", new CustomerVersionRequest(current!.Version));
        Assert.Equal(HttpStatusCode.OK, idempotent.StatusCode);
    }

    [Fact]
    public async Task Contacts_require_channel_normalize_and_preserve_primary_integrity()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var customer = await CreateCustomerAsync(client);
        var missingChannel = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Contato sem canal", null, null, null, false, customer.Version));
        Assert.Equal(HttpStatusCode.BadRequest, missingChannel.StatusCode);
        var emailOnly = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Contato por e-mail", "", "EMAIL@EXAMPLE.TEST", "", false, customer.Version));
        Assert.Equal(HttpStatusCode.Created, emailOnly.StatusCode);
        var emailOnlyPayload = await emailOnly.Content.ReadFromJsonAsync<ContactMutationResponse>();
        Assert.Equal("email@example.test", emailOnlyPayload!.Contact.Email);
        Assert.Null(emailOnlyPayload.Contact.Phone);
        var first = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Contato Principal", "Vendas", null, "+55 (11) 99876-5432", true, emailOnlyPayload.Version));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstPayload = await first.Content.ReadFromJsonAsync<ContactMutationResponse>();
        Assert.Equal("11998765432", firstPayload!.Contact.Phone);
        var secondPrimary = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Outro contato", null, "OUTRO@EXAMPLE.TEST", null, true, firstPayload.Version));
        Assert.Equal(HttpStatusCode.Conflict, secondPrimary.StatusCode);
        var deactivated = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts/{firstPayload.Contact.Id}/deactivate", new CustomerVersionRequest(firstPayload.Version));
        var deactivatedPayload = await deactivated.Content.ReadFromJsonAsync<ContactMutationResponse>();
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var replacement = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/contacts", new CreateContactRequest("Outro contato", null, "OUTRO@EXAMPLE.TEST", null, true, deactivatedPayload!.Version));
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
    }

    [Fact]
    public async Task Units_normalize_validate_and_preserve_primary_integrity()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var client = await CreateReadyAdminAsync(factory);
        var customer = await CreateCustomerAsync(client);
        var invalid = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Matriz", "Rua A", "10", null, null, "São Paulo", "XX", "01001-000", false, customer.Version));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var first = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Matriz", "Rua A", "10", null, null, "São Paulo", "sp", "01001-000", true, customer.Version));
        var firstPayload = await first.Content.ReadFromJsonAsync<UnitMutationResponse>();
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("SP", firstPayload!.Unit.StateCode);
        Assert.Equal("01001000", firstPayload.Unit.PostalCode);
        var duplicatePrimary = await SendAsync(client, HttpMethod.Post, $"/api/customers/{customer.Id}/units", new CreateUnitRequest("Filial", "Rua B", "20", null, null, "Campinas", "SP", null, true, firstPayload.Version));
        Assert.Equal(HttpStatusCode.Conflict, duplicatePrimary.StatusCode);
        var wrongParent = await SendAsync(client, HttpMethod.Put, $"/api/customers/{Guid.NewGuid()}/units/{firstPayload.Unit.Id}", new UpdateUnitRequest("Matriz", "Rua A", "10", null, null, "São Paulo", "SP", null, true, firstPayload.Version));
        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
    }

    [Fact]
    public async Task Authorization_active_visibility_and_csrf_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var admin = await CreateReadyAdminAsync(factory);
        var customer = await CreateCustomerAsync(admin);
        var inactive = await SendAsync(admin, HttpMethod.Post, $"/api/customers/{customer.Id}/deactivate", new CustomerVersionRequest(customer.Version));
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        var user = await CreateUserAsync(factory, "customer-user@example.test", IdentityRoles.User);
        using var userClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(userClient, user.Email!, "Userpass1!Password")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userClient.GetAsync($"/api/customers/{customer.Id}")).StatusCode);
        var mutation = new HttpRequestMessage(HttpMethod.Post, "/api/customers") { Content = JsonContent.Create(new CreateCustomerRequest("Bloqueada", null, "04.252.011/0001-10", null)) };
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.SendAsync(mutation)).StatusCode);
        using var anonymous = IdentityTestClient.Create(factory);
        var csrfMissing = await anonymous.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Sem csrf", null, "04.252.011/0001-10", null));
        Assert.Equal(HttpStatusCode.Unauthorized, csrfMissing.StatusCode);
        var authenticatedMissingCsrf = await admin.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("Sem csrf", null, "04.252.011/0001-10", null));
        Assert.Equal(HttpStatusCode.BadRequest, authenticatedMissingCsrf.StatusCode);
    }

    private static async Task<CustomerDetailResponse> CreateCustomerAsync(HttpClient client, CreateCustomerRequest? request = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/customers", request ?? new CreateCustomerRequest("Empresa Teste LTDA", null, "04.252.011/0001-10", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;
    }

    private static async Task<HttpClient> CreateReadyAdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        await IdentityTestClient.LoginAsync(client);
        (await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ApplicationUser> CreateUserAsync(IdentityWebApplicationFactory factory, string email, string role)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Customer Reader", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = false };
        Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private sealed record ContactMutationResponse(CustomerContactResponse Contact, Guid Version);
    private sealed record UnitMutationResponse(CustomerUnitResponse Unit, Guid Version);
}
