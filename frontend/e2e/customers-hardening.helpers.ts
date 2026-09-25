import { expect, type Page } from '@playwright/test'

export type Customer = {
  id: string; legalName: string; tradeName: string | null; cnpj: string; notes: string | null
  isActive: boolean; version: string; createdByUserId: string; updatedByUserId: string
  createdAtUtc: string; updatedAtUtc: string
  contacts: { id: string; name: string; email: string | null; phone: string | null; isPrimary: boolean; isActive: boolean }[]
  units: { id: string; name: string; postalCode: string | null; isPrimary: boolean; isActive: boolean }[]
}
export type ApiResult<T = unknown> = { status: number; body: T }

const adminEmail = process.env.E2E_ADMIN_EMAIL
const adminPassword = process.env.E2E_ADMIN_PASSWORD
if (!adminEmail || !adminPassword) throw new Error('Run Customers browser coverage through scripts/run-e2e.ps1.')

let sequence = 0
export const unique = () => `${Date.now()}-${process.pid}-${++sequence}`
export const freshSeed = () => Math.floor(Math.random() * 899_999_999_999) + 100_000_000_000
export function cnpj(seed: number) {
  const root = `${seed}`.padStart(12, '0')
  const digit = (source: string, weights: number[]) => {
    const remainder = [...source].reduce((sum, value, index) => sum + Number(value) * weights[index], 0) % 11
    return remainder < 2 ? 0 : 11 - remainder
  }
  const first = digit(root, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
  return root + first + digit(root + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
}
export const customerInput = (seed: number, changes: Record<string, unknown> = {}) => ({
  legalName: `E2E-${unique()}-Customer`, tradeName: null, cnpj: cnpj(seed), notes: null, ...changes
})
export const contactInput = (changes: Record<string, unknown> = {}) => ({
  name: `E2E Contact ${unique()}`, roleOrDepartment: null, email: `contact-${unique()}@example.test`, phone: null, isPrimary: false, ...changes
})
export const unitInput = (changes: Record<string, unknown> = {}) => ({
  name: `E2E Unit ${unique()}`, street: 'Rua E2E', number: '1', complement: null, district: null,
  city: 'São Paulo', stateCode: 'SP', postalCode: null, isPrimary: false, ...changes
})

export async function readyAdmin(page: Page) {
  await page.goto('/')
  await page.getByLabel('E-mail').fill(adminEmail!)
  await page.getByLabel('Senha').fill(adminPassword!)
  const loginResponse = page.waitForResponse(response => response.url().endsWith('/api/auth/login') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar' }).click()
  if ((await loginResponse).status() === 401) {
    await page.getByLabel('Senha').fill('E2eAdmin1!Password')
    const retryResponse = page.waitForResponse(response => response.url().endsWith('/api/auth/login') && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Entrar' }).click()
    expect((await retryResponse).status()).toBe(200)
  }
  const passwordHeading = page.getByRole('heading', { name: 'Alterar senha' })
  const administration = page.getByRole('button', { name: /^Administra/ })
  await Promise.race([passwordHeading.waitFor({ state: 'visible' }), administration.waitFor({ state: 'visible' })])
  if (await passwordHeading.isVisible()) {
    await page.getByLabel('Senha atual').fill(adminPassword!)
    await page.getByLabel('Nova senha', { exact: true }).fill('E2eAdmin1!Password')
    await page.getByLabel('Confirmar nova senha').fill('E2eAdmin1!Password')
    await page.getByRole('button', { name: 'Alterar senha' }).click()
    await expect(administration).toBeVisible()
  }
}

export async function api<T = unknown>(page: Page, method: string, path: string, body?: unknown, csrf: 'valid' | 'missing' | 'invalid' = 'valid'): Promise<ApiResult<T>> {
  return page.evaluate(async ({ method, path, body, csrf }) => {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' }
    if (csrf !== 'missing' && method !== 'GET') {
      const token = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json() as { token: string }
      headers['X-CSRF-TOKEN'] = csrf === 'invalid' ? 'invalid-token' : token.token
    }
    const response = await fetch(path, { method, headers, credentials: 'include', body: body === undefined ? undefined : JSON.stringify(body) })
    const text = await response.text()
    let parsed: unknown = null
    try { parsed = text ? JSON.parse(text) : null } catch { parsed = text }
    return { status: response.status, body: parsed as T }
  }, { method, path, body, csrf })
}

export async function createCustomer(page: Page, seed: number, changes: Record<string, unknown> = {}) {
  const result = await api<Customer>(page, 'POST', '/api/customers', customerInput(seed, changes))
  expect(result.status, JSON.stringify(result.body)).toBe(201)
  return result.body
}
export async function getCustomer(page: Page, id: string) {
  const result = await api<Customer>(page, 'GET', `/api/customers/${id}`)
  expect(result.status).toBe(200)
  return result.body
}

export async function createRoleSession(admin: Page, target: Page, role: 'MANAGER' | 'USER') {
  const email = `e2e-${role.toLowerCase()}-${unique()}@example.test`
  const created = await api<{ temporaryPassword: string; user: { id: string } }>(admin, 'POST', '/api/admin/users',
    { fullName: `E2E ${role}`, email, role })
  expect(created.status).toBe(201)
  await target.goto('/')
  expect((await api(target, 'POST', '/api/auth/login', { email, password: created.body.temporaryPassword })).status).toBe(200)
  expect((await api(target, 'POST', '/api/auth/change-password', {
    currentPassword: created.body.temporaryPassword, newPassword: 'E2eUser1!Password'
  })).status).toBe(204)
  await target.reload()
  return created.body.user.id
}
