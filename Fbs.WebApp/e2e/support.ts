// What the browser tests share: Nuxt's Playwright fixtures (which build and serve the app, see playwright.config.ts), and `api`, which answers
// the app's calls to the API in the browser, so nothing else has to be running.
import type { Request } from '@playwright/test'
import { test as nuxt } from '@nuxt/test-utils/playwright'

export { expect } from '@nuxt/test-utils/playwright'

/** Where the app calls the API in these tests: the default in nuxt.config.ts. Nothing is listening, as every call is answered in the browser. */
const apiUrl = 'https://localhost:5204'

/** What the API answers with. */
export interface Reply {
  status: number
  contentType?: string
  body: string
}

/** Answers `request` as the API would; `url` is its address. */
export type Handler = (request: Request, url: URL) => Reply | Promise<Reply>

/** `METHOD /path` (such as `GET /Me`) to how it is answered. Anything not in it is a 404. */
export type Table = Record<string, Handler | undefined>

/** A call the app made to the API. */
export interface Call {
  /** `METHOD /path`, as in a `Table` */
  key: string
  /** The `Authorization` header */
  auth?: string
  /** The JSON that was sent, or undefined if there was none */
  // eslint-disable-next-line @typescript-eslint/no-explicit-any -- what was sent is whatever a test expects it to be
  body: any
  query: Record<string, string>
}

export const json = (body: unknown, status = 200): Reply => ({ status, contentType: 'application/json', body: JSON.stringify(body) })
export const problem = (status: number, errors: { name: string, reason: string, code: string }[]): Reply =>
  ({ status, contentType: 'application/problem+json', body: JSON.stringify({ title: 'x', status, errors }) })
export const noContent = (): Reply => ({ status: 204, body: '' })

export const test = nuxt.extend<{
  /** Answers the API's calls from `table` for the rest of the test, and gives back the list of calls made, which grows as the app makes them. */
  api: (table: Table) => Promise<Call[]>
}>({
  api: async ({ page }, use) => {
    await use(async (table) => {
      const calls: Call[] = []
      await page.route(`${apiUrl}/**`, async (route) => {
        const request = route.request()
        // The app is served from another origin, so the browser checks the API allows it
        const cors = {
          'access-control-allow-origin': request.headers()['origin'] ?? '*',
          'access-control-allow-credentials': 'true',
          'access-control-allow-headers': '*',
          'access-control-allow-methods': '*',
        }
        if (request.method() === 'OPTIONS') {
          return route.fulfill({ status: 204, headers: cors })
        }

        const url = new URL(request.url())
        const key = `${request.method()} ${url.pathname}`
        calls.push({ key, auth: request.headers()['authorization'], body: request.postData() ? request.postDataJSON() : undefined, query: Object.fromEntries(url.searchParams) })
        const handler = table[key]
        const reply = handler ? await handler(request, url) : json({ title: 'not answered by the test' }, 404)
        return route.fulfill({ ...reply, headers: cors })
      })
      return calls
    })
  },
})
