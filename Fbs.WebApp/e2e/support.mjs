// What the browser tests share: a browser, and a way to answer the API's calls from a table.
import { chromium } from 'playwright'

export const base = process.env.BASE_URL ?? 'http://localhost:3123'
export const api = process.env.API_URL ?? 'https://localhost:5204'
// Screenshots are kept if this is set, for looking at
const out = process.env.SCREENSHOTS

export const json = (body, status = 200) => ({
  status,
  contentType: 'application/json',
  body: JSON.stringify(body),
  headers: { 'access-control-allow-origin': base, 'access-control-allow-credentials': 'true', 'access-control-allow-headers': '*' },
})
export const problem = (status, errors) => ({ ...json({ title: 'x', status, errors }, status), contentType: 'application/problem+json' })
export const noContent = () => ({ status: 204, body: '', headers: { 'access-control-allow-origin': base } })

/** Answers the API's calls from a table of `METHOD /path` to a function of the request, and notes what was asked. */
export async function stub(page, table, log = []) {
  await page.route(`${api}/**`, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const key = `${request.method()} ${url.pathname}`
    if (request.method() === 'OPTIONS') {
      return route.fulfill({
        status: 204,
        headers: {
          'access-control-allow-origin': base,
          'access-control-allow-headers': '*',
          'access-control-allow-methods': '*',
          'access-control-allow-credentials': 'true',
        },
      })
    }

    log.push({ key, auth: request.headers()['authorization'], body: request.postData(), query: Object.fromEntries(url.searchParams) })
    const handler = table[key]
    if (!handler) {
      return route.fulfill(json({ title: 'not stubbed' }, 404))
    }

    return route.fulfill(await handler(request, url))
  })
  return log
}

export async function shot(page, name) {
  if (out) {
    await page.screenshot({ path: `${out}/${name}.png`, fullPage: true })
  }
}

const results = []

export function check(name, ok, detail = '') {
  results.push({ name, ok })
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name} ${ok ? '' : detail}`)
}

export async function start() {
  const browser = await chromium.launch()
  const context = await browser.newContext({ viewport: { width: 1100, height: 800 }, ignoreHTTPSErrors: true })
  return { browser, context }
}

export async function finish(browser) {
  await browser.close()
  const failed = results.filter(r => !r.ok)
  console.log(`\n${results.length - failed.length}/${results.length} passed`)
  process.exit(failed.length ? 1 : 0)
}
