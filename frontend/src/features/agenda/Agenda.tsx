import { plannedLabel } from '../workOrders/planning'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { Badge } from '../workOrders/WorkOrderStatusBadge'
import { getAgenda, getEligibleAssignees } from '../workOrders/workOrderApi'
import type { AgendaEntry, EligibleAssignee } from '../workOrders/types'
import { dateKey, eventHour, movePeriod, periodLabel, sameDay, scheduledOnDay, visiblePeriod } from './agendaDates'
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
const schedule = (entry: AgendaEntry) => plannedLabel(entry.scheduledStartDate, entry.scheduledStartTime) + (entry.scheduledEndDate ? ` → ${plannedLabel(entry.scheduledEndDate, entry.scheduledEndTime)}` : '')
const weekdays = ['Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb', 'Dom']
function initialDate(): Date {
  const value = new URLSearchParams(window.location.search).get('date')
  if (value && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const parsed = new Date(`${value}T12:00:00`)
    if (!Number.isNaN(parsed.getTime()) && dateKey(parsed) === value) return parsed
  }
  return todayDate()
}
function todayDate(): Date {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  return new Date(`${today}T12:00:00`)
}

export function Agenda({ user, go, onSessionExpired }: Props) {
  const management = user.roles.includes('ADMIN') || user.roles.includes('MANAGER')
  const [view, setView] = useState<AgendaView>(() => { const requested = new URLSearchParams(window.location.search).get('view'); return requested === 'week' || requested === 'month' ? requested : 'day' }), [date, setDate] = useState(initialDate)
  const [highlight] = useState(() => new URLSearchParams(window.location.search).get('workOrderId'))
  const [confirmation] = useState(() => new URLSearchParams(window.location.search).get('confirmation'))
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
    const query = new URLSearchParams({ from: `${dateKey(period.from)}T00:00:00-03:00`, to: `${dateKey(period.to)}T00:00:00-03:00` })
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
  useEffect(() => {
    if (entries && highlight) document.getElementById(`agenda-event-${highlight}-${dateKey(period.from)}`)?.scrollIntoView?.({ block: 'nearest' })
  }, [entries, highlight, period])
  const today = todayDate()
  const selectDay = (day: Date) => { setDate(day); setView('day') }
  return <div className="agenda-page">
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>{text.title}</h2><p>{management ? text.team : text.own}</p></div></div>
    <section className="card agenda-toolbar" aria-label="Controles da Agenda">
      <div className="agenda-controls"><button className="secondary" onClick={() => setDate(todayDate())}>Hoje</button><button className="secondary" aria-label="Período anterior" onClick={() => setDate(current => movePeriod(current, view, -1))}>‹</button><button className="secondary" aria-label="Próximo período" onClick={() => setDate(current => movePeriod(current, view, 1))}>›</button><h3 aria-live="polite">{periodLabel(date, view)}</h3></div>
      <div className="agenda-controls" role="group" aria-label="Visualização">{(Object.keys(text.views) as AgendaView[]).map(option => <button key={option} className={view === option ? '' : 'secondary'} aria-pressed={view === option} onClick={() => setView(option)}>{text.views[option]}</button>)}<button className="link-button" onClick={() => void load()}>Atualizar</button></div>
      {management && <div className="agenda-filter"><label>Profissional responsável<select value={assignee} disabled={peopleLoading || peopleError} onChange={event => setAssignee(event.target.value)}><option value="">Todos</option>{people.map(person => <option key={person.id} value={person.id}>{person.fullName}</option>)}</select></label>{assignee && <button className="link-button" onClick={() => setAssignee('')}>Limpar filtros</button>}{peopleLoading && <LoadingState size="sm" />}{peopleError && <div role="alert"><p>Não foi possível carregar os profissionais.</p><button className="secondary" onClick={() => void loadPeople()}>Tentar novamente</button></div>}</div>}
    </section>
    {error ? <section className="card error-panel" role="alert"><p>{error}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></section> : entries === null ? <LoadingState /> : <>
      {!entries.length && <section className="card empty-state" role="status"><h3>{view === 'day' ? assignee ? text.filteredDay : text.emptyDay : assignee ? text.filteredPeriod : text.emptyPeriod}</h3><p>Use os controles para consultar outras datas.</p></section>}
      {highlight && entries.some(entry => entry.id === highlight) && <p className="agenda-confirmation" role="status">{confirmation === 'rescheduled' ? 'Agendamento atualizado com sucesso.' : 'OS agendada com sucesso.'} O serviço está destacado abaixo.</p>}
      <div className="agenda-calendar-scroll">
      {view === 'month' && <div className="agenda-weekdays" aria-hidden="true">{weekdays.map(label => <strong key={label}>{label}</strong>)}</div>}
      <div className={`agenda-${view}`}>
        {period.days.map(day => {
          const daily = entries.filter(entry => scheduledOnDay(entry, day)).sort((a, b) => (eventHour(a, day) ?? -1) - (eventHour(b, day) ?? -1) || (a.scheduledStartTime ?? '').localeCompare(b.scheduledStartTime ?? '') || a.number.localeCompare(b.number))
          const hours = daily.map(entry => eventHour(entry, day)).filter((hour): hour is number => hour !== null)
          const firstHour = Math.min(7, ...hours), lastHour = Math.max(19, ...hours)
          const isToday = sameDay(day, today)
          const event = (entry: AgendaEntry) => <EventCard key={entry.id} day={day} entry={entry} go={go} highlighted={entry.id === highlight} />
          return <section key={day.getTime()} className={`agenda-day-section ${isToday ? 'agenda-today' : ''} ${view === 'month' && day.getMonth() !== date.getMonth() ? 'agenda-adjacent' : ''}`} aria-label={day.toLocaleDateString('pt-BR')}>
            {view !== 'day' && <header><button className="link-button agenda-day-heading" onClick={() => selectDay(day)}>{day.toLocaleDateString('pt-BR', { weekday: 'short', day: 'numeric', ...(view === 'week' ? { month: 'short' } : {}) })}{isToday && <span className="agenda-today-label">Hoje</span>}</button></header>}
            {view === 'day' ? <>
              <div className="agenda-unscheduled"><h3>Sem horário</h3>{daily.filter(entry => eventHour(entry, day) === null).map(event)}{!daily.some(entry => eventHour(entry, day) === null) && <p>Nenhum serviço sem horário definido.</p>}</div>
              <div className="agenda-timeline">{Array.from({ length: lastHour - firstHour + 1 }, (_, index) => firstHour + index).map(hour => <div className="agenda-time-row" key={hour}><time>{String(hour).padStart(2, '0')}:00</time><div>{daily.filter(entry => eventHour(entry, day) === hour).map(event)}</div></div>)}</div>
            </> : view === 'month' || view === 'week' ? <><div className="agenda-compact-list">{daily.slice(0, view === 'week' ? 4 : 3).map(entry => <CompactEvent key={entry.id} entry={entry} day={day} go={go} highlighted={entry.id === highlight} />)}</div>{daily.length > (view === 'week' ? 4 : 3) && <button className="link-button" onClick={() => selectDay(day)}>+ {daily.length - (view === 'week' ? 4 : 3)} serviços</button>}{view === 'week' && !daily.length && <p className="agenda-day-empty">—</p>}</> : null}
          </section>
        })}
      </div></div>
    </>}
  </div>
}

function CompactEvent({ entry, day, go, highlighted }: { entry: AgendaEntry; day: Date; go: Props['go']; highlighted: boolean }) {
  return <button id={`agenda-event-${entry.id}-${dateKey(day)}`} className={`agenda-compact secondary ${highlighted ? 'agenda-highlight' : ''}`} onClick={() => go(`/ordens-servico/${entry.id}`)} title={`${schedule(entry)} · ${entry.customerLegalNameSnapshot} · ${entry.assignedUserNameSnapshot ?? ''} · ${entry.services.map(service => service.serviceNameSnapshot).join(', ')}`}><strong>{entry.scheduledStartDate === dateKey(day) ? entry.scheduledStartTime?.slice(0, 5) ?? 'Sem horário' : text.continuation} · {entry.number}</strong><span>{entry.customerLegalNameSnapshot}</span><small>{entry.assignedUserNameSnapshot}</small><Badge status={entry.status}/></button>
}

function EventCard({ entry, day, go, highlighted }: { entry: AgendaEntry; day: Date; go: Props['go']; highlighted: boolean }) {
  return <article id={`agenda-event-${entry.id}-${dateKey(day)}`} className={`card agenda-event ${highlighted ? 'agenda-highlight' : ''}`}><div className="agenda-event-heading"><strong>{schedule(entry)}</strong><Badge status={entry.status} /></div><h4>{entry.number}</h4><p className="agenda-customer">{entry.customerLegalNameSnapshot}</p><ul className="agenda-services">{entry.services.slice(0, 2).map((service, index) => <li key={index}>{service.serviceCodeSnapshot} · {service.serviceNameSnapshot}</li>)}</ul>{entry.services.length > 2 && <p>+ {entry.services.length - 2} serviços</p>}<p><strong>Responsável:</strong> {entry.assignedUserNameSnapshot ?? 'Não atribuído'}</p><p><strong>Local:</strong> {entry.serviceAddressSnapshot}</p><button className="secondary" onClick={() => go(`/ordens-servico/${entry.id}`)}>Ver OS</button></article>
}
