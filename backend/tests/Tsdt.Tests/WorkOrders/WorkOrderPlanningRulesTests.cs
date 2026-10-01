using Tsdt.Api.WorkOrders;
using Tsdt.Api.Identity;
using Xunit;

namespace Tsdt.Tests.WorkOrders;

[Trait("Category", "Unit")]
public sealed class WorkOrderPlanningRulesTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Draft_allows_empty_planning_and_schedule_requires_only_assignee_and_start_date()
    {
        Assert.True(WorkOrderRules.HasValidSchedule(null, null));
        Assert.True(WorkOrderRules.HasValidSchedule(Day, null));
        Assert.True(WorkOrderRules.CanSchedule("worker", Day));
        Assert.False(WorkOrderRules.CanSchedule(null, Day));
        Assert.False(WorkOrderRules.CanSchedule("worker", null));
    }

    [Fact]
    public void Optional_times_are_not_invented_and_explicit_times_need_dates()
    {
        Assert.True(WorkOrderRules.HasValidSchedule(Day, Day));
        Assert.True(WorkOrderRules.HasValidSchedule(Day, Day, new(9, 0), null));
        Assert.False(WorkOrderRules.HasValidSchedule(null, null, new(9, 0)));
        Assert.False(WorkOrderRules.HasValidSchedule(Day, null, null, new(18, 0)));
        Assert.False(WorkOrderRules.HasValidSchedule(Day, Day, new(9, 0), new(9, 0)));
        Assert.False(WorkOrderRules.HasValidSchedule(Day, Day.AddDays(-1)));
        Assert.True(WorkOrderRules.HasValidSchedule(Day, Day.AddDays(1), new(23, 0), new(1, 0)));
    }

    [Fact]
    public void Lifecycle_accepts_only_business_transitions_and_completion_is_terminal()
    {
        foreach (var current in Enum.GetValues<WorkOrderStatus>())
        foreach (var target in Enum.GetValues<WorkOrderStatus>())
        {
            var expected = (current, target) is (WorkOrderStatus.Draft, WorkOrderStatus.Scheduled) or
                (WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress) or (WorkOrderStatus.InProgress, WorkOrderStatus.Completed) ||
                (target == WorkOrderStatus.Cancelled && current is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress);
            Assert.Equal(expected, WorkOrderRules.CanTransition(current, target));
        }
        Assert.False(WorkOrderRules.CanPlan(WorkOrderStatus.Completed));
        Assert.DoesNotContain("Closed", Enum.GetNames<WorkOrderStatus>());
    }

    [Fact]
    public void History_snapshots_authenticated_actor_and_cancellation_reason_without_mutable_attribution()
    {
        var order = Order();
        var actor = new ApplicationUser { Id = "actor", OrganizationId = order.OrganizationId, FullName = "Original name" };
        var record = WorkOrderAudit.Create(order, actor.Id, actor, "WORK_ORDER_CANCELLED", null, "  Customer request  ");
        actor.FullName = "Changed name";
        Assert.Equal("Original name", record.ActorNameSnapshot);
        Assert.Equal(actor.Id, record.ActorUserId);
        Assert.Equal("Customer request", record.CancellationReason);
        Assert.Equal(order.Id, record.WorkOrderId);
        actor.OrganizationId = Guid.NewGuid();
        Assert.Equal("Usuário não disponível", WorkOrderAudit.Create(order, actor.Id, actor, "ACTION", null).ActorNameSnapshot);
        Assert.Equal("Sistema", WorkOrderAudit.Create(order, "", null, "ACTION", null).ActorNameSnapshot);
    }

    private static WorkOrder Order() => new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Number = "OS-test",
        CustomerLegalNameSnapshot = "Customer", ServiceAddressSnapshot = "Address", CreatedByUserId = "actor", UpdatedByUserId = "actor" };
}
