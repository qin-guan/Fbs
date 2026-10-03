// The timeline of an organization: where the bookings are drawn, what is asked for, and filtering. The browser is put in a time zone that
// isn't the organization's, to check that what is drawn is by the organization's clock. Run by e2e/run.mjs.
import { base, check, finish, json, problem, shot, start, stub } from './support.mjs'

const { browser, context: ordinary } = await start()
// Los Angeles, when the organization is in Singapore
const context = await browser.newContext({ viewport: { width: 1200, height: 900 }, ignoreHTTPSErrors: true, timezoneId: 'America/Los_Angeles' })
await ordinary.close()

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
  const get = type => Number(parts.find(p => p.type === type).value)
  return { year: get('year'), month: get('month'), day: get('day'), hour: get('hour'), minute: get('minute') }
}
/** A time in the organization, some days after today, as the moment it is. */
const at = (offset, hour, minute = 0) => {
  const today = wall()
  return new Date(Date.UTC(today.year, today.month - 1, today.day + offset, hour - 8, minute)).toISOString()
}
const booking = (id, facilityId, facilityName, start, end, conduct, extra = {}) => ({
  id, facilityId, facilityName, startDateTime: start, endDateTime: end, conduct, description: null, pocName: 'CPT Sam', pocPhone: '91234567',
  bookedBy: { memberId: 'm2', displayName: 'SGT Other', unitId: null }, updatedBy: null, canManage: false, ...extra,
})

const bookings = [
  // Over a day ago, so not in the window at all
  booking('b-old', 'f-hall', 'Hall', at(-2, 9), at(-2, 10), 'Old'),
  booking('b-hall', 'f-hall', 'Hall', at(1, 10), at(1, 11), 'Circuit training'),
  booking('b-field', 'f-field', 'Field', at(1, 14), at(1, 16), 'Run'),
]

const table = (extra = {}, role = 'Member') => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Bookings': () => json(bookings),
  ...extra,
})

const asked = log => log.filter(l => l.key === 'GET /t/alpha/Bookings').at(-1).query
const styleOf = (locator, property) => locator.evaluate((el, p) => el.style[p], property)

// 1. Where things are drawn, and what is asked for
{
  const page = await context.newPage()
  const log = await stub(page, table())
  await page.goto(`${base}/t/alpha/timeline`)
  await page.getByRole('button', { name: /Hall: Circuit training/ }).waitFor()

  const { from, to } = asked(log)
  check('the window starts at midnight in the organization, not in the browser', new Date(from).toISOString().endsWith('T16:00:00.000Z'), from)
  check('the window is a week', (new Date(to) - new Date(from)) / 86_400_000 === 7, `${from} to ${to}`)

  const hall = page.getByRole('button', { name: /Hall: Circuit training/ })
  const field = page.getByRole('button', { name: /Field: Run/ })
  // Ten in the morning tomorrow: a day, and ten hours, of 28 pixels each, however far Singapore is from Los Angeles
  check('a booking is where its hour is in the organization', (await styleOf(hall, 'left')) === `${(24 + 10) * 28}px` && (await styleOf(hall, 'width')) === '28px', `${await styleOf(hall, 'left')} ${await styleOf(hall, 'width')}`)
  check('a booking of two hours is twice as wide', (await styleOf(field, 'left')) === `${(24 + 14) * 28}px` && (await styleOf(field, 'width')) === '56px')
  check('what is not in the window is not drawn', (await page.getByRole('button', { name: /Old/ }).count()) === 0)
  check('the facilities are under their kind', (await page.getByText('Indoor', { exact: true }).count()) === 1 && (await page.getByText('Outdoor', { exact: true }).count()) === 1)

  // The browser writes calc() with its terms in an order of its own, so it is the pixels that are looked for
  const marker = Number.parseFloat(/([\d.]+)px/.exec(await page.getByTestId('now-marker').evaluate(el => el.style.left))?.[1] ?? 'NaN')
  const now = wall()
  const expected = (now.hour + now.minute / 60) * 28
  check('now is drawn where it is in the organization', Math.abs(marker - expected) < 28 / 60 * 3, `${marker} vs ${expected}`)

  const tomorrow = new Intl.DateTimeFormat('en-SG', { dateStyle: 'medium', timeZone: 'Asia/Singapore' }).format(new Date(at(1, 12)))
  check('the days are as they are in the organization', (await page.getByText(tomorrow, { exact: true }).count()) === 1, tomorrow)
  await shot(page, 'timeline')

  await hall.click()
  await page.waitForURL('**/t/alpha/bookings/b-hall')
  check('a booking opens', true)
  await page.close()
}

// 2. Filtering, and more days
{
  const page = await context.newPage()
  const log = await stub(page, table())
  await page.goto(`${base}/t/alpha/timeline`)
  await page.getByRole('button', { name: /Hall: Circuit training/ }).waitFor()

  await page.getByRole('button', { name: 'Field', exact: true }).click()
  await page.getByRole('button', { name: /Hall: Circuit training/ }).waitFor({ state: 'detached' })
  check('a facility can be picked out', page.url().includes('facility=f-field') && (await page.getByRole('button', { name: /Field: Run/ }).count()) === 1)
  check('the others are gone', (await page.getByRole('button', { name: 'Gym', exact: true }).count()) === 0)

  await page.getByRole('button', { name: 'Clear filters' }).click()
  await page.getByRole('button', { name: /Hall: Circuit training/ }).waitFor()
  // The address is changed a moment after what is shown
  await page.waitForFunction(() => !location.search.includes('facility='))
  check('and put back', true)

  await page.getByRole('combobox', { name: 'Days to show' }).click()
  await page.getByRole('option', { name: '14 days' }).click()
  await page.waitForFunction(() => location.search.includes('days=14'))
  await page.getByRole('button', { name: /Hall: Circuit training/ }).waitFor()
  const { from, to } = asked(log)
  check('more days asks for more', (new Date(to) - new Date(from)) / 86_400_000 === 14, `${from} to ${to}`)

  await page.getByRole('switch', { name: 'Only with bookings' }).click()
  await page.getByRole('button', { name: 'Gym', exact: true }).waitFor({ state: 'detached' })
  check('facilities with nothing booked can be hidden', (await page.getByRole('button', { name: 'Field', exact: true }).count()) === 1)
  await page.close()

  const shared = await context.newPage()
  await stub(shared, table())
  await shared.goto(`${base}/t/alpha/timeline?facility=f-gym&days=3`)
  await shared.getByTitle('Show all facilities').waitFor()
  check('what is in the address is what is filtered', (await shared.getByRole('button', { name: 'Hall', exact: true }).count()) === 0)
  await shared.close()
}

// 3. A facility that has gone
{
  const page = await context.newPage()
  await stub(page, table({
    'GET /t/alpha/Bookings': () => json([booking('b-gone', 'f-old', 'Old hall', at(1, 9), at(1, 10), 'Lesson')]),
  }))
  await page.goto(`${base}/t/alpha/timeline`)
  await page.getByRole('button', { name: /Old hall: Lesson/ }).waitFor()
  check('a facility that has gone is drawn with its bookings', (await page.getByText('Other', { exact: true }).count()) === 1)
  await page.close()
}

// 4. No facilities: an admin is told where to add them, and somebody else is not
{
  const empty = { 'GET /t/alpha/Facilities/Bookable': () => json([]), 'GET /t/alpha/Bookings': () => json([]) }
  const admin = await context.newPage()
  await stub(admin, table(empty, 'Admin'))
  await admin.goto(`${base}/t/alpha/timeline`)
  await admin.getByText('There are no facilities yet.').waitFor()
  check('an admin can go and add facilities', (await admin.getByRole('link', { name: 'Add facilities' }).getAttribute('href')) === '/t/alpha/admin/facilities')
  await admin.close()

  const member = await context.newPage()
  await stub(member, table(empty))
  await member.goto(`${base}/t/alpha/timeline`)
  await member.getByText('There are no facilities to book yet.').waitFor()
  check('a member is not sent to somewhere they can\'t go', (await member.getByRole('link', { name: 'Add facilities' }).count()) === 0)
  await member.close()
}

// 5. The API can't say
{
  const page = await context.newPage()
  await stub(page, table({
    'GET /t/alpha/Bookings': () => problem(400, [{ name: 'to', reason: 'There are too many bookings in that window to show at once, so ask for a shorter one.', code: 'window-too-large' }]),
  }))
  await page.goto(`${base}/t/alpha/timeline`)
  await page.getByText('Couldn\'t load the bookings').waitFor()
  check('why is said', (await page.getByText('There are too many bookings in that window').count()) === 1)
  await page.close()
}

await finish(browser)
