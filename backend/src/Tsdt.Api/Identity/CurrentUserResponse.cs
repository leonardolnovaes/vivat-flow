namespace Tsdt.Api.Identity;

public sealed record CurrentOrganizationResponse(Guid Id, string Name);

public sealed record CurrentUserResponse(string Id, string FullName, string Email, IReadOnlyList<string> Roles, bool MustChangePassword, bool IsPlatformAdministrator, string? PreferredLocale, CurrentOrganizationResponse? Organization, IReadOnlyList<string> Features);

public sealed record CsrfTokenResponse(string Token);

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UpdatePreferredLocaleRequest(string PreferredLocale);
