import { expect, test, type Page } from './customers-runtime.fixture'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const bootstrapPassword = 'E2eStable1!Password'
const adminPassword = 'E2eStable1!Password'
if (!adminEmail || !bootstrapPassword) throw new Error('Run Module 1 browser coverage through scripts/run-e2e.ps1.')

async function login(page: Page, email: string, password: string) {
  await page.goto(process.env.PLAYWRIGHT_BASE_URL!)
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(password)
  const response = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar' }).click()
  return response
}

async function readyAdmin(page: Page) {
  const response = await login(page, adminEmail!, bootstrapPassword)
  expect(response.status()).toBe(200)
  const usersHeading = page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })
  await expect(usersHeading).toBeVisible()
}

async function createUser(page: Page, role: 'MANAGER' | 'USER') {
  const email = `module1-${role.toLowerCase()}-${Date.now()}@example.test`
  await page.getByLabel('Nome completo').fill(`Module 1 ${role}`)
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Perfil').selectOption(role)
  await page.getByRole('button', { name: /^Criar usu.rio$/ }).click()
  const dialog = page.getByRole('dialog', { name: /^Senha tempor.ria$/ })
  const temporaryPassword = await dialog.locator('code').textContent()
  expect(temporaryPassword).toBeTruthy()
  await dialog.getByRole('checkbox').check()
  await dialog.getByRole('button', { name: 'Concluir' }).click()
  return { email, temporaryPassword: temporaryPassword! }
}

async function activateAccount(page: Page, email: string, temporaryPassword: string, permanentPassword: string) {
  expect((await login(page, email, temporaryPassword)).status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  await page.getByLabel('Senha atual').fill(temporaryPassword)
  await page.getByLabel('Nova senha', { exact: true }).fill(permanentPassword)
  await page.getByLabel(/^Confirmar/).fill(permanentPassword)
  await page.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(page.getByRole('heading', { name: 'Bem-vindo' })).toBeVisible()
}

test('anonymous login, invalid credentials, ADMIN refresh and logout use the real cookie flow', async ({ browser }) => {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  try {
  await page.goto(process.env.PLAYWRIGHT_BASE_URL!)
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible()
  expect((await login(page, 'invalid@example.test', 'Invalid1!Password')).status()).toBe(401)
  await expect(page.getByRole('alert')).toBeVisible()
  await readyAdmin(page)
  await page.reload()
  await expect(page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })).toBeVisible()
  await page.getByRole('button', { name: 'Sair' }).click()
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible()
  } finally { await context.close() }
})

test('MANAGER and USER cannot access Administration', async ({ browser }) => {
  const adminContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const managerContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const userContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const admin = await adminContext.newPage()
  await readyAdmin(admin)
  const manager = await createUser(admin, 'MANAGER')
  const user = await createUser(admin, 'USER')
  const managerPage = await managerContext.newPage(), userPage = await userContext.newPage()
  await activateAccount(managerPage, manager.email, manager.temporaryPassword, 'Manager1!Password')
  await activateAccount(userPage, user.email, user.temporaryPassword, 'UserPass1!Password')
  await expect(managerPage.getByRole('button', { name: /^Administra/ })).toHaveCount(0)
  await expect(userPage.getByRole('button', { name: /^Administra/ })).toHaveCount(0)
  await managerPage.goto('/admin/usuarios')
  await expect(managerPage.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
  await userPage.goto('/admin/usuarios')
  await expect(userPage.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
  await adminContext.close(); await managerContext.close(); await userContext.close()
})
