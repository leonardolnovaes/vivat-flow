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
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api') return { ApiError: class ApiError extends Error {} }
  if (name === '../customers/customerApi') return {
    getCustomer: async () => customer,
    createCustomer: async () => { throw new Error('Existing customer expected') },
    createContact: async (id, input, version) => {
      assert.equal(id, customer.id); assert.equal(version, customer.version)
      writes.push({ kind: 'contact', input })
      customer = { ...customer, version: 'v2', contacts: [{ id: 'contact-id', isActive: true, ...input }] }
    },
    updateContact: async (id, contactId, input, version) => {
      assert.equal(id, customer.id); assert.equal(contactId, customer.contacts[0].id); assert.equal(version, customer.version)
      writes.push({ kind: 'contact-update', input })
      customer = { ...customer, version: 'v2', contacts: [{ ...customer.contacts[0], ...input }] }
    },
    createUnit: async (id, input, version) => {
      assert.equal(id, customer.id); assert.equal(version, customer.version)
      writes.push({ kind: 'unit', input })
      customer = { ...customer, version: 'v3', units: [{ id: 'unit-id', isActive: true, ...input }] }
      return { unit: { id: 'unit-id' } }
    },
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { QuickCustomerDialog } = require('../src/features/quotes/QuickCustomerDialog.tsx')
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
