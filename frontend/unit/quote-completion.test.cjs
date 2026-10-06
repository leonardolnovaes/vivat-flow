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
const { completionActions } = require('../src/features/quotes/quoteCompletion.ts')
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
