namespace Tsdt.Api.Audit;

public sealed class UserAdministrationAuditRecord
{
    public long Id { get; set; }

    public required string ActorUserId { get; set; }

    public required string TargetUserId { get; set; }

    public required string Action { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? OldRole { get; set; }

    public string? NewRole { get; set; }
}
