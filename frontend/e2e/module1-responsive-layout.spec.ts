import { expect, test } from './customers-runtime.fixture'
import { readyAdmin } from './customers-hardening.helpers'

async function expectWithinViewport(page: import('@playwright/test').Page, locator: string) {
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

test('Administration users layout fits every supported responsive width', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await readyAdmin(page)
  await page.getByRole('button', { name: /^Administra/ }).click()

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
})
