using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Tsdt.Api.Identity;
namespace Tsdt.Api.Platform;
public sealed class PlatformClaimsPrincipalFactory(UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(users, roles, options)
{ public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user) { var principal = await base.CreateAsync(user); if (user.IsPlatformAdministrator) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("platform_administrator", "true")); return principal; } }
