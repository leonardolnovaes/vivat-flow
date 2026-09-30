using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;

namespace Tsdt.Tests.Identity;

internal static class ServiceLineTestData
{
    internal static readonly Guid SstId = new("11111111-1111-1111-1111-111111111111");

    internal static async Task EnableSstForBootstrapOrganizationAsync(IdentityWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = await db.Users.Where(user => user.Email == "admin@example.test").Select(user => user.OrganizationId).SingleAsync()
            ?? throw new InvalidOperationException("The bootstrap administrator must belong to an organization.");
        if (!await db.ServiceLines.AnyAsync(line => line.Id == SstId)) db.ServiceLines.Add(new ServiceLine { Id = SstId, Code = "SST", Name = "Segurança e Saúde no Trabalho" });
        if (!await db.OrganizationServiceLines.AnyAsync(item => item.OrganizationId == organizationId && item.ServiceLineId == SstId)) db.OrganizationServiceLines.Add(new OrganizationServiceLine { OrganizationId = organizationId, ServiceLineId = SstId });
        await db.SaveChangesAsync();
    }
}
