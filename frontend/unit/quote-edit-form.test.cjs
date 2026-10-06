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

for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) => {
    const source = fs.readFileSync(filename, 'utf8')
    module._compile(ts.transpileModule(source, { compilerOptions: {
      module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
    } }).outputText, filename)
  }
}

class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; this.errors = {} }
  static async from(response) {
    const body = await response.json()
    return new ApiError(body.error, response.status)
  }
}

const item = (id, serviceId) => ({ id, serviceId, serviceNameSnapshot: serviceId })
const quote = (version, items, status = 'Draft') => ({
  id: 'quote-id', customerId: 'customer-id', version, items, status,
  totalAmount: 100, paymentType: 'Cash', installmentCount: null,
  employeeCount: 10, riskDegree: 'One', serviceUnitId: null,
  responsibleUserId: null, notes: null,
})
let serverQuote = quote('version-1', [item('item-1', 'service-1')])
let releasePut
let getCount = 0
const puts = []
const quoteLoads = []
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api' && parent?.filename.replaceAll('\\', '/').includes('/features/quotes/')) {
    return { ApiError, request: async (path, init) => {
      if (path === '/api/quotes/professionals') return { ok: true, json: async () => [] }
      if (path.startsWith('/api/quotes/') && !init?.method) {
        getCount++
        const loaded = quoteLoads.shift() ?? Promise.resolve(serverQuote)
        return { ok: true, json: async () => await loaded }
      }
      if (path === '/api/quotes/quote-id' && init.method === 'PUT') {
        const body = JSON.parse(init.body)
        puts.push(body)
        if (releasePut) await new Promise(resolve => { releasePut = resolve })
        if (body.expectedVersion !== serverQuote.version) {
          return { ok: false, status: 409, json: async () => ({ error: 'Este orçamento foi alterado.' }) }
        }
        serverQuote = quote(`version-${puts.length + 1}`, body.items.map((value, index) =>
          item(value.id ?? `item-${index + 1}`, value.serviceId)))
        return { ok: true, json: async () => serverQuote }
      }
      throw new Error(`Unexpected request: ${path}`)
    } }
  }
  if (name === './quoteFormat' && parent?.filename.endsWith('quoteApi.ts')) {
    return { normalizeBrlAmount: value => value.trim() || null }
  }
  if (name === './quoteFormat' && parent?.filename.endsWith('QuotesRoutes.tsx')) {
    return { formatBrlInput: value => value }
  }
  if (name === './QuoteCommercialViews') return { QuoteDetailView: () => null, QuoteWorkspace: () => null }
  if (name === './QuickCustomerDialog') return { QuickCustomerDialog: () => null }
  if (name === '../../components/LoadingState') return { LoadingState: () => null }
  if (name === '../customers/customerApi') return {
    getCustomer: async () => ({ id: 'customer-id', legalName: 'Cliente', cnpj: '04252011000110', units: [] }),
    listCustomers: async () => ({ items: [] }), createCustomer: async () => null,
  }
  if (name === '../services/serviceApi') return {
    listServiceLines: async () => [{ id: 'line-1', code: 'SST', name: 'Saúde' }],
    listServices: async () => ({ items: [
      { id: 'service-1', code: 'A', name: 'Serviço A', serviceLineId: 'line-1' },
      { id: 'service-2', code: 'B', name: 'Serviço B', serviceLineId: 'line-1' },
    ] }),
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { QuoteEditForm } = require('../src/features/quotes/QuotesRoutes.tsx')
Module._load = originalLoad

const flush = () => new Promise(resolve => setImmediate(resolve))
async function render() {
  const container = document.getElementById('root')
  const root = createRoot(container)
  const navigation = []
  const props = {
    id: 'quote-id', path: '/orcamentos/quote-id/editar',
    go: path => navigation.push(path), onSessionExpired: () => {},
  }
  await act(async () => {
    root.render(React.createElement(QuoteEditForm, props))
    await flush()
  })
  return { container, root, navigation, props }
}
function submit(container) {
  container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }))
}

test('the Quote edit component blocks an in-flight save and redirects to detail after success', async () => {
  serverQuote = quote('version-1', [item('item-1', 'service-1')])
  puts.length = 0
  getCount = 0
  const { container, root, navigation } = await render()
  assert.equal(container.querySelector('.quote-form-actions button:last-child')?.disabled, false)
  assert.equal(getCount, 1)

  await act(async () => {
    const select = container.querySelector('.quote-add-service select')
    select.value = 'service-2'
    select.dispatchEvent(new window.Event('change', { bubbles: true }))
  })
  releasePut = true
  act(() => submit(container))
  assert.equal(container.querySelector('form').hasAttribute('inert'), true)
  assert.equal(container.querySelector('.quote-form-actions button:last-child')?.disabled, true)
  await act(async () => { releasePut(); releasePut = null; await flush() })

  assert.equal(puts[0].expectedVersion, 'version-1')
  assert.deepEqual(navigation, ['/orcamentos/quote-id'])
  await act(async () => root.unmount())
})

test('a genuine stale conflict keeps the edit blocked until latest Draft data is reloaded', async () => {
  serverQuote = quote('version-1', [item('item-1', 'service-1')])
  puts.length = 0
  getCount = 0
  const { container, root, navigation } = await render()

  serverQuote = quote('version-remote', serverQuote.items)
  await act(async () => { submit(container); await flush() })

  assert.equal(puts[0].expectedVersion, 'version-1')
  assert.equal(navigation.length, 0)
  assert.match(container.querySelector('.error-panel').textContent, /Carregue os dados mais recentes/)
  assert.equal(container.querySelector('form').hasAttribute('inert'), true)

  await act(async () => { container.querySelector('.error-panel button').click(); await flush() })
  assert.equal(container.querySelector('.error-panel'), null)
  assert.equal(container.querySelector('form').hasAttribute('inert'), false)
  assert.equal(getCount, 2)
  await act(async () => root.unmount())
})

test('a Quote that is no longer Draft leaves the edit route instead of entering a 409 reload loop', async () => {
  serverQuote = quote('version-remote', [item('item-1', 'service-1')], 'AwaitingApproval')
  puts.length = 0
  getCount = 0
  const { root, navigation } = await render()

  assert.deepEqual(navigation, ['/orcamentos/quote-id'])
  assert.equal(puts.length, 0)
  await act(async () => root.unmount())
})

test('an obsolete non-Draft response cannot redirect after navigation to another quote edit route', async () => {
  let resolveOlder
  quoteLoads.push(new Promise(resolve => { resolveOlder = resolve }), Promise.resolve({ ...quote('version-new', [item('item-new', 'service-1')]), id: 'quote-new' }))
  const { root, navigation, props } = await render()

  await act(async () => {
    root.render(React.createElement(QuoteEditForm, { ...props, id: 'quote-new', path: '/orcamentos/quote-new/editar' }))
    await flush()
  })
  await act(async () => { resolveOlder(quote('version-old', [item('item-old', 'service-1')], 'AwaitingApproval')); await flush() })

  assert.deepEqual(navigation, [])
  await act(async () => root.unmount())
})

test('a pending non-Draft load cannot redirect after leaving the edit screen', async () => {
  let resolveLoad
  quoteLoads.push(new Promise(resolve => { resolveLoad = resolve }))
  const { root, navigation } = await render()
  await act(async () => root.unmount())
  await act(async () => { resolveLoad(quote('version-old', [item('item-old', 'service-1')], 'AwaitingApproval')); await flush() })
  assert.deepEqual(navigation, [])
})
