namespace Tsdt.Api.Identity;

public sealed record UserAdministrationResponse(string Id, string FullName, string Email, IReadOnlyList<string> Roles, bool IsActive, bool MustChangePassword);
public sealed record CreateUserRequest(string FullName, string Email, string Role);
public sealed record ChangeUserRoleRequest(string Role);
public sealed record CreateUserResponse(UserAdministrationResponse User, string TemporaryPassword);
