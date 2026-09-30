export type ContractStatus = 'Draft' | 'Active' | 'Ended' | 'Cancelled'
export type PaymentType = 'Cash' | 'Installments'
export type ContractItem = { id: string; quoteItemId: string; serviceId: string; serviceCodeSnapshot: string; serviceNameSnapshot: string; serviceLineId: string; serviceLineCodeSnapshot: string; serviceLineNameSnapshot: string; displayOrder: number }
export type ContractSummary = { id: string; quoteId: string; customerId: string; customerLegalNameSnapshot: string; status: ContractStatus; approvedTotalAmount: number; startDate: string | null; endDate: string | null; updatedAtUtc: string }
export type Contract = ContractSummary & { paymentType: PaymentType; installmentCount: number | null; paymentTerms: string | null; notes: string | null; createdAtUtc: string; version: string; items: ContractItem[] }
export type ContractList = { items: ContractSummary[]; page: number; pageSize: number; totalCount: number }
export type ContractInput = { startDate: string; endDate: string; paymentTerms: string; notes: string }
