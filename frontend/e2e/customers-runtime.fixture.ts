/* oxlint-disable react-hooks/rules-of-hooks, eslint/no-empty-pattern -- Playwright fixtures require destructuring and name their continuation use. */
import { test as base, expect, type BrowserContext, type Page } from '@playwright/test'

export { expect }
export type { Page }

type RuntimeMonitor = { observe: (page: Page) => void; errors: string[] }

export const test = base.extend<{ runtimeMonitor: RuntimeMonitor; runtimeErrors: void; newIsolatedPage: () => Promise<Page> }>({
  runtimeMonitor: async ({}, use) => {
    const errors: string[] = []
    const observed = new WeakSet<Page>()
    const observe = (page: Page) => {
      if (observed.has(page)) return
      observed.add(page)
      page.on('pageerror', error => errors.push(`pageerror: ${error.message}`))
      page.on('console', message => {
        if (message.type() !== 'error') return
        // Chromium reports expected negative HTTP responses as resource errors.
        if (/Failed to load resource.*(4\d\d|5\d\d)|Failed to load resource: net::ERR_/i.test(message.text())) return
        errors.push(`console.error: ${message.text()}`)
      })
    }
    await use({ observe, errors })
  },
  runtimeErrors: [async ({ context, runtimeMonitor }, use) => {
    observeContext(context, runtimeMonitor.observe)
    await use()
    expect(runtimeMonitor.errors, 'Unexpected browser JavaScript or React errors').toEqual([])
  }, { auto: true }],
  newIsolatedPage: async ({ browser, runtimeMonitor }, use) => {
    await use(async () => {
      const page = await browser.newPage()
      runtimeMonitor.observe(page)
      return page
    })
  }
})

function observeContext(context: BrowserContext, observe: (page: Page) => void) {
  context.pages().forEach(observe)
  context.on('page', observe)
}
