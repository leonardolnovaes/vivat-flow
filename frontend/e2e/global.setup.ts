import { chromium, type Page, type FullConfig } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

const stablePassword = 'E2eStable1!Password'

export default async function globalSetup(config: FullConfig) {
  if (process.env.E2E_REUSE_AUTH === 'true') return

  const bootstrapEmail = required('E2E_ADMIN_EMAIL')
  const bootstrapPassword = required('E2E_ADMIN_PASSWORD')
  const authDirectory = process.env.E2E_AUTH_DIR ?? path.join('.local', 'e2e-auth')
  await mkdir(authDirectory, { recursive: true })

  const baseURL = config.projects[0].use.baseURL as string
  const browser = await chromium.launch()
  try {
    const bootstrap = await browser.newContext()
    const bootstrapPage = await bootstrap.newPage()
    await bootstrapPage.goto(baseURL)
    await mutate(bootstrapPage, '/api/auth/login', { email: bootstrapEmail, password: bootstrapPassword })
    await mutate(bootstrapPage, '/api/auth/change-password', { currentPassword: bootstrapPassword, newPassword: stablePassword })

    await createPrincipal(browser, bootstrapPage, 'ADMIN', authDirectory)
    await createPrincipal(browser, bootstrapPage, 'MANAGER', authDirectory)
    await createPrincipal(browser, bootstrapPage, 'USER', authDirectory)
    await bootstrap.close()
  } finally {
    await browser.close()
  }
}

async function createPrincipal(browser: Awaited<ReturnType<typeof chromium.launch>>, admin: Page, role: 'ADMIN' | 'MANAGER' | 'USER', authDirectory: string) {
  const email = `e2e-stable-${role.toLowerCase()}@example.test`
  const response = await mutate(admin, '/api/admin/users', { fullName: `E2E Stable ${role}`, email, role })
  if (response.status !== 201) throw new Error(`Could not create stable ${role} principal: ${response.status}`)
  const context = await browser.newContext()
  try {
    const page = await context.newPage()
    await page.goto(admin.url())
    await mutate(page, '/api/auth/login', { email, password: response.body.temporaryPassword })
    await mutate(page, '/api/auth/change-password', { currentPassword: response.body.temporaryPassword, newPassword: stablePassword })
    await context.storageState({ path: path.join(authDirectory, `${role.toLowerCase()}.json`) })
  } finally {
    await context.close()
  }
}

async function mutate(page: Page, endpoint: string, body: unknown): Promise<{ status: number; body: any }> {
  return page.evaluate(async ({ endpoint, body }) => {
    const csrf = await fetch('/api/auth/csrf', { credentials: 'include' })
    const { token } = await csrf.json() as { token: string }
    const response = await fetch(endpoint, {
      method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify(body)
    })
    const text = await response.text()
    return { status: response.status, body: text ? JSON.parse(text) : null }
  }, { endpoint, body })
}

function required(name: string) {
  const value = process.env[name]
  if (!value) throw new Error(`${name} is required for E2E setup.`)
  return value
}
