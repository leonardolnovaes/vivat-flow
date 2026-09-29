namespace Tsdt.Api.Platform;
public sealed record OrganizationRequest(string? Name, string? Slug);
public sealed record OrganizationResponse(Guid Id, string Name, string Slug, OrganizationStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record OrganizationDashboardResponse(int Total, int Active, int Suspended, int Deactivated);
