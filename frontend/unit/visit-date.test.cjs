const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const ts = require('typescript')

const source = fs.readFileSync('src/features/quotes/visitDate.ts', 'utf8')
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const moduleRef = { exports: {} }
new Function('module', 'exports', compiled)(moduleRef, moduleRef.exports)
const { parseLocalDateTime } = moduleRef.exports

test('rejects nonexistent calendar dates and invalid local times', () => {
  assert.equal(parseLocalDateTime('2026-09-31T22:00'), null)
  assert.equal(parseLocalDateTime('2026-02-29T09:00'), null)
  assert.equal(parseLocalDateTime('2026-09-30T25:00'), null)
})

test('keeps a valid local date and time', () => {
  const result = parseLocalDateTime('2026-09-30T22:00')
  assert.ok(result)
  assert.equal(result.getFullYear(), 2026)
  assert.equal(result.getMonth(), 8)
  assert.equal(result.getDate(), 30)
  assert.equal(result.getHours(), 22)
})
