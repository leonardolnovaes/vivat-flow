const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const ts = require('typescript')
const source = fs.readFileSync('src/features/workOrders/planning.ts', 'utf8')
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const ref = { exports: {} }
new Function('module', 'exports', compiled)(ref, ref.exports)
const { blankPlanning, calendarPlanningChanged, validatePlanning, planningPayload, plannedLabel } = ref.exports

test('only calendar planning changes trigger rescheduling', () => {
  const before = { ...blankPlanning, assignedUserId: 'worker', scheduledStartDate: '2026-10-01' }
  assert.equal(calendarPlanningChanged(before, { ...before, operationalNotes: 'Nova observação' }), false)
  for (const key of ['assignedUserId', 'scheduledStartDate', 'scheduledStartTime', 'scheduledEndDate', 'scheduledEndTime']) {
    assert.equal(calendarPlanningChanged(before, { ...before, [key]: 'changed' }), true, key)
  }
})

test('draft allows incomplete planning and date-only payload preserves absent time/end', () => {
  assert.deepEqual(validatePlanning(blankPlanning), {})
  const form = { ...blankPlanning, assignedUserId: 'worker', scheduledStartDate: '2026-10-01' }
  assert.deepEqual(validatePlanning(form), {})
  const payload = planningPayload(form)
  assert.equal(payload.scheduledStartDate, '2026-10-01')
  assert.equal(payload.scheduledStartTime, null)
  assert.equal(payload.scheduledEndDate, null)
  assert.equal(payload.scheduledEndTime, null)
  assert.equal(plannedLabel(payload.scheduledStartDate, payload.scheduledStartTime), '01/10/2026 · Sem horário')
})
test('explicit time, chronology and friendly field validation', () => {
  const form = { ...blankPlanning, scheduledStartDate: '2026-10-01', scheduledStartTime: '23:00', scheduledEndDate: '2026-10-02', scheduledEndTime: '01:00' }
  assert.deepEqual(validatePlanning(form), {})
  assert.equal(planningPayload(form).scheduledStartTime, '23:00:00')
  assert.ok(validatePlanning({ ...form, scheduledEndDate: '2026-10-01' }).scheduledEndDate)
  assert.ok(validatePlanning({ ...form, scheduledStartDate: '' }).scheduledStartDate)
  assert.ok(validatePlanning({ ...blankPlanning, scheduledStartDate: '2026-02-30' }).scheduledStartDate)
  assert.ok(validatePlanning({ ...form, scheduledStartTime: '25:00' }).scheduledStartTime)
})
