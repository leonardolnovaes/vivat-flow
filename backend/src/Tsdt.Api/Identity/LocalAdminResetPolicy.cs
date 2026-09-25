namespace Tsdt.Api.Identity;

public static class LocalAdminResetPolicy
{
    public static bool AllowsEnvironment(string? environment, string? configuredEmail) =>
        string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(configuredEmail);

    public static bool AllowsTarget(bool isActive, bool isAdmin) => isActive && isAdmin;
}
