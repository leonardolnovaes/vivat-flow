using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Audit;
using Tsdt.Api.Customers;
using Tsdt.Api.Services;
using Tsdt.Api.Quotes;
using Tsdt.Api.Platform;
using Tsdt.Api.Contracts;
using Tsdt.Api.WorkOrders;

namespace Tsdt.Api.Identity;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public Guid? TenantOrganizationId { get; set; }
    public DbSet<UserAdministrationAuditRecord> UserAdministrationAuditRecords => Set<UserAdministrationAuditRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<CustomerUnit> CustomerUnits => Set<CustomerUnit>();
    public DbSet<CustomerAuditRecord> CustomerAuditRecords => Set<CustomerAuditRecord>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceAuditRecord> ServiceAuditRecords => Set<ServiceAuditRecord>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<QuoteAuditRecord> QuoteAuditRecords => Set<QuoteAuditRecord>();
    public DbSet<QuoteVisit> QuoteVisits => Set<QuoteVisit>();
    public DbSet<QuoteNumberCounter> QuoteNumberCounters => Set<QuoteNumberCounter>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractItem> ContractItems => Set<ContractItem>();
    public DbSet<ContractAuditRecord> ContractAuditRecords => Set<ContractAuditRecord>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderItem> WorkOrderItems => Set<WorkOrderItem>();
    public DbSet<WorkOrderAuditRecord> WorkOrderAuditRecords => Set<WorkOrderAuditRecord>();
    public DbSet<WorkOrderNumberCounter> WorkOrderNumberCounters => Set<WorkOrderNumberCounter>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationAuditRecord> OrganizationAuditRecords => Set<OrganizationAuditRecord>();
    public DbSet<ServiceLine> ServiceLines => Set<ServiceLine>();
    public DbSet<OrganizationServiceLine> OrganizationServiceLines => Set<OrganizationServiceLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FullName).IsRequired().HasMaxLength(120);
            entity.Property(user => user.Email).HasMaxLength(254);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254);
            entity.Property(user => user.PreferredLocale).HasMaxLength(5);
            entity.HasIndex(user => user.OrganizationId);
            entity.HasOne(user => user.Organization).WithMany().HasForeignKey(user => user.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(user => TenantOrganizationId == null || (!user.IsPlatformAdministrator && user.OrganizationId == TenantOrganizationId));
        });
        builder.Entity<Organization>(entity => { entity.ToTable("Organizations"); entity.HasKey(item => item.Id); entity.Property(item => item.Name).IsRequired().HasMaxLength(200); entity.Property(item => item.Slug).IsRequired().HasMaxLength(80); entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16); entity.HasIndex(item => item.Slug).IsUnique(); });
        builder.Entity<OrganizationAuditRecord>(entity => { entity.ToTable("OrganizationAuditRecords"); entity.HasKey(item => item.Id); entity.Property(item => item.ActorUserId).IsRequired(); entity.Property(item => item.Action).IsRequired().HasMaxLength(80); entity.HasIndex(item => new { item.OrganizationId, item.OccurredAtUtc }); entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ServiceLine>(entity => { entity.ToTable("ServiceLines"); entity.HasKey(item => item.Id); entity.Property(item => item.Code).IsRequired().HasMaxLength(50); entity.Property(item => item.Name).IsRequired().HasMaxLength(160); entity.HasIndex(item => item.Code).IsUnique(); });
        builder.Entity<OrganizationServiceLine>(entity => { entity.ToTable("OrganizationServiceLines"); entity.HasKey(item => new { item.OrganizationId, item.ServiceLineId }); entity.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(item => item.ServiceLine).WithMany().HasForeignKey(item => item.ServiceLineId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<UserAdministrationAuditRecord>().HasIndex(record => record.TargetUserId);
        builder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.LegalName).IsRequired().HasMaxLength(200);
            entity.Property(customer => customer.TradeName).HasMaxLength(200);
            entity.Property(customer => customer.Cnpj).IsRequired().HasMaxLength(14);
            entity.Property(customer => customer.Notes).HasMaxLength(2000);
            entity.Property(customer => customer.CreatedByUserId).IsRequired();
            entity.Property(customer => customer.UpdatedByUserId).IsRequired();
            entity.Property(customer => customer.OrganizationId).IsRequired();
            entity.Property(customer => customer.Version).IsConcurrencyToken();
            entity.HasIndex(customer => new { customer.OrganizationId, customer.Cnpj }).IsUnique();
            entity.HasIndex(customer => new { customer.OrganizationId, customer.IsActive, customer.LegalName });
            entity.HasOne<Organization>().WithMany().HasForeignKey(customer => customer.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(customer => TenantOrganizationId == null || customer.OrganizationId == TenantOrganizationId);
        });
        builder.Entity<CustomerContact>(entity =>
        {
            entity.ToTable("CustomerContacts");
            entity.HasKey(contact => contact.Id);
            entity.Property(contact => contact.Name).IsRequired().HasMaxLength(120);
            entity.Property(contact => contact.RoleOrDepartment).HasMaxLength(120);
            entity.Property(contact => contact.Email).HasMaxLength(254);
            entity.Property(contact => contact.Phone).HasMaxLength(11);
            entity.HasIndex(contact => contact.CustomerId);
            entity.HasIndex(contact => new { contact.CustomerId, contact.IsPrimary })
                .IsUnique().HasFilter("\"IsActive\" AND \"IsPrimary\"");
            entity.HasOne(contact => contact.Customer).WithMany(customer => customer.Contacts)
                .HasForeignKey(contact => contact.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CustomerUnit>(entity =>
        {
            entity.ToTable("CustomerUnits");
            entity.HasKey(unit => unit.Id);
            entity.Property(unit => unit.Name).IsRequired().HasMaxLength(160);
            entity.Property(unit => unit.Street).IsRequired().HasMaxLength(160);
            entity.Property(unit => unit.Number).IsRequired().HasMaxLength(30);
            entity.Property(unit => unit.Complement).HasMaxLength(120);
            entity.Property(unit => unit.District).HasMaxLength(120);
            entity.Property(unit => unit.City).IsRequired().HasMaxLength(120);
            entity.Property(unit => unit.StateCode).IsRequired().HasMaxLength(2);
            entity.Property(unit => unit.PostalCode).HasMaxLength(8);
            entity.HasIndex(unit => unit.CustomerId);
            entity.HasIndex(unit => new { unit.CustomerId, unit.IsPrimary })
                .IsUnique().HasFilter("\"IsActive\" AND \"IsPrimary\"");
            entity.HasOne(unit => unit.Customer).WithMany(customer => customer.Units)
                .HasForeignKey(unit => unit.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CustomerAuditRecord>(entity =>
        {
            entity.ToTable("CustomerAuditRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ActorUserId).IsRequired();
            entity.Property(record => record.Action).IsRequired().HasMaxLength(80);
            entity.Property(record => record.ChangedFields).HasMaxLength(500);
            entity.HasIndex(record => new { record.CustomerId, record.OccurredAtUtc });
            entity.HasOne(record => record.Customer).WithMany().HasForeignKey(record => record.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Service>(entity =>
        {
            entity.ToTable("Services", table => table.HasCheckConstraint("CK_Services_BasePrice_NonNegative", "\"BasePrice\" IS NULL OR \"BasePrice\" >= 0"));
            entity.HasKey(service => service.Id);
            entity.Property(service => service.Code).IsRequired().HasMaxLength(50);
            entity.Property(service => service.Name).IsRequired().HasMaxLength(160);
            entity.Property(service => service.Description).HasMaxLength(2000);
            entity.Property(service => service.BasePrice).HasPrecision(18, 2);
            entity.Property(service => service.CreatedByUserId).IsRequired();
            entity.Property(service => service.UpdatedByUserId).IsRequired();
            entity.Property(service => service.OrganizationId).IsRequired();
            entity.Property(service => service.ServiceLineId).IsRequired();
            entity.Property(service => service.Version).IsConcurrencyToken();
            entity.HasIndex(service => new { service.OrganizationId, service.Code }).IsUnique();
            entity.HasIndex(service => new { service.OrganizationId, service.IsActive, service.Name });
            entity.HasOne<Organization>().WithMany().HasForeignKey(service => service.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(service => service.ServiceLine).WithMany().HasForeignKey(service => service.ServiceLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(service => TenantOrganizationId == null || service.OrganizationId == TenantOrganizationId);
        });
        builder.Entity<ServiceAuditRecord>(entity =>
        {
            entity.ToTable("ServiceAuditRecords");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.ActorUserId).IsRequired();
            entity.Property(record => record.Action).IsRequired().HasMaxLength(80);
            entity.Property(record => record.ChangedFields).HasMaxLength(500);
            entity.HasIndex(record => new { record.ServiceId, record.OccurredAtUtc });
            entity.HasOne(record => record.Service).WithMany().HasForeignKey(record => record.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Quote>(entity =>
        {
            entity.ToTable("Quotes", table => table.HasCheckConstraint("CK_Quotes_TotalAmount_NonNegative", "\"TotalAmount\" IS NULL OR \"TotalAmount\" >= 0"));
            entity.HasKey(quote => quote.Id); entity.Property(quote => quote.Number).IsRequired().HasMaxLength(16);
            entity.Property(quote => quote.CustomerLegalNameSnapshot).IsRequired().HasMaxLength(200); entity.Property(quote => quote.CustomerCnpjSnapshot).IsRequired().HasMaxLength(14);
            entity.Property(quote => quote.TotalAmount).HasPrecision(18, 2); entity.Property(quote => quote.Notes).HasMaxLength(2000); entity.Property(quote => quote.ServiceAddressSnapshot).HasMaxLength(700); entity.Property(quote => quote.ApprovalRecipientName).HasMaxLength(120); entity.Property(quote => quote.ApprovalRecipientEmail).HasMaxLength(254); entity.Property(quote => quote.ClientResponseNotes).HasMaxLength(2000); entity.Property(quote => quote.Version).IsConcurrencyToken();
            entity.Property(quote => quote.OrganizationId).IsRequired(); entity.HasIndex(quote => new { quote.OrganizationId, quote.Number }).IsUnique(); entity.HasIndex(quote => new { quote.OrganizationId, quote.Status, quote.UpdatedAtUtc }); entity.HasIndex(quote => new { quote.OrganizationId, quote.CustomerId, quote.CreatedAtUtc }); entity.HasIndex(quote => quote.SentForApprovalAt); entity.HasIndex(quote => quote.ValidUntil);
            entity.HasOne<Organization>().WithMany().HasForeignKey(quote => quote.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(quote => TenantOrganizationId == null || quote.OrganizationId == TenantOrganizationId);
            entity.HasIndex(quote => quote.ResponsibleUserId); entity.HasOne(quote => quote.ResponsibleUser).WithMany().HasForeignKey(quote => quote.ResponsibleUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(quote => quote.Customer).WithMany().HasForeignKey(quote => quote.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QuoteItem>(entity => { entity.ToTable("QuoteItems"); entity.HasKey(item => item.Id); entity.Property(item => item.ServiceCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceNameSnapshot).IsRequired().HasMaxLength(160); entity.Property(item => item.ServiceLineCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceLineNameSnapshot).IsRequired().HasMaxLength(160); entity.HasIndex(item => new { item.QuoteId, item.DisplayOrder }).IsUnique(); entity.HasOne(item => item.Quote).WithMany(quote => quote.Items).HasForeignKey(item => item.QuoteId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(item => item.Service).WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteAuditRecord>(entity => { entity.ToTable("QuoteAuditRecords"); entity.HasKey(record => record.Id); entity.Property(record => record.ActorUserId).IsRequired(); entity.Property(record => record.Action).IsRequired().HasMaxLength(80); entity.Property(record => record.ChangedFields).HasMaxLength(500); entity.HasIndex(record => new { record.QuoteId, record.OccurredAtUtc }); entity.HasOne(record => record.Quote).WithMany().HasForeignKey(record => record.QuoteId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteVisit>(entity => { entity.ToTable("QuoteVisits"); entity.HasKey(visit => visit.Id); entity.Property(visit => visit.AssignedUserId).IsRequired(); entity.Property(visit => visit.LocationSnapshot).HasMaxLength(700); entity.Property(visit => visit.Notes).HasMaxLength(2000); entity.Property(visit => visit.CreatedByUserId).IsRequired(); entity.Property(visit => visit.UpdatedByUserId).IsRequired(); entity.HasIndex(visit => visit.QuoteId); entity.HasIndex(visit => visit.AssignedUserId); entity.HasIndex(visit => visit.ScheduledStart); entity.HasOne(visit => visit.Quote).WithMany(quote => quote.Visits).HasForeignKey(visit => visit.QuoteId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(visit => visit.AssignedUser).WithMany().HasForeignKey(visit => visit.AssignedUserId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteNumberCounter>(entity => { entity.ToTable("QuoteNumberCounters", table => table.HasCheckConstraint("CK_QuoteNumberCounters_LastNumber_Range", "\"LastNumber\" >= 1 AND \"LastNumber\" <= 999999")); entity.HasKey(counter => counter.Year); });
        builder.Entity<Contract>(entity =>
        {
            entity.ToTable("Contracts", table => table.HasCheckConstraint("CK_Contracts_ApprovedTotalAmount_NonNegative", "\"ApprovedTotalAmount\" >= 0"));
            entity.HasKey(contract => contract.Id);
            entity.Property(contract => contract.CustomerLegalNameSnapshot).IsRequired().HasMaxLength(200);
            entity.Property(contract => contract.ApprovedTotalAmount).HasPrecision(18, 2);
            entity.Property(contract => contract.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(contract => contract.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(contract => contract.PaymentType).HasConversion<string>().HasMaxLength(16);
            entity.Property(contract => contract.PaymentTerms).HasMaxLength(2000);
            entity.Property(contract => contract.Notes).HasMaxLength(2000);
            entity.Property(contract => contract.CreatedByUserId).IsRequired();
            entity.Property(contract => contract.UpdatedByUserId).IsRequired();
            entity.Property(contract => contract.Version).IsConcurrencyToken();
            entity.HasIndex(contract => new { contract.OrganizationId, contract.Status, contract.UpdatedAtUtc });
            entity.HasIndex(contract => new { contract.OrganizationId, contract.QuoteId }).IsUnique().HasFilter("\"Status\" IN ('Draft', 'Active')");
            entity.HasOne<Organization>().WithMany().HasForeignKey(contract => contract.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Quote>().WithMany().HasForeignKey(contract => contract.QuoteId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(contract => contract.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(contract => TenantOrganizationId == null || contract.OrganizationId == TenantOrganizationId);
        });
        builder.Entity<ContractItem>(entity =>
        {
            entity.ToTable("ContractItems"); entity.HasKey(item => item.Id);
            entity.Property(item => item.ServiceCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceNameSnapshot).IsRequired().HasMaxLength(160);
            entity.Property(item => item.ServiceLineCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceLineNameSnapshot).IsRequired().HasMaxLength(160);
            entity.HasIndex(item => new { item.ContractId, item.DisplayOrder }).IsUnique();
            entity.HasOne(item => item.Contract).WithMany(contract => contract.Items).HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<QuoteItem>().WithMany().HasForeignKey(item => item.QuoteItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Service>().WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ContractAuditRecord>(entity => { entity.ToTable("ContractAuditRecords"); entity.HasKey(record => record.Id); entity.Property(record => record.ActorUserId).IsRequired(); entity.Property(record => record.Action).IsRequired().HasMaxLength(80); entity.Property(record => record.ChangedFields).HasMaxLength(500); entity.HasIndex(record => new { record.ContractId, record.OccurredAtUtc }); entity.HasOne(record => record.Contract).WithMany().HasForeignKey(record => record.ContractId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable("WorkOrders", table => table.HasCheckConstraint("CK_WorkOrders_Source", "(\"SourceType\" = 'Quote' AND \"ContractId\" IS NULL) OR (\"SourceType\" = 'Contract' AND \"ContractId\" IS NOT NULL)"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Number).IsRequired().HasMaxLength(16);
            entity.Property(item => item.SourceType).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.CustomerLegalNameSnapshot).IsRequired().HasMaxLength(200);
            entity.Property(item => item.ServiceAddressSnapshot).IsRequired().HasMaxLength(700);
            entity.Property(item => item.AssignedUserNameSnapshot).HasMaxLength(120);
            entity.Property(item => item.AssignedUserEmailSnapshot).HasMaxLength(254);
            entity.Property(item => item.OperationalNotes).HasMaxLength(2000);
            entity.Property(item => item.CompletionNotes).HasMaxLength(2000);
            entity.Property(item => item.CreatedByUserId).IsRequired(); entity.Property(item => item.UpdatedByUserId).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.OrganizationId, item.Number }).IsUnique();
            entity.HasIndex(item => new { item.OrganizationId, item.QuoteId }).IsUnique().HasFilter("\"Status\" <> 'Cancelled'");
            entity.HasIndex(item => new { item.OrganizationId, item.ContractId }).HasFilter("\"ContractId\" IS NOT NULL");
            entity.HasIndex(item => new { item.OrganizationId, item.Status, item.UpdatedAtUtc });
            entity.HasIndex(item => new { item.OrganizationId, item.AssignedUserId, item.UpdatedAtUtc });
            entity.HasIndex(item => new { item.OrganizationId, item.UpdatedAtUtc, item.Id });
            entity.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Quote>().WithMany().HasForeignKey(item => item.QuoteId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Contract>().WithMany().HasForeignKey(item => item.ContractId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(item => item.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(item => TenantOrganizationId == null || item.OrganizationId == TenantOrganizationId);
        });
        builder.Entity<WorkOrderItem>(entity =>
        {
            entity.ToTable("WorkOrderItems"); entity.HasKey(item => item.Id);
            entity.Property(item => item.ServiceCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceNameSnapshot).IsRequired().HasMaxLength(160);
            entity.Property(item => item.ServiceLineCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceLineNameSnapshot).IsRequired().HasMaxLength(160);
            entity.HasIndex(item => new { item.WorkOrderId, item.DisplayOrder }).IsUnique();
            entity.HasOne(item => item.WorkOrder).WithMany(order => order.Items).HasForeignKey(item => item.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<QuoteItem>().WithMany().HasForeignKey(item => item.QuoteItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ContractItem>().WithMany().HasForeignKey(item => item.ContractItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Service>().WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<WorkOrderAuditRecord>(entity => { entity.ToTable("WorkOrderAuditRecords"); entity.HasKey(item => item.Id); entity.Property(item => item.ActorUserId).IsRequired(); entity.Property(item => item.Action).IsRequired().HasMaxLength(80); entity.Property(item => item.ChangedFields).HasMaxLength(500); entity.HasIndex(item => new { item.WorkOrderId, item.OccurredAtUtc }); entity.HasOne(item => item.WorkOrder).WithMany().HasForeignKey(item => item.WorkOrderId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<WorkOrderNumberCounter>(entity => { entity.ToTable("WorkOrderNumberCounters", table => table.HasCheckConstraint("CK_WorkOrderNumberCounters_Range", "\"LastNumber\" >= 1 AND \"LastNumber\" <= 999999")); entity.HasKey(item => new { item.OrganizationId, item.Year }); entity.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict); });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenantOwnership();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTenantOwnership()
    {
        if (TenantOrganizationId is not Guid organizationId) return;
        ApplyOwnership(ChangeTracker.Entries<Customer>(), organizationId, customer => customer.OrganizationId, (customer, value) => customer.OrganizationId = value);
        ApplyOwnership(ChangeTracker.Entries<Service>(), organizationId, service => service.OrganizationId, (service, value) => service.OrganizationId = value);
        ApplyOwnership(ChangeTracker.Entries<Quote>(), organizationId, quote => quote.OrganizationId, (quote, value) => quote.OrganizationId = value);
        ApplyOwnership(ChangeTracker.Entries<Contract>(), organizationId, contract => contract.OrganizationId, (contract, value) => contract.OrganizationId = value);
        ApplyOwnership(ChangeTracker.Entries<WorkOrder>(), organizationId, order => order.OrganizationId, (order, value) => order.OrganizationId = value);
    }

    private static void ApplyOwnership<TEntity>(IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity>> entries, Guid organizationId, Func<TEntity, Guid> getOrganizationId, Action<TEntity, Guid> setOrganizationId) where TEntity : class
    {
        foreach (var entry in entries.Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added && getOrganizationId(entry.Entity) == Guid.Empty) setOrganizationId(entry.Entity, organizationId);
            if (!TenantOwnershipRules.IsOwnedBy(organizationId, getOrganizationId(entry.Entity))) throw new UnauthorizedAccessException("Cross-tenant persistence is not allowed.");
        }
    }
}
