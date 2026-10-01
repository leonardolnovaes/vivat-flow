using Microsoft.EntityFrameworkCore;
using Tsdt.Api.Contracts;
using Tsdt.Api.Identity;
using Tsdt.Api.Quotes;
using Tsdt.Api.WorkOrders;

namespace Tsdt.Api.Customers;

public static class CustomerActivityService
{
    public static bool IsActiveRelationship(QuoteStatus status) => status is QuoteStatus.Draft or QuoteStatus.AwaitingApproval or QuoteStatus.ChangesRequested or QuoteStatus.Approved;
    public static bool IsActiveRelationship(ContractStatus status) => status == ContractStatus.Active;
    public static bool IsActiveRelationship(WorkOrderStatus status) => status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress or WorkOrderStatus.AwaitingClosure;
    public static bool ShouldRemainActive(bool activeQuote, bool approvedQuoteAwaitingNextStep, bool activeContract, bool activeWorkOrder)
        => activeQuote || approvedQuoteAwaitingNextStep || activeContract || activeWorkOrder;

    public static async Task<bool> HasActiveRelationshipsAsync(ApplicationDbContext db, Guid customerId, Guid? excludeQuoteId = null, Guid? excludeContractId = null, Guid? excludeWorkOrderId = null, bool cancelledContract = false, bool cancelledWorkOrder = false)
    {
        var activeQuote = await db.Quotes.AnyAsync(x => x.CustomerId == customerId && x.Id != excludeQuoteId &&
            (x.Status == QuoteStatus.Draft || x.Status == QuoteStatus.AwaitingApproval || x.Status == QuoteStatus.ChangesRequested));
        var approvedAwaitingNextStep = await db.Quotes.AnyAsync(x => x.CustomerId == customerId && x.Id != excludeQuoteId &&
            x.Status == QuoteStatus.Approved &&
            !db.Contracts.Any(contract => contract.QuoteId == x.Id && (contract.Status == ContractStatus.Active || contract.Status == ContractStatus.Ended) && (!cancelledContract || contract.Id != excludeContractId)) &&
            !db.WorkOrders.Any(order => order.QuoteId == x.Id && order.Status != WorkOrderStatus.Cancelled && (!cancelledWorkOrder || order.Id != excludeWorkOrderId)));
        var activeContract = await db.Contracts.AnyAsync(x => x.CustomerId == customerId && x.Id != excludeContractId && x.Status == ContractStatus.Active);
        var activeWorkOrder = await db.WorkOrders.AnyAsync(x => x.CustomerId == customerId && x.Id != excludeWorkOrderId &&
            (x.Status == WorkOrderStatus.Draft || x.Status == WorkOrderStatus.Scheduled || x.Status == WorkOrderStatus.InProgress || x.Status == WorkOrderStatus.AwaitingClosure));
        return ShouldRemainActive(activeQuote, approvedAwaitingNextStep, activeContract, activeWorkOrder);
    }

    public static async Task ReconcileAsync(ApplicationDbContext db, Guid customerId, string actor, bool changedRelationshipActive,
        Guid? excludeQuoteId = null, Guid? excludeContractId = null, Guid? excludeWorkOrderId = null, bool cancelledContract = false, bool cancelledWorkOrder = false)
    {
        var customer = await db.Customers.SingleAsync(x => x.Id == customerId);
        var shouldBeActive = changedRelationshipActive || await HasActiveRelationshipsAsync(db, customerId, excludeQuoteId, excludeContractId, excludeWorkOrderId, cancelledContract, cancelledWorkOrder);
        if (customer.IsActive == shouldBeActive) return;
        customer.IsActive = shouldBeActive;
        customer.UpdatedAtUtc = DateTimeOffset.UtcNow;
        customer.UpdatedByUserId = actor;
        customer.Version = Guid.NewGuid();
        db.CustomerAuditRecords.Add(new CustomerAuditRecord
        {
            Id = Guid.NewGuid(), CustomerId = customerId, ActorUserId = actor,
            Action = shouldBeActive ? "CUSTOMER_ACTIVATED_BY_RELATIONSHIP" : "CUSTOMER_DEACTIVATED_BY_RELATIONSHIP",
            ChangedFields = "IsActive", OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }
}
