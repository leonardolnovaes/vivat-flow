import type { WorkOrderStatus } from './types'

export const labels: Record<WorkOrderStatus, string> = { Draft: 'Rascunho', Scheduled: 'Agendada', InProgress: 'Em andamento', Completed: 'Concluída', Cancelled: 'Cancelada' }
export function Badge({ status }: { status: WorkOrderStatus }) { return <span className={`wo-status wo-status-${status}`}>{labels[status]}</span> }
