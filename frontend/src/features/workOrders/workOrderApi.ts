import { planningPayload } from './planning'
import { ApiError, request } from '../../api'
import type { AgendaEntry, EligibleAssignee, Planning, WorkOrder, WorkOrderHistory, WorkOrderList, WorkOrderSourceDetail, WorkOrderSourceList, WorkOrderSource } from './types'

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
export const createWorkOrder = (contractId: string, planning: Planning) => call<WorkOrder>('/api/work-orders/from-contract', { method: 'POST', body: JSON.stringify({ sourceId: contractId, ...planningPayload(planning) }) })
export const saveWorkOrderPlanning = (id: string, version: string, planning: Planning) => call<WorkOrder>(`/api/work-orders/${id}/planning`, { method: 'PUT', body: JSON.stringify({ expectedVersion: version, ...planningPayload(planning) }) })
export const transitionWorkOrder = (id: string, action: 'schedule' | 'start' | 'complete' | 'cancel', version: string, completionNotes?: string, cancellationReason?: string) => call<WorkOrder>(`/api/work-orders/${id}/${action}`, { method: 'POST', body: JSON.stringify({ expectedVersion: version, ...(action === 'complete' ? { completionNotes: completionNotes?.trim() || null } : action === 'cancel' ? { cancellationReason: cancellationReason?.trim() || null } : {}) }) })

export const getAgenda = (query: URLSearchParams) => call<AgendaEntry[]>(`/api/work-orders/agenda?${query}`)
