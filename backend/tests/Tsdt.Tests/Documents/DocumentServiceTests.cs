using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Documents;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;
using Tsdt.Api.Quotes;
using Tsdt.Api.WorkOrders;

namespace Tsdt.Tests.Documents;

[Trait("Category", "Unit")]
public sealed class DocumentServiceTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nminimal test content");

    [Fact]
    public async Task Upload_persists_metadata_and_audit_and_can_be_listed_and_downloaded()
    {
        using var fixture = new Fixture();
        var upload = fixture.Upload(fixture.CustomerA.Id, fileName: @"folder\report.pdf");
        var result = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", upload);

        Assert.Equal("report.pdf", result.FileName);
        Assert.Equal(fixture.CustomerA.Id, result.CustomerId);
        Assert.Equal(DocumentCategory.Report, result.Category);
        Assert.Equal(DocumentPurpose.InternalSupporting, result.Purpose);
        Assert.Equal(Pdf.Length, result.SizeBytes);
        Assert.Equal("actor", result.UploadedByUserId);
        Assert.DoesNotContain("storage", string.Join(' ', typeof(DocumentResponse).GetProperties().Select(property => property.Name)), StringComparison.OrdinalIgnoreCase);
        var stored = await fixture.Db.Documents.SingleAsync();
        Assert.NotEqual(Guid.Empty, stored.StorageKey);
        Assert.Single(await fixture.Db.DocumentAuditRecords.Where(audit => audit.DocumentId == result.Id && audit.Action == "DOCUMENT_UPLOADED" && audit.ActorUserId == "actor").ToListAsync());
        Assert.Equal(result.Id, Assert.Single((await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "actor", DocumentAccessLevel.Admin)).Items).Id);
        var download = await fixture.Service.DownloadAsync(fixture.OrganizationA.Id, result.Id, "actor", DocumentAccessLevel.Admin);
        await using var content = download.Content;
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes);
        Assert.Equal(Pdf, bytes.ToArray());
        Assert.Equal("report.pdf", download.FileName);
    }

    [Fact]
    public async Task Customer_and_context_must_belong_to_the_same_organization_and_customer()
    {
        using var fixture = new Fixture();
        var orderA = fixture.AddCompletedOrder(fixture.CustomerA);
        var orderB = fixture.AddCompletedOrder(fixture.CustomerB);
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA2.Id, DocumentContextType.WorkOrder, orderA.Id)));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, orderB.Id)));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerB.Id)));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.Customer, fixture.CustomerA2.Id)));
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
        Assert.Equal(0, fixture.Storage.SavedCount);
    }

    [Fact]
    public async Task Completed_work_order_accepts_a_later_document()
    {
        using var fixture = new Fixture();
        var order = fixture.AddCompletedOrder(fixture.CustomerA);
        var result = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, order.Id));
        Assert.Equal(DocumentContextType.WorkOrder, result.ContextType);
        Assert.Equal(order.Id, result.ContextId);
        Assert.Equal(WorkOrderStatus.Completed, (await fixture.Db.WorkOrders.SingleAsync(item => item.Id == order.Id)).Status);
    }

    [Fact]
    public async Task All_supported_contexts_resolve_to_the_same_customer()
    {
        using var fixture = new Fixture();
        var order = fixture.AddCompletedOrder(fixture.CustomerA);
        var unit = new CustomerUnit { Id = Guid.NewGuid(), CustomerId = fixture.CustomerA.Id, Name = "Main",
            Street = "Street", Number = "1", District = "District", City = "City", StateCode = "SP" };
        fixture.Db.CustomerUnits.Add(unit);
        fixture.Db.SaveChanges();
        foreach (var (type, id) in new[]
        {
            (DocumentContextType.Customer, fixture.CustomerA.Id),
            (DocumentContextType.CustomerUnit, unit.Id),
            (DocumentContextType.Quote, order.QuoteId),
            (DocumentContextType.Contract, order.ContractId!.Value),
            (DocumentContextType.WorkOrder, order.Id)
        })
        {
            var result = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
                fixture.Upload(fixture.CustomerA.Id, type, id));
            Assert.Equal(type, result.ContextType);
            Assert.Equal(id, result.ContextId);
        }
        Assert.Equal(5, await fixture.Db.Documents.CountAsync());
    }

    [Fact]
    public async Task Empty_oversized_and_disguised_files_are_rejected()
    {
        using var fixture = new Fixture(maxBytes: Pdf.Length);
        foreach (var upload in new[]
        {
            fixture.Upload(fixture.CustomerA.Id, bytes: []),
            fixture.Upload(fixture.CustomerA.Id, bytes: new byte[Pdf.Length + 1]),
            fixture.Upload(fixture.CustomerA.Id, fileName: "script.exe"),
            fixture.Upload(fixture.CustomerA.Id, bytes: Encoding.ASCII.GetBytes("not a PDF")),
            fixture.Upload(fixture.CustomerA.Id, contentType: "image/png"),
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.Quote)
        })
            await Assert.ThrowsAsync<DocumentValidationException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", upload));
        var understated = fixture.Upload(fixture.CustomerA.Id, bytes: new byte[Pdf.Length + 1]) with { DeclaredLength = Pdf.Length };
        await Assert.ThrowsAsync<DocumentValidationException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", understated));
        Assert.Empty(await fixture.Db.Documents.ToListAsync());
        Assert.Equal(0, fixture.Storage.SavedCount);
    }

    [Fact]
    public async Task Storage_failure_and_database_failure_leave_no_document_or_orphaned_file()
    {
        using (var fixture = new Fixture())
        {
            fixture.Storage.FailAfterSave = true;
            await Assert.ThrowsAsync<IOException>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", fixture.Upload(fixture.CustomerA.Id)));
            Assert.Empty(await fixture.Db.Documents.AsNoTracking().ToListAsync());
            Assert.Equal(0, fixture.Storage.SavedCount);
        }
        using (var fixture = new Fixture())
        {
            await fixture.Db.Database.ExecuteSqlRawAsync("DROP TABLE \"DocumentAuditRecords\"");
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", fixture.Upload(fixture.CustomerA.Id)));
            Assert.Empty(await fixture.Db.Documents.AsNoTracking().ToListAsync());
            Assert.Equal(0, fixture.Storage.SavedCount);
        }
    }

    [Fact]
    public async Task Lists_and_downloads_do_not_disclose_other_tenants_documents()
    {
        using var fixture = new Fixture();
        var documentA = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor", fixture.Upload(fixture.CustomerA.Id));
        var documentB = await fixture.Service.UploadAsync(fixture.OrganizationB.Id, "actor-b", fixture.Upload(fixture.CustomerB.Id));
        Assert.Equal(documentA.Id, Assert.Single((await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "actor", DocumentAccessLevel.Admin)).Items).Id);
        Assert.Equal(documentB.Id, Assert.Single((await fixture.Service.ListAsync(fixture.OrganizationB.Id, fixture.CustomerB.Id, "actor-b", DocumentAccessLevel.Admin)).Items).Id);
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerB.Id, "actor", DocumentAccessLevel.Admin));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationA.Id, documentB.Id, "actor", DocumentAccessLevel.Admin));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationB.Id, documentA.Id, "actor-b", DocumentAccessLevel.Admin));
    }

    [Fact]
    public async Task User_reads_only_noncommercial_documents_for_assigned_work_on_active_customers()
    {
        using var fixture = new Fixture();
        var assigned = fixture.AddCompletedOrder(fixture.CustomerA);
        var other = fixture.AddCompletedOrder(fixture.CustomerA);
        fixture.Db.Users.AddRange(
            new ApplicationUser { Id = "assigned-user", UserName = "assigned-user", FullName = "Assigned User", OrganizationId = fixture.OrganizationA.Id },
            new ApplicationUser { Id = "other-user", UserName = "other-user", FullName = "Other User", OrganizationId = fixture.OrganizationA.Id });
        assigned.AssignedUserId = "assigned-user";
        other.AssignedUserId = "other-user";
        await fixture.Db.SaveChangesAsync();

        var visible = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "manager",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, assigned.Id) with { Purpose = DocumentPurpose.CustomerDeliverable });
        var unassigned = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "manager",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, other.Id));
        var commercialCategory = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "admin",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, assigned.Id) with { Category = DocumentCategory.Contract });
        var commercialContext = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "admin",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.Quote, assigned.QuoteId));
        var customerWide = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "manager", fixture.Upload(fixture.CustomerA.Id));

        var list = await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "assigned-user", DocumentAccessLevel.User);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(visible.Id, Assert.Single(list.Items).Id);
        var download = await fixture.Service.DownloadAsync(fixture.OrganizationA.Id, visible.Id, "assigned-user", DocumentAccessLevel.User);
        await download.Content.DisposeAsync();
        foreach (var hidden in new[] { unassigned, commercialCategory, commercialContext, customerWide })
            await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationA.Id, hidden.Id, "assigned-user", DocumentAccessLevel.User));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "unassigned-user", DocumentAccessLevel.User));

        var managerList = await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "manager", DocumentAccessLevel.Manager);
        Assert.Equal(3, managerList.TotalCount);
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationA.Id, commercialCategory.Id, "manager", DocumentAccessLevel.Manager));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationA.Id, commercialContext.Id, "manager", DocumentAccessLevel.Manager));

        fixture.CustomerA.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "assigned-user", DocumentAccessLevel.User));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.DownloadAsync(fixture.OrganizationA.Id, visible.Id, "assigned-user", DocumentAccessLevel.User));
        Assert.Equal(5, (await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "admin", DocumentAccessLevel.Admin)).TotalCount);
    }

    [Fact]
    public async Task ContextProjectionIsCustomerScopedAndRoleFiltered()
    {
        using var fixture = new Fixture();
        var ownOrder = fixture.AddCompletedOrder(fixture.CustomerA);
        var otherOrder = fixture.AddCompletedOrder(fixture.CustomerA2);
        var foreignOrder = fixture.AddCompletedOrder(fixture.CustomerB);
        var document = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.WorkOrder, ownOrder.Id));

        var listed = Assert.Single((await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "actor", DocumentAccessLevel.Manager)).Items);
        Assert.Equal(document.Id, listed.Id);
        Assert.Equal("OS " + ownOrder.Number, listed.ContextLabel);
        Assert.Equal("Usuário indisponível", listed.UploadedByName);

        var manager = await fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, DocumentAccessLevel.Manager);
        Assert.Contains(manager, option => option.Type == DocumentContextType.Customer && option.Id == fixture.CustomerA.Id);
        Assert.Contains(manager, option => option.Type == DocumentContextType.WorkOrder && option.Id == ownOrder.Id);
        Assert.DoesNotContain(manager, option => option.Type is DocumentContextType.Quote or DocumentContextType.Contract);
        Assert.DoesNotContain(manager, option => option.Id == otherOrder.Id || option.Id == foreignOrder.Id);

        var admin = await fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, DocumentAccessLevel.Admin);
        Assert.Contains(admin, option => option.Type == DocumentContextType.Quote && option.Id == ownOrder.QuoteId);
        Assert.Contains(admin, option => option.Type == DocumentContextType.Contract && option.Id == ownOrder.ContractId);
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerB.Id, DocumentAccessLevel.Admin));
        await Assert.ThrowsAsync<DocumentNotFoundException>(() => fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, DocumentAccessLevel.User));
    }

    [Fact]
    public async Task ContractsFromTheSameQuoteHaveDistinctLabelsInChoicesAndDocuments()
    {
        using var fixture = new Fixture();
        var order = fixture.AddCompletedOrder(fixture.CustomerA);
        var original = await fixture.Db.Contracts.SingleAsync(contract => contract.Id == order.ContractId);
        original.Status = ContractStatus.Ended;
        original.CreatedAtUtc = new DateTimeOffset(2025, 1, 10, 10, 0, 0, TimeSpan.Zero);
        var replacement = new Contract
        {
            Id = Guid.NewGuid(), OrganizationId = fixture.OrganizationA.Id, CustomerId = fixture.CustomerA.Id,
            QuoteId = order.QuoteId, CustomerLegalNameSnapshot = fixture.CustomerA.LegalName,
            Status = ContractStatus.Draft, CreatedAtUtc = original.CreatedAtUtc.AddDays(1),
            CreatedByUserId = "actor", UpdatedByUserId = "actor"
        };
        fixture.Db.Contracts.Add(replacement);
        await fixture.Db.SaveChangesAsync();
        var originalDocument = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.Contract, original.Id));
        var replacementDocument = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
            fixture.Upload(fixture.CustomerA.Id, DocumentContextType.Contract, replacement.Id));

        var choices = (await fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, DocumentAccessLevel.Admin))
            .Where(option => option.Type == DocumentContextType.Contract).ToDictionary(option => option.Id, option => option.Label);
        Assert.Equal($"Contrato 1 do orçamento {(await fixture.Db.Quotes.SingleAsync(quote => quote.Id == order.QuoteId)).Number}", choices[original.Id]);
        Assert.Equal(choices[original.Id].Replace("Contrato 1", "Contrato 2"), choices[replacement.Id]);
        var documents = (await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "actor", DocumentAccessLevel.Admin)).Items
            .ToDictionary(document => document.Id, document => document.ContextLabel);
        Assert.Equal(choices[original.Id], documents[originalDocument.Id]);
        Assert.Equal(choices[replacement.Id], documents[replacementDocument.Id]);
        Assert.NotEqual(documents[originalDocument.Id], documents[replacementDocument.Id]);
    }

    [Fact]
    public async Task UnitsWithTheSameNameHaveDistinctLabelsInChoicesAndDocuments()
    {
        using var fixture = new Fixture();
        var first = new CustomerUnit { Id = Guid.NewGuid(), CustomerId = fixture.CustomerA.Id, Name = "Depot",
            Street = "Rua A", Number = "10", City = "Campinas", StateCode = "SP",
            CreatedAtUtc = new DateTimeOffset(2025, 1, 10, 10, 0, 0, TimeSpan.Zero) };
        var second = new CustomerUnit { Id = Guid.NewGuid(), CustomerId = fixture.CustomerA.Id, Name = "Depot",
            Street = "Rua B", Number = "20", City = "Santos", StateCode = "SP",
            CreatedAtUtc = first.CreatedAtUtc.AddDays(1) };
        var sameAddress = new CustomerUnit { Id = Guid.NewGuid(), CustomerId = fixture.CustomerA.Id, Name = "Depot",
            Street = "Rua A", Number = "10", City = "Campinas", StateCode = "SP",
            CreatedAtUtc = first.CreatedAtUtc.AddDays(2) };
        fixture.Db.CustomerUnits.AddRange(first, second, sameAddress);
        await fixture.Db.SaveChangesAsync();
        var documents = new Dictionary<Guid, Guid>();
        foreach (var unit in new[] { first, second, sameAddress })
        {
            var document = await fixture.Service.UploadAsync(fixture.OrganizationA.Id, "actor",
                fixture.Upload(fixture.CustomerA.Id, DocumentContextType.CustomerUnit, unit.Id));
            documents.Add(document.Id, unit.Id);
        }

        var choices = (await fixture.Service.ContextOptionsAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, DocumentAccessLevel.Manager))
            .Where(option => option.Type == DocumentContextType.CustomerUnit).ToDictionary(option => option.Id, option => option.Label);
        Assert.Equal(3, choices.Count);
        Assert.Equal(3, choices.Values.Distinct().Count());
        Assert.Contains("Rua A, 10", choices[first.Id]);
        Assert.Contains("Campinas/SP", choices[first.Id]);
        Assert.Contains("Rua B, 20", choices[second.Id]);
        Assert.EndsWith("(unidade 1)", choices[first.Id]);
        Assert.EndsWith("(unidade 2)", choices[sameAddress.Id]);
        var listed = (await fixture.Service.ListAsync(fixture.OrganizationA.Id, fixture.CustomerA.Id, "actor", DocumentAccessLevel.Manager)).Items;
        foreach (var document in listed)
            Assert.Equal(choices[documents[document.Id]], document.ContextLabel);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; }
        public TestDocumentStorage Storage { get; } = new();
        public DocumentService Service { get; }
        public Organization OrganizationA { get; } = new() { Name = "Tenant A", Slug = "tenant-a" };
        public Organization OrganizationB { get; } = new() { Name = "Tenant B", Slug = "tenant-b" };
        public Customer CustomerA { get; }
        public Customer CustomerA2 { get; }
        public Customer CustomerB { get; }

        public Fixture(long maxBytes = 10 * 1024 * 1024)
        {
            connection.Open();
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            CustomerA = Customer(OrganizationA.Id, "Customer A", "04252011000110");
            CustomerA2 = Customer(OrganizationA.Id, "Customer A2", "04252011000111");
            CustomerB = Customer(OrganizationB.Id, "Customer B", "04252011000112");
            Db.Organizations.AddRange(OrganizationA, OrganizationB);
            Db.Customers.AddRange(CustomerA, CustomerA2, CustomerB);
            Db.SaveChanges();
            Service = new DocumentService(Db, Storage, Options.Create(new DocumentStorageOptions { RootPath = ".local/test-documents", MaxFileSizeBytes = maxBytes }));
        }

        public DocumentUpload Upload(Guid customerId, DocumentContextType? type = null, Guid? contextId = null,
            string fileName = "report.pdf", string contentType = "application/pdf", byte[]? bytes = null)
        {
            var content = bytes ?? Pdf;
            return new DocumentUpload(customerId, DocumentCategory.Report, DocumentPurpose.InternalSupporting, "Relatório", type, contextId,
                fileName, contentType, content.Length, new MemoryStream(content));
        }

        public WorkOrder AddCompletedOrder(Customer customer)
        {
            var quote = new Quote { Id = Guid.NewGuid(), OrganizationId = customer.OrganizationId, CustomerId = customer.Id,
                Number = $"Q-{Guid.NewGuid():N}"[..16], CustomerLegalNameSnapshot = customer.LegalName,
                CustomerCnpjSnapshot = customer.Cnpj, CreatedByUserId = "actor", UpdatedByUserId = "actor" };
            var contract = new Contract { Id = Guid.NewGuid(), OrganizationId = customer.OrganizationId, CustomerId = customer.Id,
                QuoteId = quote.Id, CustomerLegalNameSnapshot = customer.LegalName, CreatedByUserId = "actor", UpdatedByUserId = "actor" };
            var order = new WorkOrder { Id = Guid.NewGuid(), OrganizationId = customer.OrganizationId, CustomerId = customer.Id,
                QuoteId = quote.Id, ContractId = contract.Id, SourceType = WorkOrderSourceType.Contract, Status = WorkOrderStatus.Completed,
                Number = $"OS-{Guid.NewGuid():N}"[..16], CustomerLegalNameSnapshot = customer.LegalName, ServiceAddressSnapshot = "Address",
                CreatedByUserId = "actor", UpdatedByUserId = "actor" };
            Db.Quotes.Add(quote); Db.Contracts.Add(contract); Db.WorkOrders.Add(order); Db.SaveChanges();
            return order;
        }

        private static Customer Customer(Guid organizationId, string name, string cnpj) => new()
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, LegalName = name, Cnpj = cnpj,
            CreatedByUserId = "actor", UpdatedByUserId = "actor", Version = Guid.NewGuid()
        };

        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }

}
