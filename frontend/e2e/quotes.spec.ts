import { expect, test } from './customers-runtime.fixture'
import type { Page } from '@playwright/test'
import { api, contactInput, createCustomer, freshSeed, readyAdmin, unique, unitInput } from './customers-hardening.helpers'

type Service = { id: string }
type UnitResponse = { unit: { id: string }; version: string }
type ContactResponse = { version: string }

async function selectCustomer(page: Page, customer: { id: string; legalName: string; cnpj: string }, term: string) {
  const search = page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')
  await search.fill(term)
  await page.getByRole('option', { name: new RegExp(customer.legalName) }).click()
  await expect(search).toHaveValue(new RegExp(customer.legalName))
}

async function addUnit(page: Page, customerId: string, version: string) {
  const result = await api<UnitResponse>(page, 'POST', `/api/customers/${customerId}/units`, { ...unitInput({ isPrimary: true }), expectedVersion: version })
  expect(result.status, JSON.stringify(result.body)).toBe(201)
  return result.body
}

async function addContact(page: Page, customerId: string, version: string) {
  const result = await api<ContactResponse>(page, 'POST', `/api/customers/${customerId}/contacts`, { ...contactInput({ isPrimary: true }), expectedVersion: version })
  expect(result.status, JSON.stringify(result.body)).toBe(201)
  return result.body
}

test('Quote customer search supports legal name and both CNPJ forms, with active units', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed(), { legalName: `Busca Quote ${unique()}`, tradeName: `Fantasia ${unique()}` })
  const unit = await addUnit(page, customer.id, customer.version)
  await page.goto('/orcamentos/novo')

  await selectCustomer(page, customer, customer.legalName)
  await expect(page.getByLabel('Unidade/local do serviço')).toContainText('E2E Unit')
  await page.getByRole('button', { name: 'Alterar' }).click()
  await expect(page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')).toHaveValue('')
  await selectCustomer(page, customer, customer.cnpj.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5'))
  await page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ').fill('')
  await expect(page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')).toHaveValue('')
  await expect(page.getByLabel('Unidade/local do serviço')).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Alterar' })).toHaveCount(0)
  await selectCustomer(page, customer, customer.legalName)
  await page.getByRole('button', { name: 'Alterar' }).click()
  await selectCustomer(page, customer, customer.cnpj)
  await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  await expect(page.getByLabel('Unidade/local do serviço')).toHaveValue(unit.unit.id)
})

test('Quote unit state explains missing units and clears an old unit after customer switch', async ({ page }) => {
  await readyAdmin(page)
  const withUnit = await createCustomer(page, freshSeed(), { legalName: `Com unidade ${unique()}` })
  const unit = await addUnit(page, withUnit.id, withUnit.version)
  const withoutUnit = await createCustomer(page, freshSeed(), { legalName: `Sem unidade ${unique()}` })
  await page.goto('/orcamentos/novo')
  await selectCustomer(page, withUnit, withUnit.legalName)
  await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  await page.getByRole('button', { name: 'Alterar' }).click()
  await selectCustomer(page, withoutUnit, withoutUnit.legalName)
  await expect(page.getByLabel('Unidade/local do serviço')).toBeDisabled()
  await expect(page.getByRole('status')).toContainText('Este cliente não possui uma unidade ativa cadastrada.')
  await expect(page.getByRole('button', { name: 'Completar cadastro do cliente' })).toBeVisible()
})

test('Quick duplicate selects the active customer and an incomplete Draft remains savable', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed(), { legalName: `Duplicado Quote ${unique()}` })
  await page.goto('/orcamentos/novo')
  await page.getByLabel('Observações').fill('Rascunho preservado')
  await page.getByRole('button', { name: '+ Cadastrar cliente rapidamente' }).click()
  await page.getByRole('dialog').getByLabel('Razão social *').fill('Tentativa duplicada')
  await page.getByRole('dialog').getByLabel('CNPJ *').fill(customer.cnpj)
  await page.getByRole('dialog').getByRole('button', { name: 'Cadastrar cliente' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(page.getByText('Cliente selecionado com cadastro incompleto.')).toBeVisible()
  await expect(page.getByLabel('Observações')).toHaveValue('Rascunho preservado')
  const create = page.waitForResponse(response => response.url().includes('/api/quotes') && response.request().method() === 'POST' && response.status() === 201)
  await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  await create
  await expect(page.getByText('Orçamento incompleto')).toBeVisible()
})

test('Quote approval aggregates validation, services remain structurally separate, and complete quote reaches pending approval', async ({ page }) => {
  await readyAdmin(page)
  const incomplete = await createCustomer(page, freshSeed(), { legalName: `Incompleto Quote ${unique()}` })
  const serviceOne = await api<Service>(page, 'POST', '/api/services', { code: `PGR-${unique()}`, name: 'PPP Quote', description: null, basePrice: null })
  const serviceTwo = await api<Service>(page, 'POST', '/api/services', { code: `PCMSO-${unique()}`, name: 'PCMSO Quote', description: null, basePrice: null })
  const serviceThree = await api<Service>(page, 'POST', '/api/services', { code: `LTCAT-${unique()}`, name: 'LTCAT Quote', description: null, basePrice: null })
  expect(serviceOne.status).toBe(201); expect(serviceTwo.status).toBe(201); expect(serviceThree.status).toBe(201)
  await page.goto('/orcamentos/novo')
  await selectCustomer(page, incomplete, incomplete.legalName)
  for (const service of [serviceOne, serviceTwo, serviceThree]) {
    await page.getByLabel('Adicionar serviço').selectOption(service.body.id)
    await expect(page.getByLabel('Serviços adicionados').locator('.selected-service')).toHaveCount([serviceOne, serviceTwo, serviceThree].indexOf(service) + 1)
    await expect(page.getByLabel('Condições comerciais').locator('.selected-service')).toHaveCount(0)
  }
  await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  await page.getByRole('button', { name: 'Enviar para aprovação' }).click()
  const alert = page.getByRole('alert')
  for (const text of ['Informe o valor total.', 'Informe a condição de pagamento.', 'Informe a quantidade de funcionários.', 'Informe o grau de risco.', 'Informe o endereço do serviço.', 'Informe um contato ativo para o cliente.', 'Informe uma unidade ativa para o cliente.']) await expect(alert).toContainText(text)
  await expect(page.getByRole('heading', { name: 'Cliente' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Contexto comercial' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Serviços' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Condições comerciais' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Observações' })).toBeVisible()

  const complete = await createCustomer(page, freshSeed(), { legalName: `Completo Quote ${unique()}` })
  const unit = await addUnit(page, complete.id, complete.version)
  await addContact(page, complete.id, unit.version)
  await page.goto('/orcamentos/novo')
  await selectCustomer(page, complete, complete.legalName)
  await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  await page.getByLabel('Quantidade de funcionários').fill('20')
  await page.getByLabel('Grau de risco').selectOption('Two')
  await page.getByLabel('Adicionar serviço').selectOption(serviceOne.body.id)
  await page.getByLabel('Valor total (R$)').fill('1000,00')
  await page.getByLabel('Condição de pagamento').selectOption('Cash')
  await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  await page.getByRole('button', { name: 'Enviar para aprovação' }).click()
  await expect(page.getByText('Aguardando aprovação')).toBeVisible()
})

test('MANAGER and USER do not see Quotes and direct access is denied', async ({ page, newIsolatedPage }) => {
  await readyAdmin(page)
  for (const role of ['MANAGER', 'USER'] as const) {
    const isolated = await newIsolatedPage()
    const email = `quote-${role}-${unique()}@example.test`
    const created = await api<{ temporaryPassword: string }>(page, 'POST', '/api/admin/users', { fullName: `Quote ${role}`, email, role })
    expect(created.status).toBe(201)
    await isolated.goto('/')
    await api(isolated, 'POST', '/api/auth/login', { email, password: created.body.temporaryPassword })
    await api(isolated, 'POST', '/api/auth/change-password', { currentPassword: created.body.temporaryPassword, newPassword: 'E2eUser1!Password' })
    await isolated.goto('/orcamentos')
    await expect(isolated.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
    await isolated.context().close()
  }
})

test('Quote history presents changed fields in Portuguese without technical identifiers', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed(), { legalName: `Histórico Quote ${unique()}` })
  const firstUnit = await addUnit(page, customer.id, customer.version)
  const secondUnit = await addUnit(page, customer.id, firstUnit.version)
  const service = await api<Service>(page, 'POST', '/api/services', { code: `HIST-${unique()}`, name: 'Serviço histórico', description: null, basePrice: null })
  expect(service.status).toBe(201)

  type QuoteMutation = { id: string; version: string; items: { id: string; serviceId: string }[] }
  const created = await api<QuoteMutation>(page, 'POST', '/api/quotes', {
    customerId: customer.id, items: [{ serviceId: service.body.id }], totalAmount: 100, paymentType: 'Cash', installmentCount: 1,
    employeeCount: 10, riskDegree: 'One', serviceUnitId: firstUnit.unit.id, notes: 'Antes da atualização'
  })
  expect(created.status).toBe(201)
  const updated = await api<QuoteMutation>(page, 'PUT', `/api/quotes/${created.body.id}`, {
    customerId: customer.id, items: created.body.items, totalAmount: 200, paymentType: 'Installments', installmentCount: 2,
    employeeCount: 20, riskDegree: 'Two', serviceUnitId: secondUnit.unit.id, notes: 'Depois da atualização', expectedVersion: created.body.version
  })
  expect(updated.status).toBe(200)

  await page.goto(`/orcamentos/${created.body.id}`)
  const history = page.locator('.quote-history')
  await expect(history).toContainText('Campos alterados: Valor total, Condição de pagamento, Quantidade de parcelas, Quantidade de funcionários, Grau de risco, Local do serviço e Observações.')
  for (const identifier of ['TotalAmount', 'PaymentType', 'InstallmentCount', 'EmployeeCount', 'RiskDegree', 'ServiceAddress', 'Notes']) {
    await expect(history).not.toContainText(identifier)
  }
})
