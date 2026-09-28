import { expect, test } from './customers-runtime.fixture'
import { api, createCustomer, freshSeed, readyAdmin, unique, unitInput } from './customers-hardening.helpers'

type Quote = { id: string; version: string; responsibleUserName: string | null; responsibleUserEmail: string | null; currentVisit: { id: string; status: string; notes: string | null } | null }
type User = { id: string; fullName: string; email: string }
type UnitResponse = { unit: { id: string }; version: string }

async function createQuote(page: Parameters<typeof readyAdmin>[0], customer: { id: string }, unitId: string, responsibleUserId: string) {
  const response = await api<Quote>(page, 'POST', '/api/quotes', {
    customerId: customer.id, items: [], totalAmount: null, paymentType: null, installmentCount: null, notes: null,
    employeeCount: null, riskDegree: null, serviceUnitId: unitId, responsibleUserId
  })
  expect(response.status, JSON.stringify(response.body)).toBe(201)
  return response.body
}

async function createCustomerWithUnit(page: Parameters<typeof readyAdmin>[0], name: string) {
  const customer = await createCustomer(page, freshSeed(), { legalName: name })
  const unit = await api<UnitResponse>(page, 'POST', `/api/customers/${customer.id}/units`, { ...unitInput({ isPrimary: true }), expectedVersion: customer.version })
  expect(unit.status, JSON.stringify(unit.body)).toBe(201)
  return { customer, unitId: unit.body.unit.id }
}

test('Responsible professional persists after reload and the server-side filter can be cleared', async ({ page }) => {
  await readyAdmin(page)
  const professional = await api<{ user: User; temporaryPassword: string }>(page, 'POST', '/api/admin/users', {
    fullName: `Profissional responsável ${unique()} com nome longo para validação visual`, email: `responsavel-${unique()}@example.test`, role: 'MANAGER'
  })
  expect(professional.status, JSON.stringify(professional.body)).toBe(201)
  const otherProfessional = await api<{ user: User }>(page, 'POST', '/api/admin/users', {
    fullName: `Outro profissional ${unique()}`, email: `outro-responsavel-${unique()}@example.test`, role: 'MANAGER'
  })
  expect(otherProfessional.status, JSON.stringify(otherProfessional.body)).toBe(201)
  const first = await createCustomerWithUnit(page, `Cliente responsável ${unique()}`)
  const second = await createCustomerWithUnit(page, `Cliente sem filtro ${unique()}`)
  const quote = await createQuote(page, first.customer, first.unitId, professional.body.user.id)
  await createQuote(page, second.customer, second.unitId, otherProfessional.body.user.id)

  await page.goto(`/orcamentos/${quote.id}`)
  await expect(page.getByText(professional.body.user.fullName)).toBeVisible()
  await expect(page.getByText(professional.body.user.email)).toBeVisible()
  await page.reload()
  await expect(page.getByText(professional.body.user.fullName)).toBeVisible()

  await page.goto('/orcamentos')
  const filter = page.getByLabel('Responsável')
  await expect(filter.getByRole('option', { name: professional.body.user.fullName })).toHaveCount(1)
  const filtered = page.waitForResponse(response => response.url().includes('/api/quotes?') && response.url().includes('responsibleUserId=') && response.status() === 200)
  await filter.selectOption(professional.body.user.id)
  await filtered
  await expect(page.locator('.quote-results tbody tr')).toHaveCount(1)
  await expect(page.locator('.quote-results')).toContainText(first.customer.legalName)
  await page.getByRole('button', { name: 'Limpar filtros' }).click()
  await expect(page.locator('.quote-results tbody tr')).toHaveCount(2)
})

test('Visit validation, scheduling persistence, rescheduling, completion, and history work through the UI', async ({ page }) => {
  await readyAdmin(page)
  const professional = await api<{ user: User }>(page, 'POST', '/api/admin/users', { fullName: `Profissional visita ${unique()}`, email: `visita-${unique()}@example.test`, role: 'MANAGER' })
  expect(professional.status, JSON.stringify(professional.body)).toBe(201)
  const data = await createCustomerWithUnit(page, `Cliente visita ${unique()}`)
  const quote = await createQuote(page, data.customer, data.unitId, professional.body.user.id)
  await page.goto(`/orcamentos/${quote.id}`)
  await expect(page.getByText('Nenhuma visita agendada')).toBeVisible()
  await page.getByRole('button', { name: 'Agendar visita' }).click()
  const dialog = page.getByRole('dialog', { name: 'Agendar visita' })
  await dialog.getByLabel('Data e horário inicial *').fill('')
  await dialog.getByRole('button', { name: 'Agendar visita' }).click()
  await expect(dialog).toContainText('Informe a data e o horário da visita.')
  await dialog.getByLabel('Data e horário inicial *').fill('2026-10-15T09:00')
  await dialog.getByLabel('Data e horário final *').fill('2026-10-15T10:00')
  await dialog.getByLabel('Observações').fill('Primeiro agendamento')
  await dialog.getByRole('button', { name: 'Agendar visita' }).click()
  await expect(page.getByRole('status')).toContainText('Visita agendada com sucesso.')
  await expect(page.locator('.quote-visit')).toContainText('Primeiro agendamento')
  for (const label of ['Visita Técnica', 'Horário', 'Observações', 'Situação']) await expect(page.getByText(label, { exact: true })).toBeVisible()
  await page.reload()
  await expect(page.locator('.quote-visit')).toContainText('Primeiro agendamento')

  await page.getByRole('button', { name: 'Reagendar' }).click()
  const reschedule = page.getByRole('dialog', { name: 'Reagendar visita' })
  await reschedule.getByLabel('Data e horário inicial *').fill('2026-10-16T14:00')
  await reschedule.getByLabel('Data e horário final *').fill('2026-10-16T15:00')
  await reschedule.getByLabel('Observações').fill('Reagendada')
  await reschedule.getByRole('button', { name: 'Salvar reagendamento' }).click()
  await expect(page.getByRole('status')).toContainText('Visita reagendada com sucesso.')
  await expect(page.locator('.quote-visit')).toContainText('Reagendada')

  await page.getByRole('button', { name: 'Concluir visita' }).click()
  await page.getByRole('dialog', { name: 'Concluir visita' }).getByRole('button', { name: 'Concluir visita' }).click()
  await expect(page.getByRole('status')).toContainText('Visita concluída com sucesso.')
  await expect(page.locator('.quote-visit')).toContainText('Concluída')
  await expect(page.locator('.quote-history')).toContainText('Visita agendada')
  await expect(page.locator('.quote-history')).toContainText('Usuário responsável')
  await expect(page.locator('.quote-history')).toContainText('Visita reagendada')
  await expect(page.locator('.quote-history')).toContainText('Visita concluída')
  await expect(page.locator('.quote-history')).toContainText('Agendada para')
  await expect(page.locator('.quote-history')).toContainText('Reagendada de')
  await expect(page.locator('.quote-history')).not.toContainText(/\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+\+\d{2}:\d{2}\|/)
})

test('Quote screens render the required pt-BR labels without mojibake', async ({ page }) => {
  await readyAdmin(page)
  const data = await createCustomerWithUnit(page, `Cliente UTF-8 ${unique()}`)
  const quote = await api<Quote>(page, 'POST', '/api/quotes', {
    customerId: data.customer.id, items: [], totalAmount: null, paymentType: null, installmentCount: null, notes: null,
    employeeCount: null, riskDegree: null, serviceUnitId: data.unitId, responsibleUserId: null
  })
  expect(quote.status, JSON.stringify(quote.body)).toBe(201)

  await page.goto('/orcamentos')
  await expect(page.getByRole('heading', { name: 'Orçamentos' })).toBeVisible()
  await expect(page.locator('.quote-kpis')).toContainText('Aguardando aprovação')
  await expect(page.locator('.quote-kpis')).toContainText('Alterações solicitadas')
  await expect(page.getByLabel('Serviço')).toBeVisible()
  await expect(page.getByLabel('Responsável')).toBeVisible()
  await expect(page.locator('.quote-table thead')).toContainText('Número')
  await expect(page.locator('.quote-table thead')).toContainText('Ações')
  await expect(page.locator('.quote-results tbody')).toContainText('Não definido')

  await page.goto(`/orcamentos/${quote.body.id}`)
  await expect(page.getByText('Histórico do orçamento', { exact: true })).toBeVisible()
  await expect(page.locator('body')).not.toContainText(String.fromCodePoint(0x4f, 0x72, 0xc3, 0xa7, 0x61, 0x6d, 0x65, 0x6e, 0x74, 0x6f, 0x73))
})

test('A scheduled visit can be cancelled and remains recorded as cancelled', async ({ page }) => {
  await readyAdmin(page)
  const professional = await api<{ user: User }>(page, 'POST', '/api/admin/users', { fullName: `Profissional cancelamento ${unique()}`, email: `cancelamento-${unique()}@example.test`, role: 'MANAGER' })
  expect(professional.status, JSON.stringify(professional.body)).toBe(201)
  const data = await createCustomerWithUnit(page, `Cliente cancelamento ${unique()}`)
  const quote = await createQuote(page, data.customer, data.unitId, professional.body.user.id)
  const scheduled = await api<Quote>(page, 'POST', `/api/quotes/${quote.id}/visits`, { assignedUserId: professional.body.user.id, scheduledStart: '2026-10-20T12:00:00Z', scheduledEnd: '2026-10-20T13:00:00Z', customerUnitId: data.unitId, notes: 'Para cancelar' })
  expect(scheduled.status, JSON.stringify(scheduled.body)).toBe(200)
  await page.goto(`/orcamentos/${quote.id}`)
  await page.getByRole('button', { name: 'Cancelar visita' }).click()
  await page.getByRole('dialog', { name: 'Cancelar visita' }).getByRole('button', { name: 'Cancelar visita' }).click()
  await expect(page.getByRole('status')).toContainText('Visita cancelada com sucesso.')
  await expect(page.locator('.quote-visit')).toContainText('Cancelada')
  await expect(page.locator('.quote-history')).toContainText('Visita cancelada')
})
