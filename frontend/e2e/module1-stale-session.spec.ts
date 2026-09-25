import { expect, test, type BrowserContext, type Page } from '@playwright/test'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const adminPassword = process.env.E2E_ADMIN_PASSWORD
if (!adminEmail || !adminPassword) throw new Error('Run Module 1 browser coverage through scripts/run-e2e.ps1.')

async function login(page: Page, email: string, password: string) {
  await page.goto('/')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(password)
  const response = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar' }).click()
  return response
}
async function readyAdmin(page: Page) {
  let currentPassword = adminPassword!
  let loginResult = await login(page, adminEmail!, currentPassword)
  if (loginResult.status() === 401) { currentPassword = 'E2eAdmin1!Password'; loginResult = await login(page, adminEmail!, currentPassword) }
  expect(loginResult.status()).toBe(200)
  const passwordHeading = page.getByRole('heading', { name: 'Alterar senha' })
  const usersHeading = page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })
  await Promise.race([passwordHeading.waitFor({ state: 'visible' }), usersHeading.waitFor({ state: 'visible' })])
  if (await passwordHeading.isVisible()) {
    await page.getByLabel('Senha atual').fill(currentPassword)
    await page.getByLabel('Nova senha', { exact: true }).fill('E2eAdmin1!Password')
    await page.getByLabel(/^Confirmar/).fill('E2eAdmin1!Password')
    await page.getByRole('button', { name: 'Alterar senha' }).click()
  }
  await expect(usersHeading).toBeVisible()
}

test('rejects an existing USER browser session after ADMIN deactivates that exact user', async ({ browser }) => {
  const adminContext: BrowserContext = await browser.newContext(), userContext: BrowserContext = await browser.newContext()
  const adminPage = await adminContext.newPage(), userPage = await userContext.newPage()
  const email = `stale-session-validation-${Date.now()}@example.test`, userPassword = 'StaleSession1!Pass'
  await readyAdmin(adminPage)
  await adminPage.getByLabel('Nome completo').fill('Stale Session Validation')
  await adminPage.getByLabel('E-mail').fill(email)
  await adminPage.getByLabel('Perfil').selectOption('USER')
  const created = adminPage.waitForResponse(r => r.url().endsWith('/api/admin/users') && r.request().method() === 'POST')
  await adminPage.getByRole('button', { name: /^Criar usu.rio$/ }).click()
  expect((await created).status()).toBe(201)
  const passwordDialog = adminPage.getByRole('dialog', { name: /^Senha tempor.ria$/ })
  const temporaryPassword = await passwordDialog.locator('code').textContent()
  expect(temporaryPassword).toBeTruthy()
  await passwordDialog.getByRole('checkbox').check(); await passwordDialog.getByRole('button', { name: 'Concluir' }).click()
  await adminPage.reload()
  expect((await login(userPage, email, temporaryPassword!)).status()).toBe(200)
  await expect(userPage.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  await userPage.getByLabel('Senha atual').fill(temporaryPassword!)
  await userPage.getByLabel('Nova senha', { exact: true }).fill(userPassword)
  await userPage.getByLabel(/^Confirmar/).fill(userPassword)
  await userPage.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(userPage.getByRole('heading', { name: 'Bem-vindo' })).toBeVisible()
  expect(await userPage.evaluate(() => fetch('/api/auth/me').then(r => r.status))).toBe(200)
  const targetRow = adminPage.locator('tr', { hasText: email }); await expect(targetRow).toBeVisible()
  await targetRow.getByRole('button', { name: /^Desativar/ }).click()
  const deactivated = adminPage.waitForResponse(r => r.url().includes('/deactivate') && r.request().method() === 'POST')
  await adminPage.getByRole('dialog', { name: /^Desativar/ }).getByRole('button', { name: /^Confirmar desativa/ }).click()
  expect((await deactivated).status()).toBe(200); await expect(targetRow).toContainText('Inativo')
  expect(await userPage.evaluate(() => fetch('/api/auth/me').then(r => r.status))).toBe(401)
  await userPage.reload(); await expect(userPage.getByRole('heading', { name: 'Entrar' })).toBeVisible()
  await adminContext.close(); await userContext.close()
})
