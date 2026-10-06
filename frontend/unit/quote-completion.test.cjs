const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const ts = require('typescript')
const Module = require('node:module')

require.extensions['.ts'] = (module, filename) => module._compile(ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS },
}).outputText, filename)

const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../components/auditPresentation') return { formatChangedFields: () => '' }
  return originalLoad.call(this, name, parent, isMain)
}
const { completionActions, synchronizeCustomerCompletion } = require('../src/features/quotes/quoteCompletion.ts')
const { formatBrlInput, normalizeBrlAmount } = require('../src/features/quotes/quoteFormat.ts')
Module._load = originalLoad

test('completion actions follow backend field keys', () => {
  assert.deepEqual(completionActions({ paymentType: ['Informe a condição de pagamento.'] }), { customer: false, quote: true })
  assert.deepEqual(completionActions({ contact: ['Contato obrigatório.'], unit: ['Unidade obrigatória.'] }), { customer: true, quote: false })
  assert.deepEqual(completionActions({ contact: ['Contato obrigatório.'], totalAmount: ['Valor obrigatório.'] }), { customer: true, quote: true })
  assert.deepEqual(completionActions({}), { customer: false, quote: false })
})

test('BRL presentation and decimal payload use the same amount', () => {
  for (const [entry, display, decimal] of [
    ['1', 'R$ 1,00', '1'], ['10', 'R$ 10,00', '10'],
    ['1000', 'R$ 1.000,00', '1000'], ['1500.5', 'R$ 1.500,50', '1500.5'],
    ['R$ 1.500,50', 'R$ 1.500,50', '1500.50'], ['1.500', 'R$ 1.500,00', '1500'],
  ]) {
    assert.equal(formatBrlInput(entry), display)
    assert.equal(normalizeBrlAmount(entry), decimal)
  }
  assert.equal(normalizeBrlAmount(''), null)
})

const quote = (version, status = 'Draft', serviceUnitId = null) => ({
  id: 'quote-id', customerId: 'customer-id', version, status, serviceUnitId,
  items: [{ id: 'item-1', serviceId: 'service-1' }],
  totalAmount: 100, paymentType: 'Cash', installmentCount: null,
  employeeCount: 10, riskDegree: 'One', responsibleUserId: null, notes: null,
})

test('Quote completion reloads a 409 version and links the saved unit on a valid retry without repeating customer writes', async () => {
  let parentQuote = quote('version-1')
  let validation = {}
  const updates = []
  const loadedQuotes = [quote('version-2'), quote('version-3', 'Draft', 'unit-1')]
  const api = {
    updateQuote: async (id, input, version) => {
      updates.push({ id, input, version })
      if (updates.length === 1) throw Object.assign(new Error('stale Quote'), { status: 409 })
      return quote('version-3', 'Draft', input.serviceUnitId)
    },
    getQuote: async () => loadedQuotes.shift(),
    getApprovalValidation: async () => ({ errors: { contact: ['Contato obrigatório.'] } }),
    quoteUpdated: current => { parentQuote = current },
    refreshed: (current, errors) => { parentQuote = current; validation = errors },
  }

  const conflict = await synchronizeCustomerCompletion(parentQuote, 'unit-1', api)
  assert.equal(conflict.status, 'conflict')
  assert.equal(updates[0].version, 'version-1')
  assert.equal(parentQuote.version, 'version-2')
  assert.deepEqual(validation, { contact: ['Contato obrigatório.'] })

  const saved = await synchronizeCustomerCompletion(parentQuote, 'unit-1', api)
  assert.equal(saved.status, 'saved')
  assert.deepEqual(updates.map(update => update.version), ['version-1', 'version-2'])
  assert.equal(updates[1].input.serviceUnitId, 'unit-1')
  assert.equal(parentQuote.version, 'version-3')
  assert.equal(parentQuote.serviceUnitId, 'unit-1')
})

test('Quote completion does not retry unit linking when a 409 reloads a non-Draft Quote', async () => {
  let parentQuote = quote('version-1')
  let updateCount = 0
  let reloadCount = 0
  const api = {
    updateQuote: async () => { updateCount++; throw Object.assign(new Error('stale Quote'), { status: 409 }) },
    getQuote: async () => { reloadCount++; return quote('version-2', 'AwaitingApproval') },
    getApprovalValidation: async () => ({ errors: {} }),
    quoteUpdated: current => { parentQuote = current },
    refreshed: current => { parentQuote = current },
  }

  const conflict = await synchronizeCustomerCompletion(parentQuote, 'unit-1', api)
  assert.equal(conflict.status, 'not-draft')
  assert.equal(parentQuote.status, 'AwaitingApproval')
  assert.equal(parentQuote.version, 'version-2')

  const retry = await synchronizeCustomerCompletion(parentQuote, 'unit-1', api)
  assert.equal(retry.status, 'not-draft')
  assert.equal(updateCount, 1)
  assert.equal(reloadCount, 2)
})
