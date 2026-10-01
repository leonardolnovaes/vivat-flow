import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { QuoteDetailView, QuoteWorkspace } from './QuoteCommercialViews'
import { getCustomer, listCustomers } from '../customers/customerApi'
import { QuickCustomerDialog } from './QuickCustomerDialog'
import type { CustomerSummary, Unit } from '../customers/types'
import { listServiceLines, listServices } from '../services/serviceApi'
import type { ServiceLine, ServiceSummary } from '../services/types'
import { createQuote, getEligibleProfessionals, getQuote, getQuoteApprovalValidation, getQuoteSummary, listQuotes, sendForApproval, updateQuote } from './quoteApi'
import { editState } from './quoteEdit'
import type { EligibleProfessional, PaymentType, Quote, QuoteInput, QuoteItem, QuoteList, QuoteStatus, RiskDegree } from './types'

type Props = { path: string; go: (path: string, replace?: boolean) => void; onSessionExpired: () => void }
type Errors = Record<string, string[]>

const blank: QuoteInput = { customerId: '', items: [], totalAmount: '', paymentType: '', installmentCount: '', employeeCount: '', riskDegree: '', serviceUnitId: '', responsibleUserId: '', notes: '' }
const statuses: Record<QuoteStatus, string> = { Draft: 'Rascunho', AwaitingApproval: 'Aguardando aprovação', Approved: 'Aprovado', Rejected: 'Recusado', Cancelled: 'Cancelado' }
const risks: Record<RiskDegree, string> = { One: 'Grau 1', Two: 'Grau 2', Three: 'Grau 3', Four: 'Grau 4' }
Object.assign(statuses, { ChangesRequested: 'Alterações solicitadas', Expired: 'Expirado', Rejected: 'Reprovado' })

export function QuotesRoutes(props: Props) {
  if (props.path === '/orcamentos') return <QuoteWorkspace {...props} />
  if (props.path === '/orcamentos/novo') return <Form {...props} />
  const edit = props.path.match(/^\/orcamentos\/([^/]+)\/editar$/)
  const detail = props.path.match(/^\/orcamentos\/([^/]+)$/)
  return edit ? <Form {...props} id={edit[1]} /> : detail ? <QuoteDetailView {...props} id={detail[1]} /> : <section className="card"><h2>Página não encontrada</h2></section>
}

export function List({ go, onSessionExpired }: Props) {
  const [data, setData] = useState<QuoteList | null>(null)
  const [summary, setSummary] = useState<{ draft: number; awaitingApproval: number; changesRequested: number; approved: number } | null>(null)
  const [services, setServices] = useState<ServiceSummary[]>([])
  const [filters, setFilters] = useState({ search: '', status: '', serviceId: '', createdFrom: '', createdTo: '', awaitingResponse: false, expired: false })
  const [error, setError] = useState('')
  useEffect(() => { void getQuoteSummary().then(setSummary).catch(() => setSummary(null)) }, [])
  useEffect(() => { void listServices(new URLSearchParams({ page: '1', pageSize: '100', isActive: 'true' })).then(result => setServices(result.items)).catch(() => setServices([])) }, [])
  useEffect(() => { void listQuotes(new URLSearchParams({ page: '1', pageSize: '25' })).then(setData).catch(x => setError(message(x, onSessionExpired, 'Não foi possível carregar os orçamentos.'))) }, [onSessionExpired])
  useEffect(() => { const timer = window.setTimeout(() => { const query = new URLSearchParams({ page: '1', pageSize: '25' }); if (filters.search) query.set('search', filters.search); if (filters.status) query.set('status', filters.status); if (filters.serviceId) query.set('serviceId', filters.serviceId); if (filters.createdFrom) query.set('createdFrom', `${filters.createdFrom}T00:00:00Z`); if (filters.createdTo) query.set('createdTo', `${filters.createdTo}T23:59:59Z`); if (filters.awaitingResponse) query.set('awaitingResponse', 'true'); if (filters.expired) query.set('expired', 'true'); void listQuotes(query).then(setData).catch(x => setError(message(x, onSessionExpired, 'Não foi possível carregar os orçamentos.'))) }, 250); return () => window.clearTimeout(timer) }, [filters, onSessionExpired])
  const clearFilters = () => setFilters({ search: '', status: '', serviceId: '', createdFrom: '', createdTo: '', awaitingResponse: false, expired: false })
  const hasFilters = Boolean(filters.search || filters.status || filters.serviceId || filters.createdFrom || filters.createdTo || filters.awaitingResponse || filters.expired)
  return <>
    {summary && <section className="quote-summary" aria-label="Resumo comercial"><article><span>Rascunhos</span><strong>{summary.draft}</strong></article><article><span>Aguardando aprovação</span><strong>{summary.awaitingApproval}</strong></article><article><span>Alterações solicitadas</span><strong>{summary.changesRequested}</strong></article><article><span>Aprovados</span><strong>{summary.approved}</strong></article></section>}
    <div className="page-title"><h2>Orçamentos</h2><button onClick={() => go('/orcamentos/novo')}>Novo orçamento</button></div>
    {error ? <p className="error">{error}</p> : !data ? <LoadingState /> : <section className="card table-wrap"><table><thead><tr><th>Número</th><th>Cliente</th><th>Status</th><th>Ações</th></tr></thead><tbody>{data.items.map(quote => <tr key={quote.id}><td>{quote.number}</td><td>{quote.customerLegalNameSnapshot}</td><td>{statuses[quote.status]}</td><td><button className="secondary" onClick={() => go(`/orcamentos/${quote.id}`)}>Ver detalhes</button></td></tr>)}</tbody></table></section>}
    <section className="card quote-filters" aria-label="Filtros de orçamentos"><div className="filter-heading"><h3>Filtros</h3>{hasFilters && <button className="link-button" onClick={clearFilters}>Limpar filtros</button>}</div><label>Buscar por número ou cliente<input value={filters.search} onChange={event => setFilters(current => ({ ...current, search: event.target.value }))} /></label><label>Status<select value={filters.status} onChange={event => setFilters(current => ({ ...current, status: event.target.value }))}><option value="">Todos</option>{Object.entries(statuses).map(([key, value]) => <option key={key} value={key}>{value}</option>)}</select></label><label>Serviço<select value={filters.serviceId} onChange={event => setFilters(current => ({ ...current, serviceId: event.target.value }))}><option value="">Todos</option>{services.map(service => <option key={service.id} value={service.id}>{service.code} · {service.name}</option>)}</select></label><label>Criação a partir de<input type="date" value={filters.createdFrom} onChange={event => setFilters(current => ({ ...current, createdFrom: event.target.value }))} /></label><label>Criação até<input type="date" value={filters.createdTo} onChange={event => setFilters(current => ({ ...current, createdTo: event.target.value }))} /></label><label className="checkbox"><input type="checkbox" checked={filters.awaitingResponse} onChange={event => setFilters(current => ({ ...current, awaitingResponse: event.target.checked }))} />Aguardando retorno</label><label className="checkbox"><input type="checkbox" checked={filters.expired} onChange={event => setFilters(current => ({ ...current, expired: event.target.checked }))} />Expirados</label></section>
  </>
}

function Form({ id, go, onSessionExpired }: Props & { id?: string }) {
  const [form, setForm] = useState<QuoteInput>(blank)
  const [version, setVersion] = useState('')
  const [customerResults, setCustomerResults] = useState<CustomerSummary[]>([])
  const [selectedCustomer, setSelectedCustomer] = useState<CustomerSummary | null>(null)
  const [query, setQuery] = useState('')
  const [services, setServices] = useState<ServiceSummary[]>([])
  const [serviceLines, setServiceLines] = useState<ServiceLine[]>([])
  const [existingItems, setExistingItems] = useState<Record<string, QuoteItem>>({})
  const [professionals, setProfessionals] = useState<EligibleProfessional[]>([])
  const [units, setUnits] = useState<Unit[]>([])
  const [unitsLoaded, setUnitsLoaded] = useState(false)
  const [errors, setErrors] = useState<Errors>({})
  const [notice, setNotice] = useState('')
  const [quick, setQuick] = useState(false)
  const [completingCustomer, setCompletingCustomer] = useState(false)
  const [pending, setPending] = useState(false)
  const [loadingQuote, setLoadingQuote] = useState(Boolean(id))
  const [stale, setStale] = useState(false)
  const summary = useRef<HTMLDivElement>(null)

  const loadUnits = async (customerId: string) => {
    setUnitsLoaded(false)
    if (!customerId) { setUnits([]); setUnitsLoaded(true); return }
    const customer = await getCustomer(customerId)
    setUnits(customer.units.filter(unit => unit.isActive))
    setUnitsLoaded(true)
  }

  useEffect(() => {
    void Promise.all([listServices(new URLSearchParams({ page: '1', pageSize: '100', isActive: 'true' })), listServiceLines()])
      .then(([result, lines]) => { setServices(result.items); setServiceLines(lines) })
      .catch(error => setNotice(message(error, onSessionExpired, 'Não foi possível carregar os serviços.')))
  }, [onSessionExpired])
  useEffect(() => { void getEligibleProfessionals().then(setProfessionals).catch(error => setNotice(message(error, onSessionExpired, 'Não foi possível carregar os profissionais.'))) }, [onSessionExpired])

  useEffect(() => {
    if (!query.trim()) return
    const timer = window.setTimeout(() => {
      void listCustomers(new URLSearchParams({ page: '1', pageSize: '100', isActive: 'true', search: query }))
        .then(result => setCustomerResults(result.items))
        .catch(() => setCustomerResults([]))
    }, 180)
    return () => window.clearTimeout(timer)
  }, [query])

  const reloadQuote = useCallback(async () => {
    if (!id) return
    setLoadingQuote(true)
    setVersion('')
    try {
      const quote = await getQuote(id)
      if (quote.status !== 'Draft') { go(`/orcamentos/${id}`, true); return }
      const customer = await getCustomer(quote.customerId)
      const state = editState(quote)
      setSelectedCustomer(customer)
      setForm(state.form)
      setExistingItems(state.existingItems)
      setVersion(state.version)
      setUnits(customer.units.filter(unit => unit.isActive))
      setUnitsLoaded(true)
      setErrors({})
      setStale(false)
      setNotice('')
    } catch (error) { setNotice(message(error, onSessionExpired, 'Não foi possível carregar o formulário.')) }
    finally { setLoadingQuote(false) }
  }, [id, onSessionExpired, go])
  useEffect(() => { if (id) void Promise.resolve().then(reloadQuote) }, [id, reloadQuote])

  const choose = async (customer: CustomerSummary, unitId = '') => {
    setForm(current => ({ ...current, customerId: customer.id, serviceUnitId: unitId }))
    setSelectedCustomer(customer)
    setQuery('')
    setErrors(current => {
      const { customerId: _customerId, serviceUnitId: _serviceUnitId, ...rest } = current
      return rest
    })
    try { await loadUnits(customer.id) } catch { setUnits([]); setUnitsLoaded(true) }
  }

  const clearCustomer = () => {
    setForm(current => ({ ...current, customerId: '', serviceUnitId: '' }))
    setSelectedCustomer(null)
    setUnits([])
    setUnitsLoaded(true)
    setErrors(current => {
      const { customerId: _customerId, serviceUnitId: _serviceUnitId, ...rest } = current
      return rest
    })
  }

  const save = async (event: FormEvent) => {
    event.preventDefault()
    if (pending || stale || (id && !version)) return
    setPending(true); setErrors({}); setNotice('')
    try {
      const quote = id ? await updateQuote(id, form, version) : await createQuote(form)
      go(`/orcamentos/${quote.id}`, true)
    }
    catch (error) {
      if (error instanceof ApiError && error.status === 409) setStale(true)
      else if (error instanceof ApiError && Object.keys(error.errors).length) { setErrors(error.errors); requestAnimationFrame(() => summary.current?.focus()) }
      else setNotice(message(error, onSessionExpired, 'Não foi possível salvar o orçamento.'))
    } finally { setPending(false) }
  }

  const enabledServiceLineIds = new Set(serviceLines.map(line => line.id))
  const selectableServices = services.filter(service => enabledServiceLineIds.has(service.serviceLineId))
  const servicesByLine = serviceLines.map(line => ({ line, services: selectableServices.filter(service => service.serviceLineId === line.id) })).filter(group => group.services.length > 0)

  return <section className="card form-card quote-form-card">
    <h2>{id ? 'Editar orçamento' : 'Novo orçamento'}</h2>
    {notice && <p className="notice" role="status">{notice}</p>}
    {stale && <div className="error-panel" role="alert"><p>Este orçamento foi alterado. Carregue os dados mais recentes antes de salvar. As alterações não salvas serão substituídas.</p><button type="button" className="secondary" disabled={loadingQuote} onClick={() => void reloadQuote()}>{loadingQuote ? 'Carregando...' : 'Carregar dados recentes'}</button></div>}
    {Object.keys(errors).length > 0 && <ValidationSummary errors={errors} reference={summary} title="Corrija os seguintes campos:" />}
    <form className="quote-edit-form" onSubmit={save}>
      <section className="quote-form-grid" aria-label="Dados do orçamento">
        <div className="quote-customer-row"><Field label="Cliente *" error={errors.customerId}><CustomerSearch query={query} setQuery={setQuery} results={customerResults} selected={selectedCustomer} choose={choose} clear={clearCustomer} /></Field><button type="button" className="secondary quick-customer" onClick={() => { setCompletingCustomer(false); setQuick(true) }}>+ Cadastrar cliente rapidamente</button></div>
        <Field label="Responsável pelo orçamento" error={errors.responsibleUserId}><select value={form.responsibleUserId} onChange={event => setForm(current => ({ ...current, responsibleUserId: event.target.value }))}><option value="">Não definido</option>{professionals.map(person => <option key={person.id} value={person.id}>{person.fullName} · {person.email}</option>)}</select></Field>
        <Field label="Unidade/local do serviço" error={errors.serviceUnitId}><select value={form.serviceUnitId} disabled={!form.customerId || !units.length} onChange={event => setForm(current => ({ ...current, serviceUnitId: event.target.value }))}><option value="">{!form.customerId ? 'Selecione um cliente primeiro' : units.length ? 'Selecione' : 'Nenhuma unidade ativa'}</option>{units.map(unit => <option key={unit.id} value={unit.id}>{unit.name} · {address(unit)}</option>)}</select></Field>
        <Field label="Quantidade de funcionários" error={errors.employeeCount}><input type="number" min="1" step="1" value={form.employeeCount} onChange={event => setForm(current => ({ ...current, employeeCount: event.target.value }))} /></Field>
        <Field label="Grau de risco" error={errors.riskDegree}><select value={form.riskDegree} onChange={event => setForm(current => ({ ...current, riskDegree: event.target.value as RiskDegree | '' }))}><option value="">Selecione</option>{Object.entries(risks).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></Field>
        <Field className="quote-add-service" label="Adicionar serviço *" error={errors.items}><select defaultValue="" disabled={serviceLines.length === 0 || servicesByLine.length === 0} onChange={event => { const serviceId = event.target.value; if (serviceId && !form.items.some(item => item.serviceId === serviceId)) setForm(current => ({ ...current, items: [...current.items, { serviceId }] })); event.target.value = '' }}><option value="">{serviceLines.length === 0 ? 'Nenhuma linha de serviço habilitada' : servicesByLine.length === 0 ? 'Nenhum serviço ativo disponível' : 'Selecione um serviço ativo'}</option>{servicesByLine.map(({ line, services: lineServices }) => <optgroup key={line.id} label={`${line.code} · ${line.name}`}>{lineServices.map(service => <option key={service.id} value={service.id}>{service.code} · {service.name}</option>)}</optgroup>)}</select></Field>
      </section>
      {form.customerId && unitsLoaded && !units.length && <div className="unit-empty" role="status"><span>Este cliente não possui uma unidade ativa cadastrada. O rascunho pode ser salvo.</span><button type="button" className="secondary" onClick={() => { setCompletingCustomer(true); setQuick(true) }}>Completar cadastro do cliente</button></div>}
      <section className="selected-services" aria-label="Serviços adicionados"><h3>Serviços adicionados</h3>{form.items.length === 0 ? <p>Nenhum serviço adicionado.</p> : <div className="selected-service-grid">{form.items.map(item => { const service = services.find(candidate => candidate.id === item.serviceId); const historical = item.id ? existingItems[item.id] : undefined; return <div className="selected-service" key={item.id ?? item.serviceId}><div><strong>{service?.name ?? historical?.serviceNameSnapshot ?? 'Serviço histórico'}</strong><small>{service?.code ?? historical?.serviceCodeSnapshot ?? ''}</small><small className="service-line-label">{service?.serviceLineName ?? historical?.serviceLineName ?? 'Linha de serviço histórica'}</small></div><button className="secondary" type="button" onClick={() => setForm(current => ({ ...current, items: current.items.filter(candidate => candidate !== item) }))}>Remover</button></div> })}</div>}</section>
      <section className="quote-form-grid commercial-fields" aria-label="Condições comerciais"><Field label="Valor total (R$)" error={errors.totalAmount}><input value={form.totalAmount} onChange={event => setForm(current => ({ ...current, totalAmount: event.target.value }))} /></Field><Field label="Condição de pagamento" error={errors.paymentType}><select value={form.paymentType} onChange={event => setForm(current => ({ ...current, paymentType: event.target.value as PaymentType | '' }))}><option value="">Não definida</option><option value="Cash">À vista</option><option value="Installments">Parcelado</option></select></Field>{form.paymentType === 'Installments' && <Field label="Quantidade de parcelas" error={errors.installmentCount}><input value={form.installmentCount} onChange={event => setForm(current => ({ ...current, installmentCount: event.target.value }))} /></Field>}</section>
      <Field className="quote-notes" label="Observações" error={errors.notes}><textarea value={form.notes} onChange={event => setForm(current => ({ ...current, notes: event.target.value }))} /></Field>
      <div className="actions quote-form-actions"><button type="button" className="secondary" onClick={() => go('/orcamentos')}>Cancelar</button><button disabled={pending || stale || loadingQuote || (Boolean(id) && !version)}>{pending ? 'Salvando...' : 'Salvar rascunho'}</button></div>
    </form>
    {quick && <QuickCustomerDialog customerId={completingCustomer ? form.customerId : undefined} close={() => setQuick(false)} saved={(customer, unitId) => { void choose(customer, unitId); setQuick(false); setNotice('Dados do cliente salvos com sucesso.') }} />}
  </section>
}

function CustomerSearch({ query, setQuery, results, selected, choose, clear }: { query: string; setQuery: (value: string) => void; results: CustomerSummary[]; selected: CustomerSummary | null; choose: (customer: CustomerSummary) => void; clear: () => void }) {
  const [editing, setEditing] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const display = selected && !editing ? customerLabel(selected) : query
  const startEditing = () => {
    setEditing(true)
    setQuery('')
    requestAnimationFrame(() => input.current?.focus())
  }
  const change = (value: string) => {
    setQuery(value)
    if (!value) clear()
  }
  const select = (customer: CustomerSummary) => {
    setEditing(false)
    void choose(customer)
  }
  return <div className="customer-search"><input ref={input} aria-label="Buscar cliente por razão social, nome fantasia ou CNPJ" role="combobox" aria-expanded={Boolean(query.trim())} aria-controls="customer-results" placeholder="Buscar cliente por razão social, nome fantasia ou CNPJ" value={display} onChange={event => change(event.target.value)} />{selected && <button type="button" className="link-button" onClick={startEditing}>Alterar</button>}{query.trim() && <div id="customer-results" className="customer-results" role="listbox">{results.length ? results.map(customer => <button type="button" role="option" key={customer.id} onClick={() => select(customer)}><strong>{customer.legalName}</strong><small>{formatCnpj(customer.cnpj)} · {customer.isComplete ? 'Cadastro completo' : 'Cadastro incompleto'}</small></button>) : <p>Nenhum cliente ativo encontrado.</p>}</div>}</div>
}

export function Detail({ id, go, onSessionExpired }: Props & { id: string }) {
  const [quote, setQuote] = useState<Quote | null>(null); const [errors, setErrors] = useState<Errors>({}); const [error, setError] = useState(''); const [pending, setPending] = useState(false); const summary = useRef<HTMLDivElement>(null)
  useEffect(() => { void Promise.all([getQuote(id), getQuoteApprovalValidation(id)]).then(([loaded, validation]) => { setQuote(loaded); setErrors(validation.errors) }).catch(reason => setError(message(reason, onSessionExpired, 'Não foi possível carregar o orçamento.'))) }, [id, onSessionExpired])
  const submit = async () => { if (!quote) return; setPending(true); try { const updated = await sendForApproval(id, quote.version, { recipientContactIds: [], validUntil: '' }); setQuote(updated); setErrors({}) } catch (reason) { if (reason instanceof ApiError && Object.keys(reason.errors).length) { setErrors(reason.errors); requestAnimationFrame(() => summary.current?.focus()) } else setError(message(reason, onSessionExpired, 'Não foi possível atualizar o orçamento.')) } finally { setPending(false) } }
  if (!quote) return <section className="card">{error ? <p className="error" role="alert">{error}</p> : <LoadingState size="lg" />}</section>
  const customerIncomplete = errors.contact || errors.unit
  return <><div className="page-title"><div><h2>{quote.number}</h2><p>{statuses[quote.status]}</p></div>{quote.status === 'Draft' && <div className="actions"><button className="secondary" onClick={() => go(`/orcamentos/${id}/editar`)}>Editar</button><button disabled={pending} onClick={() => void submit()}>Enviar para aprovação</button></div>}</div><section className="card service-detail-card"><DetailSection title="Cliente"><dl className="service-detail-grid"><Info label="Razão social" value={quote.customerLegalNameSnapshot} /><Info label="CNPJ" value={formatCnpj(quote.customerCnpjSnapshot)} /><Info label="Cadastro" value={customerIncomplete ? 'Cadastro incompleto' : 'Cadastro completo'} /><Info label="Unidade/local do serviço" value={quote.serviceAddressSnapshot ?? 'Não informado'} /></dl></DetailSection><DetailSection title="Contexto comercial"><dl className="service-detail-grid"><Info label="Quantidade de funcionários" value={quote.employeeCount?.toString() ?? 'Não informado'} /><Info label="Grau de risco" value={quote.riskDegree ? risks[quote.riskDegree] : 'Não informado'} /><Info label="Endereço/local do serviço" value={quote.serviceAddressSnapshot ?? 'Não informado'} /></dl></DetailSection><DetailSection title="Serviços"><ul className="quote-service-list">{quote.items.length ? quote.items.map(item => <li key={item.id}>{item.serviceNameSnapshot}</li>) : <li>Nenhum serviço informado.</li>}</ul></DetailSection><DetailSection title="Condições comerciais"><dl className="service-detail-grid"><Info label="Valor total" value={quote.totalAmount === null ? 'Não informado' : brl(quote.totalAmount)} /><Info label="Condição de pagamento" value={quote.paymentType === 'Cash' ? 'À vista' : quote.paymentType === 'Installments' ? 'Parcelado' : 'Não informado'} />{quote.paymentType === 'Installments' && <Info label="Parcelas" value={quote.installmentCount?.toString() ?? 'Não informado'} />}</dl></DetailSection><DetailSection title="Observações"><p>{quote.notes || 'Nenhuma observação informada.'}</p></DetailSection>{quote.status === 'Draft' && Object.keys(errors).length > 0 && <div className="error-panel" ref={summary} tabIndex={-1} role="alert"><p><strong>Orçamento incompleto</strong></p><p>Ainda faltam informações para envio à aprovação.</p><ul>{Object.values(errors).flat().map(item => <li key={item}>{item}</li>)}</ul><button className="secondary" onClick={() => go(`/orcamentos/${id}/editar`)}>Corrigir dados</button></div>}{error && <p className="error">{error}</p>}</section></>
}

function ValidationSummary({ errors, reference, title }: { errors: Errors; reference: React.RefObject<HTMLDivElement | null>; title: string }) { return <div className="error-panel" tabIndex={-1} ref={reference} role="alert"><p><strong>{title}</strong></p><ul>{Object.values(errors).flat().map(error => <li key={error}>{error}</li>)}</ul></div> }
function DetailSection({ title, children }: { title: string; children: ReactNode }) { return <section className="quote-detail-section"><h3>{title}</h3>{children}</section> }
function Info({ label, value }: { label: string; value: string }) { return <div className="service-detail-item"><dt>{label}</dt><dd>{value}</dd></div> }
function Field({ className, label, error, children }: { className?: string; label: string; error?: string[]; children: ReactNode }) { return <label className={className}>{label}{children}{error?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}</label> }
function customerLabel(customer: CustomerSummary) { return `${customer.legalName} · ${formatCnpj(customer.cnpj)}` }
function address(unit: Unit) { return [unit.street, unit.number, unit.district, `${unit.city}/${unit.stateCode}`].filter(Boolean).join(', ') }
function formatCnpj(value: string) { return value.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5') }
function brl(value: number) { return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value) }
function message(error: unknown, expired: () => void, fallback: string) { if (error instanceof ApiError) { if (error.status === 401) { expired(); return 'Sua sessão expirou.' } if (error.status === 403) return 'Você não tem permissão para acessar Orçamentos.'; return error.message || fallback } return fallback }
