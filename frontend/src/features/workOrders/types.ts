export type WorkOrderStatus = 'Draft' | 'Scheduled' | 'InProgress' | 'Completed' | 'Cancelled'
export type WorkOrderSource = 'Quote' | 'Contract'
export type WorkOrderItem = { id: string; serviceCodeSnapshot: string; serviceNameSnapshot: string; serviceLineCodeSnapshot: string; serviceLineNameSnapshot: string; displayOrder: number }
export type WorkOrderSummary = { id: string; number: string; sourceType: WorkOrderSource; quoteId: string; contractId: string | null; customerLegalNameSnapshot: string; status: WorkOrderStatus; assignedUserId: string | null; assignedUserNameSnapshot: string | null; scheduledStartDate: string | null; scheduledStartTime: string | null; scheduledEndDate: string | null; scheduledEndTime: string | null; updatedAtUtc: string }
export type WorkOrder = WorkOrderSummary & { serviceAddressSnapshot: string; assignedUserEmailSnapshot: string | null; startedAtUtc: string | null; executionCompletedAtUtc: string | null; closedAtUtc: string | null; cancelledAtUtc: string | null; operationalNotes: string | null; completionNotes: string | null; createdAtUtc: string; version: string; items: WorkOrderItem[] }
export type WorkOrderList = { items: WorkOrderSummary[]; page: number; pageSize: number; totalCount: number }
export type WorkOrderHistory = { action: string; actorNameSnapshot: string; cancellationReason: string | null; occurredAtUtc: string }
export type EligibleAssignee = { id: string; fullName: string; email: string; role: string }
export type Planning = { assignedUserId: string; scheduledStartDate: string; scheduledStartTime: string; scheduledEndDate: string; scheduledEndTime: string; operationalNotes: string }
export type WorkOrderSourceSummary = { id: string; sourceType: WorkOrderSource; quoteId: string; contractId: string | null; reference: string; customerLegalNameSnapshot: string; serviceAddressSnapshot: string | null; status: string; canCreate: boolean; governingContractId: string | null; governingContractStatus: string | null; currentWorkOrderId: string | null; currentWorkOrderNumber: string | null }
export type WorkOrderSourceItem = Pick<WorkOrderItem, 'serviceCodeSnapshot' | 'serviceNameSnapshot' | 'serviceLineCodeSnapshot' | 'serviceLineNameSnapshot' | 'displayOrder'>
export type WorkOrderSourceDetail = { source: WorkOrderSourceSummary; items: WorkOrderSourceItem[] }
export type WorkOrderSourceList = { items: WorkOrderSourceSummary[]; page: number; pageSize: number; totalCount: number }

export type AgendaEntry = Pick<WorkOrderSummary, 'id' | 'number' | 'customerLegalNameSnapshot' | 'status' | 'assignedUserId' | 'assignedUserNameSnapshot' | 'scheduledStartTime' | 'scheduledEndDate' | 'scheduledEndTime'> & { serviceAddressSnapshot: string; scheduledStartDate: string; services: { serviceCodeSnapshot: string; serviceNameSnapshot: string }[] }
