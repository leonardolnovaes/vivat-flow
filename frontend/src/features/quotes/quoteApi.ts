import { ApiError, request } from '../../api'
import type { ClientResponseType, EligibleProfessional, Quote, QuoteHistory, QuoteInput, QuoteList, QuoteSummaryCounts } from './types'
import { normalizeBrlAmount } from './quoteFormat'
async function call<T>(path: string, init?: RequestInit): Promise<T> { const response = await request(path, init); if (!response.ok) throw await ApiError.from(response); return response.json() as Promise<T> }
function payload(input: QuoteInput) { return { ...input, totalAmount: normalizeBrlAmount(input.totalAmount), paymentType: input.paymentType || null, installmentCount: input.installmentCount.trim() ? Number(input.installmentCount) : null, employeeCount: input.employeeCount?.trim() ? Number(input.employeeCount) : null, riskDegree: input.riskDegree || null, serviceUnitId: input.serviceUnitId || null, notes: input.notes || null } }
function quoteBody(input: QuoteInput, expectedVersion?: string) { const value = payload(input); const { totalAmount, ...rest } = value; return `{${Object.entries({ ...rest, ...(expectedVersion ? { expectedVersion } : {}) }).map(([key, item]) => `${JSON.stringify(key)}:${JSON.stringify(item)}`).join(',')},"totalAmount":${totalAmount === null ? 'null' : totalAmount}}` }
export function listQuotes(query: URLSearchParams) { return call<QuoteList>(`/api/quotes?${query}`) }
export function getQuote(id: string) { return call<Quote>(`/api/quotes/${id}`) }
export function getEligibleProfessionals() { return call<EligibleProfessional[]>('/api/quotes/professionals') }
export function getQuoteHistory(id: string) { return call<QuoteHistory[]>(`/api/quotes/${id}/history`) }
export function getQuoteSummary() { return call<QuoteSummaryCounts>('/api/quotes/summary') }
export function getQuoteApprovalValidation(id: string) { return call<{ errors: Record<string, string[]> }>(`/api/quotes/${id}/approval-validation`) }
export function createQuote(input: QuoteInput) { return call<Quote>('/api/quotes', { method: 'POST', body: quoteBody(input) }) }
export function updateQuote(id: string, input: QuoteInput, expectedVersion: string) { if (!expectedVersion) throw new Error('Quote version is required for updates.'); return call<Quote>(`/api/quotes/${id}`, { method: 'PUT', body: quoteBody(input, expectedVersion) }) }
export function sendForApproval(id: string, expectedVersion: string, input: { recipientContactIds: string[]; validUntil: string }) { return call<Quote>(`/api/quotes/${id}/submit`, { method: 'POST', body: JSON.stringify({ expectedVersion, recipientContactIds: input.recipientContactIds, validUntil: input.validUntil ? new Date(`${input.validUntil}T23:59:59`).toISOString() : null }) }) }
export function registerClientResponse(id: string, expectedVersion: string, responseType: ClientResponseType, notes: string) { return call<Quote>(`/api/quotes/${id}/client-response`, { method: 'POST', body: JSON.stringify({ expectedVersion, responseType, notes: notes || null }) }) }
export function transitionQuote(id: string, action: 'reopen' | 'cancel' | 'expire', expectedVersion: string) { return call<Quote>(`/api/quotes/${id}/${action}`, { method: 'POST', body: JSON.stringify({ expectedVersion }) }) }
export function createQuoteVisit(id: string, input: { assignedUserId: string; scheduledStart: string; scheduledEnd: string | null; customerUnitId: string | null; notes: string }) { return call<Quote>(`/api/quotes/${id}/visits`, { method: 'POST', body: JSON.stringify(input) }) }
export function rescheduleQuoteVisit(id: string, visitId: string, input: { scheduledStart: string; scheduledEnd: string | null; notes: string }) { return call<Quote>(`/api/quotes/${id}/visits/${visitId}/reschedule`, { method: 'POST', body: JSON.stringify(input) }) }
export function changeQuoteVisit(id: string, visitId: string, action: 'complete' | 'cancel') { return call<Quote>(`/api/quotes/${id}/visits/${visitId}/${action}`, { method: 'POST', body: '{}' }) }
