namespace Tsdt.Api.Platform;
public enum OrganizationStatus { Active, Suspended, Deactivated }

public static class OrganizationLifecycle
{
    public static bool CanTransition(OrganizationStatus current, OrganizationStatus target) =>
        current == target || target switch
        {
            OrganizationStatus.Suspended => current == OrganizationStatus.Active,
            OrganizationStatus.Deactivated => current is OrganizationStatus.Active or OrganizationStatus.Suspended,
            OrganizationStatus.Active => current == OrganizationStatus.Suspended,
            _ => false
        };
}
public sealed class Organization { public Guid Id { get; set; } = Guid.NewGuid(); public required string Name { get; set; } public required string Slug { get; set; } public OrganizationStatus Status { get; set; } = OrganizationStatus.Active; public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow; }
public sealed class OrganizationAuditRecord { public Guid Id { get; set; } = Guid.NewGuid(); public Guid OrganizationId { get; set; } public Organization? Organization { get; set; } public required string ActorUserId { get; set; } public string? TargetUserId { get; set; } public required string Action { get; set; } public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow; }
