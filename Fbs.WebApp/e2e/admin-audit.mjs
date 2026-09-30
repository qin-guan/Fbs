// The history of an organization, for its admins: what is shown, in the organization's time, and asking for older ones a page at a time.
// The browser is put in a time zone that isn't the organization's. Run by e2e/run.mjs.
import { base, check, finish, json, problem, shot, start, stub } from './support.mjs'

const { browser, context: ordinary } = await start()
await ordinary.close()
// Los Angeles, when the organization is in Singapore
const context = await browser.newContext({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true, timezoneId: 'America/Los_Angeles' })

const org = role => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const person = (id, displayName) => ({ memberId: id, displayName })
const sam = person('m1', 'CPT Sam')
const lee = person('m2', 'SGT Lee')

const table = ({ role = 'Admin', entries = [], audit } = {}) => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Audit': audit ?? ((_r, url) => {
    const before = url.searchParams.get('before')
    const limit = Number(url.searchParams.get('limit') ?? 50)
    return json(entries.filter(e => !before || new Date(e.at) < new Date(before)).slice(0, limit))
  }),
})

const entry = (id, at, action, summary, actor, target = null) => ({ id, at, actor, action, summary, targetType: target ? 'member' : null, targetId: target?.memberId ?? null, target })
const asked = log => log.filter(l => l.key === 'GET /t/alpha/Audit')
const rows = page => page.locator('li[data-action]')

// 1. What is shown
{
  const page = await context.newPage()
  await stub(page, table({
    entries: [
      // Ten in the morning in Singapore, on a day in October
      entry('e1', '2026-10-05T02:00:00Z', 'member.let_in', 'Let a person in.', sam, lee),
      entry('e2', '2026-10-05T01:00:00Z', 'member.joined', 'Joined with an invite link, and is waiting to be let in.', lee, lee),
      entry('e3', '2026-10-04T16:30:00Z', 'tenant.suspended', 'Suspended by whoever runs the system.', null),
      entry('e4', '2026-10-04T10:00:00Z', 'facility.created', 'Added the facility Hall.', sam),
    ],
  }))
  await page.goto(`${base}/t/alpha/admin/audit`)
  await rows(page).first().waitFor()
  check('each is listed with what was done', (await rows(page).count()) === 4 && (await rows(page).first().innerText()).includes('Let a person in.'))
  check('who did it, and to whom, is said', (await rows(page).nth(0).getByTestId('actor').innerText()) === 'CPT Sam' && (await rows(page).nth(0).getByTestId('target').innerText()) === 'SGT Lee')
  check('who did it to themselves is not said twice', (await rows(page).nth(1).getByTestId('target').count()) === 0 && (await rows(page).nth(1).getByTestId('actor').innerText()) === 'SGT Lee')
  check('what was done by nobody in it is said to be by whoever runs the system', (await rows(page).nth(2).getByTestId('actor').innerText()) === 'Whoever runs the system')
  const first = await rows(page).first().innerText()
  check('the time is the organization\'s, not the browser\'s', first.includes('10:00'), first)
  check('what each is about has an icon of its own', (await rows(page).nth(3).locator('[class*="building-2"]').count()) === 1 && (await rows(page).nth(2).locator('[class*="shield"]').count()) === 1)
  check('with no more to ask for, there is no button', (await page.getByRole('button', { name: 'Show older' }).count()) === 0)
  await shot(page, 'admin-audit')
  check('it is in the sidebar', (await page.getByRole('link', { name: 'History' }).getAttribute('href')) === '/t/alpha/admin/audit')
  await page.close()
}

// 2. Older ones, a page at a time
{
  const start = Date.UTC(2026, 9, 5, 2, 0, 0)
  const entries = Array.from({ length: 120 }, (_, i) => entry(`e${i}`, new Date(start - i * 60_000).toISOString(), 'unit.created', `Added the unit ${i}.`, sam))
  const page = await context.newPage()
  const log = await stub(page, table({ entries }))
  await page.goto(`${base}/t/alpha/admin/audit`)
  await rows(page).first().waitFor()
  check('a page is 50', (await rows(page).count()) === 50 && asked(log)[0].query.limit === '50' && asked(log)[0].query.before === undefined)

  await page.getByRole('button', { name: 'Show older' }).click()
  await page.getByText('Added the unit 99.').waitFor()
  check('older ones are asked for from the last one seen', asked(log).at(-1).query.before === entries[49].at && (await rows(page).count()) === 100, `${asked(log).at(-1).query.before} vs ${entries[49].at}`)
  const texts = await rows(page).locator('p.font-medium').allInnerTexts()
  check('in order, none twice', texts.join() === entries.slice(0, 100).map(e => e.summary).join())

  await page.getByRole('button', { name: 'Show older' }).click()
  await page.getByText('Added the unit 119.').waitFor()
  check('the last page is shorter, and there is nothing to ask for after it', (await rows(page).count()) === 120 && (await page.getByRole('button', { name: 'Show older' }).count()) === 0)
  await page.close()
}

// 3. Nothing, and the API refusing
{
  const page = await context.newPage()
  await stub(page, table())
  await page.goto(`${base}/t/alpha/admin/audit`)
  await page.getByText('Nothing has been done yet.').waitFor()
  check('nothing done is said', true)
  await page.close()

  const failing = await context.newPage()
  await stub(failing, table({ audit: () => problem(400, [{ name: 'limit', reason: 'Not that.', code: 'x' }]) }))
  await failing.goto(`${base}/t/alpha/admin/audit`)
  await failing.getByText('Couldn\'t load the history').waitFor()
  check('why is said', (await failing.getByText('Not that.').count()) === 1)
  await failing.close()
}

// 4. Only for admins
{
  const page = await context.newPage()
  const log = await stub(page, table({ role: 'Member' }))
  await page.goto(`${base}/t/alpha/admin/audit`)
  await page.getByText('Only admins can do this').waitFor()
  check('a member is told it is for admins, and nothing is asked for', asked(log).length === 0)
  check('and it is not in their sidebar', (await page.getByRole('link', { name: 'History' }).count()) === 0)
  await page.close()
}

await finish(browser)
