// The pages of an organization: what is booked, one booking, and changing and cancelling it. Run by e2e/run.mjs.
import { base, check, finish, json, noContent, problem, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Member', notificationScope: 'None', phone: null },
}

const person = (id, name) => ({ memberId: id, displayName: name, unitId: null })
const day = (offset, hour, minute = 0) => {
  const d = new Date()
  d.setUTCDate(d.getUTCDate() + offset)
  d.setUTCHours(hour - 8, minute, 0, 0)
  return d.toISOString().replace('.000Z', '+00:00')
}
const booking = (id, facility, from, to, conduct, by, extra = {}) => ({
  id, facilityId: `f-${facility}`, facilityName: facility, startDateTime: from, endDateTime: to, conduct, description: null, pocName: null, pocPhone: null,
  bookedBy: by, updatedBy: null, canManage: by.memberId === 'm1', ...extra,
})

const bookings = [
  booking('b1', 'Hall', day(2, 9), day(2, 11), 'Lesson', person('m1', 'CPT Sam'), { description: 'Room 2', pocName: 'LTA Lee', pocPhone: '91234567' }),
  booking('b2', 'Gym', day(3, 14), day(3, 15), 'Circuit', person('m2', 'SGT Other')),
  booking('b3', 'Hall', day(5, 8), day(5, 10), 'Briefing', person('m1', 'CPT Sam')),
]

const table = (log, extra = {}) => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Member', status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org),
  'GET /t/alpha/Facilities/Bookable': () => json([{ id: 'f-Hall', name: 'Hall', group: 'Indoor' }, { id: 'f-Gym', name: 'Gym', group: 'Indoor' }]),
  'GET /t/alpha/Bookings': (_r, url) => json(url.searchParams.get('mine') === 'true' ? bookings.filter(b => b.canManage) : bookings),
  'GET /t/alpha/Bookings/b1': () => json(bookings[0]),
  'GET /t/alpha/Bookings/b2': () => json(bookings[1]),
  ...extra,
})

// 1. The list
{
  const page = await context.newPage()
  const log = await stub(page, table())
  await page.goto(base + '/t/alpha')
  await page.getByText('Lesson').waitFor()
  check('bookings are listed', (await page.getByText('Circuit').count()) === 1 && (await page.getByText('Briefing').count()) === 1)
  check('who booked is shown', (await page.getByText('SGT Other').count()) > 0)
  await shot(page, 'tenant-bookings')
  const asked = log.filter(l => l.key === 'GET /t/alpha/Bookings').at(-1).query
  const days = (new Date(asked.to) - new Date(asked.from)) / 86400000
  check('a window of time is asked for', days === 31 && asked.mine === 'false', JSON.stringify(asked))
  check('the organization is in the title bar', (await page.title()).includes('Alpha Company'))

  await page.getByPlaceholder('Keyword search').fill('circuit')
  await page.waitForTimeout(200)
  check('keyword search narrows it', (await page.getByText('Lesson').count()) === 0 && (await page.getByText('Circuit').count()) === 1)
  await page.getByPlaceholder('Keyword search').fill('')

  await page.getByRole('switch', { name: 'Only mine' }).click()
  await page.getByText('Circuit').waitFor({ state: 'detached' })
  check('only mine asks for it', log.filter(l => l.key === 'GET /t/alpha/Bookings').at(-1).query.mine === 'true')
  await page.close()
}

// 2. Nothing booked, and nothing to book
{
  const page = await context.newPage()
  await stub(page, table([], {
    'GET /t/alpha/Bookings': () => json([]),
    'GET /t/alpha/Facilities/Bookable': () => json([]),
  }))
  await page.goto(base + '/t/alpha')
  await page.getByText('You can\'t book anything yet').waitFor()
  await page.getByText('Nothing is booked in this time').waitFor()
  check('an empty organization says what to do', true)
  await page.close()
}

// 3. One booking
{
  const page = await context.newPage()
  await stub(page, table([]))
  await page.goto(base + '/t/alpha/bookings/b1')
  await page.getByText('Room 2').waitFor()
  check('a booking shows what it says', (await page.getByText('Room 2').count()) === 1 && (await page.getByText('LTA Lee, 91234567').count()) === 1)
  await shot(page, 'tenant-booking')
  check('somebody else\'s cannot be changed', true)
  await page.close()

  const other = await context.newPage()
  await stub(other, table([]))
  await other.goto(base + '/t/alpha/bookings/b2')
  await other.getByText('Only SGT Other, their unit and admins can change this.').waitFor()
  check('a booking that is not theirs has no buttons', (await other.getByRole('button', { name: 'Edit' }).count()) === 0)
  await other.close()
}

// 4. Changing one: what is sent, and what the API says when it can't
{
  const page = await context.newPage()
  const log = await stub(page, table([], {
    'PUT /t/alpha/Bookings/b1': (r) => {
      const body = JSON.parse(r.postData())
      return body.conduct === 'Clash' ? problem(409, [{ name: 'endDateTime', reason: 'Overlaps with booking b2.', code: 'clash' }]) : json({ ...bookings[0], conduct: body.conduct })
    },
  }))
  await page.goto(base + '/t/alpha/bookings/b1')
  await page.getByRole('button', { name: 'Edit' }).click()
  const starts = await page.getByLabel('Starts').inputValue()
  check('the time shows as it is in the organization\'s zone', starts.endsWith('T09:00'), starts)
  await page.getByLabel('Conduct').fill('Clash')
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('Overlaps with booking b2.').waitFor()
  check('a clash is said', true)
  await page.getByLabel('Conduct').fill('Renamed')
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('Saved').first().waitFor()
  const sent = JSON.parse(log.filter(l => l.key === 'PUT /t/alpha/Bookings/b1').at(-1).body)
  check('the time is left out when it did not move', sent.conduct === 'Renamed' && sent.startDateTime === null && sent.endDateTime === null, JSON.stringify(sent))

  await page.getByRole('button', { name: 'Edit' }).click()
  await page.getByLabel('Ends').fill(starts.replace('T09:00', 'T12:00'))
  await page.getByRole('button', { name: 'Save' }).click()
  await page.waitForTimeout(400)
  const moved = JSON.parse(log.filter(l => l.key === 'PUT /t/alpha/Bookings/b1').at(-1).body)
  const endsAt = new Date(moved.endDateTime)
  check('a new end is sent as the moment it is in the organization\'s zone', endsAt.getUTCHours() === 4 && endsAt.getUTCMinutes() === 0, moved.endDateTime)
  await page.close()
}

// 5. Cancelling
{
  const page = await context.newPage()
  const log = await stub(page, table([], { 'DELETE /t/alpha/Bookings/b1': () => noContent() }))
  await page.goto(base + '/t/alpha/bookings/b1')
  await page.getByRole('button', { name: 'Cancel booking' }).click()
  await page.getByText('Cancel this booking?').waitFor()
  await shot(page, 'tenant-cancel')
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel booking' }).click()
  await page.waitForURL('**/t/alpha')
  check('cancelling goes back to the list', log.some(l => l.key === 'DELETE /t/alpha/Bookings/b1'))
  await page.close()
}

// 6. Not in it, and waiting to be let in
{
  const gone = await context.newPage()
  await stub(gone, { 'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }), 'GET /t/alpha': () => ({ status: 404, body: '', headers: { 'access-control-allow-origin': base } }) })
  await gone.goto(base + '/t/alpha')
  await gone.getByText('There is no such organization').waitFor()
  check('an organization they are not in is not found', true)
  await gone.close()

  const waiting = await context.newPage()
  await stub(waiting, {
    'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }),
    'GET /t/alpha': () => ({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Waiting for approval', status: 403, code: 'pending' }), headers: { 'access-control-allow-origin': base } }),
  })
  await waiting.goto(base + '/t/alpha')
  await waiting.getByText('Waiting for an admin').waitFor()
  check('waiting for approval is said, and does not send them to sign in', waiting.url().endsWith('/t/alpha'))
  await shot(waiting, 'tenant-pending')
  await waiting.close()
}

await finish(browser)
