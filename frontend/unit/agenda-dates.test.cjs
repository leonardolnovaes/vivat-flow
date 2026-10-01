const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const ts = require('typescript')
const source = fs.readFileSync('src/features/agenda/agendaDates.ts', 'utf8')
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const moduleRef = { exports: {} }
new Function('module', 'exports', compiled)(moduleRef, moduleRef.exports)
const { visiblePeriod, movePeriod, overlapsDay, sameDay, periodLabel } = moduleRef.exports
const { scheduledOnDay, eventHour } = moduleRef.exports

test('date-only schedules include their end day without inventing a time', () => {
  const entry = { scheduledStartDate: '2026-10-01', scheduledStartTime: null, scheduledEndDate: '2026-10-02', scheduledEndTime: null }
  assert.equal(scheduledOnDay(entry, new Date(2026, 9, 1)), true)
  assert.equal(scheduledOnDay(entry, new Date(2026, 9, 2)), true)
  assert.equal(scheduledOnDay(entry, new Date(2026, 9, 3)), false)
  assert.equal(eventHour(entry, new Date(2026, 9, 1)), null)
  assert.equal(scheduledOnDay({ ...entry, scheduledEndDate: null }, new Date(2026, 9, 2)), false)
  assert.equal(scheduledOnDay({ ...entry, scheduledEndTime: '00:00:00' }, new Date(2026, 9, 2)), false)
  assert.equal(eventHour({ ...entry, scheduledStartTime: '23:00:00' }, new Date(2026, 9, 2)), 0)
})

test('day boundaries use local midnight and an exclusive next midnight', () => {
  const day = new Date(2026, 9, 1, 23, 59)
  const { from, to, days } = visiblePeriod(day, 'day')
  assert.equal(from.getHours(), 0)
  assert.equal(from.getDate(), 1)
  assert.equal(to.getDate(), 2)
  assert.equal(to.getHours(), 0)
  assert.equal(days.length, 1)
  assert.equal(new Date(from.toISOString()).getTime(), from.getTime())
  assert.equal(overlapsDay(new Date(2026, 8, 30, 23).toISOString(), new Date(2026, 9, 1, 1).toISOString(), day), true)
  assert.equal(overlapsDay(new Date(2026, 8, 30, 23).toISOString(), from.toISOString(), day), false)
  assert.equal(overlapsDay(to.toISOString(), new Date(2026, 9, 2, 1).toISOString(), day), false)
})
test('week starts Monday and includes Sunday across month boundaries', () => {
  const { from, to, days } = visiblePeriod(new Date(2026, 9, 4), 'week')
  assert.equal(from.getDay(), 1)
  assert.equal(from.getMonth(), 8)
  assert.equal(from.getDate(), 28)
  assert.equal(to.getDate(), 5)
  assert.equal(days.length, 7)
})
test('month grids contain complete weeks, leap days and adjacent days', () => {
  for (const date of [new Date(2026, 9, 1), new Date(2026, 10, 1), new Date(2028, 1, 29)]) {
    const { from, to, days } = visiblePeriod(date, 'month')
    assert.equal(from.getDay(), 1)
    assert.equal(to.getDay(), 1)
    assert.equal(days.length % 7, 0)
    assert.ok(days.length <= 42)
    assert.ok(days.some(day => sameDay(day, date)))
    assert.ok(from <= new Date(date.getFullYear(), date.getMonth(), 1))
    assert.ok(to > new Date(date.getFullYear(), date.getMonth() + 1, 0))
  }
})
test('navigation handles year rollover and month-end without skipping months', () => {
  assert.equal(movePeriod(new Date(2026, 0, 31), 'month', 1).getMonth(), 1)
  assert.equal(movePeriod(new Date(2026, 11, 31), 'day', 1).getFullYear(), 2027)
  assert.equal(movePeriod(new Date(2026, 9, 1), 'week', -1).getDate(), 24)
  assert.match(periodLabel(new Date(2026, 9, 1), 'month'), /outubro de 2026/)
})
test('local boundaries survive daylight-saving transitions', () => {
  const original = process.env.TZ
  process.env.TZ = 'America/New_York'
  try {
    const spring = visiblePeriod(new Date(2026, 2, 8, 12), 'day')
    const autumn = visiblePeriod(new Date(2026, 10, 1, 12), 'day')
    assert.equal((spring.to - spring.from) / 3600000, 23)
    assert.equal((autumn.to - autumn.from) / 3600000, 25)
    assert.equal(spring.from.getHours(), 0)
    assert.equal(spring.to.getHours(), 0)
  } finally { if (original === undefined) delete process.env.TZ; else process.env.TZ = original }
})
