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

    [Fact, Trait("Category", "Integration")]
    public async Task Quote_creation_preserves_operational_snapshots_rejects_duplicates_and_allows_cancelled_replacement()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var activeContract = await SeedActiveContractAsync(factory, quote);
        var created = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null));
        Assert.Equal(WorkOrderStatus.Draft, created.Status);
        Assert.StartsWith("OS-", created.Number);
        Assert.Equal("Historical service", created.Items.Single().ServiceNameSnapshot);
        Assert.Equal("Original address", created.ServiceAddressSnapshot);
        Assert.DoesNotContain("ApprovedTotalAmount", typeof(WorkOrderDetailResponse).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("PaymentType", typeof(WorkOrderDetailResponse).GetProperties().Select(property => property.Name));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null))).StatusCode);
        var cancelled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{created.Id}/cancel", new WorkOrderVersionRequest(created.Version));
        Assert.Equal(WorkOrderStatus.Cancelled, cancelled.Status);
        var replacement = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null));
        Assert.NotEqual(created.Number, replacement.Number);
        Assert.Equal("Historical service", replacement.Items.Single().ServiceNameSnapshot);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Legacy_quote_origin_unfinished_order_blocks_contract_creation_for_the_same_quote()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory); var contract = await SeedActiveContractAsync(factory, quote);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WorkOrders.Add(new WorkOrder { Id = Guid.NewGuid(), OrganizationId = quote.OrganizationId, Number = "OS-LEGACY", SourceType = WorkOrderSourceType.Quote, QuoteId = quote.Id, CustomerId = quote.CustomerId, CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, ServiceAddressSnapshot = "Original address", Status = WorkOrderStatus.Scheduled, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null))).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
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
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null))).StatusCode);
            contract.Status = ContractStatus.Active; await db.SaveChangesAsync();
            var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(contract.Id, null, null, null, null));
            Assert.Equal(WorkOrderSourceType.Contract, order.SourceType);
            Assert.Equal(contract.Id, order.ContractId);
            Assert.Equal("Contract historical service", order.Items.Single().ServiceNameSnapshot);
            Assert.Equal("Original address", order.ServiceAddressSnapshot);
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Assigned_user_can_execute_only_own_work_and_completion_is_terminal()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var activeContract = await SeedActiveContractAsync(factory, quote);
        var user = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "worker@test");
        var other = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "other@test");
        var start = DateTimeOffset.UtcNow.AddDays(1); var end = start.AddHours(2);
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, user.Id, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(end.DateTime), "Prepare site", TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(end.DateTime)));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(Guid.NewGuid()))).StatusCode);
        var scheduled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(order.Version));
        using var worker = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(worker, user.Email!, "Userpass1!Password");
        using var stranger = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(stranger, other.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/work-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/work-orders/{order.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Post(worker, $"/api/work-orders/{order.Id}/close", new WorkOrderVersionRequest(scheduled.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(worker, $"/api/work-orders/{order.Id}/cancel", new WorkOrderVersionRequest(scheduled.Version))).StatusCode);
        var list = await worker.GetFromJsonAsync<WorkOrderListResponse>("/api/work-orders?assignedUserId=someone-else", Json);
        Assert.Equal(order.Id, Assert.Single(list!.Items).Id);
        var started = await Send<WorkOrderDetailResponse>(worker, HttpMethod.Post, $"/api/work-orders/{order.Id}/start", new WorkOrderVersionRequest(scheduled.Version));
        var completed = await Send<WorkOrderDetailResponse>(worker, HttpMethod.Post, $"/api/work-orders/{order.Id}/complete", new CompleteWorkOrderRequest(started.Version, "Done"));
        Assert.Equal(WorkOrderStatus.Completed, completed.Status);
        Assert.NotNull(completed.ExecutionCompletedAtUtc);
        var history = await worker.GetFromJsonAsync<List<WorkOrderAuditResponse>>($"/api/work-orders/{order.Id}/history", Json);
        Assert.Contains(history!, item => item.Action == "WORK_ORDER_EXECUTION_COMPLETED");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Post(admin, $"/api/work-orders/{order.Id}/close", new WorkOrderVersionRequest(completed.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, $"/api/work-orders/{order.Id}/cancel", new WorkOrderVersionRequest(completed.Version))).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Planning_requires_eligible_assignee_and_valid_schedule()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var activeContract = await SeedActiveContractAsync(factory, quote);
        var inactive = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "inactive@test");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var tracked = (await users.FindByIdAsync(inactive.Id))!; tracked.IsActive = false; Assert.True((await users.UpdateAsync(tracked)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, inactive.Id, null, null, null))).StatusCode);
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(order.Version))).StatusCode);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(null, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(start.DateTime), null, order.Version, TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(start.DateTime)))).StatusCode);
        var worker = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "planning-worker@test");
        var planned = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(worker.Id, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(start.AddHours(1).DateTime), "Plan", order.Version, TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(start.AddHours(1).DateTime)));
        Assert.Equal(HttpStatusCode.Conflict, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(worker.Id, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(start.AddHours(1).DateTime), "Old", order.Version, TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(start.AddHours(1).DateTime)))).StatusCode);
        var scheduled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{order.Id}/schedule", new WorkOrderVersionRequest(planned.Version));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{order.Id}/planning", new UpdateWorkOrderPlanningRequest(null, DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(start.AddHours(1).DateTime), null, scheduled.Version, TimeOnly.FromDateTime(start.DateTime), TimeOnly.FromDateTime(start.AddHours(1).DateTime)))).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Schedule_entitlement_blocks_schedule_writes_but_preserves_work_order_planning_and_execution()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var activeContract = await SeedActiveContractAsync(factory, quote);
        var firstWorker = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "schedule-first-worker@test");
        var secondWorker = await CreateUserAsync(factory, quote.OrganizationId, IdentityRoles.User, "schedule-second-worker@test");
        var start = DateTimeOffset.UtcNow.AddDays(1); var end = start.AddHours(2);
        var startDate = DateOnly.FromDateTime(start.DateTime); var endDate = DateOnly.FromDateTime(end.DateTime);
        var startTime = TimeOnly.FromDateTime(start.DateTime); var endTime = TimeOnly.FromDateTime(end.DateTime);
        var draft = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, firstWorker.Id, startDate, endDate, "Initial notes", startTime, endTime));
        var scheduled = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{draft.Id}/schedule", new WorkOrderVersionRequest(draft.Version));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var grant = await db.OrganizationFeatures.SingleAsync(item => item.OrganizationId == quote.OrganizationId && item.FeatureKey == FeatureCatalog.Schedule);
            db.OrganizationFeatures.Remove(grant);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"/api/work-orders/{scheduled.Id}/schedule", new WorkOrderVersionRequest(scheduled.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendResponse(admin, HttpMethod.Put, $"/api/work-orders/{scheduled.Id}/planning", new UpdateWorkOrderPlanningRequest(firstWorker.Id, startDate.AddDays(1), endDate.AddDays(1), "Reschedule", scheduled.Version, startTime, endTime))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, firstWorker.Id, startDate, endDate, null, startTime, endTime))).StatusCode);

        var planning = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Put, $"/api/work-orders/{scheduled.Id}/planning", new UpdateWorkOrderPlanningRequest(secondWorker.Id, startDate, endDate, "Operational notes updated", scheduled.Version, startTime, endTime));
        Assert.Equal(secondWorker.Id, planning.AssignedUserId);
        Assert.Equal("Operational notes updated", planning.OperationalNotes);

        var executing = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, $"/api/work-orders/{planning.Id}/start", new WorkOrderVersionRequest(planning.Version));
        Assert.Equal(WorkOrderStatus.InProgress, executing.Status);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Tenant_and_platform_boundaries_and_invalid_quote_source_are_enforced()
    {
        using var factory = new IdentityWebApplicationFactory(); using var admin = await AdminAsync(factory);
        var quote = await SeedQuoteAsync(factory);
        var activeContract = await SeedActiveContractAsync(factory, quote);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            quote.Status = QuoteStatus.Draft; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Post(admin, "/api/work-orders/from-quote", new CreateWorkOrderRequest(quote.Id, null, null, null, null))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); quote.Status = QuoteStatus.Approved; quote.ServiceAddressSnapshot = null; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); quote.ServiceAddressSnapshot = "Original address"; db.Quotes.Update(quote); await db.SaveChangesAsync();
        }
        var order = await Send<WorkOrderDetailResponse>(admin, HttpMethod.Post, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null));
        var foreignOrganization = new Organization { Id = Guid.NewGuid(), Name = "Foreign", Slug = $"foreign-{Guid.NewGuid():N}", Status = OrganizationStatus.Active };
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); db.Organizations.Add(foreignOrganization); await db.SaveChangesAsync(); }
        var foreign = await CreateUserAsync(factory, foreignOrganization.Id, IdentityRoles.Admin, "foreign-admin@test");
        using var foreignClient = IdentityTestClient.Create(factory); await IdentityTestClient.LoginAsync(foreignClient, foreign.Email!, "Userpass1!Password");
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/work-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/work-orders/{order.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(foreignClient, "/api/work-orders/from-contract", new CreateWorkOrderRequest(activeContract.Id, null, null, null, null))).StatusCode);
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
    private static async Task<Contract> SeedActiveContractAsync(IdentityWebApplicationFactory factory, Quote quote)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var source = quote.Items.Single();
        var contract = new Contract
        {
            Id = Guid.NewGuid(), OrganizationId = quote.OrganizationId, QuoteId = quote.Id, CustomerId = quote.CustomerId,
            CustomerLegalNameSnapshot = quote.CustomerLegalNameSnapshot, Kind = ContractKind.OneOff, Status = ContractStatus.Active,
            ApprovedTotalAmount = quote.TotalAmount ?? 0, PaymentType = quote.PaymentType ?? QuotePaymentType.Cash,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "test", UpdatedByUserId = "test", Version = Guid.NewGuid()
        };
        contract.Items.Add(new ContractItem
        {
            Id = Guid.NewGuid(), ContractId = contract.Id, QuoteItemId = source.Id, ServiceId = source.ServiceId,
            ServiceCodeSnapshot = source.ServiceCodeSnapshot, ServiceNameSnapshot = source.ServiceNameSnapshot,
            ServiceLineId = source.ServiceLineIdSnapshot, ServiceLineCodeSnapshot = source.ServiceLineCodeSnapshot,
            ServiceLineNameSnapshot = source.ServiceLineNameSnapshot, DisplayOrder = source.DisplayOrder
        });
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body) => SendResponse(client, HttpMethod.Post, path, body);
    private static async Task<T> Send<T>(HttpClient client, HttpMethod method, string path, object body) { var response = await SendResponse(client, method, path, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<HttpResponseMessage> SendResponse(HttpClient client, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", await IdentityTestClient.GetCsrfTokenAsync(client)); return await client.SendAsync(request);
    }
}
