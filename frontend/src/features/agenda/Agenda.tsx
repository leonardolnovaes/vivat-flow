import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { Badge } from '../workOrders/WorkOrderStatusBadge'
import { getAgenda, getEligibleAssignees } from '../workOrders/workOrderApi'
import type { AgendaEntry, EligibleAssignee } from '../workOrders/types'
import { movePeriod, overlapsDay, periodLabel, sameDay, visiblePeriod } from './agendaDates'
import type { AgendaView } from './agendaDates'
import './agenda.css'

type Props = { user: { roles: string[] }; go: (path: string) => void; onSessionExpired: () => void }
const text = {
  title: 'Agenda', team: 'Visualize os serviços programados da sua equipe.', own: 'Visualize os serviços programados para você.',
  views: { day: 'Dia', week: 'Semana', month: 'Mês' },
  emptyDay: 'Nenhum serviço agendado para este dia.', filteredDay: 'Nenhum serviço agendado para este profissional neste dia.',
  emptyPeriod: 'Nenhum serviço agendado neste período.', filteredPeriod: 'Nenhum serviço agendado para este profissional neste período.',
  continuation: 'Continuação',
}
const time = (value: string) => new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
const schedule = (entry: AgendaEntry) => {
  const start = new Date(entry.scheduledStart), end = new Date(entry.scheduledEnd)
  return sameDay(start, end) ? `${time(entry.scheduledStart)} – ${time(entry.scheduledEnd)}` :
    `${start.toLocaleDateString('pt-BR')} ${time(entry.scheduledStart)} – ${end.toLocaleDateString('pt-BR')} ${time(entry.scheduledEnd)}`
}

export function Agenda({ user, go, onSessionExpired }: Props) {
  const management = user.roles.includes('ADMIN') || user.roles.includes('MANAGER')
  const [view, setView] = useState<AgendaView>('day'), [date, setDate] = useState(() => new Date())
  const [assignee, setAssignee] = useState(''), [people, setPeople] = useState<EligibleAssignee[]>([])
  const [entries, setEntries] = useState<AgendaEntry[] | null>(null), [error, setError] = useState('')
  const [peopleLoading, setPeopleLoading] = useState(management), [peopleError, setPeopleError] = useState(false)
  const sequence = useRef(0), peopleSequence = useRef(0)
  const expired = useRef(onSessionExpired)
  useEffect(() => { expired.current = onSessionExpired }, [onSessionExpired])
  const period = useMemo(() => visiblePeriod(date, view), [date, view])
  const load = useCallback(async () => {
    const current = ++sequence.current
    setEntries(null); setError('')
    const query = new URLSearchParams({ from: period.from.toISOString(), to: period.to.toISOString() })
    if (management && assignee) query.set('assignedUserId', assignee)
    try { const result = await getAgenda(query); if (current === sequence.current) setEntries(result) }
    catch (caught) {
      if (current !== sequence.current) return
      if (caught instanceof ApiError && caught.status === 401) { expired.current(); return }
      setError(caught instanceof ApiError && caught.status === 403 ? 'Você não tem permissão para acessar a Agenda.' : 'Não foi possível carregar a Agenda. Tente novamente.')
    }
  }, [period, assignee, management])
  const loadPeople = useCallback(async () => {
    if (!management) return
    const current = ++peopleSequence.current
    setPeopleLoading(true); setPeopleError(false)
    try { const result = await getEligibleAssignees(); if (current === peopleSequence.current) setPeople(result) }
    catch (caught) {
      if (current !== peopleSequence.current) return
      if (caught instanceof ApiError && caught.status === 401) expired.current()
      else setPeopleError(true)
    }
    finally { if (current === peopleSequence.current) setPeopleLoading(false) }
  }, [management])
  useEffect(() => { void load(); return () => { sequence.current++ } }, [load])
  useEffect(() => { void loadPeople(); return () => { peopleSequence.current++ } }, [loadPeople])
  const today = new Date()
  const selectDay = (day: Date) => { setDate(day); setView('day') }
  return <div className="agenda-page">
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>{text.title}</h2><p>{management ? text.team : text.own}</p></div></div>
    <section className="card agenda-toolbar" aria-label="Controles da Agenda">
      <div className="agenda-controls"><button className="secondary" onClick={() => setDate(new Date())}>Hoje</button><button className="secondary" aria-label="Período anterior" onClick={() => setDate(current => movePeriod(current, view, -1))}>‹</button><button className="secondary" aria-label="Próximo período" onClick={() => setDate(current => movePeriod(current, view, 1))}>›</button><h3 aria-live="polite">{periodLabel(date, view)}</h3></div>
      <div className="agenda-controls" role="group" aria-label="Visualização">{(Object.keys(text.views) as AgendaView[]).map(option => <button key={option} className={view === option ? '' : 'secondary'} aria-pressed={view === option} onClick={() => setView(option)}>{text.views[option]}</button>)}<button className="link-button" onClick={() => void load()}>Atualizar</button></div>
      {management && <div className="agenda-filter"><label>Profissional responsável<select value={assignee} disabled={peopleLoading || peopleError} onChange={event => setAssignee(event.target.value)}><option value="">Todos</option>{people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}</select></label>{assignee && <button className="link-button" onClick={() => setAssignee('')}>Limpar filtros</button>}{peopleLoading && <LoadingState size="sm" />}{peopleError && <div role="alert"><p>Não foi possível carregar os profissionais.</p><button className="secondary" onClick={() => void loadPeople()}>Tentar novamente</button></div>}</div>}
    </section>
    {error ? <section className="card error-panel" role="alert"><p>{error}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></section> : entries === null ? <LoadingState /> : <>
      {!entries.length && <section className="card empty-state" role="status"><h3>{view === 'day' ? assignee ? text.filteredDay : text.emptyDay : assignee ? text.filteredPeriod : text.emptyPeriod}</h3><p>Use os controles para consultar outras datas.</p></section>}
      {entries.length > 0 && <div className={`agenda-${view}`}>
        {period.days.map(day => {
          const daily = entries.filter(entry => overlapsDay(entry.scheduledStart, entry.scheduledEnd, day))
          const isToday = sameDay(day, today)
          return <section key={day.getTime()} className={`agenda-day-section ${isToday ? 'agenda-today' : ''} ${view === 'month' && day.getMonth() !== date.getMonth() ? 'agenda-adjacent' : ''}`} aria-label={day.toLocaleDateString('pt-BR')}>
            {view !== 'day' && <header><button className="link-button agenda-day-heading" onClick={() => selectDay(day)}>{day.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric', ...(view === 'week' ? { month: 'short' } : {}) })}{isToday && <span className="agenda-today-label">Hoje</span>}</button></header>}
            {view === 'month' ? <><div className="agenda-compact-list">{daily.slice(0, 3).map(entry => <button className="agenda-compact secondary" key={entry.id} onClick={() => go(`/ordens-servico/${entry.id}`)} title={`${schedule(entry)} · ${entry.customerLegalNameSnapshot} · ${entry.assignedUserNameSnapshot ?? ''}`}><strong>{sameDay(new Date(entry.scheduledStart), day) ? time(entry.scheduledStart) : text.continuation} · {entry.number}</strong><span>{entry.customerLegalNameSnapshot}</span><small>{entry.assignedUserNameSnapshot}</small></button>)}</div>{daily.length > 3 && <button className="link-button" onClick={() => selectDay(day)}>+ {daily.length - 3} serviços</button>}</> : daily.length ? daily.map(entry => <EventCard key={entry.id} entry={entry} go={go} />) : <p className="agenda-day-empty">{text.emptyDay}</p>}
          </section>
        })}
      </div>}
    </>}
  </div>
}

function EventCard({ entry, go }: { entry: AgendaEntry; go: Props['go'] }) {
  return <article className="card agenda-event"><div className="agenda-event-heading"><strong>{schedule(entry)}</strong><Badge status={entry.status} /></div><h4>{entry.number}</h4><p className="agenda-customer">{entry.customerLegalNameSnapshot}</p><ul className="agenda-services">{entry.services.slice(0, 2).map((service, index) => <li key={index}>{service.serviceCodeSnapshot} · {service.serviceNameSnapshot}</li>)}</ul>{entry.services.length > 2 && <p>+ {entry.services.length - 2} serviços</p>}<p><strong>Responsável:</strong> {entry.assignedUserNameSnapshot ?? 'Não atribuído'}</p><p><strong>Local:</strong> {entry.serviceAddressSnapshot}</p><button className="secondary" onClick={() => go(`/ordens-servico/${entry.id}`)}>Ver OS</button></article>
}
