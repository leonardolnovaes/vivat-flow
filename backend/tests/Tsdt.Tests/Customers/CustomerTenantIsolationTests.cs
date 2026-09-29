using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Customers;

[Trait("Category", "Unit")]
public sealed class CustomerTenantIsolationTests
{
    [Fact]
    public async Task Customers_are_scoped_to_the_authenticated_organization_and_browser_ownership_is_ignored()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var organizationAClient = await CreateReadyAdminAsync(factory);
        var organizationB = await CreateOrganizationAsync(factory);
        var organizationBUser = await CreateTenantUserAsync(factory, organizationB.Id, "organization-b-manager@example.test", IdentityRoles.Manager);
        using var organizationBClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(organizationBClient, organizationBUser.Email!, "Userpass1!Password")).StatusCode);

        var customerA = await CreateCustomerAsync(organizationAClient, "Cliente da Organização A");
        var browserSuppliedOwnership = new { legalName = "Cliente da Organização B", tradeName = (string?)null, cnpj = "04.252.011/0001-10", notes = (string?)null, organizationId = await OrganizationAIdAsync(factory) };
        var createdForB = await SendAsync(organizationBClient, HttpMethod.Post, "/api/customers", browserSuppliedOwnership);
        Assert.Equal(HttpStatusCode.Created, createdForB.StatusCode);
        var customerB = (await createdForB.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(organizationB.Id, await db.Customers.Where(customer => customer.Id == customerB.Id).Select(customer => customer.OrganizationId).SingleAsync());
        }

        var listA = (await organizationAClient.GetFromJsonAsync<CustomerListResponse>("/api/customers"))!;
        Assert.Contains(listA.Items, customer => customer.Id == customerA.Id);
        Assert.DoesNotContain(listA.Items, customer => customer.Id == customerB.Id);
        Assert.Equal(HttpStatusCode.OK, (await organizationAClient.GetAsync($"/api/customers/{customerA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await organizationAClient.GetAsync($"/api/customers/{customerB.Id}")).StatusCode);

        var searchA = (await organizationAClient.GetFromJsonAsync<CustomerListResponse>("/api/customers?search=Organização%20B"))!;
        Assert.DoesNotContain(searchA.Items, customer => customer.Id == customerB.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationAClient, HttpMethod.Put, $"/api/customers/{customerB.Id}", new UpdateCustomerRequest("Tentativa", null, customerB.Cnpj, null, customerB.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationAClient, HttpMethod.Post, $"/api/customers/{customerB.Id}/activate", new CustomerVersionRequest(customerB.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationAClient, HttpMethod.Post, $"/api/customers/{customerB.Id}/deactivate", new CustomerVersionRequest(customerB.Version))).StatusCode);
    }

    [Fact]
    public async Task Contacts_and_units_inherit_their_customer_tenant_boundary()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var organizationAClient = await CreateReadyAdminAsync(factory);
        var organizationB = await CreateOrganizationAsync(factory);
        var organizationBUser = await CreateTenantUserAsync(factory, organizationB.Id, "organization-b-manager@example.test", IdentityRoles.Manager);
        using var organizationBClient = IdentityTestClient.Create(factory);
        Assert.Equal(HttpStatusCode.OK, (await IdentityTestClient.LoginAsync(organizationBClient, organizationBUser.Email!, "Userpass1!Password")).StatusCode);

        var customerA = await CreateCustomerAsync(organizationAClient, "Cliente da Organização A");
        var customerB = await CreateCustomerAsync(organizationBClient, "Cliente da Organização B");
        var contactResponse = await SendAsync(organizationAClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/contacts", new CreateContactRequest("Contato A", null, "a@example.test", null, false, customerA.Version));
        var contact = (await contactResponse.Content.ReadFromJsonAsync<ContactMutationResponse>())!;
        var unitResponse = await SendAsync(organizationAClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/units", new CreateUnitRequest("Unidade A", "Rua A", "1", null, null, "São Paulo", "SP", null, false, contact.Version));
        var unit = (await unitResponse.Content.ReadFromJsonAsync<UnitMutationResponse>())!;
        Assert.Equal(HttpStatusCode.Created, contactResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);

        var detailA = (await organizationAClient.GetFromJsonAsync<CustomerDetailResponse>($"/api/customers/{customerA.Id}"))!;
        Assert.Contains(detailA.Contacts, item => item.Id == contact.Contact.Id);
        Assert.Contains(detailA.Units, item => item.Id == unit.Unit.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/contacts", new CreateContactRequest("Tentativa", null, "blocked@example.test", null, false, unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Put, $"/api/customers/{customerA.Id}/contacts/{contact.Contact.Id}", new UpdateContactRequest("Tentativa", null, "blocked@example.test", null, false, unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/contacts/{contact.Contact.Id}/deactivate", new CustomerVersionRequest(unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/units", new CreateUnitRequest("Tentativa", "Rua", "1", null, null, "São Paulo", "SP", null, false, unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Put, $"/api/customers/{customerA.Id}/units/{unit.Unit.Id}", new UpdateUnitRequest("Tentativa", "Rua", "1", null, null, "São Paulo", "SP", null, false, unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(organizationBClient, HttpMethod.Post, $"/api/customers/{customerA.Id}/units/{unit.Unit.Id}/deactivate", new CustomerVersionRequest(unit.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await organizationBClient.GetAsync($"/api/customers/{customerB.Id}")).StatusCode);
    }

    private static async Task<Guid> OrganizationAIdAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var organizationId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users
            .Where(user => user.Email == "admin@example.test")
            .Select(user => user.OrganizationId)
            .SingleAsync();
        return organizationId ?? throw new InvalidOperationException("The bootstrap administrator must belong to Organization A.");
    }

    private static async Task<Organization> CreateOrganizationAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organization = new Organization { Name = "Organização B", Slug = $"organization-b-{Guid.NewGuid():N}" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization;
    }

    private static async Task<ApplicationUser> CreateTenantUserAsync(IdentityWebApplicationFactory factory, Guid organizationId, string email, string role)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = "Organization B Manager", UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = false, OrganizationId = organizationId };
        Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<CustomerDetailResponse> CreateCustomerAsync(HttpClient client, string legalName)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/customers", new CreateCustomerRequest(legalName, null, "04.252.011/0001-10", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;
    }

    private static async Task<HttpClient> CreateReadyAdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(client)).EnsureSuccessStatusCode();
        (await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        return client;
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
