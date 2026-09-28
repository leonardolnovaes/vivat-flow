using Microsoft.EntityFrameworkCore;

namespace Tsdt.Api.Identity;

/// <summary>Single business boundary for selecting people who may own commercial work.</summary>
public static class EligibleProfessionalQuery
{
    public static IQueryable<ApplicationUser> Apply(ApplicationDbContext db) =>
        from user in db.Users.AsNoTracking()
        join membership in db.UserRoles on user.Id equals membership.UserId
        join role in db.Roles on membership.RoleId equals role.Id
        where user.IsActive && (role.Name == IdentityRoles.Admin || role.Name == IdentityRoles.Manager)
        select user;
    public static async Task<bool> ContainsAsync(ApplicationDbContext db, string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && await Apply(db).AnyAsync(user => user.Id == userId);
}
public sealed record EligibleProfessionalResponse(string Id, string FullName, string Email, string Role);
