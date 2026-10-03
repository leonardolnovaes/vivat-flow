using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Quotes;
using Tsdt.Api.Services;
using Tsdt.Api.WorkOrders;

namespace Tsdt.Tests.WorkOrders;

[Trait("Category", "Unit")]
public sealed class WorkOrderSourceQueryTests
{
    [Fact]
    public async Task Active_contracts_remain_visible_with_precise_eligibility()
    {
        using var fixture = new Fixture();
        var eligible = fixture.AddContract("Eligible", ContractStatus.Active);
        var open = fixture.AddContract("Open", ContractStatus.Active);
        var missingAddress = fixture.AddContract("No address", ContractStatus.Active, address: null);
        var noServices = fixture.AddContract("No services", ContractStatus.Active, withService: false);
        var multipleProblems = fixture.AddContract("Multiple problems", ContractStatus.Active, address: null, withService: false);
        fixture.AddContract("Draft", ContractStatus.Draft);
        fixture.AddContract("Ended", ContractStatus.Ended);
        fixture.AddContract("Cancelled", ContractStatus.Cancelled);
        fixture.AddOrder(open, WorkOrderStatus.Scheduled);
        fixture.AddOrder(eligible, WorkOrderStatus.Completed);
        fixture.AddOrder(eligible, WorkOrderStatus.Cancelled);
        fixture.Save();

        var result = await fixture.List();
        Assert.Equal(5, result.TotalCount);
        Assert.True(result.Items.Single(item => item.Id == eligible.Id).CanCreate);
        var blocked = result.Items.Single(item => item.Id == open.Id);
        Assert.False(blocked.CanCreate);
        Assert.Equal(["OpenWorkOrder"], blocked.CreationBlockReasons);
        Assert.NotNull(blocked.CurrentWorkOrderId);
        Assert.Equal("OS-OPEN", blocked.CurrentWorkOrderNumber);
        Assert.Equal(["MissingServiceAddress"], result.Items.Single(item => item.Id == missingAddress.Id).CreationBlockReasons);
        Assert.Equal(["NoOperationalServices"], result.Items.Single(item => item.Id == noServices.Id).CreationBlockReasons);
        Assert.Equal(["MissingServiceAddress", "NoOperationalServices"], result.Items.Single(item => item.Id == multipleProblems.Id).CreationBlockReasons);
        Assert.Equal(5, (await WorkOrderSourceEndpoints.ListSourcesAsync(fixture.Db, fixture.Tenant, null, 1, 2)).TotalCount);
        Assert.Equal(2, (await WorkOrderSourceEndpoints.ListSourcesAsync(fixture.Db, fixture.Tenant, null, 1, 2)).Items.Count);
    }

    [Fact]
    public async Task Search_matches_current_names_snapshot_cnpj_and_quote_number_with_tenant_scope()
    {
        using var fixture = new Fixture();
        var contract = fixture.AddContract("Historical Snapshot", ContractStatus.Active);
        var customer = fixture.Db.Customers.Local.Single(item => item.Id == contract.CustomerId);
        customer.LegalName = "Current Legal";
        customer.TradeName = "Current Trade";
        var foreign = fixture.AddContract("Foreign Match", ContractStatus.Active, tenant: fixture.ForeignTenant);
        fixture.Save();

        foreach (var term in new[] { "current legal", "CURRENT TRADE", "historical snapshot", "04.252.011/0001-10", "04252011000110", "ORC-Historical Snapshot" })
            Assert.Equal(contract.Id, Assert.Single((await fixture.List(term)).Items).Id);
        Assert.Empty((await fixture.List("Foreign Match")).Items);
        Assert.DoesNotContain(foreign.Id, (await fixture.List()).Items.Select(item => item.Id));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; }
        public Guid Tenant { get; } = Guid.NewGuid();
        public Guid ForeignTenant { get; } = Guid.NewGuid();
        private readonly Guid lineId = Guid.NewGuid();

        public Fixture()
        {
            connection.Open();
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Db.Organizations.AddRange(new Organization { Id = Tenant, Name = "Tenant", Slug = "source-test" }, new Organization { Id = ForeignTenant, Name = "Foreign", Slug = "source-foreign" });
            Db.ServiceLines.Add(new ServiceLine { Id = lineId, Code = "SST", Name = "SST" });
            Db.SaveChanges();
        }

        public Contract AddContract(string name, ContractStatus status, string? address = "Address", bool withService = true, Guid? tenant = null)
        {
            var organizationId = tenant ?? Tenant;
            var cnpj = Db.Customers.Local.Count == 0 ? "04252011000110" : $"{Db.Customers.Local.Count + 1:00000000000000}";
            var customer = new Customer { Id = Guid.NewGuid(), OrganizationId = organizationId, LegalName = name, Cnpj = cnpj, CreatedByUserId = "test", UpdatedByUserId = "test" };
            var quote = new Quote { Id = Guid.NewGuid(), OrganizationId = organizationId, Number = $"ORC-{name}", CustomerId = customer.Id, CustomerLegalNameSnapshot = name, CustomerCnpjSnapshot = customer.Cnpj, ServiceAddressSnapshot = address, Status = QuoteStatus.Approved, CreatedByUserId = "test", UpdatedByUserId = "test" };
            var contract = new Contract { Id = Guid.NewGuid(), OrganizationId = organizationId, QuoteId = quote.Id, CustomerId = customer.Id, CustomerLegalNameSnapshot = name, Status = status, CreatedByUserId = "test", UpdatedByUserId = "test" };
            if (withService)
            {
                var service = new Service { Id = Guid.NewGuid(), OrganizationId = organizationId, ServiceLineId = lineId, Code = $"S{Guid.NewGuid():N}"[..12], Name = "Service", CreatedByUserId = "test", UpdatedByUserId = "test" };
                var quoteItem = new QuoteItem { Id = Guid.NewGuid(), QuoteId = quote.Id, ServiceId = service.Id, ServiceCodeSnapshot = service.Code, ServiceNameSnapshot = service.Name, ServiceLineIdSnapshot = lineId, ServiceLineCodeSnapshot = "SST", ServiceLineNameSnapshot = "SST" };
                quote.Items.Add(quoteItem);
                contract.Items.Add(new ContractItem { Id = Guid.NewGuid(), ContractId = contract.Id, QuoteItemId = quoteItem.Id, ServiceId = service.Id, ServiceCodeSnapshot = service.Code, ServiceNameSnapshot = service.Name, ServiceLineId = lineId, ServiceLineCodeSnapshot = "SST", ServiceLineNameSnapshot = "SST" });
                Db.Services.Add(service);
            }
            Db.AddRange(customer, quote, contract);
            return contract;
        }

        public void AddOrder(Contract contract, WorkOrderStatus status) => Db.WorkOrders.Add(new WorkOrder
        {
            Id = Guid.NewGuid(), OrganizationId = contract.OrganizationId, Number = status == WorkOrderStatus.Scheduled ? "OS-OPEN" : $"OS-{status}",
            SourceType = WorkOrderSourceType.Contract, QuoteId = contract.QuoteId, ContractId = contract.Id, CustomerId = contract.CustomerId,
            CustomerLegalNameSnapshot = contract.CustomerLegalNameSnapshot, ServiceAddressSnapshot = "Address", Status = status,
            CreatedByUserId = "test", UpdatedByUserId = "test"
        });

        public void Save() => Db.SaveChanges();
        public Task<WorkOrderSourceListResponse> List(string? search = null) => WorkOrderSourceEndpoints.ListSourcesAsync(Db, Tenant, search, 1, 20);
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
}
