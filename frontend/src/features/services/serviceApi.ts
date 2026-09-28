import { ApiError, request } from '../../api'
import type { Service, ServiceInput, ServiceList } from './types'

async function call<T>(path: string, init?: RequestInit): Promise<T> { const response = await request(path, init); if (!response.ok) throw await ApiError.from(response); return response.json() as Promise<T> }
export function listServices(query: URLSearchParams) { return call<ServiceList>(`/api/services?${query}`) }
export function getService(id: string) { return call<Service>(`/api/services/${id}`) }
export function createService(input: ServiceInput) { return call<Service>('/api/services', { method: 'POST', body: JSON.stringify(toRequest(input)) }) }
export function updateService(id: string, input: ServiceInput, expectedVersion: string) { return call<Service>(`/api/services/${id}`, { method: 'PUT', body: JSON.stringify({ ...toRequest(input), expectedVersion }) }) }
export function changeServiceStatus(id: string, active: boolean, expectedVersion: string) { return call<Service>(`/api/services/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) }) }
function toRequest(input: ServiceInput) { return { code: input.code, name: input.name, description: input.description || null, basePrice: input.basePrice.trim() ? Number(input.basePrice.replace(',', '.')) : null } }
