namespace Tsdt.Api.Identity;

public sealed record CurrentUserResponse(string Id, string FullName, string Email, IReadOnlyList<string> Roles, bool MustChangePassword);

public sealed record CsrfTokenResponse(string Token);

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
