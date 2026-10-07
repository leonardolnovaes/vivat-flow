import { blankPlanning, calendarPlanningChanged, plannedLabel, validatePlanning } from './planning'
import { Badge, labels } from './WorkOrderStatusBadge'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { Dispatch, FormEvent, ReactNode, SetStateAction } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { WorkOrderSourcePicker } from './WorkOrderSourcePicker'
import { createWorkOrder, getEligibleAssignees, getWorkOrder, getWorkOrderHistory, getWorkOrderSource, listWorkOrders, saveWorkOrderPlanning, transitionWorkOrder } from './workOrderApi'
import type { EligibleAssignee, Planning, WorkOrder, WorkOrderHistory, WorkOrderItem, WorkOrderList, WorkOrderSourceItem } from './types'

type Props = { path: string; go: (path: string, replace?: boolean) => void; onSessionExpired: () => void; user: { id: string; roles: string[]; features: string[] } }
type Errors = Record<string, string[]>
type Source = { id: string; kind: 'contract'; status: string; customer: string; address: string | null; items: WorkOrderSourceItem[]; canCreate: boolean }
const blank = blankPlanning

const events: Record<string, string> = { WORK_ORDER_CREATED_FROM_QUOTE: 'OS criada a partir do orçamento', WORK_ORDER_CREATED_FROM_CONTRACT: 'OS criada a partir do contrato', WORK_ORDER_PLANNING_UPDATED: 'Planejamento atualizado', WORK_ORDER_SCHEDULED: 'OS agendada', WORK_ORDER_STARTED: 'Execução iniciada', WORK_ORDER_EXECUTION_COMPLETED: 'Execução concluída', WORK_ORDER_CLOSED: 'Conclusão confirmada (histórico)', WORK_ORDER_CANCELLED: 'OS cancelada' }
const management = (user: Props['user']) => user.roles.includes('ADMIN') || user.roles.includes('MANAGER')
const date = (value: string | null) => value ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : 'Não informado'
const problem = (error: unknown, expired: () => void, fallback: string) => {
  if (error instanceof ApiError) {
    if (error.status === 401) { expired(); return 'Sua sessão expirou.' }
    if (error.status === 403) return 'Você não tem permissão para acessar esta OS.'
    if (error.status === 404) return 'Esta OS não foi encontrada ou não está disponível para você.'
    if (error.message && error.status < 500) return error.message
  }
  return fallback
}
const planningFrom = (order: WorkOrder): Planning => ({ assignedUserId: order.assignedUserId ?? '', scheduledStartDate: order.scheduledStartDate ?? '', scheduledStartTime: order.scheduledStartTime?.slice(0, 5) ?? '', scheduledEndDate: order.scheduledEndDate ?? '', scheduledEndTime: order.scheduledEndTime?.slice(0, 5) ?? '', operationalNotes: order.operationalNotes ?? '' })

export function WorkOrdersRoutes(props: Props) {
  const [search, setSearch] = useState(location.search)
  useEffect(() => { const listener = () => setSearch(location.search); addEventListener('popstate', listener); return () => removeEventListener('popstate', listener) }, [])
  const detail = props.path.match(/^\/ordens-servico\/([^/]+)$/)
  const query = new URLSearchParams(search)
  if (props.path === '/ordens-servico') return <List {...props} />
  if (props.path === '/ordens-servico/novo') return !management(props.user) ? <Empty title="Acesso restrito" text="A criação de OS está disponível para a gestão." go={props.go} /> : query.has('quoteId') ? <Empty title="Contrato obrigatório" text="Formalize e ative o contrato antes de criar a OS." go={props.go} /> : !query.has('contractId') ? <WorkOrderSourcePicker go={props.go} onSessionExpired={props.onSessionExpired} /> : <Create {...props} quoteId={query.get('quoteId')} contractId={query.get('contractId')} />
  if (detail) return <Detail {...props} id={detail[1]} />
  return <Empty title="Página não encontrada" text="O endereço informado não corresponde a uma OS." go={props.go} />
}

export function RelatedWorkOrderAction({ contractId, eligible, go }: { contractId: string; eligible: boolean; go: Props['go'] }) {
  const [currentOrderId, setCurrentOrderId] = useState<string | null>(null)
  const [canCreate, setCanCreate] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const requestId = useRef(0)
  const load = useCallback(async () => {
    const currentRequest = ++requestId.current
    setLoading(true); setError(false)
    try {
      const result = await getWorkOrderSource('Contract', contractId)
      if (currentRequest !== requestId.current) return
      setCurrentOrderId(result.source.currentWorkOrderId)
      setCanCreate(result.source.canCreate)
    }
    catch { if (currentRequest === requestId.current) setError(true) }
    finally { if (currentRequest === requestId.current) setLoading(false) }
  }, [contractId])
  useEffect(() => { void load() }, [load])
  if (loading) return <LoadingState size="sm" />
  if (error) return <div className="actions"><p>Não foi possível verificar a OS vinculada.</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></div>
  if (currentOrderId) return <button type="button" className="secondary" onClick={() => go(`/ordens-servico/${currentOrderId}`)}>Ver OS</button>
  if (!eligible) return null
  if (!canCreate) return <p className="wo-hint">Finalize a OS em aberto ou verifique o local e os serviços antes de criar uma nova OS.</p>
  return <button type="button" onClick={() => go(`/ordens-servico/novo?contractId=${contractId}`)}>Criar OS</button>
}

function List({ go, onSessionExpired, user }: Props) {
  const manager = management(user)
  const [status, setStatus] = useState(''), [assignee, setAssignee] = useState(''), [page, setPage] = useState(1)
  const [data, setData] = useState<WorkOrderList | null>(null), [people, setPeople] = useState<EligibleAssignee[]>([]), [error, setError] = useState('')
  const requestId = useRef(0)
  const load = useCallback(async () => {
    const current = ++requestId.current
    setData(null); setError('')
    const query = new URLSearchParams({ page: String(page), pageSize: '25' })
    if (status) query.set('status', status)
    if (manager && assignee) query.set('assignedUserId', assignee)
    try { const result = await listWorkOrders(query); if (current === requestId.current) setData(result) }
    catch (caught) { if (current === requestId.current) setError(problem(caught, onSessionExpired, 'Não foi possível carregar as ordens de serviço.')) }
  }, [page, status, assignee, manager, onSessionExpired])
  useEffect(() => { void load() }, [load])
  useEffect(() => { if (manager) void getEligibleAssignees().then(setPeople).catch(() => setPeople([])) }, [manager])
  const filtered = Boolean(status || assignee)
  return <><div className="page-title"><div><p className="eyebrow">Operação</p><h2>Ordens de Serviço</h2><p>{manager ? 'Acompanhe o planejamento e a execução dos serviços.' : 'Acompanhe os serviços atribuídos a você.'}</p></div>{manager && <button onClick={() => go('/ordens-servico/novo')}>Nova OS</button>}</div><section className="card wo-toolbar"><label>Status<select value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}><option value="">Todos</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>{manager && <label>Profissional responsável<select value={assignee} onChange={event => { setPage(1); setAssignee(event.target.value) }}><option value="">Todos</option>{people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}</select></label>}{filtered && <button className="link-button" onClick={() => { setStatus(''); setAssignee(''); setPage(1) }}>Limpar filtros</button>}<button className="secondary" onClick={() => void load()}>Atualizar</button></section>{error ? <ErrorState text={error} retry={() => void load()} /> : !data ? <LoadingState /> : !data.items.length ? <section className="card empty-state"><h3>{filtered ? 'Nenhuma OS encontrada' : 'Nenhuma OS no momento'}</h3><p>{filtered ? 'Ajuste os filtros para ver outras ordens.' : manager ? 'As ordens serão exibidas aqui quando forem criadas a partir de um contrato ativo.' : 'As ordens atribuídas a você aparecerão aqui.'}</p></section> : <section className="card wo-results"><div className="section-heading"><div><h3>Ordens de Serviço</h3><p>{data.totalCount} OS encontrada(s)</p></div></div><div className="table-wrap wo-table"><table><thead><tr><th>OS</th><th>Cliente</th><th>Status</th><th>Responsável</th><th>Início</th><th>Término</th><th>Atualização</th><th>Ação</th></tr></thead><tbody>{data.items.map(item => <tr key={item.id}><td><strong>{item.number}</strong></td><td>{item.customerLegalNameSnapshot}</td><td><Badge status={item.status}/></td><td>{item.assignedUserNameSnapshot ?? 'Não atribuído'}</td><td>{plannedLabel(item.scheduledStartDate, item.scheduledStartTime)}</td><td>{plannedLabel(item.scheduledEndDate, item.scheduledEndTime)}</td><td>{date(item.updatedAtUtc)}</td><td><button className="secondary" onClick={() => go(`/ordens-servico/${item.id}`)}>Ver OS</button></td></tr>)}</tbody></table></div><div className="wo-mobile-list">{data.items.map(item => <article key={item.id}><div><strong>{item.number}</strong><Badge status={item.status}/></div><p>{item.customerLegalNameSnapshot}</p><small>{item.assignedUserNameSnapshot ?? 'Não atribuído'} · Início: {plannedLabel(item.scheduledStartDate, item.scheduledStartTime)}</small><button className="secondary" onClick={() => go(`/ordens-servico/${item.id}`)}>Ver OS</button></article>)}</div></section>}{data && data.totalCount > data.pageSize && <div className="pagination"><button className="secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {data.page} de {Math.ceil(data.totalCount / data.pageSize)}</span><button className="secondary" disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}</>
}

function Create({ quoteId, contractId, go, onSessionExpired, user }: Props & { quoteId: string | null; contractId: string | null }) {
  const canSchedule = user.features.includes('schedule')
  const [source, setSource] = useState<Source | null>(null), [existingId, setExistingId] = useState<string | null>(null), [people, setPeople] = useState<EligibleAssignee[]>([])
  const [form, setForm] = useState<Planning>(blank), [errors, setErrors] = useState<Errors>({}), [error, setError] = useState(''), [loading, setLoading] = useState(true), [pending, setPending] = useState(false)
  const loadRequestId = useRef(0)
  const load = useCallback(async () => {
    const currentRequest = ++loadRequestId.current
    if (!contractId || quoteId) { setSource(null); setError('Selecione um contrato ativo para criar a OS.'); setLoading(false); return }
    setLoading(true); setError(''); setSource(null); setExistingId(null)
    try {
      const [record, assignees] = await Promise.all([getWorkOrderSource('Contract', contractId), getEligibleAssignees()])
      if (currentRequest !== loadRequestId.current) return
      setPeople(assignees)
      setExistingId(record.source.currentWorkOrderId)
      setSource({ id: record.source.id, kind: 'contract', status: record.source.status, customer: record.source.customerLegalNameSnapshot, address: record.source.serviceAddressSnapshot, items: record.items, canCreate: record.source.canCreate })
    } catch (caught) {
      if (currentRequest === loadRequestId.current) setError(problem(caught, onSessionExpired, 'Não foi possível carregar o contrato da OS.'))
    }
    finally { if (currentRequest === loadRequestId.current) setLoading(false) }
  }, [quoteId, contractId, onSessionExpired])
  useEffect(() => { void load() }, [load])
  const save = async (event: FormEvent) => {
    event.preventDefault(); if (!source || pending || !source.canCreate) return
    const next = validatePlanning(form); setErrors(next); if (Object.keys(next).length) return
    setPending(true); setError('')
    try { const order = await createWorkOrder(source.id, form); go(`/ordens-servico/${order.id}`, true) }
    catch (caught) { if (caught instanceof ApiError && Object.keys(caught.errors).length) setErrors(caught.errors); else setError(problem(caught, onSessionExpired, 'Não foi possível criar a OS.')) }
    finally { setPending(false) }
  }
  if (loading) return <LoadingState size="lg" />
  if (!source) return <ErrorState text={error} retry={() => void load()} />
  if (existingId) return <section className="card empty-state"><h2>Já existe uma OS para este escopo</h2><p>Abra a OS atual para acompanhar a execução.</p><button onClick={() => go(`/ordens-servico/${existingId}`)}>Ver OS</button></section>
  const eligible = source.canCreate
  const reason = source.status !== 'Active' ? 'Somente contratos ativos podem originar uma OS.' : 'Finalize a OS em aberto ou verifique o local e os serviços antes de criar uma nova OS.'
  return <>
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>Criar Ordem de Serviço</h2><p>{canSchedule ? 'A OS ser\u00e1 criada como rascunho. O agendamento \u00e9 uma etapa separada.' : 'A OS ser\u00e1 criada como rascunho. O agendamento n\u00e3o est\u00e1 habilitado para sua empresa.'}</p></div></div>
    {error && <p className="error" role="alert">{error}</p>}
    {errors.sourceId?.map(item => <p className="error" role="alert" key={item}>{item}</p>)}
    {!eligible && <section className="card error-panel"><p>{reason}</p></section>}
    <section className="wo-create-layout">
      <article className="card wo-scope"><h3>Escopo operacional</h3><dl className="wo-definition"><Info label="Cliente" value={source.customer}/><Info label="Local do serviço" value={source.address || 'Não informado'}/></dl><h3>Serviços</h3><Services items={source.items}/></article>
      <section className="card wo-planning"><h3>Planejamento inicial</h3><p>Você pode completar o planejamento depois de criar o rascunho.</p><form noValidate onSubmit={save}><PlanningFields form={form} setForm={setForm} errors={errors} people={people} pending={pending} canSchedule={canSchedule}/><div className="actions"><button className="secondary" type="button" onClick={() => go('/ordens-servico/novo')}>Voltar</button><button disabled={pending || !eligible}>{pending ? 'Criando...' : 'Criar OS'}</button></div></form></section>
    </section>
  </>
}

function Detail({ id, go, onSessionExpired, user }: Props & { id: string }) {
  const manager = management(user)
  const canSchedule = user.features.includes('schedule')
  const [order, setOrder] = useState<WorkOrder | null>(null), [history, setHistory] = useState<WorkOrderHistory[] | null>(null), [people, setPeople] = useState<EligibleAssignee[]>([])
  const [form, setForm] = useState<Planning>(blank), [errors, setErrors] = useState<Errors>({}), [error, setError] = useState(''), [notice, setNotice] = useState(''), [historyError, setHistoryError] = useState(''), [pending, setPending] = useState(false), [stale, setStale] = useState(false)
  const [confirm, setConfirm] = useState<'complete' | 'cancel' | null>(null), [completionNotes, setCompletionNotes] = useState(''), [cancellationReason, setCancellationReason] = useState('')
  const load = useCallback(async () => {
    setError(''); setNotice('')
    try { const current = await getWorkOrder(id); setOrder(current); setForm(planningFrom(current)); setErrors({}); setStale(false) }
    catch (caught) { setError(problem(caught, onSessionExpired, 'Não foi possível carregar a OS.')) }
    try { setHistory(await getWorkOrderHistory(id)); setHistoryError('') }
    catch { setHistoryError('Não foi possível carregar o histórico.'); setHistory([]) }
  }, [id, onSessionExpired])
  useEffect(() => { void load() }, [load])
  useEffect(() => { if (manager) void getEligibleAssignees().then(setPeople).catch(() => setPeople([])) }, [manager])
  const failure = (caught: unknown, fallback: string) => {
    if (caught instanceof ApiError && caught.status === 409) { setStale(true); setConfirm(null); setError('Esta OS foi alterada por outro usuário. Atualize os dados antes de tentar novamente.'); return }
    if (caught instanceof ApiError && Object.keys(caught.errors).length) { setErrors(caught.errors); return }
    setError(problem(caught, onSessionExpired, fallback))
  }
  const save = async (event: FormEvent) => {
    event.preventDefault(); if (!order || pending || stale) return
    const next = validatePlanning(form); setErrors(next); if (Object.keys(next).length) return
    setPending(true); setError(''); setNotice('')
    try { const rescheduled = canSchedule && order.status === 'Scheduled' && calendarPlanningChanged(planningFrom(order), form); const updated = await saveWorkOrderPlanning(id, order.version, form); setOrder(updated); setForm(planningFrom(updated)); if (rescheduled && updated.scheduledStartDate) { go(`/agenda?date=${updated.scheduledStartDate}&view=day&workOrderId=${updated.id}&confirmation=rescheduled`); return } setNotice('Planejamento salvo com sucesso.'); void getWorkOrderHistory(id).then(setHistory).catch(() => setHistoryError('Não foi possível atualizar o histórico.')) }
    catch (caught) { failure(caught, 'Não foi possível salvar o planejamento.') }
    finally { setPending(false) }
  }
  const act = async (action: 'schedule' | 'start' | 'complete' | 'cancel') => {
    if (!order || pending || stale || (action === 'schedule' && !canSchedule)) return
    setPending(true); setError(''); setNotice(''); setErrors({})
    try { const updated = await transitionWorkOrder(id, action, order.version, completionNotes, cancellationReason); setOrder(updated); setForm(planningFrom(updated)); setConfirm(null); setCompletionNotes(''); setCancellationReason(''); if (action === 'schedule') { go(`/agenda?date=${updated.scheduledStartDate}&view=day&workOrderId=${updated.id}`); return } void getWorkOrderHistory(id).then(setHistory).catch(() => setHistoryError('Não foi possível atualizar o histórico.')) }
    catch (caught) { failure(caught, 'Não foi possível atualizar a OS.') }
    finally { setPending(false) }
  }
  if (!order) return error ? <ErrorState text={error} retry={() => void load()} /> : <LoadingState size="lg" />
  const canPlan = manager && ['Draft', 'Scheduled'].includes(order.status)
  const ready = Boolean(order.assignedUserId && order.scheduledStartDate && order.serviceAddressSnapshot.trim() && order.items.length)
  const planningChanged = JSON.stringify(form) !== JSON.stringify(planningFrom(order))
  const canExecute = manager || order.assignedUserId === user.id
  const terminal = order.status === 'Completed' || order.status === 'Cancelled'
  return <><div className="wo-detail-header"><div><p className="eyebrow">Operação · {order.customerLegalNameSnapshot}</p><h2>{order.number}</h2><p>Atualizada em {date(order.updatedAtUtc)}</p></div><Badge status={order.status}/></div>{notice && <p className="notice" role="status">{notice}</p>}{stale && <section className="card error-panel" role="alert"><p>Esta OS foi alterada por outro usuário. Atualize os dados antes de tentar novamente.</p><button className="secondary" onClick={() => void load()}>Atualizar dados</button></section>}{error && !stale && <p className="error" role="alert">{error}</p>}{Object.keys(errors).length > 0 && <div className="card error-panel" role="alert"><p>Revise os campos indicados antes de continuar.</p>{errors.sourceId?.map(item => <p key={item}>{item}</p>)}{errors.serviceAddress?.map(item => <p key={item}>{item}</p>)}{errors.items?.map(item => <p key={item}>{item}</p>)}</div>}<div className="wo-detail-layout"><div className="wo-main"><section className="card"><h3>Resumo</h3><dl className="wo-definition"><Info label="Origem" value={order.sourceType === 'Quote' ? 'Orçamento' : 'Contrato'}/><Info label="Cliente" value={order.customerLegalNameSnapshot}/><Info label="Local do serviço" value={order.serviceAddressSnapshot}/><Info label="Responsável" value={order.assignedUserNameSnapshot ?? 'Não atribuído'}/><Info label="Início da execução" value={date(order.startedAtUtc)}/><Info label="Conclusão da execução" value={date(order.executionCompletedAtUtc)}/> {order.cancelledAtUtc && <Info label="Cancelamento" value={date(order.cancelledAtUtc)}/>}</dl></section><section className="card"><h3>Serviços</h3><Services items={order.items}/></section><section className="card"><h3>Planejamento</h3>{canPlan ? <form noValidate onSubmit={save}><PlanningFields form={form} setForm={setForm} errors={errors} people={people} pending={pending || stale} scheduled={order.status === 'Scheduled'} canSchedule={canSchedule}/><div className="actions"><button disabled={pending || stale}>{pending ? 'Salvando...' : 'Salvar planejamento'}</button></div></form> : <dl className="wo-definition"><Info label="Responsável" value={order.assignedUserNameSnapshot ?? 'Não atribuído'}/><Info label="Início previsto" value={plannedLabel(order.scheduledStartDate, order.scheduledStartTime)}/><Info label="Término previsto" value={plannedLabel(order.scheduledEndDate, order.scheduledEndTime)}/><Info label="Observações operacionais" value={order.operationalNotes || 'Nenhuma observação operacional.'}/></dl>}</section>{order.completionNotes && <section className="card"><h3>Conclusão da execução</h3><p>{order.completionNotes}</p></section>}<section className="card wo-history"><h3>Histórico</h3>{historyError && <div className="actions"><p className="error" role="alert">{historyError}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></div>}{history === null ? <LoadingState size="sm" /> : history.length === 0 ? <p>Nenhum evento registrado.</p> : <ol>{history.map((event, index) => <li key={`${event.occurredAtUtc}-${index}`}><strong>{events[event.action] ?? 'Atualização da OS'}</strong><span>{event.actorNameSnapshot} · {date(event.occurredAtUtc)}</span>{event.cancellationReason && <p>Motivo: {event.cancellationReason}</p>}</li>)}</ol>}</section></div><aside className="card wo-action-panel"><h3>Execução</h3><Badge status={order.status}/><p>{order.status === 'Draft' ? (canSchedule ? 'Complete o planejamento para agendar a execu\u00e7\u00e3o.' : 'O agendamento n\u00e3o est\u00e1 habilitado para sua empresa.') : order.status === 'Scheduled' ? 'A OS está pronta para iniciar a execução.' : order.status === 'InProgress' ? 'A execução está em andamento.' : order.status === 'Completed' ? 'Execução concluída.' : 'Esta OS está em um estado final.'}</p><div className="wo-business-actions">{manager && canSchedule && order.status === 'Draft' && <><button disabled={pending || stale || !ready || planningChanged} onClick={() => void act('schedule')}>Agendar OS</button>{!ready && <p className="wo-hint">Antes de agendar, informe o profissional responsável e a data de início no planejamento. O horário e o término são opcionais. O local e os serviços também devem estar disponíveis.</p>}{planningChanged && <p className="wo-hint">Salve o planejamento antes de agendar.</p>}</>}{canExecute && order.status === 'Scheduled' && <button disabled={pending || stale} onClick={() => void act('start')}>Iniciar execução</button>}{canExecute && order.status === 'InProgress' && <button disabled={pending || stale} onClick={() => setConfirm('complete')}>Concluir execução</button>}{manager && !terminal && <button className="secondary" disabled={pending || stale} onClick={() => setConfirm('cancel')}>Cancelar OS</button>}</div><button className="link-button" onClick={() => go('/ordens-servico')}>Voltar à lista</button></aside></div>{confirm && <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-label={confirm === 'complete' ? 'Concluir execução' : 'Cancelar OS'}><h2>{confirm === 'complete' ? 'Concluir execução' : 'Cancelar OS'}</h2><p>{confirm === 'complete' ? 'Confirme que os serviços foram executados.' : 'O cancelamento é definitivo para esta OS.'}</p>{confirm === 'complete' && <label>Observações da conclusão<textarea maxLength={2000} value={completionNotes} onChange={event => setCompletionNotes(event.target.value)}/>{errors.completionNotes?.map(item => <small className="error" key={item}>{item}</small>)}</label>}{confirm === 'cancel' && <label>Motivo do cancelamento<textarea maxLength={500} value={cancellationReason} onChange={event => setCancellationReason(event.target.value)}/>{errors.cancellationReason?.map(item => <small className="error" key={item}>{item}</small>)}</label>}<div className="actions"><button className="secondary" disabled={pending} onClick={() => setConfirm(null)}>Voltar</button><button disabled={pending} onClick={() => void act(confirm)}>{pending ? 'Salvando...' : 'Confirmar'}</button></div></section></div>}</>
}

function PlanningFields({ form, setForm, errors, people, pending, scheduled = false, canSchedule }: { form: Planning; setForm: Dispatch<SetStateAction<Planning>>; errors: Errors; people: EligibleAssignee[]; pending: boolean; scheduled?: boolean; canSchedule: boolean }) {
  const scheduleFields = [
    ['scheduledStartDate', 'Data de in\u00edcio prevista', 'date'],
    ['scheduledStartTime', 'Hor\u00e1rio de in\u00edcio previsto', 'time'],
    ['scheduledEndDate', 'Data de t\u00e9rmino prevista', 'date'],
    ['scheduledEndTime', 'Hor\u00e1rio de t\u00e9rmino previsto', 'time']
  ] as const

  return <div className="wo-form-grid">
    <Field label={scheduled ? 'Profissional respons\u00e1vel *' : 'Profissional respons\u00e1vel'} errors={errors.assignedUserId}>
      <select disabled={pending} value={form.assignedUserId} onChange={event => setForm(current => ({ ...current, assignedUserId: event.target.value }))}>
        <option value="">N&#227;o atribu&#237;do</option>
        {people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}
      </select>
      {canSchedule && <small>Obrigat&#243;rio para agendar.</small>}
    </Field>
    {canSchedule ? <>
      <p className="wo-hint">Para agendar, informe o respons&#225;vel e a data de in&#237;cio. Hor&#225;rios e t&#233;rmino s&#227;o opcionais.</p>
      {scheduleFields.map(([key, label, type]) => <Field key={key} label={scheduled && key === 'scheduledStartDate' ? label + ' *' : label} errors={errors[key]}>
        <input disabled={pending} type={type} value={form[key]} onChange={event => setForm(current => ({ ...current, [key]: event.target.value }))}/>
        {key === 'scheduledStartDate' && <small>Obrigat&#243;ria para agendar.</small>}
      </Field>)}
    </> : <>
      <p className="wo-hint">O agendamento n&#227;o est&#225; habilitado para sua empresa. Datas e hor&#225;rios permanecem somente para consulta.</p>
      {(form.scheduledStartDate || form.scheduledEndDate) && <dl className="wo-definition">
        <Info label="In&#237;cio previsto" value={plannedLabel(form.scheduledStartDate || null, form.scheduledStartTime || null)}/>
        <Info label="T&#233;rmino previsto" value={plannedLabel(form.scheduledEndDate || null, form.scheduledEndTime || null)}/>
      </dl>}
    </>}
    <Field label="Observa&#231;&#245;es operacionais" errors={errors.operationalNotes}>
      <textarea disabled={pending} maxLength={2000} value={form.operationalNotes} onChange={event => setForm(current => ({ ...current, operationalNotes: event.target.value }))}/>
    </Field>
  </div>
}
function Services({ items }: { items: WorkOrderSourceItem[] | WorkOrderItem[] }) { return items.length ? <div className="wo-services">{[...items].sort((a, b) => a.displayOrder - b.displayOrder).map(item => <div className="wo-service" key={`${item.displayOrder}-${item.serviceCodeSnapshot}`}><strong>{item.serviceCodeSnapshot} · {item.serviceNameSnapshot}</strong><small>{item.serviceLineCodeSnapshot} · {item.serviceLineNameSnapshot}</small></div>)}</div> : <p>Nenhum serviço informado.</p> }
function Info({ label, value }: { label: string; value: string }) { return <><dt>{label}</dt><dd>{value}</dd></> }
function Field({ label, errors, children }: { label: string; errors?: string[]; children: ReactNode }) { return <label>{label}{children}{errors?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}</label> }
function Empty({ title, text, go }: { title: string; text: string; go: Props['go'] }) { return <section className="card empty-state"><h2>{title}</h2><p>{text}</p><button className="secondary" onClick={() => go('/ordens-servico')}>Voltar</button></section> }
function ErrorState({ text, retry }: { text: string; retry: () => void }) { return <section className="card error-panel"><p role="alert">{text}</p><button className="secondary" onClick={retry}>Tentar novamente</button></section> }
