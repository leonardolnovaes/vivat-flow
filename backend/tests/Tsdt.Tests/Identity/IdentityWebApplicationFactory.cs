using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly bool bootstrapEnabled;

    public IdentityWebApplicationFactory(bool bootstrapEnabled = true) => this.bootstrapEnabled = bootstrapEnabled;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Data Source=ignored.db" };
        if (bootstrapEnabled)
        {
            settings["BootstrapAdmin:Email"] = "admin@example.test";
            settings["BootstrapAdmin:FullName"] = "Bootstrap Administrator";
            settings["BootstrapAdmin:Password"] = "Bootstrap1!Pass";
        }
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

public static class IdentityTestClient
{
    public static HttpClient Create(IdentityWebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true
    });

    public static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        return Assert.IsType<string>(payload?.Token);
    }

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email = "admin@example.test", string password = "Bootstrap1!Pass")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfTokenAsync(client));
        return await client.SendAsync(request);
    }
}
