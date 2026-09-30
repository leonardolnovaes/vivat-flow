import { ApiError, request } from '../../api'
import type { EligibleAssignee, Planning, WorkOrder, WorkOrderHistory, WorkOrderList } from './types'

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init)
  if (!response.ok) throw await ApiError.from(response)
  return response.json() as Promise<T>
}

export const listWorkOrders = (query: URLSearchParams) => call<WorkOrderList>(`/api/work-orders?${query}`)
export async function findCurrentWorkOrder(query: { quoteId?: string; contractId?: string }) {
  for (let page = 1; ; page++) {
    const result = await listWorkOrders(new URLSearchParams({ ...query, page: String(page), pageSize: '100' }))
    const current = result.items.find(item => item.status !== 'Cancelled')
    if (current) return current
    if (page * result.pageSize >= result.totalCount) return null
  }
}
export const getWorkOrder = (id: string) => call<WorkOrder>(`/api/work-orders/${id}`)
export const getWorkOrderHistory = (id: string) => call<WorkOrderHistory[]>(`/api/work-orders/${id}/history`)
export const getEligibleAssignees = () => call<EligibleAssignee[]>('/api/work-orders/eligible-assignees')
export const createWorkOrder = (source: 'quote' | 'contract', sourceId: string, planning: Planning) => call<WorkOrder>(`/api/work-orders/from-${source}`, { method: 'POST', body: JSON.stringify({ sourceId, ...planningPayload(planning) }) })
export const saveWorkOrderPlanning = (id: string, version: string, planning: Planning) => call<WorkOrder>(`/api/work-orders/${id}/planning`, { method: 'PUT', body: JSON.stringify({ expectedVersion: version, ...planningPayload(planning) }) })
export const transitionWorkOrder = (id: string, action: 'schedule' | 'start' | 'complete' | 'close' | 'cancel', version: string, completionNotes?: string) => call<WorkOrder>(`/api/work-orders/${id}/${action}`, { method: 'POST', body: JSON.stringify({ expectedVersion: version, ...(action === 'complete' ? { completionNotes: completionNotes?.trim() || null } : {}) }) })

export function toLocalInput(value: string | null) {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}
function toOffsetIso(value: string) { return value ? new Date(value).toISOString() : null }
function planningPayload(planning: Planning) { return { assignedUserId: planning.assignedUserId || null, scheduledStart: toOffsetIso(planning.scheduledStart), scheduledEnd: toOffsetIso(planning.scheduledEnd), operationalNotes: planning.operationalNotes.trim() || null } }
