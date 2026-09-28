using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Audit;
using Tsdt.Api.Customers;
using Tsdt.Api.Services;
using Tsdt.Api.Quotes;

namespace Tsdt.Api.Identity;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FullName).IsRequired().HasMaxLength(120);
            entity.Property(user => user.Email).HasMaxLength(254);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254);
        });
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
            entity.Property(customer => customer.Version).IsConcurrencyToken();
            entity.HasIndex(customer => customer.Cnpj).IsUnique();
            entity.HasIndex(customer => new { customer.IsActive, customer.LegalName });
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
            entity.Property(service => service.Version).IsConcurrencyToken();
            entity.HasIndex(service => service.Code).IsUnique();
            entity.HasIndex(service => new { service.IsActive, service.Name });
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
            entity.HasIndex(quote => quote.Number).IsUnique(); entity.HasIndex(quote => new { quote.Status, quote.UpdatedAtUtc }); entity.HasIndex(quote => new { quote.CustomerId, quote.CreatedAtUtc }); entity.HasIndex(quote => quote.SentForApprovalAt); entity.HasIndex(quote => quote.ValidUntil);
            entity.HasIndex(quote => quote.ResponsibleUserId); entity.HasOne(quote => quote.ResponsibleUser).WithMany().HasForeignKey(quote => quote.ResponsibleUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(quote => quote.Customer).WithMany().HasForeignKey(quote => quote.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QuoteItem>(entity => { entity.ToTable("QuoteItems"); entity.HasKey(item => item.Id); entity.Property(item => item.ServiceCodeSnapshot).IsRequired().HasMaxLength(50); entity.Property(item => item.ServiceNameSnapshot).IsRequired().HasMaxLength(160); entity.HasIndex(item => new { item.QuoteId, item.DisplayOrder }).IsUnique(); entity.HasOne(item => item.Quote).WithMany(quote => quote.Items).HasForeignKey(item => item.QuoteId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(item => item.Service).WithMany().HasForeignKey(item => item.ServiceId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteAuditRecord>(entity => { entity.ToTable("QuoteAuditRecords"); entity.HasKey(record => record.Id); entity.Property(record => record.ActorUserId).IsRequired(); entity.Property(record => record.Action).IsRequired().HasMaxLength(80); entity.Property(record => record.ChangedFields).HasMaxLength(500); entity.HasIndex(record => new { record.QuoteId, record.OccurredAtUtc }); entity.HasOne(record => record.Quote).WithMany().HasForeignKey(record => record.QuoteId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteVisit>(entity => { entity.ToTable("QuoteVisits"); entity.HasKey(visit => visit.Id); entity.Property(visit => visit.AssignedUserId).IsRequired(); entity.Property(visit => visit.LocationSnapshot).HasMaxLength(700); entity.Property(visit => visit.Notes).HasMaxLength(2000); entity.Property(visit => visit.CreatedByUserId).IsRequired(); entity.Property(visit => visit.UpdatedByUserId).IsRequired(); entity.HasIndex(visit => visit.QuoteId); entity.HasIndex(visit => visit.AssignedUserId); entity.HasIndex(visit => visit.ScheduledStart); entity.HasOne(visit => visit.Quote).WithMany(quote => quote.Visits).HasForeignKey(visit => visit.QuoteId).OnDelete(DeleteBehavior.Restrict); entity.HasOne(visit => visit.AssignedUser).WithMany().HasForeignKey(visit => visit.AssignedUserId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<QuoteNumberCounter>(entity => { entity.ToTable("QuoteNumberCounters", table => table.HasCheckConstraint("CK_QuoteNumberCounters_LastNumber_Range", "\"LastNumber\" >= 1 AND \"LastNumber\" <= 999999")); entity.HasKey(counter => counter.Year); });
    }
}
