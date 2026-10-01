using Tsdt.Api.Contracts;
using Tsdt.Api.Customers;
using Tsdt.Api.Quotes;
using Tsdt.Api.WorkOrders;
using Xunit;

namespace Tsdt.Tests.Customers;

public sealed class CustomerActivityRulesTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(QuoteStatus.Draft, true)]
    [InlineData(QuoteStatus.AwaitingApproval, true)]
    [InlineData(QuoteStatus.ChangesRequested, true)]
    [InlineData(QuoteStatus.Approved, true)]
    [InlineData(QuoteStatus.Rejected, false)]
    [InlineData(QuoteStatus.Expired, false)]
    [InlineData(QuoteStatus.Cancelled, false)]
    public void Quote_status_is_classified_for_customer_activity(QuoteStatus status, bool expected)
        => Assert.Equal(expected, CustomerActivityService.IsActiveRelationship(status));

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, true, true, true)]
    public void Any_active_relationship_prevents_deactivation(bool quote, bool approvedWaiting, bool contract, bool workOrder, bool expected)
        => Assert.Equal(expected, CustomerActivityService.ShouldRemainActive(quote, approvedWaiting, contract, workOrder));

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(ContractStatus.Draft, false)]
    [InlineData(ContractStatus.Active, true)]
    [InlineData(ContractStatus.Ended, false)]
    [InlineData(ContractStatus.Cancelled, false)]
    public void Only_active_contract_keeps_customer_active(ContractStatus status, bool expected)
        => Assert.Equal(expected, CustomerActivityService.IsActiveRelationship(status));

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(WorkOrderStatus.Draft, true)]
    [InlineData(WorkOrderStatus.Scheduled, true)]
    [InlineData(WorkOrderStatus.InProgress, true)]
    [InlineData(WorkOrderStatus.Completed, false)]
    [InlineData(WorkOrderStatus.Cancelled, false)]
    public void Work_order_execution_keeps_customer_active_until_closed_or_cancelled(WorkOrderStatus status, bool expected)
        => Assert.Equal(expected, CustomerActivityService.IsActiveRelationship(status));
}
