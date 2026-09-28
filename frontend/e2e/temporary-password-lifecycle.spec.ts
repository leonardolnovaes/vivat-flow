import { expect, test, type BrowserContext, type Page } from './customers-runtime.fixture'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const adminPassword = 'E2eStable1!Password'
if (!adminEmail || !adminPassword) throw new Error('Run Module 1 browser coverage through scripts/run-e2e.ps1.')
async function login(page: Page, email: string, password: string) { await page.goto(process.env.PLAYWRIGHT_BASE_URL!); await page.getByLabel('E-mail').fill(email); await page.getByLabel('Senha').fill(password); const response = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST'); await page.getByRole('button', { name: 'Entrar' }).click(); return response }
async function readyAdmin(page: Page) { expect((await login(page, adminEmail!, adminPassword)).status()).toBe(200); await expect(page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })).toBeVisible() }

test('ADMIN-created USER can replace a regenerated temporary password and then use a permanent password', async ({ browser }) => {
  const adminContext: BrowserContext = await browser.newContext({ storageState: { cookies: [], origins: [] } }), firstUserContext: BrowserContext = await browser.newContext({ storageState: { cookies: [], origins: [] } }), permanentUserContext: BrowserContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const adminPage = await adminContext.newPage(), firstUserPage = await firstUserContext.newPage(), permanentUserPage = await permanentUserContext.newPage()
  const email = `temporary-password-validation-${Date.now()}@example.test`, permanentPassword = 'Permanent1!Password'
  await readyAdmin(adminPage); await adminPage.getByLabel('Nome completo').fill('Temporary Password Validation'); await adminPage.getByLabel('E-mail').fill(email); await adminPage.getByLabel('Perfil').selectOption('USER'); await adminPage.getByRole('button', { name: /^Criar usu.rio$/ }).click()
  const firstDialog = adminPage.getByRole('dialog', { name: /^Senha tempor.ria$/ }), firstTemporaryPassword = await firstDialog.locator('code').textContent(); expect(firstTemporaryPassword).toBeTruthy(); await firstDialog.getByRole('checkbox').check(); await firstDialog.getByRole('button', { name: 'Concluir' }).click(); await adminPage.reload()
  expect((await login(firstUserPage, email, firstTemporaryPassword!)).status()).toBe(200); await expect(firstUserPage.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  const targetRow = adminPage.locator('tr', { hasText: email }); await targetRow.getByRole('button', { name: /^Gerar senha tempor.ria$/ }).click()
  const replacementDialog = adminPage.getByRole('dialog', { name: /^Senha tempor.ria$/ }), replacementTemporaryPassword = await replacementDialog.locator('code').textContent(); expect(replacementTemporaryPassword).toBeTruthy(); expect(replacementTemporaryPassword).not.toBe(firstTemporaryPassword); await replacementDialog.getByRole('checkbox').check(); await replacementDialog.getByRole('button', { name: 'Concluir' }).click()
  expect((await login(permanentUserPage, email, firstTemporaryPassword!)).status()).toBe(401); expect((await login(permanentUserPage, email, replacementTemporaryPassword!)).status()).toBe(200); await expect(permanentUserPage.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  await permanentUserPage.getByLabel('Senha atual').fill(replacementTemporaryPassword!); await permanentUserPage.getByLabel('Nova senha', { exact: true }).fill(permanentPassword); await permanentUserPage.getByLabel(/^Confirmar/).fill(permanentPassword); await permanentUserPage.getByRole('button', { name: 'Alterar senha' }).click(); await expect(permanentUserPage.getByRole('heading', { name: 'Bem-vindo' })).toBeVisible()
  expect((await login(firstUserPage, email, permanentPassword)).status()).toBe(200); await expect(firstUserPage.getByRole('heading', { name: 'Bem-vindo' })).toBeVisible()
  await adminContext.close(); await firstUserContext.close(); await permanentUserContext.close()
})
