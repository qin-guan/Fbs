// Checks the pages for accounts in a real browser, against a build made with NUXT_PUBLIC_AUTH_MODE=test (see
// nuxt.config.ts), where nobody needs to sign in. The API is answered by this script, so nothing else has to be running.
//
//   pnpm test:e2e            builds the app for it, serves it, and runs this
//
import { chromium } from 'playwright'

const base = process.env.BASE_URL ?? 'http://localhost:3123'
const api = process.env.API_URL ?? 'https://localhost:5204'
// Screenshots are kept if this is set, for looking at
const out = process.env.SCREENSHOTS
async function shot(page, name) {
  if (out) {
    await page.screenshot({ path: `${out}/${name}.png`, fullPage: true })
  }
}

const json = (body, status = 200) => ({ status, contentType: 'application/json', body: JSON.stringify(body), headers: { 'access-control-allow-origin': base, 'access-control-allow-credentials': 'true', 'access-control-allow-headers': '*' } })
const problem = (status, errors) => json({ title: 'x', status, errors }, status)

/** Answers the API's calls from a table of `METHOD /path` to a function of the request. */
async function stub(page, table, log) {
  await page.route(`${api}/**`, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const key = `${request.method()} ${url.pathname}`
    if (request.method() === 'OPTIONS') {
      return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': base, 'access-control-allow-headers': '*', 'access-control-allow-methods': '*', 'access-control-allow-credentials': 'true' } })
    }
    log.push({ key, auth: request.headers()['authorization'], body: request.postData() })
    const handler = table[key]
    if (!handler) return route.fulfill(json({ title: 'not stubbed' }, 404))
    return route.fulfill(await handler(request))
  })
}

const results = []
function check(name, ok, detail = '') {
  results.push({ name, ok })
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name} ${ok ? '' : detail}`)
}

const browser = await chromium.launch()
const context = await browser.newContext({ viewport: { width: 1000, height: 800 }, ignoreHTTPSErrors: true })

// 1. Nobody in any organization yet: sent to get started, and the token goes with the request
{
  const page = await context.newPage()
  const log = []
  await stub(page, { 'GET /Me': () => json({ id: 'a', name: 'Sam Lee', email: 'sam@example.com', memberships: [] }) }, log)
  await page.goto(base + '/')
  await page.waitForURL('**/onboarding')
  check('no memberships goes to onboarding', page.url().endsWith('/onboarding'))
  check('token sent as a bearer', log.some(l => l.key === 'GET /Me' && l.auth === 'Bearer test-token'), JSON.stringify(log))
  await page.getByLabel('Name').first().fill('Alpha Company')
  const slug = await page.getByPlaceholder('alpha-company').inputValue()
  check('the address follows the name', slug === 'alpha-company', slug)
  await shot(page, 'onboarding')
  await page.close()
}

// 2. Making an organization
{
  const page = await context.newPage()
  const log = []
  await stub(page, {
    'GET /Me': () => json({ id: 'a', name: 'Sam Lee', email: 'sam@example.com', memberships: [] }),
    'POST /Tenants': (r) => {
      const b = JSON.parse(r.postData())
      return b.slug === 'taken' ? problem(409, [{ name: 'slug', reason: 'That address is taken.', code: 'slug-taken' }]) : json({ slug: b.slug, name: b.name, timeZone: b.timeZone }, 201)
    },
  }, log)
  await page.goto(base + '/onboarding')
  await page.getByLabel('Name').first().fill('Taken One')
  await page.getByPlaceholder('alpha-company').fill('taken')
  await page.getByRole('button', { name: 'Make organization' }).click()
  await page.getByText('That address is taken').waitFor()
  check('a taken address is said so', true)
  await page.getByPlaceholder('alpha-company').fill('bravo-company')
  // The message going away moves the button, so this submits from the field rather than chasing it
  await page.getByPlaceholder('alpha-company').press('Enter')
  await page.waitForURL('**/t/bravo-company')
  check('making one goes into it', page.url().endsWith('/t/bravo-company'))
  const posted = JSON.parse(log.filter(l => l.key === 'POST /Tenants').at(-1).body)
  check('what is sent', posted.name === 'Taken One' && posted.slug === 'bravo-company' && !!posted.timeZone && /^\d+$/.test(posted.defaultCountryCode), JSON.stringify(posted))
  await page.close()
}

// 3. One place goes straight in, several are picked from
{
  const page = await context.newPage()
  const log = []
  await stub(page, { 'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] }) }, log)
  await page.goto(base + '/')
  await page.waitForURL('**/t/alpha')
  check('one active membership goes straight in', page.url().endsWith('/t/alpha'))
  await page.close()

  const many = await context.newPage()
  await stub(many, { 'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [
    { tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' },
    { tenantSlug: 'bravo', tenantName: 'Bravo Club', role: 'Member', status: 'Active', displayName: 'Sam' },
    { tenantSlug: 'charlie', tenantName: 'Charlie Team', role: 'Member', status: 'Pending', displayName: 'Sam' },
  ] }) }, [])
  await many.goto(base + '/')
  await many.waitForURL('**/orgs')
  check('several go to the picker', many.url().endsWith('/orgs'))
  await many.getByText('Waiting for approval').first().waitFor()
  check('a pending one is marked', true)
  await shot(many, 'orgs')
  await many.close()
}

// 4. Joining with a link
{
  const page = await context.newPage()
  const log = []
  await stub(page, {
    'GET /Me': () => json({ id: 'a', name: 'Sam Lee', email: null, memberships: [] }),
    'GET /Invites/goodtokengoodtokengoodtoken': () => json({ organizationName: 'Alpha Company', requiresApproval: true }),
    'POST /Invites/goodtokengoodtokengoodtoken/Accept': () => json({ slug: 'alpha', organizationName: 'Alpha Company', status: 'Pending' }),
  }, log)
  await page.goto(base + '/join/goodtokengoodtokengoodtoken')
  await page.getByText('Join Alpha Company').waitFor()
  await shot(page, 'join')
  const name = await page.getByLabel('Your name').inputValue()
  check('the name starts as the account name', name === 'Sam Lee', name)
  await page.getByRole('button', { name: 'Ask to join' }).click()
  await page.getByText('You asked to join Alpha Company').waitFor()
  check('waiting for approval is said', true)
  check('the name is sent', JSON.parse(log.find(l => l.key.startsWith('POST /Invites')).body).displayName === 'Sam Lee')
  await page.close()

  const gone = await context.newPage()
  await stub(gone, { 'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }) }, [])
  await gone.goto(base + '/join/badtokenbadtokenbadtokenbad')
  await gone.getByText('This link doesn\'t work').waitFor()
  check('a link that does not work is said so', true)
  await gone.close()

  const paste = await context.newPage()
  await stub(paste, { 'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }) }, [])
  await paste.goto(base + '/onboarding')
  await paste.getByLabel('Link to join with').fill('https://app.example/join/goodtokengoodtokengoodtoken')
  await paste.getByRole('button', { name: 'Join', exact: true }).click()
  await paste.waitForURL('**/join/goodtokengoodtokengoodtoken')
  check('a pasted link goes to it', true)
  await paste.close()
}

// 5. Claiming
{
  const page = await context.newPage()
  const log = []
  let claimed = false
  await stub(page, {
    'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: claimed ? [{ tenantSlug: '3sib', tenantName: '3SIB', role: 'Member', status: 'Active', displayName: 'CPT Sam' }] : [] }),
    'GET /Claims/3sib': () => json({ organizationName: '3SIB' }),
    'POST /Claims/3sib/Start': () => json({ url: 'https://t.me/fbs_bot?start=claim_abc', expiresAt: new Date(Date.now() + 600000).toISOString() }),
  }, log)
  await page.goto(base + '/claim/3sib')
  await page.getByText('Take over your place in 3SIB').waitFor()
  await page.getByRole('button', { name: 'Get my link' }).click()
  const link = page.getByRole('link', { name: 'Open in Telegram' })
  await link.waitFor()
  check('the link to open is shown', (await link.getAttribute('href')) === 'https://t.me/fbs_bot?start=claim_abc')
  await shot(page, 'claim')
  claimed = true
  await page.waitForURL('**/t/3sib', { timeout: 10000 })
  check('once claimed it goes in by itself', true)
  await page.close()

  const nothing = await context.newPage()
  await stub(nothing, { 'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }) }, [])
  await nothing.goto(base + '/claim/other')
  await nothing.getByText('There is nothing to take over here').waitFor()
  check('nothing to claim is said', true)
  await nothing.close()
}

// 6. The account page and Telegram
{
  const page = await context.newPage()
  const log = []
  let linked = false
  await stub(page, {
    'GET /Me': () => json({ id: 'a', name: 'Sam Lee', email: 'sam@example.com', memberships: [] }),
    'GET /Me/Telegram': () => json({ linked, linkedAt: linked ? new Date().toISOString() : null }),
    'POST /Me/Telegram/Link': () => json({ url: 'https://t.me/fbs_bot?start=abc', expiresAt: new Date(Date.now() + 600000).toISOString() }),
    'DELETE /Me/Telegram': () => {
      linked = false
      return { status: 204, body: '', headers: { 'access-control-allow-origin': base } }
    },
  }, log)
  await page.goto(base + '/account')
  await page.getByText('sam@example.com').or(page.locator('input[value="sam@example.com"]')).first().waitFor()
  await page.getByRole('button', { name: 'Connect Telegram' }).waitFor()
  await shot(page, 'account')
  await page.getByRole('button', { name: 'Connect Telegram' }).click()
  await page.getByText('Press Start in Telegram').waitFor()
  check('connecting says what to do', true)
  linked = true
  await page.getByText('Connected').first().waitFor({ timeout: 10000 })
  check('it notices the connection', true)
  await page.getByRole('button', { name: 'Disconnect' }).click()
  await page.getByRole('button', { name: 'Connect Telegram' }).waitFor()
  check('disconnecting works', true)
  await page.close()
}

// 7. The old pages are not there
{
  const page = await context.newPage()
  await stub(page, { 'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }) }, [])
  await page.goto(base + '/booking')
  await page.waitForURL('**/onboarding')
  check('the old phone number pages are not reachable', !page.url().includes('/booking'))
  await page.close()
}

await browser.close()
const failed = results.filter(r => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} passed`)
process.exit(failed.length ? 1 : 0)
