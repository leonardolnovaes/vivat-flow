using Microsoft.AspNetCore.Identity;
using Tsdt.Api.Platform;

namespace Tsdt.Api.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public required string FullName { get; set; }

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; }
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public bool IsPlatformAdministrator { get; set; }
    public string? PreferredLocale { get; set; }

}
