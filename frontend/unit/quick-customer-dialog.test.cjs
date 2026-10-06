const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const Module = require('node:module')
const ts = require('typescript')
const { JSDOM } = require('jsdom')

const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost' })
global.window = dom.window
global.document = dom.window.document
global.navigator = dom.window.navigator
global.HTMLElement = dom.window.HTMLElement
global.IS_REACT_ACT_ENVIRONMENT = true
const React = require('react')
const { act } = React
const { createRoot } = require('react-dom/client')
for (const extension of ['.ts', '.tsx']) require.extensions[extension] = (module, filename) => module._compile(ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText, filename)

let customer
const writes = []
let failContact = false
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api') return { ApiError: class ApiError extends Error {} }
  if (name === '../customers/customerApi') return {
    getCustomer: async () => customer,
    createCustomer: async () => { throw new Error('Existing customer expected') },
    createContact: async (id, input, version) => {
      assert.equal(id, customer.id); assert.equal(version, customer.version)
      if (failContact) throw new Error('contact persistence failed')
      writes.push({ kind: 'contact', input })
      const contact = { id: 'contact-id', customerId: id, isActive: true, ...input }
      customer = { ...customer, version: 'v2', contacts: [contact] }
      return { contact, version: customer.version }
    },
    updateContact: async (id, contactId, input, version) => {
      assert.equal(id, customer.id); assert.equal(contactId, customer.contacts[0].id); assert.equal(version, customer.version)
      writes.push({ kind: 'contact-update', input })
      const contact = { ...customer.contacts[0], ...input }
      customer = { ...customer, version: 'v2', contacts: [contact] }
      return { contact, version: customer.version }
    },
    createUnit: async (id, input, version) => {
      assert.equal(id, customer.id); assert.equal(version, customer.version)
      writes.push({ kind: 'unit', input })
      const unit = { id: 'unit-id', customerId: id, isActive: true, ...input }
      customer = { ...customer, version: 'v3', units: [unit] }
      return { unit, version: customer.version }
    },
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { QuickCustomerDialog } = require('../src/features/quotes/QuickCustomerDialog.tsx')
const { synchronizeCustomerCompletion } = require('../src/features/quotes/quoteCompletion.ts')
Module._load = originalLoad

const flush = () => new Promise(resolve => setImmediate(resolve))
function fill(container, label, value) {
  const input = [...container.querySelectorAll('label')].find(item => item.textContent.includes(label)).querySelector('input')
  Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, value)
  input.dispatchEvent(new window.Event('input', { bubbles: true }))
}

test('quick completion persists a contact and unit using the refreshed customer version', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [], units: [] }
  writes.length = 0
  failContact = false
  const saved = []
  const root = createRoot(document.getElementById('root'))
  await act(async () => { root.render(React.createElement(QuickCustomerDialog, { customerId: customer.id, close: () => {}, saved: (...args) => saved.push(args) })); await flush() })
  const container = document.getElementById('root')
  await act(async () => {
    for (const [label, value] of [['Nome do contato', 'Ana'], ['E-mail', 'ana@example.com'], ['Nome da unidade', 'Matriz'], ['Rua', 'Rua A'], ['Número', '10'], ['Cidade', 'São Paulo'], ['UF', 'SP']]) fill(container, label, value)
  })
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.deepEqual(writes.map(write => write.kind), ['contact', 'unit'])
  assert.equal(writes[0].input.email, 'ana@example.com')
  assert.equal(writes[1].input.city, 'São Paulo')
  assert.equal(saved[0][1], 'unit-id')
  assert.equal(customer.contacts.length, 1)
  assert.equal(customer.units.length, 1)
  await act(async () => root.unmount())
})

test('quick completion fills an existing contact without creating a duplicate', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [{ id: 'contact-id', name: 'Ana', email: null, phone: '123', roleOrDepartment: 'Compras', isPrimary: true, isActive: true }], units: [{ id: 'unit-id', isActive: true }] }
  writes.length = 0
  failContact = false
  const saved = []
  const root = createRoot(document.getElementById('root'))
  await act(async () => { root.render(React.createElement(QuickCustomerDialog, { customerId: customer.id, close: () => {}, saved: (...args) => saved.push(args) })); await flush() })
  const container = document.getElementById('root')
  assert.equal(container.querySelectorAll('input[type="checkbox"]').length, 0)
  await act(async () => fill(container, 'E-mail', 'ana@example.com'))
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.deepEqual(writes.map(write => write.kind), ['contact-update'])
  assert.equal(writes[0].input.roleOrDepartment, 'Compras')
  assert.equal(customer.contacts.length, 1)
  assert.equal(saved.length, 1)
  await act(async () => root.unmount())
})

async function renderDialog(saved, onClose = () => {}, retryLabel) {
  const root = createRoot(document.getElementById('root'))
  const close = () => { onClose(); root.render(null) }
  const saveAndClose = async (...args) => { await saved(...args); close() }
  await act(async () => { root.render(React.createElement(QuickCustomerDialog, { customerId: customer.id, close, saved: saveAndClose, retryLabel })); await flush() })
  const container = document.getElementById('root')
  await act(async () => {
    for (const [label, value] of [['Nome do contato', 'Ana'], ['E-mail', 'ana@example.com'], ['Nome da unidade', 'Matriz'], ['Rua', 'Rua A'], ['Número', '10'], ['Cidade', 'São Paulo'], ['UF', 'SP']]) fill(container, label, value)
  })
  return { root, container }
}

test('customer persistence failure is reported as a save failure', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [], units: [] }
  writes.length = 0
  failContact = true
  const { root, container } = await renderDialog(async () => {})
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.match(container.querySelector('[role="alert"]').textContent, /Não foi possível salvar o cliente/)
  assert.equal(container.querySelector('[role="dialog"]') !== null, true)
  assert.equal(writes.length, 0)
  await act(async () => root.unmount())
})

test('refresh failure after successful customer writes is reported as a synchronization failure', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [], units: [] }
  writes.length = 0
  failContact = false
  let failRefresh = true
  let closed = false
  const { root, container } = await renderDialog(async () => {
    if (failRefresh) {
      failRefresh = false
      throw new Error('Os dados foram salvos, mas não foi possível atualizar a tela. Tente recarregar.')
    }
  }, () => { closed = true })
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.equal(writes.length, 2)
  assert.match(container.querySelector('[role="alert"]').textContent, /Os dados foram salvos, mas não foi possível atualizar a tela/)
  assert.doesNotMatch(container.querySelector('[role="alert"]').textContent, /Não foi possível salvar o cliente/)
  assert.equal(container.querySelector('[role="dialog"]') !== null, true)
  assert.equal(container.querySelector('input[type="email"]').value, 'ana@example.com')
  assert.equal(container.querySelector('input[type="email"]').disabled, true)
  assert.equal(container.querySelector('button').textContent, 'Tentar atualizar tela')
  assert.equal(closed, false)
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.equal(writes.length, 2)
  assert.equal(closed, true)
  assert.equal(container.querySelector('[role="dialog"]'), null)
  await act(async () => root.unmount())
})

test('successful customer writes and refresh callback complete normally', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [], units: [] }
  writes.length = 0
  failContact = false
  let completed = false
  const { root, container } = await renderDialog(async () => { completed = true })
  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.equal(completed, true)
  assert.equal(writes.length, 2)
  assert.equal(container.querySelector('[role="alert"]'), null)
  assert.equal(container.querySelector('[role="dialog"]'), null)
  await act(async () => root.unmount())
})

test('Quote completion recovers a 409 and retries with the refreshed version without repeating contact or unit writes', async () => {
  customer = { id: 'customer-id', version: 'v1', contacts: [], units: [] }
  writes.length = 0
  failContact = false
  let parentQuote = {
    id: 'quote-id', customerId: customer.id, version: 'quote-v1', status: 'Draft', serviceUnitId: null,
    items: [{ id: 'item-1', serviceId: 'service-1' }], totalAmount: 100,
    paymentType: 'Cash', installmentCount: null, employeeCount: 10,
    riskDegree: 'One', responsibleUserId: null, notes: null,
  }
  const updateVersions = []
  const currentQuotes = [
    { ...parentQuote, version: 'quote-v2' },
    { ...parentQuote, version: 'quote-v3', serviceUnitId: 'unit-id' },
  ]
  let quoteFetches = 0
  let validationRefreshes = 0
  const api = {
    updateQuote: async (id, input, version) => {
      updateVersions.push(version)
      if (updateVersions.length === 1) throw Object.assign(new Error('stale Quote'), { status: 409 })
      return { ...parentQuote, ...input, version: 'quote-v3' }
    },
    getQuote: async () => { quoteFetches++; return currentQuotes.shift() },
    getApprovalValidation: async () => { validationRefreshes++; return { errors: { contact: ['Contato obrigatório.'] } } },
    quoteUpdated: loaded => { parentQuote = loaded },
    refreshed: loaded => { parentQuote = loaded },
  }
  let closed = false
  const { root, container } = await renderDialog(async (savedCustomer, unitId) => {
    const result = await synchronizeCustomerCompletion(parentQuote, unitId || savedCustomer.units.find(item => item.isActive)?.id, api)
    if (result.status === 'conflict') throw new Error('Os dados do cliente já foram salvos. O orçamento mudou e os dados atuais foram recarregados. Você pode tentar novamente se ele ainda estiver como rascunho.')
    if (result.status === 'not-draft') throw new Error('Os dados do cliente já foram salvos, mas o orçamento não está mais como rascunho.')
  }, () => { closed = true }, 'Tentar vincular unidade')

  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.deepEqual(writes.map(write => write.kind), ['contact', 'unit'])
  assert.deepEqual(updateVersions, ['quote-v1'])
  assert.equal(quoteFetches, 1)
  assert.equal(validationRefreshes, 1)
  assert.equal(parentQuote.version, 'quote-v2')
  assert.match(container.querySelector('[role="alert"]').textContent, /já foram salvos.*mudou.*recarregados/i)
  assert.equal(container.querySelector('button').textContent, 'Tentar vincular unidade')
  assert.equal(closed, false)

  await act(async () => { container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); await flush() })
  assert.deepEqual(writes.map(write => write.kind), ['contact', 'unit'])
  assert.deepEqual(updateVersions, ['quote-v1', 'quote-v2'])
  assert.equal(quoteFetches, 2)
  assert.equal(validationRefreshes, 2)
  assert.equal(parentQuote.version, 'quote-v3')
  assert.equal(parentQuote.serviceUnitId, 'unit-id')
  assert.equal(closed, true)
  assert.equal(container.querySelector('[role="dialog"]'), null)
  await act(async () => root.unmount())
})
