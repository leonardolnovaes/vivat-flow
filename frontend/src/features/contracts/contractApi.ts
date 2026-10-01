import { ApiError, request } from '../../api'
import type { Contract, ContractInput, ContractList, ContractStatus } from './types'

async function call<T>(path: string, init?: RequestInit): Promise<T> { const response = await request(path, init); if (!response.ok) throw await ApiError.from(response); return response.json() as Promise<T> }
const payload = (input: ContractInput) => ({ startDate: input.startDate || null, endDate: input.endDate || null, paymentTerms: input.paymentTerms.trim() || null, notes: input.notes.trim() || null })
export const listContracts = (query: URLSearchParams) => call<ContractList>(`/api/contracts?${query}`)
export const getContract = (id: string) => call<Contract>(`/api/contracts/${id}`)
export const createContract = (quoteId: string, input: ContractInput) => call<Contract>('/api/contracts', { method: 'POST', body: JSON.stringify({ quoteId, type: input.type, ...payload(input) }) })
export const updateContract = (id: string, input: ContractInput, expectedVersion: string) => call<Contract>(`/api/contracts/${id}`, { method: 'PUT', body: JSON.stringify({ ...payload(input), expectedVersion }) })
export const transitionContract = (id: string, action: 'activate' | 'end' | 'cancel', expectedVersion: string) => call<Contract>(`/api/contracts/${id}/${action}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) })
export async function findOpenContractForQuote(quoteId: string) { for (const status of ['Draft', 'Active'] as ContractStatus[]) { for (let page = 1; ; page++) { const result = await listContracts(new URLSearchParams({ page: String(page), pageSize: '100', status })); const found = result.items.find(item => item.quoteId === quoteId); if (found) return found; if (page * result.pageSize >= result.totalCount) break } } return null }
