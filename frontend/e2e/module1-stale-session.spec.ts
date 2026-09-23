import { expect, test, type BrowserContext, type Page } from '@playwright/test'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const adminPassword = process.env.E2E_ADMIN_PASSWORD

if (!adminEmail || !adminPassword) {
  throw new Error('E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD must be set to run this acceptance test.')
}

async function login(page: Page, email: string, password: string) {
  await page.goto('/')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

async function changeMandatoryPassword(page: Page, currentPassword: string, newPassword: string) {
  await expect(page.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  await page.getByLabel('Senha atual').fill(currentPassword)
  await page.getByLabel('Nova senha', { exact: true }).fill(newPassword)
  await page.getByLabel('Confirmar nova senha').fill(newPassword)
  await page.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(page.getByText('Bem-vindo')).toBeVisible()
}

test('rejects an existing USER browser session after ADMIN deactivates that exact user', async ({ browser, baseURL }) => {
  const adminContext: BrowserContext = await browser.newContext()
  const userContext: BrowserContext = await browser.newContext()
  const adminPage = await adminContext.newPage()
  const userPage = await userContext.newPage()
  const email = `stale-session-validation-${Date.now()}@example.test`
  const userPassword = 'StaleSession1!Pass'

  await login(adminPage, adminEmail!, adminPassword!)
  await expect(adminPage.getByText('Administração → Usuários')).toBeVisible()

  await adminPage.getByLabel('Nome completo').fill('Stale Session Validation')
  await adminPage.getByLabel('E-mail').fill(email)
  await adminPage.getByLabel('Perfil').selectOption('USER')
  const createResponse = adminPage.waitForResponse(response => response.url().endsWith('/api/admin/users') && response.request().method() === 'POST')
  await adminPage.getByRole('button', { name: 'Criar usuário' }).click()
  expect((await createResponse).status()).toBe(201)
  const temporaryPassword = await adminPage.locator('.password-result code').textContent()
  expect(temporaryPassword).toBeTruthy()
  await adminPage.getByRole('button', { name: 'Fechar' }).click()

  await login(userPage, email, temporaryPassword!)
  await changeMandatoryPassword(userPage, temporaryPassword!, userPassword)
  const statusBeforeDeactivation = await userPage.evaluate(() => fetch('https://localhost:7226/api/auth/me', { credentials: 'include' }).then(response => response.status))
  expect(statusBeforeDeactivation).toBe(200)

  await adminPage.reload()
  await expect(adminPage.getByText('Administração → Usuários')).toBeVisible()
  const targetRow = adminPage.locator('tr', { hasText: email })
  await expect(targetRow).toBeVisible()
  const deactivateResponse = adminPage.waitForResponse(response => response.url().includes('/deactivate') && response.request().method() === 'POST')
  adminPage.once('dialog', dialog => dialog.accept())
  await targetRow.getByRole('button', { name: 'Desativar' }).click()
  expect((await deactivateResponse).status()).toBe(200)
  const refreshedTargetRow = adminPage.locator('tr', { hasText: email })
  await expect(refreshedTargetRow).toContainText('Inativo')

  const users = await adminPage.evaluate(targetEmail => fetch(`https://localhost:7226/api/admin/users?search=${encodeURIComponent(targetEmail)}`, { credentials: 'include' }).then(response => response.json()), email) as Array<{ email: string; isActive: boolean }>
  expect(users).toEqual(expect.arrayContaining([expect.objectContaining({ email, isActive: false })]))

  const statusAfterDeactivation = await userPage.evaluate(() => fetch('https://localhost:7226/api/auth/me', { credentials: 'include' }).then(response => response.status))
  expect(statusAfterDeactivation).toBe(401)
  await userPage.goto(baseURL!)
  await expect(userPage.getByRole('heading', { name: 'Entrar' })).toBeVisible()

  console.log(JSON.stringify({ email, statusBeforeDeactivation, deactivationStatus: 200, inactiveInUi: true, inactiveInApi: true, originalUserContextReused: true, statusAfterDeactivation, resultingLoginScreen: true }))
  await adminContext.close()
  await userContext.close()
})
