import type { Quote, QuoteInput, QuoteItem } from './types'

export function editState(quote: Quote): { form: QuoteInput; version: string; existingItems: Record<string, QuoteItem> } {
  return {
    form: {
      customerId: quote.customerId,
      items: quote.items.map(item => ({ id: item.id, serviceId: item.serviceId })),
      totalAmount: quote.totalAmount?.toString() ?? '',
      paymentType: quote.paymentType ?? '',
      installmentCount: quote.installmentCount?.toString() ?? '',
      employeeCount: quote.employeeCount?.toString() ?? '',
      riskDegree: quote.riskDegree ?? '',
      serviceUnitId: quote.serviceUnitId ?? '',
      responsibleUserId: quote.responsibleUserId ?? '',
      notes: quote.notes ?? '',
    },
    version: quote.version,
    existingItems: Object.fromEntries(quote.items.map(item => [item.id, item])),
  }
}
