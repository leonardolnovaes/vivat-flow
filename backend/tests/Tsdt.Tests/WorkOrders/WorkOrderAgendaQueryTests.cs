using Tsdt.Api.WorkOrders;
using Tsdt.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tsdt.Tests.WorkOrders;

[Trait("Category", "Unit")]
public sealed class WorkOrderAgendaQueryTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset To = From.AddDays(1);
    private static readonly Guid Tenant = Guid.NewGuid();

    private static WorkOrder Order(WorkOrderStatus status = WorkOrderStatus.Scheduled, string actor = "worker", Guid? tenant = null,
        DateTimeOffset? start = null, DateTimeOffset? end = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = tenant ?? Tenant, Number = "OS-2026-000001", Status = status,
        AssignedUserId = actor, ScheduledStartDate = DateOnly.FromDateTime((start ?? From.AddHours(9)).ToOffset(TimeSpan.FromHours(-3)).DateTime),
        ScheduledStartTime = TimeOnly.FromDateTime((start ?? From.AddHours(9)).ToOffset(TimeSpan.FromHours(-3)).DateTime),
        ScheduledEndDate = DateOnly.FromDateTime((end ?? From.AddHours(11)).ToOffset(TimeSpan.FromHours(-3)).DateTime),
        ScheduledEndTime = TimeOnly.FromDateTime((end ?? From.AddHours(11)).ToOffset(TimeSpan.FromHours(-3)).DateTime),
        CustomerLegalNameSnapshot = "Historical customer", ServiceAddressSnapshot = "Historical address",
        CreatedByUserId = "test", UpdatedByUserId = "test"
    };

    [Theory]
    [InlineData(WorkOrderStatus.Scheduled, true)]
    [InlineData(WorkOrderStatus.InProgress, true)]
    [InlineData(WorkOrderStatus.Completed, true)]
    [InlineData(WorkOrderStatus.Draft, false)]
    [InlineData(WorkOrderStatus.Cancelled, false)]
    public void Only_formally_scheduled_lifecycle_states_appear(WorkOrderStatus status, bool expected)
        => Assert.Equal(expected, WorkOrderAgendaQuery.InRange(new[] { Order(status) }.AsQueryable(), From, To).Any());

    [Theory]
    [InlineData(-2, 2, true)]
    [InlineData(23, 26, true)]
    [InlineData(-2, 0, false)]
    [InlineData(24, 26, false)]
    [InlineData(-24, 48, true)]
    [InlineData(9, 9, false)]
    [InlineData(11, 9, false)]
    public void Overlap_uses_exclusive_end_boundaries_and_valid_schedules(int startHours, int endHours, bool expected)
    {
        var order = Order(start: From.AddHours(startHours), end: From.AddHours(endHours));
        Assert.Equal(expected, WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To).Any());
    }

    [Fact]
    public void Missing_schedule_is_excluded()
    {
        var order = Order(); order.ScheduledStartDate = null;
        Assert.Empty(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
        order.ScheduledStartDate = DateOnly.FromDateTime(From.DateTime); order.ScheduledEndDate = null; order.ScheduledEndTime = null;
        Assert.Single(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
    }

    [Fact]
    public void Range_must_be_forward_and_bounded()
    {
        Assert.NotNull(WorkOrderAgendaQuery.RangeError(From, From));
        Assert.NotNull(WorkOrderAgendaQuery.RangeError(To, From));
        Assert.NotNull(WorkOrderAgendaQuery.RangeError(From, From.AddDays(62).AddTicks(1)));
        Assert.Null(WorkOrderAgendaQuery.RangeError(From, From.AddDays(62)));
        Assert.Null(WorkOrderAgendaQuery.RangeError(From, To));
    }

    [Fact]
    public void Date_only_open_ended_orders_are_visible_without_invented_times()
    {
        var order = Order();
        order.ScheduledStartTime = null; order.ScheduledEndDate = null; order.ScheduledEndTime = null;
        Assert.Single(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
        Assert.Empty(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), To, To.AddDays(1)));
        order.ScheduledEndDate = DateOnly.FromDateTime(To.DateTime);
        Assert.Single(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), To, To.AddDays(1)));
        order.ScheduledEndTime = TimeOnly.MinValue;
        Assert.Empty(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), To, To.AddDays(1)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("invalid", false)]
    [InlineData("2026-10-01T00:00:00", false)]
    [InlineData("2026-02-30T00:00:00Z", false)]
    [InlineData("2026-10-01T00:00:00-03:00", true)]
    [InlineData("2026-10-01T03:00:00.000Z", true)]
    public void Timestamp_requires_explicit_offset(string? value, bool expected)
        => Assert.Equal(expected, WorkOrderAgendaQuery.TryTimestamp(value, out _));

    [Fact]
    public void Offset_equivalent_instants_overlap()
    {
        var order = Order(start: From.ToUniversalTime(), end: To.ToUniversalTime());
        Assert.Single(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
    }

    [Fact]
    public void PostgreSql_query_translates_offset_boundaries_without_a_database_connection()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused_agenda_query").Options;
        using var db = new ApplicationDbContext(options);
        var visible = WorkOrderAgendaQuery.Visible(db.WorkOrders, Tenant, false, "worker");
        var query = WorkOrderAgendaQuery.InRange(visible, From, To, db.WorkOrderAuditRecords);
        var sql = query.ToQueryString();
        Assert.Contains("ScheduledStart", sql);
        Assert.Contains("ScheduledEnd", sql);
        Assert.Contains("OrganizationId", sql);
        Assert.Contains("AssignedUserId", sql);
        Assert.Contains("WORK_ORDER_SCHEDULED", sql);
    }

    [Fact]
    public void Cancelled_orders_require_their_own_formal_scheduling_history()
    {
        var order = Order(WorkOrderStatus.Cancelled);
        var orders = new[] { order }.AsQueryable();
        var history = new List<WorkOrderAuditRecord>();
        Assert.Empty(WorkOrderAgendaQuery.InRange(orders, From, To, history.AsQueryable()));
        history.Add(new() { Id = Guid.NewGuid(), WorkOrderId = Guid.NewGuid(), ActorUserId = "worker", Action = "WORK_ORDER_SCHEDULED" });
        Assert.Empty(WorkOrderAgendaQuery.InRange(orders, From, To, history.AsQueryable()));
        history.Add(new() { Id = Guid.NewGuid(), WorkOrderId = order.Id, ActorUserId = "worker", Action = "WORK_ORDER_SCHEDULED" });
        Assert.Single(WorkOrderAgendaQuery.InRange(orders, From, To, history.AsQueryable()));
    }

    [Fact]
    public void User_scope_ignores_forged_filter_and_excludes_other_tenants()
    {
        var own = Order();
        var orders = new[] { own, Order(actor: "other"), Order(tenant: Guid.NewGuid()) }.AsQueryable();
        var visible = WorkOrderAgendaQuery.Visible(orders, Tenant, false, "worker");
        var result = WorkOrderAgendaQuery.ForAssignee(visible, false, "other");
        Assert.Equal(own.Id, Assert.Single(WorkOrderAgendaQuery.InRange(result, From, To)).Id);
    }

    [Fact]
    public void Management_filter_cannot_escape_tenant()
    {
        var own = Order(actor: "other");
        var orders = new[] { own, Order(), Order(actor: "other", tenant: Guid.NewGuid()) }.AsQueryable();
        var visible = WorkOrderAgendaQuery.Visible(orders, Tenant, true, "manager");
        Assert.Equal(2, WorkOrderAgendaQuery.ForAssignee(visible, true, null).Count());
        Assert.Equal(own.Id, Assert.Single(WorkOrderAgendaQuery.ForAssignee(visible, true, "other")).Id);
        Assert.Empty(WorkOrderAgendaQuery.ForAssignee(visible, true, "foreign-worker"));
    }
}
