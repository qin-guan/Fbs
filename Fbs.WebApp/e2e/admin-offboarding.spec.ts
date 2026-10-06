// Deleting an organization, being told that it is to be deleted and taking that back, and downloading copies of data. The API is kept by
// the test, so what is shown after a change is what the change made.
import { readFile } from 'node:fs/promises'
import { expect, json, noContent, problem, test } from './support'
import type { Reply, Table } from './support'

const org = (role: string) => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const settings = { name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, requireApproval: false, legacyClaimEnabled: false }
const deleteAfter = () => new Date(Date.now() + 30 * 86_400_000).toISOString()

/** An organization that is kept, and answered from as the API does: usable, then to be deleted, then usable again. */
function organization({ role = 'Admin', tenantStatus = 'Active', exportLimited = false } = {}) {
  const state = { tenantStatus, deleteAfter: tenantStatus === 'PendingDeletion' ? deleteAfter() : null as string | null, asked: [] as { confirm: string }[] }
  const refusal = (code: string): Reply => ({ ...json({ title: 'Not available', status: 403, detail: 'This organisation is not available.', code }, 403), contentType: 'application/problem+json' })
  const usable = (answer: () => Reply) => () => state.tenantStatus === 'PendingDeletion' ? refusal('pending-deletion') : state.tenantStatus === 'Suspended' ? refusal('unavailable') : answer()
  const limited = () => problem(429, [{ name: 'x', reason: 'slow down', code: 'rate-limited' }])
  const table: Table = {
    'GET /Me': () => json({
      id: 'a',
      name: 'Sam Lee',
      email: 'sam@example.com',
      memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam', tenantStatus: state.tenantStatus, deleteAfter: state.deleteAfter }],
    }),
    'GET /t/alpha': usable(() => json(org(role))),
    'GET /t/alpha/Settings': usable(() => json(settings)),
    'GET /t/alpha/Calendar': usable(() => json({ status: 'None', calendarId: null, lastError: null, verificationExpiresAt: null, serviceAccountEmail: null })),
    'GET /t/alpha/Bookings': usable(() => json([])),
    'GET /t/alpha/Facilities/Bookable': usable(() => json([])),
    'GET /t/alpha/Facilities': usable(() => json([])),
    'GET /t/alpha/Units': usable(() => json([])),
    'GET /t/alpha/Invites': usable(() => json([])),
    'GET /t/alpha/Members': usable(() => json([])),
    'GET /t/alpha/Export': () => exportLimited ? limited() : json({ organization: { slug: 'alpha', name: 'Alpha Company' }, members: [{ displayName: 'CPT Sam', phone: '+6591234567' }], bookings: [] }),
    'GET /Me/Export': () => exportLimited ? limited() : json({ account: { name: 'Sam Lee', email: 'sam@example.com' }, memberships: [], bookings: [] }),
    'POST /Tenants/alpha/Deletion': (request) => {
      const body = request.postDataJSON()
      state.asked.push(body)
      if (body.confirm !== 'alpha') {
        return problem(400, [{ name: 'confirm', reason: 'Type the address of the organisation to say that you mean it.', code: 'invalid' }])
      }

      state.tenantStatus = 'PendingDeletion'
      state.deleteAfter = deleteAfter()
      return json({ deleteAfter: state.deleteAfter })
    },
    'DELETE /Tenants/alpha/Deletion': () => {
      state.tenantStatus = 'Active'
      state.deleteAfter = null
      return noContent()
    },
  }
  return { state, table }
}

test('deleting it needs its address typed, says it is to be deleted, and an admin can take that back', async ({ page, goto, api }) => {
  const { state, table } = organization()
  const calls = await api(table)
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Delete this organization' }).click()
  const dialog = page.getByRole('dialog')
  const confirm = dialog.getByRole('button', { name: 'Delete the organization' })
  await expect(confirm).toBeDisabled()
  await dialog.getByLabel('The address of the organization').fill('alph')
  await expect(confirm).toBeDisabled()
  await dialog.getByLabel('The address of the organization').fill('alpha')
  await expect(confirm).toBeEnabled()
  await confirm.click()

  await expect(page).toHaveURL(/\/orgs$/)
  // What is sent is the address that was typed
  expect(state.asked).toEqual([{ confirm: 'alpha' }])
  // The list of organizations says it is to be deleted, and until when
  await expect(page.getByText(/To be deleted, on .* at the earliest/)).toHaveCount(1)

  // The whole card is the link, to where they are told why it can't be used. What is clicked is the card, not the link in it, so the test goes
  // to where it leads.
  await expect(page.getByRole('link', { name: /Alpha Company/ })).toHaveAttribute('href', '/t/alpha')
  await goto('/t/alpha', { waitUntil: 'hydration' })
  await expect(page.getByText('This organization is to be deleted')).toBeVisible()
  await expect(page.getByText('but you can restore it')).toHaveCount(1)
  await page.getByRole('button', { name: 'Restore it' }).click()

  // Restored, it is used as it was
  await expect(page.getByPlaceholder('Keyword search')).toBeVisible()
  expect(state.tenantStatus).toBe('Active')
  expect(calls.map(c => c.key)).toContain('DELETE /Tenants/alpha/Deletion')
})

test('a refusal of the API is said in the modal, which stays', async ({ page, goto, api }) => {
  const { table } = organization()
  table['POST /Tenants/alpha/Deletion'] = () => problem(409, [{ name: 'generalErrors', reason: 'It can\'t be deleted now: it is to be deleted already, or has been suspended.', code: 'not-active' }])
  await api(table)
  await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Delete this organization' }).click()
  await page.getByRole('dialog').getByLabel('The address of the organization').fill('alpha')
  await page.getByRole('dialog').getByRole('button', { name: 'Delete the organization' }).click()

  await expect(page.getByRole('dialog').getByText('It can\'t be deleted now')).toBeVisible()
  await expect(page).toHaveURL(/\/admin\/settings$/)
})

test('a member can\'t restore it, is told any admin can, and the organization is not asked for over and over', async ({ page, goto, api }) => {
  const calls = await api(organization({ role: 'Member', tenantStatus: 'PendingDeletion' }).table)

  await goto('/t/alpha', { waitUntil: 'hydration' })

  await expect(page.getByText('This organization is to be deleted')).toBeVisible()
  await expect(page.getByText('any admin of it can restore it')).toHaveCount(1)
  await expect(page.getByRole('button', { name: 'Restore it' })).toHaveCount(0)
  // It once asked for the organization again each time it was shown, and it is shown because it can't be had: so it never stopped
  await page.waitForTimeout(1500)
  expect(calls.filter(c => c.key === 'GET /t/alpha').length).toBeLessThanOrEqual(2)
})

test('an organization that has been paused is said to be', async ({ page, goto, api }) => {
  await api(organization({ tenantStatus: 'Suspended' }).table)

  await goto('/orgs', { waitUntil: 'hydration' })

  await expect(page.getByText('Paused', { exact: true }).first()).toBeVisible()
})

test.describe('downloading copies', () => {
  test('a person gets their data as a file', async ({ page, goto, api }) => {
    await api(organization().table)
    await goto('/account', { waitUntil: 'hydration' })

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Download my data' }).click()])

    expect(download.suggestedFilename()).toBe('my-data.json')
    expect(JSON.parse(await readFile(await download.path(), 'utf8')).account.name).toBe('Sam Lee')
  })

  test('an admin gets the organization\'s as a file named for it', async ({ page, goto, api }) => {
    await api(organization().table)
    await goto('/t/alpha/admin/settings', { waitUntil: 'hydration' })

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Download a copy' }).click()])

    expect(download.suggestedFilename()).toBe('alpha-data.json')
    expect(JSON.parse(await readFile(await download.path(), 'utf8')).members[0].phone).toBe('+6591234567')
  })

  test('being asked for too often is said', async ({ page, goto, api }) => {
    await api(organization({ exportLimited: true }).table)
    await goto('/account', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Download my data' }).click()

    await expect(page.getByText('Too many tries', { exact: true })).toBeVisible()
  })
})
