import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

// For the code that has no Nuxt in it, such as lib/slots.ts. The pages are tried in a browser, see e2e/.
export default defineConfig({
  resolve: { alias: { '~': fileURLToPath(new URL('.', import.meta.url)) } },
  test: { include: ['tests/**/*.test.ts'], environment: 'node' },
})
