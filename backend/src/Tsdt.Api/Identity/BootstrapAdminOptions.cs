namespace Tsdt.Api.Identity;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string? Email { get; init; }

    public string? FullName { get; init; }

    public string? Password { get; init; }
}
