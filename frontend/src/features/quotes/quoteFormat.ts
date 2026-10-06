import { formatChangedFields } from '../../components/auditPresentation'

const quoteAuditFieldLabels: Readonly<Record<string, string>> = {
  Customer: 'Cliente',
  TotalAmount: 'Valor total',
  PaymentType: 'Condição de pagamento',
  InstallmentCount: 'Quantidade de parcelas',
  EmployeeCount: 'Quantidade de funcionários',
  RiskDegree: 'Grau de risco',
  ServiceAddress: 'Local do serviço',
  Notes: 'Observações',
  Items: 'Serviços'
}

export function formatQuoteChangedFields(changedFields: string): string {
  return formatChangedFields(changedFields, quoteAuditFieldLabels, 'Outros campos do orçamento')
}

export function normalizeBrlAmount(value: string): string | null {
  const raw = value.trim().replace(/^R\$\s*/, '')
  if (!raw) return null
  const compact = raw.replace(/\s/g, '')
  const normalized = compact.includes(',') ? compact.replace(/\./g, '').replace(',', '.') : /^(?:\d{1,3}\.)+\d{3}$/.test(compact) ? compact.replace(/\./g, '') : compact
  return /^\d+(?:\.\d{1,2})?$/.test(normalized) ? normalized : null
}

export function formatBrlInput(value: string): string {
  const normalized = normalizeBrlAmount(value)
  if (normalized === null) return value.trim() ? value : ''
  const [integer, fraction = ''] = normalized.split('.')
  return `R$ ${integer.replace(/\B(?=(\d{3})+(?!\d))/g, '.')},${fraction.padEnd(2, '0')}`
}
