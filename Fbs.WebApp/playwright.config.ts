import { defineConfig, devices } from '@playwright/test'
import type { ConfigOptions } from '@nuxt/test-utils/playwright'

const isCI = !!process.env.CI
const port = 3123

// See https://nuxt.com/docs/getting-started/testing#testing-with-playwright-test-runner
export default defineConfig<ConfigOptions>({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  reporter: isCI ? [['github'], ['list']] : 'list',
  // Builds the app once and serves it for every test, instead of @nuxt/test-utils building it again in each worker. It is the `test` build (see
  // nuxt.config.ts): nobody has to sign in, and the API is answered by the tests, in the browser.
  webServer: {
    command: 'nuxt build && node .output/server/index.mjs',
    url: `http://localhost:${port}`,
    env: { NUXT_PUBLIC_AUTH_MODE: 'test', NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY: '', PORT: String(port) },
    timeout: 5 * 60_000,
  },
  use: {
    nuxt: {
      host: `http://localhost:${port}`,
    },
    ...devices['Desktop Chrome'],
    viewport: { width: 1100, height: 800 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
})
