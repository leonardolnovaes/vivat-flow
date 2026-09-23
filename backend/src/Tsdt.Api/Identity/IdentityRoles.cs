namespace Tsdt.Api.Identity;

public static class IdentityRoles
{
    public const string Admin = "ADMIN";
    public const string Manager = "MANAGER";
    public const string User = "USER";

    public static readonly string[] All = [Admin, Manager, User];
}
