namespace Tsdt.Api.WorkOrders;

public sealed record CreateWorkOrderRequest(Guid SourceId, string? AssignedUserId, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, string? OperationalNotes);
public sealed record UpdateWorkOrderPlanningRequest(string? AssignedUserId, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, string? OperationalNotes, Guid ExpectedVersion);
public sealed record WorkOrderVersionRequest(Guid ExpectedVersion);
public sealed record CompleteWorkOrderRequest(Guid ExpectedVersion, string? CompletionNotes);
public sealed record WorkOrderItemResponse(Guid Id, Guid QuoteItemId, Guid? ContractItemId, Guid ServiceId, string ServiceCodeSnapshot, string ServiceNameSnapshot, Guid ServiceLineIdSnapshot, string ServiceLineCodeSnapshot, string ServiceLineNameSnapshot, int DisplayOrder);
public sealed record WorkOrderSummaryResponse(Guid Id, string Number, WorkOrderSourceType SourceType, Guid QuoteId, Guid? ContractId, Guid CustomerId, string CustomerLegalNameSnapshot, WorkOrderStatus Status, string? AssignedUserId, string? AssignedUserNameSnapshot, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, DateTimeOffset UpdatedAtUtc);
public sealed record WorkOrderDetailResponse(Guid Id, string Number, WorkOrderSourceType SourceType, Guid QuoteId, Guid? ContractId, Guid CustomerId, string CustomerLegalNameSnapshot, string ServiceAddressSnapshot, WorkOrderStatus Status, string? AssignedUserId, string? AssignedUserNameSnapshot, string? AssignedUserEmailSnapshot, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, DateTimeOffset? StartedAtUtc, DateTimeOffset? ExecutionCompletedAtUtc, DateTimeOffset? ClosedAtUtc, DateTimeOffset? CancelledAtUtc, string? OperationalNotes, string? CompletionNotes, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Version, IReadOnlyList<WorkOrderItemResponse> Items);
public sealed record WorkOrderListResponse(IReadOnlyList<WorkOrderSummaryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record WorkOrderAuditResponse(string Action, string ActorUserId, DateTimeOffset OccurredAtUtc, string? ChangedFields);
