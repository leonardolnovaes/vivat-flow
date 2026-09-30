namespace Tsdt.Api.WorkOrders;

public static class WorkOrderRules
{
    public static bool CanPlan(WorkOrderStatus status) => status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled;
    public static bool CanCancel(WorkOrderStatus status) => status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress or WorkOrderStatus.AwaitingClosure;
    public static bool CanTransition(WorkOrderStatus current, WorkOrderStatus target) =>
        (current, target) is (WorkOrderStatus.Draft, WorkOrderStatus.Scheduled) or
        (WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress) or
        (WorkOrderStatus.InProgress, WorkOrderStatus.AwaitingClosure) or
        (WorkOrderStatus.AwaitingClosure, WorkOrderStatus.Closed) ||
        (target == WorkOrderStatus.Cancelled && CanCancel(current));
    public static bool HasValidSchedule(DateTimeOffset? start, DateTimeOffset? end) => !start.HasValue || !end.HasValue || end > start;
}
