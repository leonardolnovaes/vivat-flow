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
global.IS_REACT_ACT_ENVIRONMENT = true
const React = require('react')
const { act } = React
const { createRoot } = require('react-dom/client')
for (const extension of ['.ts', '.tsx']) {
  require.extensions[extension] = (module, filename) => {
    module._compile(ts.transpileModule(fs.readFileSync(filename, 'utf8'), { compilerOptions: {
      module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX,
    } }).outputText, filename)
  }
}
require.extensions['.css'] = () => {}
class ApiError extends Error { constructor(status) { super('Request failed'); this.status = status } }
let calls = [], response = [], fail = false, peopleCalls = 0, expired = 0
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api') return { ApiError }
  if (name === '../../components/LoadingState') return { LoadingState: () => React.createElement('p', { role: 'status' }, 'Loading') }
  if (name === '../workOrders/workOrderApi') return {
    getAgenda: async query => { calls.push(new URLSearchParams(query)); if (fail) throw new ApiError(fail); return typeof response === 'function' ? response() : response },
    getEligibleAssignees: async () => { peopleCalls++; return [{ id: 'worker', fullName: 'Profissional ativo' }] },
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { Agenda } = require('../src/features/agenda/Agenda.tsx')
Module._load = originalLoad
const flush = () => new Promise(resolve => setImmediate(resolve))
async function render(roles) {
  calls = []; peopleCalls = 0; expired = 0
  const root = createRoot(document.getElementById('root'))
  const paths = []
  await act(async () => { root.render(React.createElement(Agenda, { user: { roles }, go: path => paths.push(path), onSessionExpired: () => { expired++ } })); await flush() })
  return { root, paths }
}
async function click(label) {
  const button = [...document.querySelectorAll('button')].find(button => button.textContent === label || button.getAttribute('aria-label') === label)
  assert.ok(button, label)
  await act(async () => { button.click(); await flush() })
}
const entry = index => {
  const start = new Date(); start.setHours(9, 0, 0, 0)
  const end = new Date(start); end.setHours(11)
  return { id: `order-${index}`, number: `OS-${index}`, status: 'Scheduled', scheduledStart: start.toISOString(), scheduledEnd: end.toISOString(),
    customerLegalNameSnapshot: 'Cliente histórico', serviceAddressSnapshot: 'Local histórico', assignedUserNameSnapshot: 'Profissional histórico',
    services: [1, 2, 3, 4].map(index => ({ serviceCodeSnapshot: `S-${index}`, serviceNameSnapshot: `Serviço ${index}` })) }
}
test('management filter, view navigation, historical cards and month overflow work', async () => {
  response = [1, 2, 3, 4].map(entry); fail = false
  const { root, paths } = await render(['MANAGER'])
  try {
    assert.equal(peopleCalls, 1)
    assert.match(document.body.textContent, /Cliente histórico/)
    assert.match(document.body.textContent, /Local histórico/)
    assert.match(document.body.textContent, /Agendada/)
    assert.match(document.body.textContent, /\+ 2 serviços/)
    await click('Ver OS'); assert.equal(paths[0], '/ordens-servico/order-1')
    const select = document.querySelector('select')
    await act(async () => { select.value = 'worker'; select.dispatchEvent(new dom.window.Event('change', { bubbles: true })); await flush() })
    assert.equal(calls.at(-1).get('assignedUserId'), 'worker')
    await click('Limpar filtros'); assert.equal(calls.at(-1).get('assignedUserId'), null)
    await click('Semana'); assert.equal(document.querySelectorAll('.agenda-day-section').length, 7)
    const previousFrom = calls.at(-1).get('from')
    await click('Próximo período'); assert.ok(new Date(calls.at(-1).get('from')) > new Date(previousFrom))
    await click('Período anterior'); assert.equal(calls.at(-1).get('from'), previousFrom)
    await click('Hoje'); await click('Mês')
    assert.ok(document.querySelectorAll('.agenda-day-section').length >= 28)
    await click('+ 1 serviços'); assert.equal(document.querySelectorAll('.agenda-day-section').length, 1)
  } finally { await act(async () => root.unmount()) }
})
test('USER does not load professionals or send a professional filter; errors recover', async () => {
  response = []; fail = 500
  const { root } = await render(['USER'])
  try {
    assert.equal(document.querySelector('select'), null)
    assert.equal(peopleCalls, 0)
    assert.equal(calls[0].get('assignedUserId'), null)
    assert.ok(document.querySelector('[role="alert"]'))
    fail = false; await click('Tentar novamente')
    assert.match(document.body.textContent, /Nenhum serviço agendado para este dia/)
    fail = 401; await click('Atualizar'); assert.equal(expired, 1)
  } finally { await act(async () => root.unmount()) }
})
test('stale period responses cannot replace the currently visible period', async () => {
  let release
  fail = false; response = () => new Promise(resolve => { release = resolve })
  const { root } = await render(['USER'])
  try {
    assert.match(document.body.textContent, /Loading/)
    response = []; await click('Próximo período')
    await act(async () => { release([entry(1)]); await flush() })
    assert.doesNotMatch(document.body.textContent, /Cliente histórico/)
    assert.match(document.body.textContent, /Nenhum serviço agendado/)
  } finally { await act(async () => root.unmount()) }
})
