import { useCallback, useEffect, useRef, useState } from 'react'
import type { Dispatch, FormEvent, ReactNode, SetStateAction } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { WorkOrderSourcePicker } from './WorkOrderSourcePicker'
import { createWorkOrder, getEligibleAssignees, getWorkOrder, getWorkOrderHistory, getWorkOrderSource, listWorkOrders, saveWorkOrderPlanning, toLocalInput, transitionWorkOrder } from './workOrderApi'
import type { EligibleAssignee, Planning, WorkOrder, WorkOrderHistory, WorkOrderItem, WorkOrderList, WorkOrderSourceItem, WorkOrderStatus } from './types'

type Props = { path: string; go: (path: string, replace?: boolean) => void; onSessionExpired: () => void; user: { id: string; roles: string[] } }
type Errors = Record<string, string[]>
type Source = { id: string; kind: 'quote' | 'contract'; status: string; customer: string; address: string | null; items: WorkOrderSourceItem[]; canCreate: boolean }
const blank: Planning = { assignedUserId: '', scheduledStart: '', scheduledEnd: '', operationalNotes: '' }
const labels: Record<WorkOrderStatus, string> = { Draft: 'Rascunho', Scheduled: 'Agendada', InProgress: 'Em andamento', AwaitingClosure: 'Aguardando encerramento', Closed: 'Encerrada', Cancelled: 'Cancelada' }
const events: Record<string, string> = { WORK_ORDER_CREATED_FROM_QUOTE: 'OS criada a partir do orçamento', WORK_ORDER_CREATED_FROM_CONTRACT: 'OS criada a partir do contrato', WORK_ORDER_PLANNING_UPDATED: 'Planejamento atualizado', WORK_ORDER_SCHEDULED: 'OS agendada', WORK_ORDER_STARTED: 'Execução iniciada', WORK_ORDER_EXECUTION_COMPLETED: 'Execução concluída', WORK_ORDER_CLOSED: 'OS encerrada', WORK_ORDER_CANCELLED: 'OS cancelada' }
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
const planningFrom = (order: WorkOrder): Planning => ({ assignedUserId: order.assignedUserId ?? '', scheduledStart: toLocalInput(order.scheduledStart), scheduledEnd: toLocalInput(order.scheduledEnd), operationalNotes: order.operationalNotes ?? '' })
function validatePlanning(form: Planning): Errors {
  const errors: Errors = {}
  if (form.scheduledStart && form.scheduledEnd && new Date(form.scheduledEnd) <= new Date(form.scheduledStart)) errors.scheduledEnd = ['O término deve ser posterior ao início.']
  if (form.operationalNotes.trim().length > 2000) errors.operationalNotes = ['Use no máximo 2.000 caracteres.']
  return errors
}

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

export function RelatedWorkOrderAction({ quoteId, contractId, eligible, go }: { quoteId: string; contractId?: string; eligible: boolean; go: Props['go'] }) {
  const [currentOrderId, setCurrentOrderId] = useState<string | null>(null)
  const [governingContract, setGoverningContract] = useState<{ id: string; status: string } | null>(null)
  const [canCreate, setCanCreate] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const load = useCallback(async () => {
    setLoading(true); setError(false)
    try { const result = await getWorkOrderSource(contractId ? 'Contract' : 'Quote', contractId ?? quoteId); setCurrentOrderId(result.source.currentWorkOrderId); setGoverningContract(result.source.governingContractId ? { id: result.source.governingContractId, status: result.source.governingContractStatus ?? '' } : null); setCanCreate(result.source.canCreate) }
    catch { setError(true) }
    finally { setLoading(false) }
  }, [quoteId, contractId])
  useEffect(() => { void load() }, [load])
  if (loading) return <LoadingState size="sm" />
  if (error) return <div className="actions"><p>Não foi possível verificar a OS vinculada.</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></div>
  if (currentOrderId) return <button type="button" className="secondary" onClick={() => go(`/ordens-servico/${currentOrderId}`)}>Ver OS</button>
  if (governingContract) return <p className="wo-hint">Este orçamento possui contrato {governingContract.status === 'Active' ? 'ativo' : 'em formalização'}. {governingContract.status === 'Active' ? 'Crie a OS a partir do contrato.' : 'Aguarde a ativação do contrato para criar a OS.'} {governingContract.status === 'Active' && <button className="link-button" onClick={() => go(`/ordens-servico/novo?contractId=${governingContract.id}`)}>Usar contrato</button>}</p>
  if (!eligible) return null
  if (!canCreate) return <p className="wo-hint">Informe um local válido e pelo menos um serviço operacional antes de criar a OS.</p>
  return <button type="button" onClick={() => go(`/ordens-servico/novo?${contractId ? `contractId=${contractId}` : `quoteId=${quoteId}`}`)}>Criar OS</button>
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
  return <><div className="page-title"><div><p className="eyebrow">Operação</p><h2>Ordens de Serviço</h2><p>{manager ? 'Acompanhe o planejamento e a execução dos serviços.' : 'Acompanhe os serviços atribuídos a você.'}</p></div>{manager && <button onClick={() => go('/ordens-servico/novo')}>Nova OS</button>}</div><section className="card wo-toolbar"><label>Status<select value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}><option value="">Todos</option>{Object.entries(labels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>{manager && <label>Profissional responsável<select value={assignee} onChange={event => { setPage(1); setAssignee(event.target.value) }}><option value="">Todos</option>{people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}</select></label>}{filtered && <button className="link-button" onClick={() => { setStatus(''); setAssignee(''); setPage(1) }}>Limpar filtros</button>}<button className="secondary" onClick={() => void load()}>Atualizar</button></section>{error ? <ErrorState text={error} retry={() => void load()} /> : !data ? <LoadingState /> : !data.items.length ? <section className="card empty-state"><h3>{filtered ? 'Nenhuma OS encontrada' : 'Nenhuma OS no momento'}</h3><p>{filtered ? 'Ajuste os filtros para ver outras ordens.' : manager ? 'As ordens serão exibidas aqui quando forem criadas a partir de um orçamento ou contrato.' : 'As ordens atribuídas a você aparecerão aqui.'}</p></section> : <section className="card wo-results"><div className="section-heading"><div><h3>Ordens de Serviço</h3><p>{data.totalCount} OS encontrada(s)</p></div></div><div className="table-wrap wo-table"><table><thead><tr><th>OS</th><th>Cliente</th><th>Status</th><th>Responsável</th><th>Início</th><th>Término</th><th>Atualização</th><th>Ação</th></tr></thead><tbody>{data.items.map(item => <tr key={item.id}><td><strong>{item.number}</strong></td><td>{item.customerLegalNameSnapshot}</td><td><Badge status={item.status}/></td><td>{item.assignedUserNameSnapshot ?? 'Não atribuído'}</td><td>{date(item.scheduledStart)}</td><td>{date(item.scheduledEnd)}</td><td>{date(item.updatedAtUtc)}</td><td><button className="secondary" onClick={() => go(`/ordens-servico/${item.id}`)}>Ver OS</button></td></tr>)}</tbody></table></div><div className="wo-mobile-list">{data.items.map(item => <article key={item.id}><div><strong>{item.number}</strong><Badge status={item.status}/></div><p>{item.customerLegalNameSnapshot}</p><small>{item.assignedUserNameSnapshot ?? 'Não atribuído'} · Início: {date(item.scheduledStart)}</small><button className="secondary" onClick={() => go(`/ordens-servico/${item.id}`)}>Ver OS</button></article>)}</div></section>}{data && data.totalCount > data.pageSize && <div className="pagination"><button className="secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {data.page} de {Math.ceil(data.totalCount / data.pageSize)}</span><button className="secondary" disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}</>
}

function Create({ quoteId, contractId, go, onSessionExpired }: Props & { quoteId: string | null; contractId: string | null }) {
  const [source, setSource] = useState<Source | null>(null), [existingId, setExistingId] = useState<string | null>(null), [governingContract, setGoverningContract] = useState<{ id: string; status: string } | null>(null), [people, setPeople] = useState<EligibleAssignee[]>([])
  const [form, setForm] = useState<Planning>(blank), [errors, setErrors] = useState<Errors>({}), [error, setError] = useState(''), [loading, setLoading] = useState(true), [pending, setPending] = useState(false)
  const sourceId = quoteId || contractId
  const load = useCallback(async () => {
    if (!sourceId || Boolean(quoteId) === Boolean(contractId)) { setError('Selecione uma origem para criar a OS.'); setLoading(false); return }
    setLoading(true); setError('')
    try {
      const [record, assignees] = await Promise.all([getWorkOrderSource(quoteId ? 'Quote' : 'Contract', sourceId), getEligibleAssignees()])
      setPeople(assignees); setExistingId(record.source.currentWorkOrderId); setGoverningContract(record.source.governingContractId ? { id: record.source.governingContractId, status: record.source.governingContractStatus ?? '' } : null)
      setSource({ id: record.source.id, kind: record.source.sourceType === 'Quote' ? 'quote' : 'contract', status: record.source.status, customer: record.source.customerLegalNameSnapshot, address: record.source.serviceAddressSnapshot, items: record.items, canCreate: record.source.canCreate })
    } catch (caught) { setError(problem(caught, onSessionExpired, 'Não foi possível carregar a origem da OS.')) }
    finally { setLoading(false) }
  }, [quoteId, contractId, sourceId, onSessionExpired])
  useEffect(() => { void load() }, [load])
  const save = async (event: FormEvent) => {
    event.preventDefault(); if (!source || pending || !source.canCreate) return
    const next = validatePlanning(form); setErrors(next); if (Object.keys(next).length) return
    setPending(true); setError('')
    try { const order = await createWorkOrder(source.kind, source.id, form); go(`/ordens-servico/${order.id}`, true) }
    catch (caught) { if (caught instanceof ApiError && Object.keys(caught.errors).length) setErrors(caught.errors); else setError(problem(caught, onSessionExpired, 'Não foi possível criar a OS.')) }
    finally { setPending(false) }
  }
  if (loading) return <LoadingState size="lg" />
  if (!source) return <ErrorState text={error} retry={() => void load()} />
  if (existingId) return <section className="card empty-state"><h2>Já existe uma OS para este escopo</h2><p>Abra a OS atual para acompanhar a execução.</p><button onClick={() => go(`/ordens-servico/${existingId}`)}>Ver OS</button></section>
  const eligible = source.canCreate
  const reason = governingContract ? governingContract.status === 'Active' ? 'Este orçamento possui contrato ativo. Crie a OS a partir dele.' : 'Este orçamento possui contrato em formalização. Aguarde a ativação.' : source.kind === 'quote' && source.status !== 'Approved' ? 'Somente orçamentos aprovados podem originar uma OS.' : source.kind === 'contract' && source.status !== 'Active' ? 'Somente contratos ativos podem originar uma OS.' : 'Informe um local válido e pelo menos um serviço operacional antes de criar a OS.'
  return <>
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>Criar Ordem de Serviço</h2><p>A OS será criada como rascunho. O agendamento é uma etapa separada.</p></div></div>
    {error && <p className="error" role="alert">{error}</p>}
    {errors.sourceId?.map(item => <p className="error" role="alert" key={item}>{item}</p>)}
    {!eligible && <section className="card error-panel"><p>{reason}</p>{governingContract?.status === 'Active' && <button className="secondary" onClick={() => go(`/ordens-servico/novo?contractId=${governingContract.id}`)}>Usar contrato</button>}</section>}
    <section className="wo-create-layout">
      <article className="card wo-scope"><h3>Escopo operacional</h3><dl className="wo-definition"><Info label="Origem" value={source.kind === 'quote' ? 'Orçamento' : 'Contrato'}/><Info label="Cliente" value={source.customer}/><Info label="Local do serviço" value={source.address || 'Não informado'}/></dl><h3>Serviços</h3><Services items={source.items}/></article>
      <section className="card wo-planning"><h3>Planejamento inicial</h3><p>Você pode completar o planejamento depois de criar o rascunho.</p><form onSubmit={save}><PlanningFields form={form} setForm={setForm} errors={errors} people={people} pending={pending}/><div className="actions"><button className="secondary" type="button" onClick={() => go('/ordens-servico/novo')}>Voltar</button><button disabled={pending || !eligible}>{pending ? 'Criando...' : 'Criar OS'}</button></div></form></section>
    </section>
  </>
}

function Detail({ id, go, onSessionExpired, user }: Props & { id: string }) {
  const manager = management(user)
  const [order, setOrder] = useState<WorkOrder | null>(null), [history, setHistory] = useState<WorkOrderHistory[] | null>(null), [people, setPeople] = useState<EligibleAssignee[]>([])
  const [form, setForm] = useState<Planning>(blank), [errors, setErrors] = useState<Errors>({}), [error, setError] = useState(''), [historyError, setHistoryError] = useState(''), [pending, setPending] = useState(false), [stale, setStale] = useState(false)
  const [confirm, setConfirm] = useState<'complete' | 'close' | 'cancel' | null>(null), [completionNotes, setCompletionNotes] = useState('')
  const load = useCallback(async () => {
    setError('')
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
    setPending(true); setError('')
    try { const updated = await saveWorkOrderPlanning(id, order.version, form); setOrder(updated); setForm(planningFrom(updated)); void getWorkOrderHistory(id).then(setHistory).catch(() => setHistoryError('Não foi possível atualizar o histórico.')) }
    catch (caught) { failure(caught, 'Não foi possível salvar o planejamento.') }
    finally { setPending(false) }
  }
  const act = async (action: 'schedule' | 'start' | 'complete' | 'close' | 'cancel') => {
    if (!order || pending || stale) return
    setPending(true); setError(''); setErrors({})
    try { const updated = await transitionWorkOrder(id, action, order.version, completionNotes); setOrder(updated); setForm(planningFrom(updated)); setConfirm(null); setCompletionNotes(''); void getWorkOrderHistory(id).then(setHistory).catch(() => setHistoryError('Não foi possível atualizar o histórico.')) }
    catch (caught) { failure(caught, 'Não foi possível atualizar a OS.') }
    finally { setPending(false) }
  }
  if (!order) return error ? <ErrorState text={error} retry={() => void load()} /> : <LoadingState size="lg" />
  const canPlan = manager && ['Draft', 'Scheduled'].includes(order.status)
  const ready = Boolean(order.assignedUserId && order.scheduledStart && order.scheduledEnd && order.serviceAddressSnapshot.trim() && order.items.length && new Date(order.scheduledEnd!) > new Date(order.scheduledStart!))
  const planningChanged = form.assignedUserId !== (order.assignedUserId ?? '') || form.scheduledStart !== toLocalInput(order.scheduledStart) || form.scheduledEnd !== toLocalInput(order.scheduledEnd) || form.operationalNotes !== (order.operationalNotes ?? '')
  const canExecute = manager || order.assignedUserId === user.id
  const terminal = order.status === 'Closed' || order.status === 'Cancelled'
  return <><div className="wo-detail-header"><div><p className="eyebrow">Operação · {order.customerLegalNameSnapshot}</p><h2>{order.number}</h2><p>Atualizada em {date(order.updatedAtUtc)}</p></div><Badge status={order.status}/></div>{stale && <section className="card error-panel" role="alert"><p>Esta OS foi alterada por outro usuário. Atualize os dados antes de tentar novamente.</p><button className="secondary" onClick={() => void load()}>Atualizar dados</button></section>}{error && !stale && <p className="error" role="alert">{error}</p>}{Object.keys(errors).length > 0 && <div className="card error-panel" role="alert"><p>Revise os campos indicados antes de continuar.</p>{errors.sourceId?.map(item => <p key={item}>{item}</p>)}{errors.serviceAddress?.map(item => <p key={item}>{item}</p>)}{errors.items?.map(item => <p key={item}>{item}</p>)}</div>}<div className="wo-detail-layout"><div className="wo-main"><section className="card"><h3>Resumo</h3><dl className="wo-definition"><Info label="Origem" value={order.sourceType === 'Quote' ? 'Orçamento' : 'Contrato'}/><Info label="Cliente" value={order.customerLegalNameSnapshot}/><Info label="Local do serviço" value={order.serviceAddressSnapshot}/><Info label="Responsável" value={order.assignedUserNameSnapshot ?? 'Não atribuído'}/><Info label="Início previsto" value={date(order.scheduledStart)}/><Info label="Término previsto" value={date(order.scheduledEnd)}/><Info label="Início da execução" value={date(order.startedAtUtc)}/><Info label="Conclusão da execução" value={date(order.executionCompletedAtUtc)}/>{order.closedAtUtc && <Info label="Encerramento" value={date(order.closedAtUtc)}/>} {order.cancelledAtUtc && <Info label="Cancelamento" value={date(order.cancelledAtUtc)}/>}</dl></section><section className="card"><h3>Serviços</h3><Services items={order.items}/></section><section className="card"><h3>Planejamento</h3>{canPlan ? <form onSubmit={save}><PlanningFields form={form} setForm={setForm} errors={errors} people={people} pending={pending || stale}/><div className="actions"><button disabled={pending || stale}>{pending ? 'Salvando...' : 'Salvar planejamento'}</button></div></form> : <p>{order.operationalNotes || 'Nenhuma observação operacional.'}</p>}</section>{order.completionNotes && <section className="card"><h3>Conclusão da execução</h3><p>{order.completionNotes}</p></section>}<section className="card wo-history"><h3>Histórico</h3>{historyError && <div className="actions"><p className="error" role="alert">{historyError}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></div>}{history === null ? <LoadingState size="sm" /> : history.length === 0 ? <p>Nenhum evento registrado.</p> : <ol>{history.map((event, index) => <li key={`${event.occurredAtUtc}-${index}`}><strong>{events[event.action] ?? 'Atualização da OS'}</strong><span>{date(event.occurredAtUtc)}</span></li>)}</ol>}</section></div><aside className="card wo-action-panel"><h3>Execução</h3><Badge status={order.status}/><p>{order.status === 'Draft' ? 'Complete o planejamento para agendar a execução.' : order.status === 'Scheduled' ? 'A OS está pronta para iniciar a execução.' : order.status === 'InProgress' ? 'A execução está em andamento.' : order.status === 'AwaitingClosure' ? 'Execução concluída. A OS está aguardando encerramento pela gestão.' : 'Esta OS está em um estado final.'}</p>{manager && order.status === 'Draft' && <><button disabled={pending || stale || !ready || planningChanged} onClick={() => void act('schedule')}>Agendar OS</button>{!ready && <p className="wo-hint">Antes de agendar, informe o profissional responsável, o início e o término no planejamento. O local e os serviços também devem estar disponíveis.</p>}{planningChanged && <p className="wo-hint">Salve o planejamento antes de agendar.</p>}</>}{canExecute && order.status === 'Scheduled' && <button disabled={pending || stale} onClick={() => void act('start')}>Iniciar execução</button>}{canExecute && order.status === 'InProgress' && <button disabled={pending || stale} onClick={() => setConfirm('complete')}>Concluir execução</button>}{manager && order.status === 'AwaitingClosure' && <button disabled={pending || stale} onClick={() => setConfirm('close')}>Encerrar OS</button>}{manager && !terminal && <button className="secondary" disabled={pending || stale} onClick={() => setConfirm('cancel')}>Cancelar OS</button>}<button className="link-button" onClick={() => go('/ordens-servico')}>Voltar à lista</button></aside></div>{confirm && <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-label={confirm === 'complete' ? 'Concluir execução' : confirm === 'close' ? 'Encerrar OS' : 'Cancelar OS'}><h2>{confirm === 'complete' ? 'Concluir execução' : confirm === 'close' ? 'Encerrar OS' : 'Cancelar OS'}</h2><p>{confirm === 'complete' ? 'Confirme que os serviços foram executados. A gestão fará o encerramento da OS.' : confirm === 'close' ? 'A execução foi concluída. Confirme o encerramento final desta OS.' : 'O cancelamento é definitivo para esta OS.'}</p>{confirm === 'complete' && <label>Observações da conclusão<textarea maxLength={2000} value={completionNotes} onChange={event => setCompletionNotes(event.target.value)}/>{errors.completionNotes?.map(item => <small className="error" key={item}>{item}</small>)}</label>}<div className="actions"><button className="secondary" disabled={pending} onClick={() => setConfirm(null)}>Voltar</button><button disabled={pending} onClick={() => void act(confirm)}>{pending ? 'Salvando...' : 'Confirmar'}</button></div></section></div>}</>
}

function PlanningFields({ form, setForm, errors, people, pending }: { form: Planning; setForm: Dispatch<SetStateAction<Planning>>; errors: Errors; people: EligibleAssignee[]; pending: boolean }) {
  return <div className="wo-form-grid"><Field label="Profissional responsável" errors={errors.assignedUserId}><select disabled={pending} value={form.assignedUserId} onChange={event => setForm(current => ({ ...current, assignedUserId: event.target.value }))}><option value="">Não atribuído</option>{people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}</select></Field><Field label="Início previsto" errors={errors.scheduledStart}><input disabled={pending} type="datetime-local" value={form.scheduledStart} onChange={event => setForm(current => ({ ...current, scheduledStart: event.target.value }))}/></Field><Field label="Término previsto" errors={errors.scheduledEnd}><input disabled={pending} type="datetime-local" value={form.scheduledEnd} onChange={event => setForm(current => ({ ...current, scheduledEnd: event.target.value }))}/></Field><Field label="Observações operacionais" errors={errors.operationalNotes}><textarea disabled={pending} maxLength={2000} value={form.operationalNotes} onChange={event => setForm(current => ({ ...current, operationalNotes: event.target.value }))}/></Field></div>
}
function Badge({ status }: { status: WorkOrderStatus }) { return <span className={`wo-status wo-status-${status}`}>{labels[status]}</span> }
function Services({ items }: { items: WorkOrderSourceItem[] | WorkOrderItem[] }) { return items.length ? <div className="wo-services">{[...items].sort((a, b) => a.displayOrder - b.displayOrder).map(item => <div className="wo-service" key={`${item.displayOrder}-${item.serviceCodeSnapshot}`}><strong>{item.serviceCodeSnapshot} · {item.serviceNameSnapshot}</strong><small>{item.serviceLineCodeSnapshot} · {item.serviceLineNameSnapshot}</small></div>)}</div> : <p>Nenhum serviço informado.</p> }
function Info({ label, value }: { label: string; value: string }) { return <><dt>{label}</dt><dd>{value}</dd></> }
function Field({ label, errors, children }: { label: string; errors?: string[]; children: ReactNode }) { return <label>{label}{children}{errors?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}</label> }
function Empty({ title, text, go }: { title: string; text: string; go: Props['go'] }) { return <section className="card empty-state"><h2>{title}</h2><p>{text}</p><button className="secondary" onClick={() => go('/ordens-servico')}>Voltar</button></section> }
function ErrorState({ text, retry }: { text: string; retry: () => void }) { return <section className="card error-panel"><p role="alert">{text}</p><button className="secondary" onClick={retry}>Tentar novamente</button></section> }
