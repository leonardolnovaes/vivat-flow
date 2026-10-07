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
global.location = dom.window.location
global.IS_REACT_ACT_ENVIRONMENT = true
const React = require('react')
const { act } = React
const { createRoot } = require('react-dom/client')
for (const extension of ['.ts', '.tsx']) require.extensions[extension] = (module, filename) => module._compile(ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX },
}).outputText, filename)

let quoteContractRequests = 0
let workOrderSourceRequests = 0
const quote = {
  id: 'quote-1', number: 'ORC-1', status: 'Approved', updatedAtUtc: '2026-10-01T12:00:00Z',
  customerLegalNameSnapshot: 'Cliente', customerCnpjSnapshot: '12345678000199', serviceAddressSnapshot: null,
  responsibleUserName: null, responsibleUserEmail: null, employeeCount: null, riskDegree: null,
  items: [], totalAmount: 100, paymentType: 'Cash', installmentCount: null, notes: null,
  sentForApprovalAt: null, validUntil: null, approvalRecipients: [], approvalRecipientEmail: null,
  clientResponseType: null, currentVisit: null,
}
const contract = {
  id: 'contract-1', quoteId: 'quote-1', status: 'Active', updatedAtUtc: '2026-10-01T12:00:00Z',
  createdAtUtc: '2026-10-01T12:00:00Z', customerLegalNameSnapshot: 'Cliente', kind: 'OneOff',
  approvedTotalAmount: 100, paymentType: 'Cash', installmentCount: null, paymentTerms: null,
  notes: null, startDate: '2026-10-01', endDate: null, items: [],
}
const originalLoad = Module._load
Module._load = function (name, parent, isMain) {
  if (name === '../../api') return { ApiError: class ApiError extends Error {} }
  if (name === '../../components/LoadingState') return { LoadingState: () => React.createElement('p', null, 'Loading') }
  if (name === '../contracts/ContractsRoutes') return {
    QuoteContractAction: ({ quoteId }) => {
      React.useEffect(() => { quoteContractRequests++; void Promise.resolve(quoteId) }, [quoteId])
      return React.createElement('div', { 'data-testid': 'quote-contract-action' }, 'Contract action')
    },
  }
  if (name === '../customers/customerApi') return { getCustomer: async () => null }
  if (name === './QuickCustomerDialog' || name === './QuoteSendDialog') return { QuickCustomerDialog: () => null, QuoteSendDialog: () => null }
  if (name === './quoteCompletion') return {
    completionActions: () => ({ customer: false, quote: false }),
    QuoteCompletionRefreshError: class QuoteCompletionRefreshError extends Error {},
    synchronizeCustomerCompletion: async () => ({ status: 'saved' }),
  }
  if (name === './visitDate') return { parseLocalDateTime: () => null }
  if (name === '../services/serviceApi') return { listServices: async () => ({ items: [] }) }
  if (name === './quoteApi') return {
    getQuote: async () => quote,
    getQuoteHistory: async () => [],
    getQuoteApprovalValidation: async () => ({ errors: {} }),
  }
  if (name === './quoteFormat') return { formatQuoteChangedFields: () => '' }
  if (name === '../quotes/quoteApi') return { getQuote: async () => quote }
  if (name === '../workOrders/WorkOrdersRoutes') return {
    RelatedWorkOrderAction: ({ contractId }) => {
      React.useEffect(() => { workOrderSourceRequests++; void Promise.resolve(contractId) }, [contractId])
      return React.createElement('div', { 'data-testid': 'related-work-order-action' }, 'Work Order action')
    },
  }
  if (name === './contractApi') return {
    createContract: async () => contract,
    findOpenContractForQuote: async () => null,
    getContract: async () => contract,
    listContracts: async () => ({ items: [], totalCount: 0, pageSize: 100 }),
    transitionContract: async () => contract,
    updateContract: async () => contract,
  }
  return originalLoad.call(this, name, parent, isMain)
}

const { QuoteDetailView } = require('../src/features/quotes/QuoteCommercialViews.tsx')
const { ContractsRoutes } = require('../src/features/contracts/ContractsRoutes.tsx')
Module._load = originalLoad

const flush = () => new Promise(resolve => setImmediate(resolve))
async function render(element) {
  const root = createRoot(document.getElementById('root'))
  await act(async () => { root.render(element); await flush(); await flush() })
  return root
}
async function unmount(root) { await act(async () => root.unmount()) }

test('approved Quote does not mount or request Contracts action without contracts entitlement', async () => {
  quoteContractRequests = 0
  const root = await render(React.createElement(QuoteDetailView, {
    id: quote.id, go: () => {}, onSessionExpired: () => {}, features: ['quotes', 'customers', 'services'],
  }))
  try {
    assert.equal(document.querySelector('[data-testid="quote-contract-action"]'), null)
    assert.equal(quoteContractRequests, 0)
  } finally { await unmount(root) }
})

test('approved Quote keeps its Contract action when contracts entitlement is enabled', async () => {
  quoteContractRequests = 0
  const root = await render(React.createElement(QuoteDetailView, {
    id: quote.id, go: () => {}, onSessionExpired: () => {}, features: ['quotes', 'customers', 'services', 'contracts'],
  }))
  try {
    assert.ok(document.querySelector('[data-testid="quote-contract-action"]'))
    assert.equal(quoteContractRequests, 1)
  } finally { await unmount(root) }
})

test('Active Contract does not mount or request Work Order action without work-orders entitlement', async () => {
  workOrderSourceRequests = 0
  const root = await render(React.createElement(ContractsRoutes, {
    path: '/contratos/contract-1', go: () => {}, onSessionExpired: () => {}, features: ['contracts', 'quotes'],
  }))
  try {
    assert.equal(document.querySelector('[data-testid="related-work-order-action"]'), null)
    assert.equal(workOrderSourceRequests, 0)
    assert.ok(document.querySelector('.contract-info'))
  } finally { await unmount(root) }
})

test('Active Contract keeps its Work Order action when work-orders entitlement is enabled', async () => {
  workOrderSourceRequests = 0
  const root = await render(React.createElement(ContractsRoutes, {
    path: '/contratos/contract-1', go: () => {}, onSessionExpired: () => {}, features: ['contracts', 'quotes', 'work-orders'],
  }))
  try {
    assert.ok(document.querySelector('[data-testid="related-work-order-action"]'))
    assert.equal(workOrderSourceRequests, 1)
  } finally { await unmount(root) }
})
