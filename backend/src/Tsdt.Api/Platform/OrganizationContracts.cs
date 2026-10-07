namespace Tsdt.Api.Platform;
public sealed record OrganizationRequest(string? Name, string? Slug);
public sealed record OrganizationResponse(Guid Id, string Name, string Slug, OrganizationStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record OrganizationDashboardResponse(int Total, int Active, int Suspended, int Deactivated);
public sealed record TenantAdministratorResponse(string Id, string FullName, string Email, bool IsActive, bool MustChangePassword);
public sealed record CreateTenantAdministratorRequest(string? FullName, string? Email);
public sealed record OrganizationAuditResponse(Guid Id, string Action, string ActorName, string? TargetUserName, string? TargetUserEmail, string? FeatureKey, DateTimeOffset OccurredAtUtc);
