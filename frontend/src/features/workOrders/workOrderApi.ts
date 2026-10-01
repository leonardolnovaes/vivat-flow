import { ApiError, request } from '../../api'

export type WorkOrderSummary = { id: string; number: string; status: string; contractId: string | null }
type WorkOrderList = { items: WorkOrderSummary[]; totalCount: number; pageSize: number }

export async function findCurrentWorkOrderForQuote(quoteId: string): Promise<WorkOrderSummary | null> {
  for (let page = 1; ; page++) {
    const response = await request(`/api/work-orders?quoteId=${encodeURIComponent(quoteId)}&page=${page}&pageSize=100`)
    if (!response.ok) throw await ApiError.from(response)
    const result = await response.json() as WorkOrderList
    const current = result.items.find(item => item.status !== 'Cancelled')
    if (current) return current
    if (page * result.pageSize >= result.totalCount) return null
  }
}

export async function createWorkOrderFromContract(contractId: string): Promise<WorkOrderSummary> {
  const response = await request('/api/work-orders/from-contract', {
    method: 'POST',
    body: JSON.stringify({ sourceId: contractId, assignedUserId: null, scheduledStart: null, scheduledEnd: null, operationalNotes: null }),
  })
  if (!response.ok) throw await ApiError.from(response)
  return response.json() as Promise<WorkOrderSummary>
}
