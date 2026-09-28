/* oxlint-disable react-hooks/rules-of-hooks, eslint/no-empty-pattern -- Playwright fixtures require destructuring and name their continuation use. */
import { test as base, expect, type Browser, type BrowserContext, type Page, type Response } from '@playwright/test'
import path from 'node:path'

export { expect }
export type { BrowserContext, Page }

type RuntimeMonitor = {
  observe: (page: Page) => void
  allowServerError: (url: string | RegExp) => void
  allowNetworkFailure: (url: string | RegExp) => void
  errors: string[]
}

export const test = base.extend<{ runtimeMonitor: RuntimeMonitor; runtimeErrors: void; newIsolatedPage: () => Promise<Page> }>({
  runtimeMonitor: async ({}, use) => {
    const errors: string[] = []
    const allowedServerErrors: (string | RegExp)[] = []
    const allowedNetworkFailures: (string | RegExp)[] = []
    const observed = new WeakSet<Page>()
    const matches = (patterns: (string | RegExp)[], url: string) => patterns.some(pattern =>
      typeof pattern === 'string' ? url.includes(pattern) : pattern.test(url))
    const observe = (page: Page) => {
      if (observed.has(page)) return
      observed.add(page)
      page.on('pageerror', error => errors.push(`pageerror: ${error.message}`))
      page.on('console', message => {
        if (message.type() !== 'error') return
        // Resource failures are recorded with their URL below. Chromium's generic
        // console message has no URL, so retaining it would duplicate that signal.
        if (/^Failed to load resource: (the server responded with a status of \d+ \(.*\)|net::ERR_)/i.test(message.text())) return
        errors.push(`console.error: ${message.text()}`)
      })
      page.on('response', response => recordServerError(response, matches, allowedServerErrors, errors))
      page.on('requestfailed', request => {
        // Navigation and React route changes intentionally cancel superseded fetches.
        // Chromium reports those cancellations as ERR_ABORTED, not as failed requests.
        if (request.failure()?.errorText === 'net::ERR_ABORTED') return
        if (isTestedApplicationResource(request.url()) && !matches(allowedNetworkFailures, request.url())) {
          errors.push(`network failure: ${request.method()} ${request.url()}: ${request.failure()?.errorText ?? 'unknown error'}`)
        }
      })
    }
    await use({
      observe,
      allowServerError: url => allowedServerErrors.push(url),
      allowNetworkFailure: url => allowedNetworkFailures.push(url),
      errors
    })
  },
  runtimeErrors: [async ({ browser, context, runtimeMonitor }, use) => {
    observeContext(context, runtimeMonitor.observe)
    const restore = observeBrowser(browser, runtimeMonitor.observe)
    try {
      await use()
      expect(runtimeMonitor.errors, 'Unexpected browser JavaScript, console, API 5xx, or network errors').toEqual([])
    } finally {
      restore()
    }
  }, { auto: true }],
  newIsolatedPage: async ({ browser, runtimeMonitor }, use) => {
    await use(async () => {
      const authDirectory = process.env.E2E_AUTH_DIR ?? path.join('.local', 'e2e-auth')
      const context = await browser.newContext({ storageState: path.join(authDirectory, 'admin.json') })
      const page = await context.newPage()
      runtimeMonitor.observe(page)
      return page
    })
  }
})

function observeContext(context: BrowserContext, observe: (page: Page) => void) {
  context.pages().forEach(observe)
  context.on('page', observe)
}

function observeBrowser(browser: Browser, observe: (page: Page) => void) {
  browser.contexts().forEach(context => observeContext(context, observe))
  const originalNewContext = browser.newContext.bind(browser)
  const originalNewPage = browser.newPage.bind(browser)
  browser.newContext = async (...args) => {
    const context = await originalNewContext(...args)
    observeContext(context, observe)
    return context
  }
  browser.newPage = async (...args) => {
    const page = await originalNewPage(...args)
    observe(page)
    return page
  }
  return () => {
    browser.newContext = originalNewContext
    browser.newPage = originalNewPage
  }
}

function recordServerError(response: Response, matches: (patterns: (string | RegExp)[], url: string) => boolean, allowed: (string | RegExp)[], errors: string[]) {
  if (response.status() < 500 || !isTestedApplicationResource(response.url()) || matches(allowed, response.url())) return
  errors.push(`server error: ${response.status()} ${response.request().method()} ${response.url()}`)
}

function isTestedApplicationResource(url: string) {
  const parsed = new URL(url)
  return parsed.hostname === '127.0.0.1' || parsed.hostname === 'localhost'
}
