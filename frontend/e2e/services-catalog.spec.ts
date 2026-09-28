import { expect, test } from './customers-runtime.fixture'
import { api, createRoleSession, freshSeed, readyAdmin } from './customers-hardening.helpers'

type Service = { id: string; code: string; name: string; isActive: boolean; version: string }

test('Service Catalog creates, edits, deactivates and reactivates a service', async ({ page }) => {
  await readyAdmin(page)
  const suffix = freshSeed()
  await page.goto('/servicos/novo')
  await page.getByLabel('Código *').fill(`PGR-${suffix}`)
  await page.getByLabel('Nome *').fill(`PGR ${suffix}`)
  await page.getByLabel('Descrição').fill('Programa de Gerenciamento de Riscos')
  await page.getByLabel('Preço base (R$)').fill('250,50')
  await page.getByRole('button', { name: 'Salvar serviço' }).click()
  await expect(page.getByRole('heading', { name: `PGR ${suffix}` })).toBeVisible()
  await expect(page.getByText('R$ 250,50')).toBeVisible()
  await page.getByRole('button', { name: 'Desativar' }).click()
  await page.getByRole('dialog', { name: 'Desativar serviço' }).getByRole('button', { name: 'Desativar serviço' }).click()
  await expect(page.getByRole('button', { name: 'Ativar' })).toBeVisible()
  await page.getByRole('button', { name: 'Ativar' }).click()
  await page.getByRole('dialog', { name: 'Ativar serviço' }).getByRole('button', { name: 'Ativar serviço' }).click()
  await expect(page.getByRole('button', { name: 'Desativar' })).toBeVisible()
})

test('USER sees only active services and cannot create one', async ({ page, newIsolatedPage }) => {
  await readyAdmin(page)
  const suffix = freshSeed()
  const active = await api<Service>(page, 'POST', '/api/services', { code: `ATV-${suffix}`, name: `Ativo ${suffix}`, description: null, basePrice: null })
  const inactive = await api<Service>(page, 'POST', '/api/services', { code: `INA-${suffix}`, name: `Inativo ${suffix}`, description: null, basePrice: null })
  expect(active.status).toBe(201); expect(inactive.status).toBe(201)
  expect((await api(page, 'POST', `/api/services/${inactive.body.id}/deactivate`, { expectedVersion: inactive.body.version })).status).toBe(200)
  const userPage = await newIsolatedPage()
  await createRoleSession(page, userPage, 'USER')
  await userPage.goto('/servicos')
  await expect(userPage.getByText(active.body.name)).toBeVisible()
  await expect(userPage.getByText(inactive.body.name)).toHaveCount(0)
  await expect(userPage.getByRole('button', { name: 'Novo serviço' })).toHaveCount(0)
  await userPage.context().close()
})
