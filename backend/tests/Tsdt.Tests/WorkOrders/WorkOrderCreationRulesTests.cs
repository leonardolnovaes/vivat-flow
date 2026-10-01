using Tsdt.Api.Contracts;
using Tsdt.Api.WorkOrders;
using Xunit;

namespace Tsdt.Tests.WorkOrders;

public sealed class WorkOrderCreationRulesTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Active_contract_allows_successive_closed_work_orders()
    {
        Assert.True(WorkOrderRules.CanCreateFromContract(ContractStatus.Active, []));
        Assert.True(WorkOrderRules.CanCreateFromContract(ContractStatus.Active, [WorkOrderStatus.Closed, WorkOrderStatus.Cancelled]));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(WorkOrderStatus.Draft)]
    [InlineData(WorkOrderStatus.Scheduled)]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.AwaitingClosure)]
    public void Unfinished_work_order_blocks_another_for_same_scope(WorkOrderStatus status)
        => Assert.False(WorkOrderRules.CanCreateFromContract(ContractStatus.Active, [WorkOrderStatus.Closed, status]));

    [Fact]
    [Trait("Category", "Unit")]
    public void Contract_must_be_active()
    {
        Assert.False(WorkOrderRules.CanCreateFromContract(ContractStatus.Draft, []));
        Assert.False(WorkOrderRules.CanCreateFromContract(ContractStatus.Ended, []));
        Assert.False(WorkOrderRules.CanCreateFromContract(ContractStatus.Cancelled, []));
    }
}
