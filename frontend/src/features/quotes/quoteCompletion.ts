import { editState } from './quoteEdit'
import type { Quote, QuoteInput } from './types'

export function completionActions(errors: Record<string, string[]>): { customer: boolean; quote: boolean } {
  const keys = Object.keys(errors)
  const customerKeys = new Set(['contact', 'unit'])
  return {
    customer: keys.some(key => customerKeys.has(key)),
    quote: keys.some(key => !customerKeys.has(key)),
  }
}

export class QuoteCompletionRefreshError extends Error {}

export type QuoteCompletionResult = {
  status: 'saved' | 'conflict' | 'not-draft'
  quote: Quote
  errors: Record<string, string[]>
}

export async function synchronizeCustomerCompletion(
  quote: Quote,
  serviceUnitId: string | undefined,
  api: {
    updateQuote: (id: string, input: QuoteInput, version: string) => Promise<Quote>
    getQuote: (id: string) => Promise<Quote>
    getApprovalValidation: (id: string) => Promise<{ errors: Record<string, string[]> }>
    quoteUpdated: (quote: Quote) => void
    refreshed: (quote: Quote, errors: Record<string, string[]>) => void
  },
): Promise<QuoteCompletionResult> {
  const needsUnitLink = Boolean(serviceUnitId && !quote.serviceUnitId)
  let unitLinked = !needsUnitLink
  const refresh = async () => {
    try {
      const [current, approval] = await Promise.all([api.getQuote(quote.id), api.getApprovalValidation(quote.id)])
      api.refreshed(current, approval.errors)
      return { quote: current, errors: approval.errors }
    } catch {
      throw new QuoteCompletionRefreshError('Os dados foram salvos, mas não foi possível atualizar a tela. Tente recarregar.')
    }
  }

  if (needsUnitLink && quote.status === 'Draft') {
    try {
      const form = editState(quote).form
      const updated = await api.updateQuote(quote.id, { ...form, serviceUnitId }, quote.version)
      unitLinked = true
      api.quoteUpdated(updated)
    } catch (caught) {
      if ((caught as { status?: number })?.status !== 409) {
        throw new Error('Os dados do cliente foram salvos, mas não foi possível vincular a unidade ao orçamento. Atualize o orçamento e tente novamente.')
      }
      let current: Awaited<ReturnType<typeof refresh>>
      try {
        current = await refresh()
      } catch {
        throw new QuoteCompletionRefreshError('Os dados do cliente foram salvos. O orçamento mudou, mas não foi possível recarregar seus dados. Recarregue antes de tentar vincular a unidade novamente.')
      }
      return { status: current.quote.status === 'Draft' ? 'conflict' : 'not-draft', ...current }
    }
  }

  const current = await refresh()
  if (!unitLinked) return { status: current.quote.status === 'Draft' ? 'conflict' : 'not-draft', ...current }
  return { status: 'saved', ...current }
}
