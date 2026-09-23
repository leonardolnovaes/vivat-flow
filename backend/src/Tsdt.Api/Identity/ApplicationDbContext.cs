using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Audit;

namespace Tsdt.Api.Identity;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<UserAdministrationAuditRecord> UserAdministrationAuditRecords => Set<UserAdministrationAuditRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().Property(user => user.FullName).IsRequired();
        builder.Entity<UserAdministrationAuditRecord>().HasIndex(record => record.TargetUserId);
    }
}
