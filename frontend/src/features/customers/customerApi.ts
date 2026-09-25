import { ApiError, request } from '../../api'
import type { ContactInput, Customer, CustomerInput, CustomerList, UnitInput } from './types'

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init)
  if (!response.ok) throw await ApiError.from(response)
  return response.json() as Promise<T>
}

export function listCustomers(query: URLSearchParams) { return call<CustomerList>(`/api/customers?${query}`) }
export function getCustomer(id: string) { return call<Customer>(`/api/customers/${id}`) }
export function createCustomer(input: CustomerInput) { return call<Customer>('/api/customers', { method: 'POST', body: JSON.stringify(input) }) }
export function updateCustomer(id: string, input: CustomerInput, expectedVersion: string) { return call<Customer>(`/api/customers/${id}`, { method: 'PUT', body: JSON.stringify({ ...input, expectedVersion }) }) }
export function changeCustomerStatus(id: string, active: boolean, expectedVersion: string) { return call<Customer>(`/api/customers/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) }) }
export function createContact(customerId: string, input: ContactInput, expectedVersion: string) { return call(`/api/customers/${customerId}/contacts`, { method: 'POST', body: JSON.stringify({ ...input, expectedVersion }) }) }
export function updateContact(customerId: string, contactId: string, input: ContactInput, expectedVersion: string) { return call(`/api/customers/${customerId}/contacts/${contactId}`, { method: 'PUT', body: JSON.stringify({ ...input, expectedVersion }) }) }
export function changeContactStatus(customerId: string, contactId: string, active: boolean, expectedVersion: string) { return call(`/api/customers/${customerId}/contacts/${contactId}/${active ? 'activate' : 'deactivate'}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) }) }
export function createUnit(customerId: string, input: UnitInput, expectedVersion: string) { return call(`/api/customers/${customerId}/units`, { method: 'POST', body: JSON.stringify({ ...input, expectedVersion }) }) }
export function updateUnit(customerId: string, unitId: string, input: UnitInput, expectedVersion: string) { return call(`/api/customers/${customerId}/units/${unitId}`, { method: 'PUT', body: JSON.stringify({ ...input, expectedVersion }) }) }
export function changeUnitStatus(customerId: string, unitId: string, active: boolean, expectedVersion: string) { return call(`/api/customers/${customerId}/units/${unitId}/${active ? 'activate' : 'deactivate'}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) }) }
