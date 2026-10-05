// What an admin is shown when an organization has just been made: what is left to do, and that it goes when done or when they say so.
import type { Page } from '@playwright/test'
import { expect, json, test } from './support'
import type { Call, Table } from './support'

const org = (role: string) => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const me = { id: 'm1', displayName: 'CPT Sam', phone: null, unitId: null, role: 'Admin', notificationScope: 'None', status: 'Active', hasAccount: true }
const facility = { id: 'f1', name: 'Hall', group: null, availableToAll: true, unitIds: [] }
const invite = { id: 'i1', role: 'Member', unitId: null, expiresAt: new Date(Date.now() + 5 * 86_400_000).toISOString(), maxUses: 10, uses: 0, status: 'Active', createdAt: new Date().toISOString() }

/** An organization with what is in it given */
const table = ({ role = 'Admin', facilities = [], units = [], invites = [], members = [me] }: { role?: string, facilities?: object[], units?: object[], invites?: object[], members?: object[] } = {}): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Bookings': () => json([]),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Facilities': () => json(facilities),
  'GET /t/alpha/Units': () => json(units),
  'GET /t/alpha/Invites': () => json(invites),
  'GET /t/alpha/Members': () => json(members),
})
/** What the list is worked out from, which is only for admins */
const forAdmins = ['GET /t/alpha/Facilities', 'GET /t/alpha/Units', 'GET /t/alpha/Invites', 'GET /t/alpha/Members']

const list = (page: Page) => page.getByTestId('setup-checklist')
const step = (page: Page, id: string) => list(page).locator(`li[data-step="${id}"]`)

/** Nothing on the page says the list was left out, so this waits for what it is worked out from to be asked for, and a moment for it to be drawn. */
const settled = async (page: Page, calls: Call[]) => {
  await expect.poll(() => forAdmins.every(key => calls.some(c => c.key === key))).toBe(true)
  await page.waitForTimeout(500)
}

test('with nothing done, all three are to do, each going to where it is done', async ({ page, goto, api }) => {
  await api(table())

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await expect(list(page).locator('li[data-done="false"]')).toHaveCount(3)
  await expect(step(page, 'facilities').getByRole('link', { name: 'Add facilities' })).toHaveAttribute('href', '/t/alpha/admin/facilities')
  await expect(step(page, 'units').getByRole('link', { name: 'Add units' })).toHaveAttribute('href', '/t/alpha/admin/units')
  await expect(step(page, 'people').getByRole('link', { name: 'Make a link' })).toHaveAttribute('href', '/t/alpha/admin/invites')
  await expect(step(page, 'units')).toContainText('Optional')
  // It is not said twice that there are no facilities
  await expect(page.getByText('There are no facilities yet')).toHaveCount(0)
})

test('what is done is shown to be, and has no button, and what is not is still to do', async ({ page, goto, api }) => {
  await api(table({ facilities: [facility] }))

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await expect(step(page, 'facilities')).toHaveAttribute('data-done', 'true')
  await expect(step(page, 'facilities').getByRole('link')).toHaveCount(0)
  await expect(step(page, 'people')).toHaveAttribute('data-done', 'false')
  await expect(step(page, 'units')).toHaveAttribute('data-done', 'false')
})

test('it goes when it is done', async ({ page, goto, api }) => {
  const calls = await api(table({ facilities: [facility], invites: [invite] }))

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await settled(page, calls)
  await expect(list(page)).toHaveCount(0)
})

test('somebody else being in is the people being invited, whichever way they came, and units are not needed', async ({ page, goto, api }) => {
  const calls = await api(table({ facilities: [facility], members: [me, { ...me, id: 'm2', displayName: 'SGT Lee' }] }))

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await settled(page, calls)
  await expect(list(page)).toHaveCount(0)
})

test('it can be hidden for the organization, and stays hidden', async ({ page, goto, api }) => {
  const calls = await api(table())
  await goto('/t/alpha', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Hide this' }).click()
  await expect(list(page)).toHaveCount(0)

  await page.reload()
  await expect(page.getByPlaceholder('Keyword search')).toBeVisible()
  await page.waitForTimeout(500)
  await expect(list(page)).toHaveCount(0)
  // And what it is worked out from isn't asked for again
  expect(calls.filter(c => c.key === 'GET /t/alpha/Invites')).toHaveLength(1)
})

test('a member is told to ask an admin, and is not shown the list, or asked for what is only for admins', async ({ page, goto, api }) => {
  const calls = await api(table({ role: 'Member' }))

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await expect(page.getByText('You can\'t book anything yet')).toBeVisible()
  await expect(list(page)).toHaveCount(0)
  expect(calls.map(c => c.key).filter(key => forAdmins.includes(key))).toEqual([])
})
