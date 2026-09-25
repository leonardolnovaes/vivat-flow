namespace Tsdt.Api.Identity;

public sealed record UserAdministrationResponse(string Id, string FullName, string Email, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword);
public sealed record CreateUserRequest(string FullName, string Email, string Role);
public sealed record UpdateUserRequest(string FullName, string Email);
public sealed record ChangeUserRoleRequest(string Role);
public sealed record CreateUserResponse(UserAdministrationResponse User, string TemporaryPassword);
public sealed record PasswordPolicyResponse(int MinimumLength, bool RequiresDigit, bool RequiresLowercase, bool RequiresUppercase, bool RequiresNonAlphanumeric, string Description);
