using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tsdt.Api.Customers;
using Tsdt.Api.Documents;
using Tsdt.Api.Identity;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.Documents;

[Trait("Category", "Unit")]
public sealed class DocumentEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public async Task Authenticated_upload_list_and_download_use_customer_scope_and_sanitized_attachment()
    {
        using var baseFactory = new IdentityWebApplicationFactory();
        var storage = new TestDocumentStorage();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDocumentStorage>();
            services.AddSingleton<IDocumentStorage>(storage);
        }));
        using var client = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/documents/{Guid.NewGuid()}/download")).StatusCode);
        (await IdentityTestClient.LoginAsync(client)).EnsureSuccessStatusCode();
        (await SendJsonAsync(client, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        var customerResponse = await SendJsonAsync(client, "/api/customers", new CreateCustomerRequest("Document Customer", null, "04252011000110", null));
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;

        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("Report"), "category");
        multipart.Add(new StringContent("CustomerDeliverable"), "purpose");
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nexample"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "file", @"folder\report.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customer.Id}/documents") { Content = multipart };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        var document = JsonSerializer.Deserialize<DocumentResponse>(responseBody, Json)!;
        Assert.Equal("report.pdf", document.FileName);
        Assert.Equal(DocumentPurpose.CustomerDeliverable, document.Purpose);
        Assert.DoesNotContain("storageKey", responseBody, StringComparison.OrdinalIgnoreCase);

        var list = (await client.GetFromJsonAsync<DocumentListResponse>($"/api/customers/{customer.Id}/documents", Json))!;
        Assert.Equal(document.Id, Assert.Single(list.Items).Id);
        Assert.Equal("Cliente", Assert.Single(list.Items).ContextLabel);
        var contexts = (await client.GetFromJsonAsync<List<DocumentContextOption>>($"/api/customers/{customer.Id}/documents/contexts", Json))!;
        Assert.Contains(contexts, option => option.Type == DocumentContextType.Customer && option.Id == customer.Id);
        using var download = await client.GetAsync($"/api/documents/{document.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("report.pdf", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName ?? "");
        Assert.Equal("%PDF-1.7\nexample", await download.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Tenant_user_without_assigned_work_cannot_read_or_upload()
    {
        using var baseFactory = new IdentityWebApplicationFactory();
        var storage = new TestDocumentStorage();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDocumentStorage>();
            services.AddSingleton<IDocumentStorage>(storage);
        }));
        using var admin = CreateClient(factory);
        (await IdentityTestClient.LoginAsync(admin)).EnsureSuccessStatusCode();
        (await SendJsonAsync(admin, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        var customerResponse = await SendJsonAsync(admin, "/api/customers", new CreateCustomerRequest("Tenant User Customer", null, "04252011000110", null));
        customerResponse.EnsureSuccessStatusCode();
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var administrator = await users.FindByEmailAsync("admin@example.test");
            var user = new ApplicationUser { FullName = "Tenant User", UserName = "document-user@example.test", Email = "document-user@example.test",
                EmailConfirmed = true, IsActive = true, MustChangePassword = false, OrganizationId = administrator!.OrganizationId };
            Assert.True((await users.CreateAsync(user, "Userpass1!Password")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, IdentityRoles.User)).Succeeded);
        }
        using var userClient = CreateClient(factory);
        (await IdentityTestClient.LoginAsync(userClient, "document-user@example.test", "Userpass1!Password")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await userClient.GetAsync($"/api/customers/{customer.Id}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync($"/api/customers/{customer.Id}/documents/contexts")).StatusCode);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("Report"), "category");
        multipart.Add(new ByteArrayContent("%PDF-1.7"u8.ToArray()), "file", "report.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customer.Id}/documents") { Content = multipart };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(userClient));
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Manager_cannot_upload_commercial_documents()
    {
        using var factory = new IdentityWebApplicationFactory();
        using var bootstrap = CreateClient(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var administrator = await users.FindByEmailAsync("admin@example.test");
            var manager = new ApplicationUser { FullName = "Document Manager", UserName = "document-manager@example.test",
                Email = "document-manager@example.test", EmailConfirmed = true, IsActive = true,
                MustChangePassword = false, OrganizationId = administrator!.OrganizationId };
            Assert.True((await users.CreateAsync(manager, "Manager1!Password")).Succeeded);
            Assert.True((await users.AddToRoleAsync(manager, IdentityRoles.Manager)).Succeeded);
        }
        using var client = CreateClient(factory);
        (await IdentityTestClient.LoginAsync(client, "document-manager@example.test", "Manager1!Password")).EnsureSuccessStatusCode();
        var csrf = await IdentityTestClient.GetCsrfTokenAsync(client);
        foreach (var (category, contextType) in new[] { ("Contract", ""), ("Report", "Quote") })
        {
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(category), "category");
            multipart.Add(new StringContent("InternalSupporting"), "purpose");
            if (contextType.Length > 0)
            {
                multipart.Add(new StringContent(contextType), "contextType");
                multipart.Add(new StringContent(Guid.NewGuid().ToString()), "contextId");
            }
            multipart.Add(new ByteArrayContent("%PDF-1.7"u8.ToArray()), "file", "report.pdf");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{Guid.NewGuid()}/documents") { Content = multipart };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }

}
