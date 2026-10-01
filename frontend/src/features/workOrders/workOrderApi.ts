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
import { ApiError, request } from '../../api'
import type { EligibleAssignee, Planning, WorkOrder, WorkOrderHistory, WorkOrderList, WorkOrderSourceDetail, WorkOrderSourceList, WorkOrderSource } from './types'

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init)
  if (!response.ok) throw await ApiError.from(response)
  return response.json() as Promise<T>
}

export const listWorkOrders = (query: URLSearchParams) => call<WorkOrderList>(`/api/work-orders?${query}`)
export const getWorkOrder = (id: string) => call<WorkOrder>(`/api/work-orders/${id}`)
export const getWorkOrderHistory = (id: string) => call<WorkOrderHistory[]>(`/api/work-orders/${id}/history`)
export const getEligibleAssignees = () => call<EligibleAssignee[]>('/api/work-orders/eligible-assignees')
export const listWorkOrderSources = (query: URLSearchParams) => call<WorkOrderSourceList>(`/api/work-orders/sources?${query}`)
export const getWorkOrderSource = (type: WorkOrderSource, id: string) => call<WorkOrderSourceDetail>(`/api/work-orders/sources/${type.toLowerCase()}/${id}`)
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
