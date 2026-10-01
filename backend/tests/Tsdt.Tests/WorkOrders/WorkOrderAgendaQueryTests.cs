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
        AssignedUserId = actor, ScheduledStart = start ?? From.AddHours(9), ScheduledEnd = end ?? From.AddHours(11),
        CustomerLegalNameSnapshot = "Historical customer", ServiceAddressSnapshot = "Historical address",
        CreatedByUserId = "test", UpdatedByUserId = "test"
    };

    [Theory]
    [InlineData(WorkOrderStatus.Scheduled, true)]
    [InlineData(WorkOrderStatus.InProgress, true)]
    [InlineData(WorkOrderStatus.AwaitingClosure, true)]
    [InlineData(WorkOrderStatus.Closed, true)]
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
        var order = Order(); order.ScheduledStart = null;
        Assert.Empty(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
        order.ScheduledStart = From; order.ScheduledEnd = null;
        Assert.Empty(WorkOrderAgendaQuery.InRange(new[] { order }.AsQueryable(), From, To));
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
        var query = WorkOrderAgendaQuery.InRange(visible, From, To);
        var sql = query.ToQueryString();
        Assert.Contains("ScheduledStart", sql);
        Assert.Contains("ScheduledEnd", sql);
        Assert.Contains("OrganizationId", sql);
        Assert.Contains("AssignedUserId", sql);
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
