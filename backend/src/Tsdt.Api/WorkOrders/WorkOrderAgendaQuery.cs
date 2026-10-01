using System.Globalization;
using System.Text.RegularExpressions;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderAgendaQuery
{
    public static IQueryable<WorkOrder> Visible(IQueryable<WorkOrder> orders, Guid organizationId, bool management, string actor)
        => orders.Where(order => order.OrganizationId == organizationId && (management || order.AssignedUserId == actor));

    public static IQueryable<WorkOrder> ForAssignee(IQueryable<WorkOrder> visibleOrders, bool management, string? assignedUserId)
        => management && !string.IsNullOrWhiteSpace(assignedUserId)
            ? visibleOrders.Where(order => order.AssignedUserId == assignedUserId) : visibleOrders;

    public static IQueryable<WorkOrder> InRange(IQueryable<WorkOrder> orders, DateTimeOffset from, DateTimeOffset to)
    {
        // PostgreSQL timestamptz parameters require UTC DateTimeOffset values.
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        return orders.Where(order => (order.Status == WorkOrderStatus.Scheduled || order.Status == WorkOrderStatus.InProgress ||
            order.Status == WorkOrderStatus.AwaitingClosure || order.Status == WorkOrderStatus.Closed) &&
            order.ScheduledStart != null && order.ScheduledEnd != null && order.ScheduledEnd > order.ScheduledStart &&
            order.ScheduledStart < to && order.ScheduledEnd > from);
    }

    public static string? RangeError(DateTimeOffset from, DateTimeOffset to)
        => to <= from ? "O término deve ser posterior ao início." :
            to - from > TimeSpan.FromDays(62) ? "Selecione um período de no máximo 62 dias." : null;

    public static bool TryTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null && Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }
}
