// The timeline of an organization: where the bookings are drawn, what is asked for, and filtering. The browser is put in a time zone that
// isn't the organization's, to check that what is drawn is by the organization's clock.
import type { Locator } from '@playwright/test'
import { expect, json, problem, test } from './support'
import type { Call, Table } from './support'

// Los Angeles, when the organization is in Singapore
test.use({ timezoneId: 'America/Los_Angeles', viewport: { width: 1200, height: 900 } })

const org = (role = 'Member') => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const facilities = [
  { id: 'f-hall', name: 'Hall', group: 'Indoor' },
  { id: 'f-gym', name: 'Gym', group: 'Indoor' },
  { id: 'f-field', name: 'Field', group: 'Outdoor' },
]

/** Where the organization's wall clock is now. */
const wall = () => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Singapore', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date())
  const get = (type: string) => Number(parts.find(p => p.type === type)!.value)
  return { year: get('year'), month: get('month'), day: get('day'), hour: get('hour'), minute: get('minute') }
}
/** A time in the organization, some days after today, as the moment it is. */
const at = (offset: number, hour: number, minute = 0) => {
  const today = wall()
  return new Date(Date.UTC(today.year, today.month - 1, today.day + offset, hour - 8, minute)).toISOString()
}
const booking = (id: string, facilityId: string, facilityName: string, start: string, end: string, conduct: string) => ({
  id, facilityId, facilityName, startDateTime: start, endDateTime: end, conduct, description: null, pocName: 'CPT Sam', pocPhone: '91234567',
  bookedBy: { memberId: 'm2', displayName: 'SGT Other', unitId: null }, updatedBy: null, canManage: false,
})

const bookings = [
  // Over a day ago, so not in the window at all
  booking('b-old', 'f-hall', 'Hall', at(-2, 9), at(-2, 10), 'Old'),
  booking('b-hall', 'f-hall', 'Hall', at(1, 10), at(1, 11), 'Circuit training'),
  booking('b-field', 'f-field', 'Field', at(1, 14), at(1, 16), 'Run'),
]

const table = (extra: Table = {}, role = 'Member'): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Bookings': () => json(bookings),
  ...extra,
})

/** How many days the last window asked for is */
const daysAsked = (calls: Call[]) => {
  const { from, to } = calls.filter(c => c.key === 'GET /t/alpha/Bookings').at(-1)!.query
  return (new Date(to!).getTime() - new Date(from!).getTime()) / 86_400_000
}
const styleOf = (locator: Locator, property: 'left' | 'width') => locator.evaluate((el, p) => (el as HTMLElement).style[p], property)

test.describe('where things are drawn', () => {
  test('the window asked for is a week from midnight in the organization, not in the browser', async ({ page, goto, api }) => {
    const calls = await api(table())

    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    await expect(page.getByRole('button', { name: /Hall: Circuit training/ })).toBeVisible()
    const { from } = calls.filter(c => c.key === 'GET /t/alpha/Bookings').at(-1)!.query
    expect(new Date(from!).toISOString()).toMatch(/T16:00:00\.000Z$/)
    expect(daysAsked(calls)).toBe(7)
  })

  test('a booking is where its hour is in the organization, and what is not in the window is not drawn', async ({ page, goto, api }) => {
    await api(table())

    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    const hall = page.getByRole('button', { name: /Hall: Circuit training/ })
    const field = page.getByRole('button', { name: /Field: Run/ })
    await expect(hall).toBeVisible()
    // Ten in the morning tomorrow: a day, and ten hours, of 28 pixels each, however far Singapore is from Los Angeles
    expect(await styleOf(hall, 'left')).toBe(`${(24 + 10) * 28}px`)
    expect(await styleOf(hall, 'width')).toBe('28px')
    // Two hours are twice as wide
    expect(await styleOf(field, 'left')).toBe(`${(24 + 14) * 28}px`)
    expect(await styleOf(field, 'width')).toBe('56px')
    await expect(page.getByRole('button', { name: /Old/ })).toHaveCount(0)
    // The facilities are under their kind
    await expect(page.getByText('Indoor', { exact: true })).toHaveCount(1)
    await expect(page.getByText('Outdoor', { exact: true })).toHaveCount(1)
  })

  test('now, and the days, are as they are in the organization', async ({ page, goto, api }) => {
    await api(table())

    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    // The browser writes calc() with its terms in an order of its own, so it is the pixels that are looked for
    const left = await page.getByTestId('now-marker').evaluate(el => (el as HTMLElement).style.left)
    const marker = Number.parseFloat(/([\d.]+)px/.exec(left)?.[1] ?? 'NaN')
    const now = wall()
    expect(Math.abs(marker - (now.hour + now.minute / 60) * 28)).toBeLessThan(28 / 60 * 3)
    const tomorrow = new Intl.DateTimeFormat('en-SG', { dateStyle: 'medium', timeZone: 'Asia/Singapore' }).format(new Date(at(1, 12)))
    await expect(page.getByText(tomorrow, { exact: true })).toHaveCount(1)
  })

  test('a booking opens', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: /Hall: Circuit training/ }).click()

    await expect(page).toHaveURL(/\/t\/alpha\/bookings\/b-hall$/)
  })
})

test.describe('filtering', () => {
  test('a facility can be picked out, and put back', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })
    await expect(page.getByRole('button', { name: /Hall: Circuit training/ })).toBeVisible()

    await page.getByRole('button', { name: 'Field', exact: true }).click()

    await expect(page.getByRole('button', { name: /Hall: Circuit training/ })).toHaveCount(0)
    await expect(page).toHaveURL(/facility=f-field/)
    await expect(page.getByRole('button', { name: /Field: Run/ })).toHaveCount(1)
    await expect(page.getByRole('button', { name: 'Gym', exact: true })).toHaveCount(0)

    await page.getByRole('button', { name: 'Clear filters' }).click()

    await expect(page.getByRole('button', { name: /Hall: Circuit training/ })).toBeVisible()
    await expect(page).not.toHaveURL(/facility=/)
  })

  test('more days asks for more', async ({ page, goto, api }) => {
    const calls = await api(table())
    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })
    await expect(page.getByRole('button', { name: /Hall: Circuit training/ })).toBeVisible()

    await page.getByRole('combobox', { name: 'Days to show' }).click()
    await page.getByRole('option', { name: '14 days' }).click()

    await expect(page).toHaveURL(/days=14/)
    await expect.poll(() => daysAsked(calls)).toBe(14)
  })

  test('facilities with nothing booked can be hidden', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })
    await expect(page.getByRole('button', { name: 'Gym', exact: true })).toBeVisible()

    await page.getByRole('switch', { name: 'Only with bookings' }).click()

    await expect(page.getByRole('button', { name: 'Gym', exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Field', exact: true })).toHaveCount(1)
  })

  test('what is in the address is what is filtered', async ({ page, goto, api }) => {
    await api(table())

    await goto('/t/alpha/timeline?facility=f-gym&days=3', { waitUntil: 'hydration' })

    await expect(page.getByTitle('Show all facilities')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Hall', exact: true })).toHaveCount(0)
  })
})

test('a facility that has gone is drawn with its bookings', async ({ page, goto, api }) => {
  await api(table({
    'GET /t/alpha/Bookings': () => json([booking('b-gone', 'f-old', 'Old hall', at(1, 9), at(1, 10), 'Lesson')]),
  }))

  await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

  await expect(page.getByRole('button', { name: /Old hall: Lesson/ })).toBeVisible()
  await expect(page.getByText('Other', { exact: true })).toHaveCount(1)
})

test.describe('no facilities', () => {
  const empty: Table = { 'GET /t/alpha/Facilities/Bookable': () => json([]), 'GET /t/alpha/Bookings': () => json([]) }

  test('an admin is told where to add them', async ({ page, goto, api }) => {
    await api(table(empty, 'Admin'))

    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    await expect(page.getByText('There are no facilities yet.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Add facilities' })).toHaveAttribute('href', '/t/alpha/admin/facilities')
  })

  test('a member is not sent somewhere they can\'t go', async ({ page, goto, api }) => {
    await api(table(empty))

    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    await expect(page.getByText('There are no facilities to book yet.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Add facilities' })).toHaveCount(0)
  })
})

test('when the API can\'t say what is booked, why is said', async ({ page, goto, api }) => {
  await api(table({
    'GET /t/alpha/Bookings': () => problem(400, [{ name: 'to', reason: 'There are too many bookings in that window to show at once, so ask for a shorter one.', code: 'window-too-large' }]),
  }))

  await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

  // The app asks three more times before it gives up (plugins/vue-query.ts), which takes about seven seconds
  await expect(page.getByText('Couldn\'t load the bookings')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('There are too many bookings in that window')).toHaveCount(1)
})
