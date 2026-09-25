import { expect, test, type Page } from '@playwright/test'

const adminEmail = process.env.E2E_ADMIN_EMAIL
const bootstrapPassword = process.env.E2E_ADMIN_PASSWORD
const permanentPassword = 'E2eAdmin1!Password'

if (!adminEmail || !bootstrapPassword) throw new Error('Run Module 1 browser coverage through scripts/run-e2e.ps1.')

async function readyAdmin(page: Page) {
  let password = bootstrapPassword!
  await page.goto('/')
  await page.getByLabel('E-mail').fill(adminEmail!)
  await page.getByLabel('Senha').fill(password)
  let response = page.waitForResponse(request => request.url().endsWith('/api/auth/login') && request.request().method() === 'POST')
  await page.getByRole('button', { name: 'Entrar' }).click()
  if ((await response).status() === 401) {
    password = permanentPassword
    await page.goto('/')
    await page.getByLabel('E-mail').fill(adminEmail!)
    await page.getByLabel('Senha').fill(password)
    response = page.waitForResponse(request => request.url().endsWith('/api/auth/login') && request.request().method() === 'POST')
    await page.getByRole('button', { name: 'Entrar' }).click()
    expect((await response).status()).toBe(200)
  }
  const passwordHeading = page.getByRole('heading', { name: 'Alterar senha' })
  const usersHeading = page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })
  await Promise.race([passwordHeading.waitFor({ state: 'visible' }), usersHeading.waitFor({ state: 'visible' })])
  if (await passwordHeading.isVisible()) {
    await page.getByLabel('Senha atual').fill(password)
    await page.getByLabel('Nova senha', { exact: true }).fill(permanentPassword)
    await page.getByLabel(/^Confirmar/).fill(permanentPassword)
    await page.getByRole('button', { name: 'Alterar senha' }).click()
  }
  await expect(usersHeading).toBeVisible()
}

async function expectWithinViewport(page: Page, locator: string) {
  const boxes = await page.locator(locator).evaluateAll(elements => elements.map(element => {
    const box = element.getBoundingClientRect()
    return { left: box.left, right: box.right, width: box.width }
  }))
  for (const box of boxes) {
    expect(box.left).toBeGreaterThanOrEqual(0)
    expect(box.right).toBeLessThanOrEqual((await page.evaluate(() => window.innerWidth)) + 1)
    expect(box.width).toBeGreaterThan(0)
  }
}

test('Administration users layout fits every supported responsive width', async ({ browser }) => {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  await readyAdmin(page)

  for (const width of [320, 390, 430, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    await expect(page.getByRole('heading', { name: /^Usu.rios$/, level: 2 })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth === document.documentElement.clientWidth)).toBe(true)
    await expectWithinViewport(page, '.admin-grid > .card')
    await expectWithinViewport(page, '.admin-grid input, .admin-grid select, .admin-grid > .card > form > button, .admin-grid .filters > button')

    if (width <= 760) {
      await expect(page.locator('.mobile-record-list')).toBeVisible()
      await expect(page.locator('.desktop-record-table')).toBeHidden()
      await expectWithinViewport(page, '.mobile-record .actions button')
      if (width <= 560) {
        expect(await page.locator('.filters').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length)).toBe(1)
      }
    } else {
      await expect(page.locator('.desktop-record-table')).toBeVisible()
      await expect(page.locator('.mobile-record-list')).toBeHidden()
    }
  }

  await page.close()
})
