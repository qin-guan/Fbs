// The settings of an organization, for its admins: what is shown, what is sent, and what the API says when it can't.
import { expect, json, problem, test } from './support'
import type { Call, Table } from './support'

const org = (role = 'Admin') => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const settings = (extra = {}) => ({ name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, requireApproval: false, legacyClaimEnabled: true, ...extra })
const calendar = (extra = {}) => ({ status: 'None', calendarId: null, lastError: null, verificationExpiresAt: null, serviceAccountEmail: 'sync@example.iam.gserviceaccount.com', ...extra })

const table = (role = 'Admin', extra: Table = {}): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Settings': () => json(settings()),
  'GET /t/alpha/Calendar': () => json(calendar()),
  ...extra,
})
/** Saving answers with what was sent, and claiming as it was when it was left out */
const saving: Table = {
  'PUT /t/alpha/Settings': (request) => {
    const body = request.postDataJSON()
    return json(settings({ ...body, legacyClaimEnabled: body.legacyClaimEnabled ?? true }))
  },
}

const puts = (calls: Call[]) => calls.filter(c => c.key === 'PUT /t/alpha/Settings')

test('the settings are shown, and there is nothing to save until something is changed', async ({ page, goto, api }) => {
  await api(table())

  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Alpha Company')
  // The address can't be changed
  await expect(page.getByLabel('Address')).toBeDisabled()
  await expect(page.getByLabel('Address')).toHaveValue('/t/alpha')
  await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled()
})

test('a change can be undone', async ({ page, goto, api }) => {
  await api(table())
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled()
  await page.getByRole('button', { name: 'Undo' }).click()

  await expect(page.getByLabel('Name', { exact: true })).toHaveValue('Alpha Company')
  await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled()
})

test('what was changed is sent, and claiming is left out when it is not turned off', async ({ page, goto, api }) => {
  const calls = await api(table('Admin', saving))
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  await page.getByRole('combobox', { name: 'Shortest booking' }).click()
  await page.getByRole('option', { name: '15 minutes' }).click()
  await page.getByRole('switch', { name: 'An admin lets people in' }).click()
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByText('Saved', { exact: true }).first()).toBeVisible()
  const sent = puts(calls).at(-1)!.body
  expect(sent).toMatchObject({ name: 'Alpha Coy', slotMinutes: 15, requireApproval: true, timeZone: 'Asia/Singapore', defaultCountryCode: '65' })
  expect(sent).not.toHaveProperty('legacyClaimEnabled')
  // It is saved, and there is nothing more to save
  await expect(page.getByRole('button', { name: 'Save' })).toBeDisabled()
})

test.describe('claiming a place from before accounts', () => {
  test('turning it off is sent', async ({ page, goto, api }) => {
    const calls = await api(table('Admin', saving))
    await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

    await page.getByRole('switch', { name: 'Stop people claiming their place from before' }).click()
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.getByText('Saved', { exact: true }).first()).toBeVisible()
    expect(puts(calls).at(-1)!.body.legacyClaimEnabled).toBe(false)
  })

  test('once it is off, it can\'t be turned on again', async ({ page, goto, api }) => {
    await api(table('Admin', { 'GET /t/alpha/Settings': () => json(settings({ legacyClaimEnabled: false })) }))

    await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

    await expect(page.getByText('People can no longer claim a place from before accounts.')).toBeVisible()
    await expect(page.getByRole('switch', { name: 'Stop people claiming their place from before' })).toHaveCount(0)
  })
})

test('what is wrong is said before anything is sent, and what the API says after it is, by the field', async ({ page, goto, api }) => {
  const calls = await api(table('Admin', {
    'PUT /t/alpha/Settings': () => problem(400, [{ name: 'timeZone', reason: 'That is not a time zone this server knows.', code: 'invalid' }]),
  }))
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await page.getByLabel('Name', { exact: true }).fill('A')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('The name has to be between 2 and 100 characters.')).toBeVisible()
  expect(puts(calls)).toHaveLength(0)

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  // The form looks at what was typed as it is typed, and is not pressed on until it has
  await expect(page.getByText('The name has to be between 2 and 100 characters.')).toHaveCount(0)
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('That is not a time zone this server knows.')).toBeVisible()
})

test('a calendar is asked for with its id, and the code is then what is sent', async ({ page, goto, api }) => {
  const calls = await api(table('Admin', {
    'POST /t/alpha/Calendar': request => json(calendar({ status: 'Pending', calendarId: request.postDataJSON().calendarId, verificationExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString() })),
    'POST /t/alpha/Calendar/Confirm': () => json(calendar({ status: 'Active', calendarId: 'team@group.calendar.google.com' })),
  }))
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await expect(page.getByText('sync@example.iam.gserviceaccount.com')).toBeVisible()
  await page.getByRole('button', { name: 'Connect the calendar' }).click()
  await expect(page.getByText('The calendar id is the address of the calendar')).toBeVisible()
  expect(calls.filter(c => c.key === 'POST /t/alpha/Calendar')).toHaveLength(0)

  await page.getByLabel('Calendar id').fill('team@group.calendar.google.com')
  await page.getByRole('button', { name: 'Connect the calendar' }).click()
  await expect(page.getByText('Look on the calendar for the code', { exact: true }).first()).toBeVisible()
  expect(calls.filter(c => c.key === 'POST /t/alpha/Calendar').at(-1)!.body).toEqual({ calendarId: 'team@group.calendar.google.com' })

  await page.getByLabel('Code').fill('ab23cdef')
  await page.getByRole('button', { name: 'Confirm the code' }).click()
  await expect(page.getByText('The calendar is connected', { exact: true }).first()).toBeVisible()
  expect(calls.filter(c => c.key === 'POST /t/alpha/Calendar/Confirm').at(-1)!.body).toEqual({ code: 'ab23cdef' })
  await expect(page.getByRole('button', { name: 'Stop copying' })).toBeVisible()
})

test('copying to a calendar can be stopped', async ({ page, goto, api }) => {
  const calls = await api(table('Admin', {
    'GET /t/alpha/Calendar': () => json(calendar({ status: 'Active', calendarId: 'team@group.calendar.google.com' })),
    'DELETE /t/alpha/Calendar': () => ({ status: 204, body: '' }),
  }))
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await expect(page.getByText('team@group.calendar.google.com')).toBeVisible()
  await page.getByRole('button', { name: 'Stop copying' }).click()

  await expect(page.getByText('Stopped copying to the calendar', { exact: true }).first()).toBeVisible()
  expect(calls.map(c => c.key)).toContain('DELETE /t/alpha/Calendar')
  await expect(page.getByRole('button', { name: 'Connect the calendar' })).toBeVisible()
})

test('a member is told it is for admins, and nothing of the settings is asked for', async ({ page, goto, api }) => {
  const calls = await api(table('Member'))

  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await expect(page.getByText('Only admins can do this')).toBeVisible()
  expect(calls.map(c => c.key)).not.toContain('GET /t/alpha/Settings')
  // No admin links in the sidebar
  await expect(page.getByRole('link', { name: 'Settings' })).toHaveCount(0)
})
