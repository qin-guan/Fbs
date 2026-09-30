// The history of an organization, for its admins: what is shown, in the organization's time, and asking for older ones a page at a time.
// The browser is put in a time zone that isn't the organization's.
import type { Page } from '@playwright/test'
import { expect, json, problem, test } from './support'
import type { Call, Handler, Table } from './support'

// Los Angeles, when the organization is in Singapore
test.use({ timezoneId: 'America/Los_Angeles', viewport: { width: 1100, height: 900 } })

const org = (role: string) => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
interface Person { memberId: string, displayName: string }
const sam: Person = { memberId: 'm1', displayName: 'CPT Sam' }
const lee: Person = { memberId: 'm2', displayName: 'SGT Lee' }

const entry = (id: string, at: string, action: string, summary: string, actor: Person | null, target: Person | null = null) =>
  ({ id, at, actor, action, summary, targetType: target ? 'member' : null, targetId: target?.memberId ?? null, target })
type Entry = ReturnType<typeof entry>

const table = ({ role = 'Admin', entries = [], audit }: { role?: string, entries?: Entry[], audit?: Handler } = {}): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Audit': audit ?? ((_request, url) => {
    const before = url.searchParams.get('before')
    const limit = Number(url.searchParams.get('limit') ?? 50)
    return json(entries.filter(e => !before || new Date(e.at) < new Date(before)).slice(0, limit))
  }),
})

const asked = (calls: Call[]) => calls.filter(c => c.key === 'GET /t/alpha/Audit')
const rows = (page: Page) => page.locator('li[data-action]')

test('each is listed with what was done, who did it and to whom, and when in the organization\'s time', async ({ page, goto, api }) => {
  await api(table({
    entries: [
      // Ten in the morning in Singapore, on a day in October
      entry('e1', '2026-10-05T02:00:00Z', 'member.let_in', 'Let a person in.', sam, lee),
      entry('e2', '2026-10-05T01:00:00Z', 'member.joined', 'Joined with an invite link, and is waiting to be let in.', lee, lee),
      entry('e3', '2026-10-04T16:30:00Z', 'tenant.suspended', 'Suspended by whoever runs the system.', null),
      entry('e4', '2026-10-04T10:00:00Z', 'facility.created', 'Added the facility Hall.', sam),
    ],
  }))

  await goto('/t/alpha/admin/audit', { waitUntil: 'hydration' })

  await expect(rows(page)).toHaveCount(4)
  await expect(rows(page).first()).toContainText('Let a person in.')
  await expect(rows(page).nth(0).getByTestId('actor')).toHaveText('CPT Sam')
  await expect(rows(page).nth(0).getByTestId('target')).toHaveText('SGT Lee')
  // Who did it to themselves is not said twice
  await expect(rows(page).nth(1).getByTestId('target')).toHaveCount(0)
  await expect(rows(page).nth(1).getByTestId('actor')).toHaveText('SGT Lee')
  // What was done by nobody in it is by whoever runs the system
  await expect(rows(page).nth(2).getByTestId('actor')).toHaveText('Whoever runs the system')
  await expect(rows(page).first()).toContainText('10:00')
  // What each is about has an icon of its own
  await expect(rows(page).nth(3).locator('[class*="building-2"]')).toHaveCount(1)
  await expect(rows(page).nth(2).locator('[class*="shield"]')).toHaveCount(1)
  // With no more to ask for, there is no button
  await expect(page.getByRole('button', { name: 'Show older' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'History' })).toHaveAttribute('href', '/t/alpha/admin/audit')
})

test('older ones are asked for a page at a time, from the last one seen', async ({ page, goto, api }) => {
  const start = Date.UTC(2026, 9, 5, 2, 0, 0)
  const entries = Array.from({ length: 120 }, (_, i) => entry(`e${i}`, new Date(start - i * 60_000).toISOString(), 'unit.created', `Added the unit ${i}.`, sam))
  const calls = await api(table({ entries }))
  await goto('/t/alpha/admin/audit', { waitUntil: 'hydration' })

  // A page is 50
  await expect(rows(page)).toHaveCount(50)
  expect(asked(calls)[0]!.query.limit).toBe('50')
  expect(asked(calls)[0]!.query).not.toHaveProperty('before')

  await page.getByRole('button', { name: 'Show older' }).click()
  await expect(rows(page)).toHaveCount(100)
  expect(asked(calls).at(-1)!.query.before).toBe(entries[49]!.at)
  // In order, none twice
  await expect(rows(page).locator('p.font-medium')).toHaveText(entries.slice(0, 100).map(e => e.summary))

  await page.getByRole('button', { name: 'Show older' }).click()
  // The last page is shorter, and there is nothing to ask for after it
  await expect(rows(page)).toHaveCount(120)
  await expect(page.getByRole('button', { name: 'Show older' })).toHaveCount(0)
})

test('nothing done is said', async ({ page, goto, api }) => {
  await api(table())

  await goto('/t/alpha/admin/audit', { waitUntil: 'hydration' })

  await expect(page.getByText('Nothing has been done yet.')).toBeVisible()
})

test('when the API refuses, why is said', async ({ page, goto, api }) => {
  await api(table({ audit: () => problem(400, [{ name: 'limit', reason: 'Not that.', code: 'x' }]) }))

  await goto('/t/alpha/admin/audit', { waitUntil: 'hydration' })

  // The app asks three more times before it gives up (plugins/vue-query.ts), which takes about seven seconds
  await expect(page.getByText('Couldn\'t load the history')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('Not that.')).toHaveCount(1)
})

test('a member is told it is for admins, nothing is asked for, and it is not in their sidebar', async ({ page, goto, api }) => {
  const calls = await api(table({ role: 'Member' }))

  await goto('/t/alpha/admin/audit', { waitUntil: 'hydration' })

  await expect(page.getByText('Only admins can do this')).toBeVisible()
  expect(asked(calls)).toHaveLength(0)
  await expect(page.getByRole('link', { name: 'History' })).toHaveCount(0)
})
