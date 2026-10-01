using Tsdt.Api.Identity;

namespace Tsdt.Api.WorkOrders;

public static class WorkOrderAudit
{
    public static WorkOrderAuditRecord Create(WorkOrder order, string actorId, ApplicationUser? actor, string action, string? fields, string? cancellationReason = null)
    {
        // Only a resolved actor belonging to the aggregate's tenant may decorate its history.
        var name = string.IsNullOrEmpty(actorId) ? "Sistema" :
            actor?.Id == actorId && actor.OrganizationId == order.OrganizationId ? actor.FullName : "Usuário não disponível";
        return new WorkOrderAuditRecord { Id = Guid.NewGuid(), WorkOrderId = order.Id, ActorUserId = actorId,
            ActorNameSnapshot = name, Action = action, OccurredAtUtc = DateTimeOffset.UtcNow,
            ChangedFields = fields, CancellationReason = string.IsNullOrWhiteSpace(cancellationReason) ? null : cancellationReason.Trim() };
    }
}
