namespace Tsdt.Api.WorkOrders;

public enum WorkOrderSourceType { Quote, Contract }
public enum WorkOrderStatus { Draft, Scheduled, InProgress, Completed, Cancelled }

public sealed class WorkOrder
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Number { get; set; }
    public WorkOrderSourceType SourceType { get; set; }
    public Guid QuoteId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid CustomerId { get; set; }
    public required string CustomerLegalNameSnapshot { get; set; }
    public required string ServiceAddressSnapshot { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;
    public string? AssignedUserId { get; set; }
    public string? AssignedUserNameSnapshot { get; set; }
    public string? AssignedUserEmailSnapshot { get; set; }
    public DateOnly? ScheduledStartDate { get; set; }
    public TimeOnly? ScheduledStartTime { get; set; }
    public DateOnly? ScheduledEndDate { get; set; }
    public TimeOnly? ScheduledEndTime { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? ExecutionCompletedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? OperationalNotes { get; set; }
    public string? CompletionNotes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string UpdatedByUserId { get; set; }
    public Guid Version { get; set; }
    public List<WorkOrderItem> Items { get; set; } = [];
}

public sealed class WorkOrderItem
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid QuoteItemId { get; set; }
    public Guid? ContractItemId { get; set; }
    public Guid ServiceId { get; set; }
    public required string ServiceCodeSnapshot { get; set; }
    public required string ServiceNameSnapshot { get; set; }
    public Guid ServiceLineIdSnapshot { get; set; }
    public required string ServiceLineCodeSnapshot { get; set; }
    public required string ServiceLineNameSnapshot { get; set; }
    public int DisplayOrder { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
}

public sealed class WorkOrderAuditRecord
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public required string ActorUserId { get; set; }
    public string? ActorNameSnapshot { get; set; }
    public string? CancellationReason { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? ChangedFields { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
}

public sealed class WorkOrderNumberCounter
{
    public Guid OrganizationId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
