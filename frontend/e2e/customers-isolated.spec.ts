import { expect, test, type Page } from './customers-runtime.fixture'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const adminPassword = process.env.E2E_ADMIN_PASSWORD

if (!adminEmail || !adminPassword) {
  throw new Error('Run Customers browser coverage through scripts/run-e2e.ps1.')
}

const runId = `${Date.now()}-${process.pid}`
const cnpj = (seed: number) => {
  const value = `${seed}`.padStart(12, '0')
  const digit = (source: string, weights: number[]) => {
    const total = [...source].reduce((sum, character, index) => sum + Number(character) * weights[index], 0)
    const rest = total % 11
    return rest < 2 ? 0 : 11 - rest
  }
  const first = digit(value, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
  return value + first + digit(value + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
}

async function login(page: Page, email: string, password: string) {
  await page.goto(process.env.PLAYWRIGHT_BASE_URL!)
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(password)
  const response = page.waitForResponse(candidate => candidate.url().endsWith('/api/auth/login') && candidate.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar' }).click()
  return response
}

async function completePasswordChange(page: Page, current: string, replacement: string, destination: 'home' | 'admin' = 'home') {
  await expect(page.getByRole('heading', { name: 'Alterar senha' })).toBeVisible()
  await page.getByLabel('Senha atual').fill(current)
  await page.getByLabel('Nova senha', { exact: true }).fill(replacement)
  await page.getByLabel('Confirmar nova senha').fill(replacement)
  await page.getByRole('button', { name: 'Alterar senha' }).click()
  await expect(destination === 'admin' ? page.getByRole('heading', { name: /^Usu.rios$/, level: 2 }) : page.getByRole('heading', { name: 'Bem-vindo' })).toBeVisible()
}

async function readyAdmin(page: Page) {
  await page.goto('/')
  await expect(page.getByRole('button', { name: /^Administra/ })).toBeVisible()
  await page.getByRole('button', { name: 'Clientes' }).click()
  await expect(page.getByRole('heading', { name: 'Clientes' })).toBeVisible()
}

async function createAccount(admin: Page, role: 'MANAGER' | 'USER') {
  const email = `e2e-${role.toLowerCase()}-${runId}@example.test`
  await admin.getByRole('button', { name: 'Administração' }).click()
  await admin.getByLabel('Nome completo').fill(`E2E ${role}`)
  await admin.getByLabel('E-mail').fill(email)
  await admin.getByLabel('Perfil').selectOption(role)
  await admin.getByRole('button', { name: 'Criar usuário' }).click()
  const temporary = await admin.getByRole('dialog').locator('code').textContent()
  expect(temporary).toBeTruthy()
  await admin.getByRole('dialog').getByRole('checkbox').check()
  await admin.getByRole('button', { name: 'Concluir' }).click()
  return { email, temporary: temporary! }
}

async function createCustomer(page: Page, suffix: string, seed: number) {
  const legalName = `E2E-${runId}-${suffix}`
  await page.goto('/clientes/novo')
  await page.getByLabel('Razão social *').fill(legalName)
  await page.getByLabel('Nome fantasia').fill(`Comercial ${suffix}`)
  await page.getByLabel('CNPJ *').fill(cnpj(seed))
  await page.getByRole('button', { name: 'Criar cliente' }).click()
  await expect(page.getByRole('heading', { name: legalName })).toBeVisible()
  return { legalName, id: page.url().split('/').pop()! }
}

test.describe.configure({ mode: 'parallel' })
test.describe('Customers isolated E2E', () => {
  test('ADMIN completes the customer, contact, unit, lifecycle and persistence journey', async ({ page }) => {
    await readyAdmin(page)
    const customer = await createCustomer(page, 'Customer', 1)
    await expect(page.getByText(cnpj(1).replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5')).first()).toBeVisible()

    await page.getByRole('button', { name: 'Editar empresa' }).click()
    await page.getByLabel('Nome fantasia').fill('Comercial Atualizado')
    await page.getByRole('button', { name: 'Salvar alterações' }).click()
    await page.reload()
    await expect(page.getByLabel('Nome fantasia')).toHaveValue('Comercial Atualizado')
    await page.goto(`/clientes/${customer.id}`)

    await page.getByRole('button', { name: 'Adicionar contato' }).click()
    await page.getByLabel('Nome').fill(`Contato ${runId}`)
    await page.getByLabel('E-mail').fill(`contact-${runId}@example.test`)
    await page.getByLabel('Contato principal').check()
    await page.getByRole('button', { name: 'Salvar contato' }).click()
    await expect(page.getByText(`Contato ${runId}`).first()).toBeVisible()
    await page.getByRole('button', { name: 'Adicionar unidade' }).click()
    await page.getByLabel('Nome da unidade').fill(`Matriz ${runId}`)
    await page.getByLabel('Logradouro').fill('Rua E2E')
    await page.getByLabel('Número').fill('1')
    await page.getByLabel('Cidade').fill('São Paulo')
    await page.getByLabel('UF').selectOption('SP')
    await page.getByLabel('Unidade principal').check()
    await page.getByRole('button', { name: 'Salvar unidade' }).click()
    await expect(page.getByText(`Matriz ${runId}`).first()).toBeVisible()

    await page.getByRole('button', { name: 'Desativar cliente' }).click()
    await page.getByRole('dialog', { name: 'Desativar cliente' }).getByRole('button', { name: 'Desativar cliente' }).click()
    await expect(page.getByText('Inativo')).toBeVisible()
    await page.getByRole('button', { name: 'Ativar cliente' }).click()
    await page.getByRole('dialog', { name: 'Ativar cliente' }).getByRole('button', { name: 'Ativar cliente' }).click()
    await expect(page.getByRole('button', { name: 'Desativar cliente' })).toBeVisible()
    await page.goto('/clientes')
    await page.getByLabel('Buscar').fill(customer.legalName)
    await expect(page.getByText(customer.legalName)).toBeVisible()
  })

  test('MANAGER can mutate Customers while USER remains read-only in UI, routing and API', async ({ newIsolatedPage, browser }) => {
    const admin = await newIsolatedPage()
    await readyAdmin(admin)
    const customer = await createCustomer(admin, 'Access', 2)
    const managerAccount = await createAccount(admin, 'MANAGER')
    const userAccount = await createAccount(admin, 'USER')
    const managerContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
    const manager = await managerContext.newPage()
    await login(manager, managerAccount.email, managerAccount.temporary)
    await completePasswordChange(manager, managerAccount.temporary, 'E2eManager1!Password')
    await expect(manager.getByRole('button', { name: 'Clientes' })).toBeVisible()
    await expect(manager.getByRole('button', { name: 'Administração' })).toHaveCount(0)
    await manager.goto(`/clientes/${customer.id}/editar`)
    await expect(manager.getByRole('button', { name: 'Salvar alterações' })).toBeVisible()

    const userContext = await browser.newContext({ storageState: { cookies: [], origins: [] } })
    const user = await userContext.newPage()
    await login(user, userAccount.email, userAccount.temporary)
    await completePasswordChange(user, userAccount.temporary, 'E2eUser1!Password')
    await user.goto('/clientes')
    await expect(user.getByRole('button', { name: 'Novo cliente' })).toHaveCount(0)
    await expect(user.getByLabel('Status')).toHaveCount(0)
    await expect(user.getByRole('button', { name: 'Administração' })).toHaveCount(0)
    await user.goto(`/clientes/${customer.id}/editar`)
    await expect(user.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
    const status = await user.evaluate(async () => (await fetch('/api/customers', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ legalName: 'Blocked', cnpj: '04252011000110' }) })).status)
    expect(status).toBe(403)
    await admin.close(); await managerContext.close(); await userContext.close()
  })

  test('real stale writes are rejected and missing CSRF is rejected', async ({ newIsolatedPage }) => {
    const first = await newIsolatedPage(); const second = await newIsolatedPage()
    await readyAdmin(first)
    const customer = await createCustomer(first, 'Concurrency', 3)
    await second.goto(`/clientes/${customer.id}/editar`)
    await first.goto(`/clientes/${customer.id}/editar`)
    await first.getByLabel('Nome fantasia').fill('Valor atualizado A')
    await first.getByRole('button', { name: 'Salvar alterações' }).click()
    await second.getByLabel('Nome fantasia').fill('Valor obsoleto B')
    await second.getByRole('button', { name: 'Salvar alterações' }).click()
    await expect(second.getByText('Este cliente foi alterado por outro usuário. Atualize os dados e tente novamente.')).toBeVisible()
    const csrfStatus = await first.evaluate(async () => (await fetch(`/api/customers/${location.pathname.split('/')[2]}/deactivate`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ expectedVersion: crypto.randomUUID() }) })).status)
    expect(csrfStatus).toBe(400)
    await first.close(); await second.close()
  })
})
