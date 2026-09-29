namespace Tsdt.Api.Platform;
public sealed class OrganizationBootstrapOptions { public const string SectionName = "OrganizationBootstrap"; public string Name { get; init; } = "Organização inicial"; public string Slug { get; init; } = "organizacao-inicial"; }
public sealed class PlatformBootstrapAdminOptions { public const string SectionName = "PlatformBootstrapAdmin"; public string? Email { get; init; } public string? FullName { get; init; } public string? Password { get; init; } }
