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

    public static IQueryable<WorkOrder> InRange(IQueryable<WorkOrder> orders, DateTimeOffset from, DateTimeOffset to, IQueryable<WorkOrderAuditRecord>? history = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var localFrom = TimeZoneInfo.ConvertTime(from, zone).DateTime;
        var localTo = TimeZoneInfo.ConvertTime(to, zone).DateTime;
        var fromDate = DateOnly.FromDateTime(localFrom);
        var toDate = DateOnly.FromDateTime(localTo);
        var fromTime = TimeOnly.FromDateTime(localFrom);
        var toTime = TimeOnly.FromDateTime(localTo);
        // Date-only ends include their entire day. Explicit end times remain exclusive.
        var query = orders.Where(order => order.Status != WorkOrderStatus.Draft &&
            order.ScheduledStartDate != null &&
            (order.ScheduledEndDate == null || order.ScheduledEndDate > order.ScheduledStartDate ||
                (order.ScheduledEndDate == order.ScheduledStartDate &&
                    (order.ScheduledStartTime == null || order.ScheduledEndTime == null || order.ScheduledEndTime > order.ScheduledStartTime))) &&
            (order.ScheduledStartDate < toDate || (order.ScheduledStartDate == toDate && toTime > TimeOnly.MinValue &&
                (order.ScheduledStartTime == null || order.ScheduledStartTime < toTime))) &&
            ((order.ScheduledEndDate ?? order.ScheduledStartDate) > fromDate ||
                ((order.ScheduledEndDate ?? order.ScheduledStartDate) == fromDate &&
                    (order.ScheduledEndTime == null || order.ScheduledEndTime > fromTime))));
        // Tentative planning on a cancelled draft never becomes a historical appointment.
        return history is null ? query.Where(order => order.Status != WorkOrderStatus.Cancelled) :
            query.Where(order => order.Status != WorkOrderStatus.Cancelled ||
                history.Any(item => item.WorkOrderId == order.Id && item.Action == "WORK_ORDER_SCHEDULED"));
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
