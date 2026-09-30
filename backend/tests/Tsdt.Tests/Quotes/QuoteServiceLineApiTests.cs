using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Quotes;
using Tsdt.Api.Services;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Quotes;

[Trait("Category", "Unit")]
public sealed class QuoteServiceLineApiTests
{
    [Fact]
    public async Task Quote_items_support_multiple_enabled_lines_and_preserve_disabled_line_history()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var admin = await ReadyAdminAsync(factory);
        var organizationId = await BootstrapOrganizationIdAsync(factory);
        var additionalLine = new ServiceLine { Code = "CLEANING", Name = "Limpeza" };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ServiceLines.Add(additionalLine);
            db.OrganizationServiceLines.Add(new OrganizationServiceLine { OrganizationId = organizationId, ServiceLineId = additionalLine.Id });
            await db.SaveChangesAsync();
        }

        var customer = await SendAsync<CustomerDetailResponse>(admin, HttpMethod.Post, "/api/customers", new CreateCustomerRequest("Quote customer", null, "04.252.011/0001-10", null));
        var sst = await SendAsync<ServiceDetailResponse>(admin, HttpMethod.Post, "/api/services", new CreateServiceRequest("SST-001", "Avaliação SST", null, 100m, ServiceLineTestData.SstId));
        var cleaning = await SendAsync<ServiceDetailResponse>(admin, HttpMethod.Post, "/api/services", new CreateServiceRequest("CLEAN-001", "Limpeza pós-obra", null, 150m, additionalLine.Id));

        var quote = await SendAsync<QuoteDetailResponse>(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, sst.Id), new QuoteItemRequest(null, cleaning.Id)], 250m, QuotePaymentType.Cash, null, null));
        Assert.Equal(250m, quote.TotalAmount);
        Assert.Equal(["Segurança e Saúde no Trabalho", "Limpeza"], quote.Items.Select(item => item.ServiceLineName));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OrganizationServiceLines.Remove(await db.OrganizationServiceLines.SingleAsync(item => item.OrganizationId == organizationId && item.ServiceLineId == additionalLine.Id));
            await db.SaveChangesAsync();
        }

        var historical = await admin.GetFromJsonAsync<QuoteDetailResponse>($"/api/quotes/{quote.Id}", Json);
        Assert.Equal("Limpeza", historical!.Items.Single(item => item.ServiceId == cleaning.Id).ServiceLineName);
        var updated = await SendAsync<QuoteDetailResponse>(admin, HttpMethod.Put, $"/api/quotes/{quote.Id}", new UpdateQuoteRequest(customer.Id, historical.Items.Select(item => new QuoteItemRequest(item.Id, item.ServiceId)).ToList(), 250m, QuotePaymentType.Cash, null, "Historical item retained", historical.Version));
        Assert.Equal("Historical item retained", updated.Notes);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponseAsync(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, cleaning.Id)], 150m, QuotePaymentType.Cash, null, null))).StatusCode);

        var foreignServiceId = await CreateForeignServiceAsync(factory, additionalLine.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponseAsync(admin, HttpMethod.Post, "/api/quotes", new CreateQuoteRequest(customer.Id, [new QuoteItemRequest(null, foreignServiceId)], 100m, QuotePaymentType.Cash, null, null))).StatusCode);
    }

    private static async Task<HttpClient> ReadyAdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory);
        (await IdentityTestClient.LoginAsync(client)).EnsureSuccessStatusCode();
        (await SendResponseAsync(client, HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        await ServiceLineTestData.EnableSstForBootstrapOrganizationAsync(factory);
        return client;
    }

    private static async Task<Guid> BootstrapOrganizationIdAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(user => user.Email == "admin@example.test").Select(user => user.OrganizationId).SingleAsync() ?? throw new InvalidOperationException();
    }

    private static async Task<Guid> CreateForeignServiceAsync(IdentityWebApplicationFactory factory, Guid serviceLineId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organization = new Organization { Name = "Other tenant", Slug = $"other-tenant-{Guid.NewGuid():N}" };
        var service = new Service { Id = Guid.NewGuid(), OrganizationId = organization.Id, ServiceLineId = serviceLineId, Code = "FOREIGN-001", Name = "Foreign service", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
        db.Organizations.Add(organization); db.Services.Add(service); await db.SaveChangesAsync();
        return service.Id;
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object body)
    {
        var response = await SendResponseAsync(client, method, path, body); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpResponseMessage> SendResponseAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
