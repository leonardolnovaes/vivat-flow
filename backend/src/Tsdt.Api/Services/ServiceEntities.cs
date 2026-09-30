using Tsdt.Api.Platform;

namespace Tsdt.Api.Services;

public sealed class Service
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ServiceLineId { get; set; }
    public ServiceLine ServiceLine { get; set; } = null!;
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal? BasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string UpdatedByUserId { get; set; }
    public Guid Version { get; set; }
}

public sealed class ServiceAuditRecord
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public required string ActorUserId { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? ChangedFields { get; set; }
    public Service Service { get; set; } = null!;
}
