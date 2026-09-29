import { expect, test, type Page } from './customers-runtime.fixture'
import { cnpj, createCustomer, freshSeed, readyAdmin, unique } from './customers-hardening.helpers'

async function holdMutation(page: Page, method: string, path: string) {
  let count = 0
  let release!: () => void
  let signal!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const arrived = new Promise<void>(resolve => { signal = resolve })
  await page.route('**/api/customers**', async route => {
    if (route.request().method() !== method || new URL(route.request().url()).pathname !== path) return route.continue()
    count++
    signal()
    await gate
    await route.continue()
  })
  return { arrived, release, count: () => count }
}

test('Customer create and edit keep one real request while pending', async ({ page }) => {
  await readyAdmin(page)
  const name = `E2E Pending ${unique()}`
  await page.goto('/clientes/novo')
  await page.getByLabel('Razão social *').fill(name)
  await page.getByLabel('CNPJ *').fill(cnpj(freshSeed()))
  const create = await holdMutation(page, 'POST', '/api/customers')
  await page.getByRole('button', { name: 'Criar cliente' }).click()
  await create.arrived
  await expect(page.getByRole('button', { name: 'Salvando...' })).toBeDisabled()
  await page.keyboard.press('Enter')
  create.release()
  await expect(page.getByRole('heading', { name })).toBeVisible()
  expect(create.count()).toBe(1)
  await page.unroute('**/api/customers**')
  const id = page.url().split('/').pop()!
  await page.getByRole('button', { name: 'Editar empresa' }).click()
  await page.getByLabel('Nome fantasia').fill('Pending Trade')
  const edit = await holdMutation(page, 'PUT', `/api/customers/${id}`)
  await page.getByRole('button', { name: 'Salvar alterações' }).click()
  await edit.arrived
  await expect(page.getByRole('button', { name: 'Salvando...' })).toBeDisabled()
  await page.keyboard.press('Enter')
  edit.release()
  await expect(page.getByRole('heading', { name })).toBeVisible()
  expect(edit.count()).toBe(1)
})

test('Contact and Unit create each send one real request while pending', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  await page.goto(`/clientes/${customer.id}`)
  await page.getByRole('button', { name: 'Adicionar contato' }).click()
  await page.getByLabel('Nome *', { exact: true }).fill('E2E Pending Contact')
  await page.getByLabel('E-mail').fill('pending-contact@example.test')
  const contact = await holdMutation(page, 'POST', `/api/customers/${customer.id}/contacts`)
  await page.getByRole('button', { name: 'Salvar contato' }).click()
  await contact.arrived
  await expect(page.getByRole('button', { name: 'Salvando...' })).toBeDisabled()
  await page.keyboard.press('Enter')
  contact.release()
  await expect(page.getByText('E2E Pending Contact').first()).toBeVisible()
  expect(contact.count()).toBe(1)
  await page.unroute('**/api/customers**')
  await page.getByRole('button', { name: 'Adicionar unidade' }).click()
  await page.getByLabel('Nome da unidade *').fill('E2E Pending Unit')
  await page.getByLabel('Logradouro *').fill('Rua E2E')
  await page.getByLabel('Número *').fill('1')
  await page.getByLabel('Cidade *').fill('São Paulo')
  await page.getByLabel('UF *').selectOption('SP')
  const unit = await holdMutation(page, 'POST', `/api/customers/${customer.id}/units`)
  await page.getByRole('button', { name: 'Salvar unidade' }).click()
  await unit.arrived
  await expect(page.getByRole('button', { name: 'Salvando...' })).toBeDisabled()
  await page.keyboard.press('Enter')
  unit.release()
  await expect(page.getByText('E2E Pending Unit').first()).toBeVisible()
  expect(unit.count()).toBe(1)
})

test('Customer deactivation sends one real request while confirmation is pending', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  await page.goto(`/clientes/${customer.id}`)
  await page.getByRole('button', { name: 'Desativar cliente' }).click()
  const status = await holdMutation(page, 'POST', `/api/customers/${customer.id}/deactivate`)
  await page.getByRole('dialog').getByRole('button', { name: 'Desativar cliente' }).click()
  await status.arrived
  await expect(page.getByRole('dialog').getByRole('button', { name: 'Processando...' })).toBeDisabled()
  await page.keyboard.press('Enter')
  status.release()
  await expect(page.getByRole('button', { name: 'Ativar cliente' })).toBeVisible()
  expect(status.count()).toBe(1)
})
