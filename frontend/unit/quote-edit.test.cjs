const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const Module = require('node:module')
const ts = require('typescript')

require.extensions['.ts'] = (module, filename) => {
  const source = fs.readFileSync(filename, 'utf8')
  module._compile(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, filename)
}

let responseQuote
const requests = []
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api' && parent?.filename.endsWith('quoteApi.ts')) {
    return {
      request: async (path, init) => {
        requests.push({ path, init, body: JSON.parse(init.body) })
        return { ok: true, json: async () => responseQuote }
      },
    }
  }
  if (name === './quoteFormat' && parent?.filename.endsWith('quoteApi.ts')) {
    return { normalizeBrlAmount: value => value.trim() || null }
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { editState } = require('../src/features/quotes/quoteEdit.ts')
const { updateQuote } = require('../src/features/quotes/quoteApi.ts')
Module._load = originalLoad

const quote = (version, items) => ({
  id: 'quote-id', customerId: 'customer-id', version, items,
  totalAmount: 100, paymentType: 'Cash', installmentCount: null,
  employeeCount: 10, riskDegree: 'One', serviceUnitId: null,
  responsibleUserId: null, notes: null,
})
const item = (id, serviceId) => ({ id, serviceId, serviceNameSnapshot: serviceId })

test('an unloaded Quote cannot be updated with the default empty version', () => {
  requests.length = 0
  assert.throws(() => updateQuote('quote-id', editState(quote('', [])).form, ''), /version is required/)
  assert.equal(requests.length, 0)
})

test('loaded and saved Quote versions and item IDs drive consecutive service edits', async () => {
  requests.length = 0
  const loaded = editState(quote('version-1', [item('item-1', 'service-1')]))
  loaded.form.items.push({ serviceId: 'service-2' })
  responseQuote = quote('version-2', [item('item-1', 'service-1'), item('item-2', 'service-2')])

  const saved = editState(await updateQuote('quote-id', loaded.form, loaded.version))
  assert.equal(requests[0].path, '/api/quotes/quote-id')
  assert.equal(requests[0].init.method, 'PUT')
  assert.equal(requests[0].body.expectedVersion, 'version-1')
  assert.deepEqual(requests[0].body.items, [{ id: 'item-1', serviceId: 'service-1' }, { serviceId: 'service-2' }])

  saved.form.items = saved.form.items.filter(value => value.serviceId !== 'service-1')
  responseQuote = quote('version-3', [item('item-2', 'service-2')])
  const savedAgain = editState(await updateQuote('quote-id', saved.form, saved.version))
  assert.equal(requests[1].body.expectedVersion, 'version-2')
  assert.deepEqual(requests[1].body.items, [{ id: 'item-2', serviceId: 'service-2' }])
  assert.equal(savedAgain.version, 'version-3')
})
