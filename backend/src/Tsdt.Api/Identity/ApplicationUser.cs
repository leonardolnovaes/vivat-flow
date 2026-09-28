using Microsoft.AspNetCore.Identity;

namespace Tsdt.Api.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public required string FullName { get; set; }

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; }

}
