using System.Text.RegularExpressions;
namespace Tsdt.Api.Platform;
public static partial class OrganizationRules { [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")] private static partial Regex Pattern(); public static string NormalizeSlug(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant(); public static bool IsValidSlug(string value) => value.Length is > 0 and <= 80 && Pattern().IsMatch(value); public static bool CanProvisionTenantAdministrator(OrganizationStatus status) => status == OrganizationStatus.Active; }
