const { test } = require('node:test')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const Module = require('node:module')
const ts = require('typescript')
const { JSDOM } = require('jsdom')
const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost/ordens-servico/order-1' })
global.window = dom.window
global.document = dom.window.document
global.navigator = dom.window.navigator
global.location = dom.window.location
global.addEventListener = dom.window.addEventListener.bind(dom.window)
global.removeEventListener = dom.window.removeEventListener.bind(dom.window)
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
class ApiError extends Error {}
let order, saved = []
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api') return { ApiError }
  if (name === '../../components/LoadingState') return { LoadingState: () => React.createElement('p', null, 'Loading') }
  if (name === './WorkOrderSourcePicker') return { WorkOrderSourcePicker: () => null }
  if (name === './workOrderApi') return {
    getWorkOrder: async () => order,
    getWorkOrderHistory: async () => [],
    getEligibleAssignees: async () => [{ id: 'worker-1', fullName: 'Ana' }, { id: 'worker-2', fullName: 'Bia' }],
    saveWorkOrderPlanning: async (id, version, planning) => { saved.push({ id, version, planning }); order = { ...order, ...planning, version: 'v2' }; return order },
  }
  return originalLoad.call(this, name, parent, isMain)
}
const { WorkOrdersRoutes } = require('../src/features/workOrders/WorkOrdersRoutes.tsx')
Module._load = originalLoad
const flush = () => new Promise(resolve => setImmediate(resolve))
const baseOrder = status => ({ id: 'order-1', number: 'OS-1', version: 'v1', status,
  customerLegalNameSnapshot: 'Cliente', sourceType: 'Contract', serviceAddressSnapshot: 'Unidade',
  assignedUserId: 'worker-1', assignedUserNameSnapshot: 'Ana', scheduledStartDate: '2026-10-01',
  scheduledStartTime: '09:00:00', scheduledEndDate: null, scheduledEndTime: null,
  operationalNotes: '', updatedAtUtc: '2026-10-01T12:00:00Z', startedAtUtc: null,
  executionCompletedAtUtc: null, cancelledAtUtc: null, completionNotes: null, items: [{
    displayOrder: 1, serviceCodeSnapshot: 'S-1', serviceNameSnapshot: 'Serviço',
    serviceLineCodeSnapshot: 'L-1', serviceLineNameSnapshot: 'Linha',
  }] })
async function render(status) {
  order = baseOrder(status); saved = []
  const paths = []
  const root = createRoot(document.getElementById('root'))
  await act(async () => { root.render(React.createElement(WorkOrdersRoutes, { path: '/ordens-servico/order-1', go: path => paths.push(path), onSessionExpired: () => {}, user: { id: 'manager', roles: ['MANAGER'] } })); await flush() })
  return { root, paths }
}
async function save() {
  const button = [...document.querySelectorAll('button')].find(button => button.textContent === 'Salvar planejamento')
  assert.ok(button)
  await act(async () => { button.click(); await flush() })
}
test('draft planning save confirms success on detail', async () => {
  const { root, paths } = await render('Draft')
  try {
    await save()
    assert.equal(saved.length, 1)
    assert.match(document.querySelector('[role="status"]').textContent, /Planejamento salvo com sucesso/)
    assert.deepEqual(paths, [])
  } finally { await act(async () => root.unmount()) }
})
test('scheduled assignee change opens highlighted Agenda with rescheduling confirmation', async () => {
  const { root, paths } = await render('Scheduled')
  try {
    const select = [...document.querySelectorAll('select')].find(select => select.value === 'worker-1')
    assert.ok(select)
    await act(async () => { select.value = 'worker-2'; select.dispatchEvent(new dom.window.Event('change', { bubbles: true })); await flush() })
    await save()
    assert.equal(saved[0].planning.assignedUserId, 'worker-2')
    assert.equal(paths[0], '/agenda?date=2026-10-01&view=day&workOrderId=order-1&confirmation=rescheduled')
  } finally { await act(async () => root.unmount()) }
})
test('scheduled note-only save confirms success without leaving detail', async () => {
  const { root, paths } = await render('Scheduled')
  try {
    const notes = document.querySelector('.wo-form-grid textarea')
    assert.ok(notes)
    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(dom.window.HTMLTextAreaElement.prototype, 'value').set
      setter.call(notes, 'Nova observação')
      notes.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
      await flush()
    })
    await save()
    assert.equal(saved[0].planning.operationalNotes, 'Nova observação')
    assert.deepEqual(paths, [])
    assert.match(document.querySelector('[role="status"]').textContent, /Planejamento salvo com sucesso/)
  } finally { await act(async () => root.unmount()) }
})
