import type { Planning } from './types'

export const blankPlanning: Planning = { assignedUserId: '', scheduledStartDate: '', scheduledStartTime: '', scheduledEndDate: '', scheduledEndTime: '', operationalNotes: '' }

export const calendarPlanningChanged = (before: Planning, after: Planning) => (['assignedUserId', 'scheduledStartDate', 'scheduledStartTime', 'scheduledEndDate', 'scheduledEndTime'] as const).some(key => before[key] !== after[key])

export function plannedLabel(date: string | null, time: string | null): string {
  if (!date) return 'Não informado'
  const [year, month, day] = date.split('-')
  return `${day}/${month}/${year}${time ? ` ${time.slice(0, 5)}` : ' · Sem horário'}`
}

export function validatePlanning(form: Planning): Record<string, string[]> {
  const errors: Record<string, string[]> = {}
  for (const prefix of ['scheduledStart', 'scheduledEnd'] as const) {
    const date = form[`${prefix}Date`], time = form[`${prefix}Time`]
    if (date && (!/^\d{4}-\d{2}-\d{2}$/.test(date) || Number.isNaN(Date.parse(date)) || new Date(`${date}T12:00:00Z`).toISOString().slice(0, 10) !== date)) errors[`${prefix}Date`] = ['Informe uma data válida.']
    if (time && !/^([01]\d|2[0-3]):[0-5]\d$/.test(time)) errors[`${prefix}Time`] = ['Informe um horário válido.']
    if (time && !date) errors[`${prefix}Date`] = ['Informe a data correspondente ao horário.']
  }
  if (form.scheduledStartDate && form.scheduledEndDate && (form.scheduledEndDate < form.scheduledStartDate ||
    (form.scheduledEndDate === form.scheduledStartDate && form.scheduledStartTime && form.scheduledEndTime && form.scheduledEndTime <= form.scheduledStartTime))) errors.scheduledEndDate = ['O término deve ser posterior ao início.']
  if (form.operationalNotes.trim().length > 2000) errors.operationalNotes = ['Use no máximo 2.000 caracteres.']
  return errors
}

export function planningPayload(form: Planning) {
  return { assignedUserId: form.assignedUserId || null, scheduledStartDate: form.scheduledStartDate || null,
    scheduledStartTime: form.scheduledStartTime ? `${form.scheduledStartTime}:00` : null,
    scheduledEndDate: form.scheduledEndDate || null, scheduledEndTime: form.scheduledEndTime ? `${form.scheduledEndTime}:00` : null,
    operationalNotes: form.operationalNotes.trim() || null }
}
