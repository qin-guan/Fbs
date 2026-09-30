// Booking: picking a slot on the calendar and several with the builder, and what is sent. The browser is put in a time zone that isn't
// the organization's, to check that what is drawn, picked and sent is in the organization's.
import { expect, json, problem, test } from './support'
import type { Table } from './support'

// Los Angeles, when the organization is in Singapore
test.use({ timezoneId: 'America/Los_Angeles', viewport: { width: 1200, height: 900 } })

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Member', notificationScope: 'None', phone: '91234567' },
}
const facilities = [
  { id: 'f-hall', name: 'Hall', group: 'Indoor' },
  { id: 'f-gym', name: 'Gym', group: 'Indoor' },
  { id: 'f-field', name: 'Field', group: 'Outdoor' },
]
interface Slot { facilityId: string, startDateTime: string, endDateTime: string }
const created = (slots: Slot[], conduct: string) => slots.map((s, i) => ({
  id: `new-${i}`, facilityId: s.facilityId, facilityName: facilities.find(f => f.id === s.facilityId)!.name, startDateTime: s.startDateTime, endDateTime: s.endDateTime, conduct,
  description: null, pocName: null, pocPhone: null, bookedBy: { memberId: 'm1', displayName: 'CPT Sam', unitId: null }, updatedBy: null, canManage: true,
}))

const table = (extra: Table = {}): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Member', status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Bookings': () => json([]),
  'POST /t/alpha/Bookings': (request) => {
    const body = request.postDataJSON()
    return json(created(body.slots, body.conduct), 201)
  },
  ...extra,
})

/** Where the organization's wall clock is now, so a test can pick a day that has not started there. */
const wallToday = () => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Singapore', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
  const get = (type: string) => Number(parts.find(p => p.type === type)!.value)
  return { year: get('year'), month: get('month'), day: get('day') }
}

test('a slot dragged on the calendar is described, and sent, in the organization\'s time', async ({ page, goto, api }) => {
  const calls = await api(table())
  await goto('/t/alpha/book', { waitUntil: 'hydration' })
  await expect(page.locator('.vuecal__schedule--cell').first()).toBeVisible()
  // The time zone is said, as it isn't the browser's
  await expect(page.getByText('Times are in Asia/Singapore')).toBeVisible()

  // Tomorrow in the organization, when it is not the day the browser thinks it is
  await page.locator('.vuecal__nav--next').first().click()
  await page.waitForTimeout(500)
  // The calendar is taller than the window, so the time is scrolled to before it is dragged over
  const tenOClock = page.locator('.vuecal__time-cell', { hasText: '10:00' }).first()
  await tenOClock.scrollIntoViewIfNeeded()
  await page.waitForTimeout(300)
  const cell = (await page.locator('.vuecal__schedule--cell').first().boundingBox())!
  const ten = (await tenOClock.boundingBox())!
  const x = cell.x + cell.width / 2
  await page.mouse.move(x, ten.y + 5)
  await page.mouse.down()
  await page.mouse.move(x, ten.y + 45, { steps: 6 })
  await page.mouse.move(x, ten.y + 85, { steps: 6 })
  await page.mouse.up()

  const summary = page.locator('div.min-w-0.text-sm')
  await expect(summary).toContainText('Hall')
  await expect(summary).toContainText('10:00 am – 11:00 am')

  await page.getByRole('button', { name: 'Confirm selection' }).click()
  await expect(page).toHaveURL(/\/t\/alpha\/book\/confirm$/)
  await page.getByPlaceholder('Conduct name').fill('Circuit training')
  // The point of contact starts as who they are
  await expect(page.getByPlaceholder('Rank and name')).toHaveValue('CPT Sam')
  await page.getByRole('button', { name: 'Confirm', exact: true }).click()

  await expect(page).toHaveURL(/\/t\/alpha\/bookings\/new-0$/)
  const sent = calls.filter(c => c.key === 'POST /t/alpha/Bookings').at(-1)!.body
  expect(sent).toMatchObject({ conduct: 'Circuit training', pocName: 'CPT Sam', pocPhone: '91234567' })
  expect(sent.slots[0].facilityId).toBe('f-hall')
  // Ten in the morning in Singapore is 02:00 UTC, and the end is an hour or half hour on
  const start = new Date(sent.slots[0].startDateTime)
  expect(start.toISOString().slice(11, 16)).toBe('02:00')
  expect(new Date(sent.slots[0].endDateTime).getTime()).toBeGreaterThan(start.getTime())
})

test('two facilities on two days with the builder are four bookings, all day from midnight in the organization', async ({ page, goto, api }) => {
  const calls = await api(table())
  await goto('/t/alpha/book', { waitUntil: 'hydration' })
  await page.getByRole('button', { name: 'Book multiple' }).click()
  await expect(page.getByText('Book multiple facilities or days')).toBeVisible()

  await page.getByText('Choose facilities').click()
  await page.getByRole('option', { name: 'Hall' }).click()
  await page.getByRole('option', { name: 'Gym' }).click()
  await page.keyboard.press('Escape')

  // Two days in the organization, after today there
  const today = wallToday()
  const isoDay = (offset: number) => {
    const d = new Date(Date.UTC(today.year, today.month - 1, today.day + offset))
    return { iso: d.toISOString().slice(0, 10), monthIndex: d.getUTCFullYear() * 12 + d.getUTCMonth() }
  }
  // The calendar opens on this month, and on the last days of it tomorrow is in the next one
  let visible = today.year * 12 + today.month - 1
  const pick = async (day: ReturnType<typeof isoDay>) => {
    for (; visible < day.monthIndex; visible++) {
      await page.getByRole('dialog').getByRole('button', { name: 'Next month' }).click()
    }
    await page.getByRole('dialog').locator(`[data-value="${day.iso}"]`).click()
  }
  await pick(isoDay(1))
  await pick(isoDay(2))
  await expect(page.getByText('4 bookings')).toBeVisible()

  await page.getByRole('button', { name: 'Add 4 to list' }).click()
  await expect(page.getByRole('heading', { name: 'Your list' })).toBeVisible()
  await page.getByRole('button', { name: 'Continue with 4 slots' }).click()
  await expect(page).toHaveURL(/\/t\/alpha\/book\/confirm$/)
  await page.getByPlaceholder('Conduct name').fill('Exercise')
  await page.getByRole('button', { name: 'Confirm 4 bookings' }).click()

  await expect(page.getByText('4 bookings created').first()).toBeVisible()
  const sent = calls.filter(c => c.key === 'POST /t/alpha/Bookings').at(-1)!.body as { slots: Slot[] }
  expect(sent.slots).toHaveLength(4)
  expect(new Set(sent.slots.map(s => s.facilityId)).size).toBe(2)
  // Midnight in Singapore is 16:00 UTC the day before
  for (const slot of sent.slots) {
    expect(new Date(slot.startDateTime).toISOString()).toMatch(/T16:00:00\.000Z$/)
  }
})

test('a slot the API says can\'t be booked is shown as such, and nothing is confirmed', async ({ page, goto, api }) => {
  await api(table({
    'POST /t/alpha/Bookings': () => problem(409, [{ name: 'slots[0]', reason: 'Overlaps with booking abc.', code: 'clash' }]),
  }))
  // A slot picked earlier, three days from now at ten in the morning in Singapore
  await page.addInitScript(() => {
    const start = new Date(Date.now() + 3 * 86_400_000)
    start.setUTCHours(2, 0, 0, 0)
    const end = new Date(start.getTime() + 3_600_000)
    sessionStorage.setItem('booking-basket:alpha', JSON.stringify([{ id: 's1', facilityId: 'f-hall', start: start.toISOString(), end: end.toISOString() }]))
  })
  await goto('/t/alpha/book/confirm', { waitUntil: 'hydration' })

  await page.getByPlaceholder('Conduct name').fill('Clash')
  await page.getByRole('button', { name: 'Confirm', exact: true }).click()

  await expect(page.getByText('Overlaps with booking abc.').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Confirm', exact: true })).toBeDisabled()
})

test('nothing to book says what to do', async ({ page, goto, api }) => {
  await api(table({ 'GET /t/alpha/Facilities/Bookable': () => json([]) }))

  await goto('/t/alpha/book', { waitUntil: 'hydration' })

  await expect(page.getByText('There is nothing you can book yet')).toBeVisible()
})
