using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Audit;
using Tsdt.Api.Customers;

namespace Tsdt.Api.Identity;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<UserAdministrationAuditRecord> UserAdministrationAuditRecords => Set<UserAdministrationAuditRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<CustomerUnit> CustomerUnits => Set<CustomerUnit>();
    public DbSet<CustomerAuditRecord> CustomerAuditRecords => Set<CustomerAuditRecord>();

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
    }
}
