import { defineConfig } from '@playwright/test'
import path from 'node:path'

const authDirectory = process.env.E2E_AUTH_DIR ?? path.join('.local', 'e2e-auth')

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  globalSetup: './e2e/global.setup.ts',
  workers: 4,
  forbidOnly: Boolean(process.env.CI),
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://127.0.0.1:5173',
    storageState: path.join(authDirectory, 'admin.json'),
    trace: 'retain-on-failure'
  }
})
