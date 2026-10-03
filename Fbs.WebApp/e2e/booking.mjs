// Booking: picking a slot on the calendar and several with the builder, and what is sent. The browser is put in a time zone that isn't
// the organization's, to check that what is drawn, picked and sent is in the organization's. Run by e2e/run.mjs.
import { base, check, finish, json, problem, shot, start, stub } from './support.mjs'

const { browser, context: ordinary } = await start()
// Los Angeles, when the organization is in Singapore
const context = await browser.newContext({ viewport: { width: 1200, height: 900 }, ignoreHTTPSErrors: true, timezoneId: 'America/Los_Angeles' })
await ordinary.close()

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
const created = (slots, conduct) => slots.map((s, i) => ({
  id: `new-${i}`, facilityId: s.facilityId, facilityName: facilities.find(f => f.id === s.facilityId).name, startDateTime: s.startDateTime, endDateTime: s.endDateTime, conduct,
  description: null, pocName: null, pocPhone: null, bookedBy: { memberId: 'm1', displayName: 'CPT Sam', unitId: null }, updatedBy: null, canManage: true,
}))

const table = (extra = {}) => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Member', status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Bookings': () => json([]),
  ...extra,
})

/** Where the organization's wall clock is now, so a test can pick a day that has not started there. */
const wallToday = () => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Singapore', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
  const get = type => Number(parts.find(p => p.type === type).value)
  return { year: get('year'), month: get('month'), day: get('day') }
}

// 1. Dragging on the calendar
{
  const page = await context.newPage()
  const log = await stub(page, table({
    'POST /t/alpha/Bookings': (r) => {
      const body = JSON.parse(r.postData())
      return { ...json(created(body.slots, body.conduct), 201) }
    },
  }))
  await page.goto(`${base}/t/alpha/book`)
  await page.waitForSelector('.vuecal__schedule--cell')
  await page.getByText('Times are in Asia/Singapore').waitFor()
  check('a time zone that is not the browser\'s is said', true)

  // Tomorrow in the organization, when it is not the day the browser thinks it is
  await page.locator('.vuecal__nav--next').first().click()
  await page.waitForTimeout(500)
  const cells = page.locator('.vuecal__schedule--cell')
  // The calendar is taller than the window, so the time is scrolled to before it is dragged over
  await page.locator('.vuecal__time-cell', { hasText: '10:00' }).first().scrollIntoViewIfNeeded()
  await page.waitForTimeout(300)
  const box = await cells.first().boundingBox()
  const ten = await page.locator('.vuecal__time-cell', { hasText: '10:00' }).first().boundingBox()
  const x = box.x + box.width / 2
  await page.mouse.move(x, ten.y + 5)
  await page.mouse.down()
  await page.mouse.move(x, ten.y + 45, { steps: 6 })
  await page.mouse.move(x, ten.y + 85, { steps: 6 })
  await page.mouse.up()
  await page.getByText('10:00 am –').waitFor()
  const summary = await page.locator('div.min-w-0.text-sm').innerText()
  check('the picked slot is described in the organization\'s time', summary.includes('Hall') && summary.includes('10:00 am – 11:00 am'), summary)
  await shot(page, 'book-calendar')

  await page.getByRole('button', { name: 'Confirm selection' }).click()
  await page.waitForURL('**/t/alpha/book/confirm')
  check('continuing goes to confirm', true)
  await page.getByPlaceholder('Conduct name').fill('Circuit training')
  const poc = await page.getByPlaceholder('Rank and name').inputValue()
  check('the point of contact starts as who they are', poc === 'CPT Sam', poc)
  await page.getByRole('button', { name: 'Confirm', exact: true }).click()
  await page.waitForURL('**/t/alpha/bookings/new-0')
  const sent = JSON.parse(log.filter(l => l.key === 'POST /t/alpha/Bookings').at(-1).body)
  const start = new Date(sent.slots[0].startDateTime)
  const end = new Date(sent.slots[0].endDateTime)
  // Ten in the morning in Singapore is 02:00 UTC, and the end is an hour or half hour on
  check('what is sent is the organization\'s time, not the browser\'s', start.getUTCHours() === 2 && start.getUTCMinutes() === 0 && end.getTime() > start.getTime(), sent.slots[0].startDateTime)
  check('the facility is sent by its id', sent.slots[0].facilityId === 'f-hall', JSON.stringify(sent.slots[0]))
  check('who to contact is sent', sent.conduct === 'Circuit training' && sent.pocName === 'CPT Sam' && sent.pocPhone === '91234567', JSON.stringify(sent))
  await page.close()
}

// 2. Several at once, with the builder
{
  const page = await context.newPage()
  const log = await stub(page, table({
    'POST /t/alpha/Bookings': (r) => {
      const body = JSON.parse(r.postData())
      return json(created(body.slots, body.conduct), 201)
    },
  }))
  await page.goto(`${base}/t/alpha/book`)
  await page.getByRole('button', { name: 'Book multiple' }).click()
  await page.getByText('Book multiple facilities or days').waitFor()

  await page.getByText('Choose facilities').click()
  await page.getByRole('option', { name: 'Hall' }).click()
  await page.getByRole('option', { name: 'Gym' }).click()
  await page.keyboard.press('Escape')

  // Two days in the organization, after today there
  const today = wallToday()
  const isoDay = (offset) => {
    const d = new Date(Date.UTC(today.year, today.month - 1, today.day + offset))
    return { iso: d.toISOString().slice(0, 10), monthIndex: d.getUTCFullYear() * 12 + d.getUTCMonth() }
  }
  const d1 = isoDay(1)
  const d2 = isoDay(2)
  // The calendar opens on this month, and on the last days of it tomorrow is in the next one
  let visible = today.year * 12 + today.month - 1
  const pick = async (day) => {
    for (; visible < day.monthIndex; visible++) {
      await page.getByRole('dialog').getByRole('button', { name: 'Next month' }).click()
    }
    await page.getByRole('dialog').locator(`[data-value="${day.iso}"]`).click()
  }
  await pick(d1)
  await pick(d2)
  await page.getByText('4 bookings').waitFor()
  check('two facilities on two days makes four bookings', true)
  await shot(page, 'book-builder')
  await page.getByRole('button', { name: 'Add 4 to list' }).click()
  await page.getByRole('heading', { name: 'Your list' }).waitFor()
  await page.getByRole('button', { name: 'Continue with 4 slots' }).click()
  await page.waitForURL('**/t/alpha/book/confirm')
  await page.getByPlaceholder('Conduct name').fill('Exercise')
  await page.getByRole('button', { name: 'Confirm 4 bookings' }).click()
  await page.getByText('4 bookings created').first().waitFor()
  const sent = JSON.parse(log.filter(l => l.key === 'POST /t/alpha/Bookings').at(-1).body)
  const starts = sent.slots.map(s => new Date(s.startDateTime).toISOString())
  check('all day is from midnight in the organization', starts.every(s => s.endsWith('T16:00:00.000Z')), starts.join())
  check('four slots, two facilities', sent.slots.length === 4 && new Set(sent.slots.map(s => s.facilityId)).size === 2)
  await page.close()
}

// 3. When the API says a slot can't be booked
{
  const page = await context.newPage()
  await stub(page, table({
    'POST /t/alpha/Bookings': () => problem(409, [{ name: 'slots[0]', reason: 'Overlaps with booking abc.', code: 'clash' }]),
  }))
  await page.addInitScript(() => {
    const start = new Date(Date.now() + 3 * 86_400_000)
    start.setUTCHours(2, 0, 0, 0)
    const end = new Date(start.getTime() + 3_600_000)
    sessionStorage.setItem('booking-basket:alpha', JSON.stringify([{ id: 's1', facilityId: 'f-hall', start: start.toISOString(), end: end.toISOString() }]))
  })
  await page.goto(`${base}/t/alpha/book/confirm`)
  await page.getByPlaceholder('Conduct name').fill('Clash')
  await page.getByRole('button', { name: 'Confirm', exact: true }).click()
  await page.getByText('Overlaps with booking abc.').first().waitFor()
  check('a clash the API finds is shown on the slot, and nothing is confirmed', (await page.getByRole('button', { name: 'Confirm', exact: true }).isDisabled()))
  await shot(page, 'book-clash')
  await page.close()
}

// 4. Nothing to book
{
  const page = await context.newPage()
  await stub(page, table({ 'GET /t/alpha/Facilities/Bookable': () => json([]) }))
  await page.goto(`${base}/t/alpha/book`)
  await page.getByText('There is nothing you can book yet').waitFor()
  check('nothing to book says what to do', true)
  await page.close()
}

await finish(browser)
