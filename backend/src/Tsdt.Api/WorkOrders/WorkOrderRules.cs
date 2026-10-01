using Tsdt.Api.Contracts;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderRules
{
    public static bool CanCreateFromContract(ContractStatus contractStatus, IEnumerable<WorkOrderStatus> previousOrders)
        => contractStatus == ContractStatus.Active && previousOrders.All(status => status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled);
    public static bool CanPlan(WorkOrderStatus status) => status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled;
    public static bool CanCancel(WorkOrderStatus status) => status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress;
    public static bool CanTransition(WorkOrderStatus current, WorkOrderStatus target) =>
        (current, target) is (WorkOrderStatus.Draft, WorkOrderStatus.Scheduled) or
        (WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress) or
        (WorkOrderStatus.InProgress, WorkOrderStatus.Completed) ||
        (target == WorkOrderStatus.Cancelled && CanCancel(current));
    public static bool HasValidSchedule(DateOnly? start, DateOnly? end, TimeOnly? startTime = null, TimeOnly? endTime = null)
        => !(startTime.HasValue && !start.HasValue) && !(endTime.HasValue && !end.HasValue) &&
            (!start.HasValue || !end.HasValue || end > start ||
                (end == start && (!startTime.HasValue || !endTime.HasValue || endTime > startTime)));

    public static bool CanSchedule(string? assignedUserId, DateOnly? start)
        => !string.IsNullOrWhiteSpace(assignedUserId) && start.HasValue;
}
