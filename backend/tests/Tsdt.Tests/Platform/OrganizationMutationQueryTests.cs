using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Identity;
using Tsdt.Api.Platform;

namespace Tsdt.Tests.Platform;

[Trait("Category", "Unit")]
public sealed class OrganizationMutationQueryTests
{
    [Fact]
    public void Organization_mutation_query_locks_the_target_row_on_postgresql()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=vivat_test")
            .Options;
        using var db = new ApplicationDbContext(options);

        var sql = OrganizationEndpoints.OrganizationForUpdateQuery(db, Guid.NewGuid()).ToQueryString();

        Assert.Contains("WHERE \"Id\" =", sql);
        Assert.Contains("FOR UPDATE", sql);
    }
}
