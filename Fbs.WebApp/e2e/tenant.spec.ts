// The pages of an organization: what is booked, one booking, and changing and cancelling it.
import { expect, json, noContent, problem, test } from './support'
import type { Table } from './support'

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Member', notificationScope: 'None', phone: null },
}

const person = (id: string, name: string) => ({ memberId: id, displayName: name, unitId: null })
/** `offset` days from today, at `hour`:`minute` in Singapore */
const day = (offset: number, hour: number, minute = 0) => {
  const d = new Date()
  d.setUTCDate(d.getUTCDate() + offset)
  d.setUTCHours(hour - 8, minute, 0, 0)
  return d.toISOString().replace('.000Z', '+00:00')
}
const booking = (id: string, facility: string, from: string, to: string, conduct: string, by: ReturnType<typeof person>, extra = {}) => ({
  id, facilityId: `f-${facility}`, facilityName: facility, startDateTime: from, endDateTime: to, conduct, description: null, pocName: null, pocPhone: null,
  bookedBy: by, updatedBy: null, canManage: by.memberId === 'm1', ...extra,
})

const bookings = [
  booking('b1', 'Hall', day(2, 9), day(2, 11), 'Lesson', person('m1', 'CPT Sam'), { description: 'Room 2', pocName: 'LTA Lee', pocPhone: '91234567' }),
  booking('b2', 'Gym', day(3, 14), day(3, 15), 'Circuit', person('m2', 'SGT Other')),
  booking('b3', 'Hall', day(5, 8), day(5, 10), 'Briefing', person('m1', 'CPT Sam')),
]

const table = (extra: Table = {}): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Member', status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org),
  'GET /t/alpha/Facilities/Bookable': () => json([{ id: 'f-Hall', name: 'Hall', group: 'Indoor' }, { id: 'f-Gym', name: 'Gym', group: 'Indoor' }]),
  'GET /t/alpha/Bookings': (_request, url) => json(url.searchParams.get('mine') === 'true' ? bookings.filter(b => b.canManage) : bookings),
  'GET /t/alpha/Bookings/b1': () => json(bookings[0]),
  'GET /t/alpha/Bookings/b2': () => json(bookings[1]),
  ...extra,
})

test.describe('the list', () => {
  test('shows what is booked in the next 31 days, and who booked it', async ({ page, goto, api }) => {
    const calls = await api(table())

    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByText('Lesson')).toBeVisible()
    await expect(page.getByText('Circuit')).toHaveCount(1)
    await expect(page.getByText('Briefing')).toHaveCount(1)
    await expect(page.getByText('SGT Other').first()).toBeVisible()
    await expect(page).toHaveTitle(/Alpha Company/)
    const asked = calls.filter(c => c.key === 'GET /t/alpha/Bookings').at(-1)!.query
    expect((new Date(asked.to!).getTime() - new Date(asked.from!).getTime()) / 86_400_000).toBe(31)
    expect(asked.mine).toBe('false')
  })

  test('keyword search narrows it', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha', { waitUntil: 'hydration' })
    await expect(page.getByText('Lesson')).toBeVisible()

    await page.getByPlaceholder('Keyword search').fill('circuit')

    await expect(page.getByText('Lesson')).toHaveCount(0)
    await expect(page.getByText('Circuit')).toHaveCount(1)
  })

  test('only mine asks the API for only the caller\'s', async ({ page, goto, api }) => {
    const calls = await api(table())
    await goto('/t/alpha', { waitUntil: 'hydration' })
    await expect(page.getByText('Circuit')).toBeVisible()

    await page.getByRole('switch', { name: 'Only mine' }).click()

    await expect(page.getByText('Circuit')).toHaveCount(0)
    expect(calls.filter(c => c.key === 'GET /t/alpha/Bookings').at(-1)?.query.mine).toBe('true')
  })

  test('an organization with nothing booked, and nothing to book, says what to do', async ({ page, goto, api }) => {
    await api(table({
      'GET /t/alpha/Bookings': () => json([]),
      'GET /t/alpha/Facilities/Bookable': () => json([]),
    }))

    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByText('You can\'t book anything yet')).toBeVisible()
    await expect(page.getByText('Nothing is booked in this time')).toBeVisible()
  })
})

test.describe('a booking', () => {
  test('shows what it says', async ({ page, goto, api }) => {
    await api(table())

    await goto('/t/alpha/bookings/b1', { waitUntil: 'hydration' })

    await expect(page.getByText('Room 2')).toHaveCount(1)
    await expect(page.getByText('LTA Lee, 91234567')).toHaveCount(1)
  })

  test('somebody else\'s has no buttons to change it, and says who can', async ({ page, goto, api }) => {
    await api(table())

    await goto('/t/alpha/bookings/b2', { waitUntil: 'hydration' })

    await expect(page.getByText('Only SGT Other, their unit and admins can change this.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0)
  })

  test('changing one says when it clashes, leaves the time out when it didn\'t move, and sends a new end in the organization\'s time zone', async ({ page, goto, api }) => {
    const calls = await api(table({
      'PUT /t/alpha/Bookings/b1': (request) => {
        const body = request.postDataJSON()
        return body.conduct === 'Clash'
          ? problem(409, [{ name: 'endDateTime', reason: 'Overlaps with booking b2.', code: 'clash' }])
          : json({ ...bookings[0], conduct: body.conduct })
      },
    }))
    const lastPut = () => calls.filter(c => c.key === 'PUT /t/alpha/Bookings/b1').at(-1)?.body
    await goto('/t/alpha/bookings/b1', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Edit' }).click()
    // The time is shown as it is in the organization's zone
    await expect(page.getByLabel('Starts')).toHaveValue(/T09:00$/)
    const starts = await page.getByLabel('Starts').inputValue()

    await page.getByLabel('Conduct').fill('Clash')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Overlaps with booking b2.')).toBeVisible()

    await page.getByLabel('Conduct').fill('Renamed')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Saved').first()).toBeVisible()
    expect(lastPut()).toMatchObject({ conduct: 'Renamed', startDateTime: null, endDateTime: null })

    await page.getByRole('button', { name: 'Edit' }).click()
    await page.getByLabel('Ends').fill(starts.replace('T09:00', 'T12:00'))
    await page.getByRole('button', { name: 'Save' }).click()
    // 12:00 in Singapore
    await expect.poll(() => lastPut()?.endDateTime && new Date(lastPut().endDateTime).toISOString().slice(11, 16)).toBe('04:00')
  })

  test('cancelling one goes back to the list', async ({ page, goto, api }) => {
    const calls = await api(table({ 'DELETE /t/alpha/Bookings/b1': () => noContent() }))
    await goto('/t/alpha/bookings/b1', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Cancel booking' }).click()
    await expect(page.getByText('Cancel this booking?')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel booking' }).click()

    await expect(page).toHaveURL(/\/t\/alpha$/)
    expect(calls.map(c => c.key)).toContain('DELETE /t/alpha/Bookings/b1')
  })
})

test.describe('somebody who can\'t see the organization', () => {
  test('one they are not in is not found', async ({ page, goto, api }) => {
    await api({
      'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }),
      'GET /t/alpha': () => ({ status: 404, body: '' }),
    })

    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByText('There is no such organization')).toBeVisible()
  })

  test('one they are waiting to be let in to says so, and doesn\'t send them to sign in', async ({ page, goto, api }) => {
    await api({
      'GET /Me': () => json({ id: 'a', name: 'S', email: null, memberships: [] }),
      'GET /t/alpha': () => ({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ title: 'Waiting for approval', status: 403, code: 'pending' }) }),
    })

    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByText('Waiting for an admin')).toBeVisible()
    await expect(page).toHaveURL(/\/t\/alpha$/)
  })
})
