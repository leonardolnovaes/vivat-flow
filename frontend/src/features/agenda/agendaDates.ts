export type AgendaView = 'day' | 'week' | 'month'

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate())
}
export function addDays(date: Date, amount: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + amount)
}
export function startOfWeek(date: Date): Date {
  return addDays(date, -((date.getDay() + 6) % 7))
}
export function visiblePeriod(date: Date, view: AgendaView): { from: Date; to: Date; days: Date[] } {
  const from = view === 'day' ? startOfDay(date) : view === 'week' ? startOfWeek(date) : startOfWeek(new Date(date.getFullYear(), date.getMonth(), 1))
  const last = view === 'month' ? new Date(date.getFullYear(), date.getMonth() + 1, 0) : date
  const to = view === 'day' ? addDays(from, 1) : view === 'week' ? addDays(from, 7) : addDays(startOfWeek(last), 7)
  const days: Date[] = []
  for (let day = from; day < to; day = addDays(day, 1)) days.push(day)
  return { from, to, days }
}
export function movePeriod(date: Date, view: AgendaView, direction: number): Date {
  return view === 'month' ? new Date(date.getFullYear(), date.getMonth() + direction, 1) : addDays(date, direction * (view === 'week' ? 7 : 1))
}
export function overlapsDay(start: string, end: string, day: Date): boolean {
  return new Date(start).getTime() < addDays(day, 1).getTime() && new Date(end).getTime() > startOfDay(day).getTime()
}
export function sameDay(left: Date, right: Date): boolean {
  return startOfDay(left).getTime() === startOfDay(right).getTime()
}
export function dateKey(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

const saoPauloDate = new Intl.DateTimeFormat('en-US', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' })
export function saoPauloDayStartUtc(date: Date): string {
  const key = dateKey(date)
  const midnightUtc = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()) / 1000
  // Find the first instant of this calendar day, including days when DST skips midnight.
  let low = midnightUtc - 12 * 3600, high = midnightUtc + 36 * 3600
  while (low < high) {
    const middle = Math.floor((low + high) / 2)
    const parts = Object.fromEntries(saoPauloDate.formatToParts(middle * 1000).map(part => [part.type, part.value]))
    const localKey = `${parts.year}-${parts.month}-${parts.day}`
    if (localKey < key) low = middle + 1
    else high = middle
  }
  return new Date(low * 1000).toISOString()
}

export type CalendarSchedule = { scheduledStartDate: string; scheduledStartTime: string | null; scheduledEndDate: string | null; scheduledEndTime: string | null }
export function scheduledOnDay(entry: CalendarSchedule, day: Date): boolean {
  const key = dateKey(day)
  const end = entry.scheduledEndDate ?? entry.scheduledStartDate
  return entry.scheduledStartDate <= key && (end > key || (end === key && (!entry.scheduledEndTime || entry.scheduledEndTime !== '00:00:00')))
}

export function eventHour(entry: CalendarSchedule, day: Date): number | null {
  if (!entry.scheduledStartTime) return null
  return entry.scheduledStartDate < dateKey(day) ? 0 : Number(entry.scheduledStartTime.slice(0, 2))
}
export function periodLabel(date: Date, view: AgendaView): string {
  const format = (value: Date) => value.toLocaleDateString('pt-BR', { day: 'numeric', month: 'long', year: 'numeric' })
  if (view === 'day') return format(date)
  if (view === 'month') return date.toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })
  const { from, to } = visiblePeriod(date, view)
  return `${format(from)} – ${format(addDays(to, -1))}`
}
