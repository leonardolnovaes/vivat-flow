using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Quotes;
using Tsdt.Api.Services;
using Tsdt.Api.WorkOrders;
using Tsdt.Tests.Identity;

namespace Tsdt.Tests.WorkOrders;

public sealed class WorkOrderApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact, Trait("Category", "Unit")]
    public async Task Operational_sources_allow_management_only_and_do_not_disclose_commercial_data()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var manager = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.Manager, "source-manager@test");
        var worker = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "source-worker@test");
        using var managerClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(managerClient, manager.Email!, "Userpass1!Password");
        using var workerClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(workerClient, worker.Email!, "Userpass1!Password");

        foreach (var client in new[] { admin, managerClient })
        {
            var response = await client.GetAsync($"/api/work-orders/sources/quote/{quote.Id}");
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync();
            var source = JsonSerializer.Deserialize<WorkOrderSourceDetailResponse>(raw, Json)!;
            Assert.Equal(quote.Id, source.Source.QuoteId);
            Assert.True(source.Source.CanCreate);
            Assert.Equal("Original address", source.Source.ServiceAddressSnapshot);
            Assert.Equal("Historical service", Assert.Single(source.Items).ServiceNameSnapshot);
            foreach (var forbidden in new[] { "totalAmount", "approvedTotalAmount", "paymentType", "installmentCount", "paymentTerms", "price" })
                Assert.False(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Operational source includes {forbidden}.");
            var list = await client.GetFromJsonAsync<WorkOrderSourceListResponse>("/api/work-orders/sources?sourceType=Quote", Json);
            Assert.Contains(list!.Items, item => item.Id == quote.Id && item.CanCreate);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.GetAsync($"/api/quotes/{quote.Id}")).StatusCode);
        var managerOrder = await Send<WorkOrderDetailResponse>(managerClient, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null));
        Assert.Equal(WorkOrderStatus.Draft, managerOrder.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await workerClient.GetAsync($"/api/work-orders/sources/quote/{quote.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await workerClient.GetAsync("/api/work-orders/sources?sourceType=Quote")).StatusCode);

        var foreignOrganization = new Organization { Id = Guid.NewGuid(), Name = "Foreign", Slug = $"foreign-source-{Guid.NewGuid():N}", Status = OrganizationStatus.Active };
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Organizations.Add(foreignOrganization); await db.SaveChangesAsync(); }
        var foreign = await CreateUserAsync(factory, foreignOrganization.Id, IdentityRoles.Manager, "foreign-source-manager@test");
        using var foreignClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(foreignClient, foreign.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/work-orders/sources/quote/{quote.Id}")).StatusCode);
        Assert.Empty((await foreignClient.GetFromJsonAsync<WorkOrderSourceListResponse>("/api/work-orders/sources?sourceType=Quote", Json))!.Items);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var platform = new ApplicationUser { FullName = "Platform", UserName = "wo-source-platform@test", Email = "wo-source-platform@test", EmailConfirmed = true, IsActive = true, MustChangePassword = false, IsPlatformAdministrator = true };
            Assert.True((await users.CreateAsync(platform, "Userpass1!Password")).Succeeded);
            using var platformClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(platformClient, platform.Email!, "Userpass1!Password");
            Assert.Equal(HttpStatusCode.Forbidden, (await platformClient.GetAsync($"/api/work-orders/sources/quote/{quote.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await platformClient.GetAsync("/api/work-orders/sources?sourceType=Quote")).StatusCode);
        }
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Source_preview_tracks_governing_contract_and_current_work_order()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var contract = await SeedContractAsync(factory, quote, ContractStatus.Draft);
        var quotePath = $"/api/work-orders/sources/quote/{quote.Id}";
        var draftPreview = await admin.GetFromJsonAsync<WorkOrderSourceDetailResponse>(quotePath, Json);
        Assert.False(draftPreview!.Source.CanCreate);
        Assert.Equal(contract.Id, draftPreview.Source.GoverningContractId);
        Assert.Equal(nameof(ContractStatus.Draft), draftPreview.Source.GoverningContractStatus);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/work-orders/sources/contract/{Guid.NewGuid()}")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            contract.Status = ContractStatus.Active; db.Contracts.Update(contract); await db.SaveChangesAsync();
        }
        var contractPath = $"/api/work-orders/sources/contract/{contract.Id}";
        var contractPreview = await admin.GetFromJsonAsync<WorkOrderSourceDetailResponse>(contractPath, Json);
        Assert.True(contractPreview!.Source.CanCreate);
        Assert.Equal("Contract historical service", Assert.Single(contractPreview.Items).ServiceNameSnapshot);
        Assert.Equal("Original address", contractPreview.Source.ServiceAddressSnapshot);
        Assert.False((await admin.GetFromJsonAsync<WorkOrderSourceDetailResponse>(quotePath, Json))!.Source.CanCreate);
        Assert.Contains((await admin.GetFromJsonAsync<WorkOrderSourceListResponse>("/api/work-orders/sources?sourceType=Contract", Json))!.Items, item => item.Id == contract.Id && item.CanCreate);

        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null));
        Assert.Equal(order.Id, (await admin.GetFromJsonAsync<WorkOrderSourceDetailResponse>(quotePath, Json))!.Source.CurrentWorkOrderId);
        var occupied = await admin.GetFromJsonAsync<WorkOrderSourceDetailResponse>(contractPath, Json);
        Assert.Equal(order.Id, occupied!.Source.CurrentWorkOrderId);
        Assert.Equal(order.Number, occupied.Source.CurrentWorkOrderNumber);
        Assert.False(occupied.Source.CanCreate);
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Quote_creation_preserves_operational_snapshots_rejects_duplicates_and_allows_cancelled_replacement()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var created = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null));
        Assert.Equal(WorkOrderStatus.Draft, created.Status);
        Assert.StartsWith("OS-", created.Number);
        Assert.Equal("Historical service", created.Items.Single().ServiceNameSnapshot);
        Assert.Equal("Original address", created.ServiceAddressSnapshot);
        Assert.DoesNotContain("ApprovedTotalAmount", typeof(WorkOrderDetailResponse).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("PaymentType", typeof(WorkOrderDetailResponse).GetProperties().Select(property => property.Name));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
        var cancelled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{created.Id}/cancel", new WorkOrderVersionRequest(created.Version));
        Assert.Equal(WorkOrderStatus.Cancelled, cancelled.Status);
        var replacement = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null));
        Assert.NotEqual(created.Number, replacement.Number);
        Assert.Equal("Historical service", replacement.Items.Single().ServiceNameSnapshot);
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Contract_governance_and_source_validation_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var contract = new Contract { Id = Guid.NewGuid(), OrganizationId = quote.OrganizationId, QuoteId = quote.Id, CustomerId = quote.CustomerId, CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, ApprovedTotalAmount = 99, PaymentType = QuotePaymentType.Cash, Status = ContractStatus.Draft, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
            contract.Items.Add(new ContractItem { Id = Guid.NewGuid(), ContractId = contract.Id, QuoteItemId = quote.Items.Single().Id, ServiceId = quote.Items.Single().ServiceId, ServiceCodeSnapshot = "OLD", ServiceNameSnapshot = "Contract historical service", ServiceLineId = quote.Items.Single().ServiceLineIdSnapshot, ServiceLineCodeSnapshot = "OLD", ServiceLineNameSnapshot = "Contract historical line", DisplayOrder = 0 });
            db.Contracts.Add(contract); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null))).StatusCode);
            contract.Status = ContractStatus.Active; await db.SaveChangesAsync();
            var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null));
            Assert.Equal(WorkOrderSourceType.Contract, order.SourceType);
            Assert.Equal(contract.Id, order.ContractId);
            Assert.Equal("Contract historical service", order.Items.Single().ServiceNameSnapshot);
            Assert.Equal("Original address", order.ServiceAddressSnapshot);
        }
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Assigned_user_can_execute_only_own_work_and_management_closes()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var user = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "worker@test");
        var other = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "other@test");
        var start = DateTimeOffset.UtcNow.AddDays(1); var end = start.AddHours(2);
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, user.Id, start, end, "Prepare site"));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(Guid.NewGuid()))).StatusCode);
        var scheduled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(order.Version));
        using var worker = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(worker, user.Email!, "Userpass1!Password");
        using var stranger = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(stranger, other.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/work-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/work-orders/{order.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(worker, $"/api/work-orders/{order.Id}/close", new WorkOrderVersionRequest(scheduled.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(worker, $"/api/work-orders/{order.Id}/cancel", new WorkOrderVersionRequest(scheduled.Version))).StatusCode);
        var list = await worker.GetFromJsonAsync<WorkOrderListResponse>("/api/work-orders?assignedUserId=someone-else", Json);
        Assert.Equal(order.Id, Assert.Single(list!.Items).Id);
        var started = await Send<WorkOrderDetailResponse>(worker, HttpMethod.Post, $"/api/work-orders/{order.Id}/start", new WorkOrderVersionRequest(scheduled.Version));
        var completed = await Send<WorkOrderDetailResponse>(worker, HttpMethod.Post, $"/api/work-orders/{order.Id}/complete", new CompleteWorkOrderRequest(started.Version, "Done"));
        Assert.Equal(WorkOrderStatus.AwaitingClosure, completed.Status);
        Assert.NotNull(completed.ExecutionCompletedAtUtc);
        var history = await worker.GetFromJsonAsync<List<WorkOrderAuditResponse>>($"/api/work-orders/{order.Id}/history", Json);
        Assert.Contains(history!, item => item.Action == "WORK_ORDER_EXECUTION_COMPLETED");
        var closed = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{order.Id}/close", new WorkOrderVersionRequest(completed.Version));
        Assert.Equal(WorkOrderStatus.Closed, closed.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/work-orders/{order.Id}/cancel", new WorkOrderVersionRequest(closed.Version))).StatusCode);
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Planning_requires_eligible_assignee_and_valid_schedule()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var inactive = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "inactive@test");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var tracked = (await users.FindByIdAsync(inactive.Id))!; tracked.IsActive = false; Assert.True((await users.UpdateAsync(tracked)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, inactive.Id, null, null, null))).StatusCode);
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(order.Version))).StatusCode);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(null, start, start, null, order.Version))).StatusCode);
        var worker = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "planning-worker@test");
        var planned = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(worker.Id, start, start.AddHours(1), "Plan", order.Version));
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(worker.Id, start, start.AddHours(1), "Old", order.Version))).StatusCode);
        var scheduled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(planned.Version));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(null, start, start.AddHours(1), null, scheduled.Version))).StatusCode);
    }

    [Fact, Trait("Category", "Unit")]
    public async Task Tenant_and_platform_boundaries_and_invalid_quote_source_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            quote.Status = QuoteStatus.Draft; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); quote.Status = QuoteStatus.Approved; quote.ServiceAddressSnapshot = null; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); quote.ServiceAddressSnapshot = "Original address"; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null));
        var foreignOrganization = new Organization { Id = Guid.NewGuid(), Name = "Foreign", Slug = $"foreign-{Guid.NewGuid():N}", Status = OrganizationStatus.Active };
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Organizations.Add(foreignOrganization); await db.SaveChangesAsync(); }
        var foreign = await CreateUserAsync(factory, foreignOrganization.Id, IdentityRoles.Admin, "foreign-admin@test");
        using var foreignClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(foreignClient, foreign.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/work-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/work-orders/{order.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(foreignClient, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var platform = new ApplicationUser { FullName = "Platform", UserName = "wo-platform@test", Email = "wo-platform@test", EmailConfirmed = true, IsActive = true, MustChangePassword = false, IsPlatformAdministrator = true };
            Assert.True((await users.CreateAsync(platform, "Userpass1!Password")).Succeeded);
            using var platformClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(platformClient, platform.Email!, "Userpass1!Password");
            Assert.Equal(HttpStatusCode.Forbidden, (await platformClient.GetAsync("/api/work-orders")).StatusCode);
        }
    }

    private static async Task<HttpClient> AdminAsync(IdentityWebApplicationFactory factory)
    {
        var client = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(client);
        (await Post(client, "/api/auth/change-password", new ChangePasswordRequest("Bootstrap1!Pass", "Changed1!Password"))).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<ApplicationUser> CreateUserAsync(IdentityWebApplicationFactory factory, Guid organizationId, string role, string email)
    {
        using var scope = factory.Services.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { FullName = email, UserName = email, Email = email, EmailConfirmed = true, IsActive = true, MustChangePassword = false, OrganizationId = organizationId };
        Assert.True((await manager.CreateAsync(user, "Userpass1!Password")).Succeeded); Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded); return user;
    }
    private static async Task<Quote> SeedQuoteAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = (await db.Users.SingleAsync(user => user.Email == "admin@example.test")).OrganizationId!.Value;
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        var line = new ServiceLine { Id = Guid.NewGuid(), Code = $"L{id:N}"[..16], Name = "Current line" };
        var customer = new Customer { Id = Guid.NewGuid(), OrganizationId = organizationId, LegalName = "Historical customer", Cnpj = "12345678000199", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
        var service = new Service { Id = Guid.NewGuid(), OrganizationId = organizationId, ServiceLineId = line.Id, Code = "CURRENT", Name = "Current service", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
        var quote = new Quote { Id = id, OrganizationId = organizationId, Number = $"Q{id:N}"[..16], CustomerId = customer.Id, CustomerLegalNameSnapshot = customer.LegalName, CustomerCnpjSnapshot = customer.Cnpj, ServiceAddressSnapshot = "Original address", Status = QuoteStatus.Approved, TotalAmount = 99, PaymentType = QuotePaymentType.Cash, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
        quote.Items.Add(new QuoteItem { Id = Guid.NewGuid(), QuoteId = id, ServiceId = service.Id, ServiceCodeSnapshot = "OLD", ServiceNameSnapshot = "Historical service", ServiceLineIdSnapshot = line.Id, ServiceLineCodeSnapshot = "OLD", ServiceLineNameSnapshot = "Historical line", DisplayOrder = 0 });
        db.AddRange(line, customer, service, quote); await db.SaveChangesAsync(); return quote;
    }
    private static async Task<Contract> SeedContractAsync(IdentityWebApplicationFactory factory, Quote quote, ContractStatus status)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var contract = new Contract { Id = Guid.NewGuid(), OrganizationId = quote.OrganizationId, QuoteId = quote.Id, CustomerId = quote.CustomerId, CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, ApprovedTotalAmount = 99, PaymentType = QuotePaymentType.Cash, Status = status, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() };
        contract.Items.Add(new ContractItem { Id = Guid.NewGuid(), ContractId = contract.Id, QuoteItemId = quote.Items.Single().Id, ServiceId = quote.Items.Single().ServiceId, ServiceCodeSnapshot = "OLD", ServiceNameSnapshot = "Contract historical service", ServiceLineId = quote.Items.Single().ServiceLineIdSnapshot, ServiceLineCodeSnapshot = "OLD", ServiceLineNameSnapshot = "Contract historical line", DisplayOrder = 0 });
        db.Contracts.Add(contract); await db.SaveChangesAsync(); return contract;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body) => SendResponse(client, HttpMethod.Post, path, body);
    private static async Task<T> Send<T>(HttpClient client, HttpMethod method, string path, object body) { var response = await SendResponse(client, method, path, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<HttpResponseMessage> SendResponse(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request);
    }
}
