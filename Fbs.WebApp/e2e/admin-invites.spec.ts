// The invite links of an organization, for its admins: making one, which is the only time the link is known, and stopping one. The API is
// kept by the test, so what is listed again after a change is what the change made.
import type { Page } from '@playwright/test'
import { expect, json, noContent, problem, test } from './support'
import type { Call, Routes } from './support'

// The link is copied to the clipboard, which a page is only let read and write if it has been given the right to
test.use({ viewport: { width: 1100, height: 900 }, permissions: ['clipboard-read', 'clipboard-write'] })

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}
const units = [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }]
const day = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString()
const invite = (id: string, status: string, extra = {}) => ({ id, role: 'Member', unitId: null as string | null, expiresAt: day(5), maxUses: 10, uses: 0, status, createdAt: day(-2), ...extra })
type Invite = ReturnType<typeof invite>
const token = 'Kq3xT9mZ0pLw2VbN7sRfYc4uHdEa8JgXoI1tA5nBvMk'

/** An organization with its links kept, and answered from as the API does, with the limit it has. */
function organization({ invites = [], requireApproval = false, limit = 5 }: { invites?: Invite[], requireApproval?: boolean, limit?: number } = {}) {
  const state = { invites: [...invites], requireApproval }
  let made = 0
  const routes: Routes = [
    [/^GET \/Me$/, () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] })],
    [/^GET \/t\/alpha$/, () => json(org)],
    [/^GET \/t\/alpha\/Units$/, () => json(units)],
    [/^GET \/t\/alpha\/Settings$/, () => json({ name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, requireApproval: state.requireApproval, legacyClaimEnabled: false })],
    [/^GET \/t\/alpha\/Invites$/, () => json(state.invites)],
    [/^POST \/t\/alpha\/Invites$/, (request) => {
      const body = request.postDataJSON()
      if (state.invites.filter(i => i.status === 'Active').length >= limit) {
        return problem(403, [{ name: 'generalErrors', reason: `There can be ${limit} invite links at a time. Revoke one that isn't needed.`, code: 'invite-limit' }])
      }

      const added = invite(`new-${++made}`, 'Active', { role: body.role, unitId: body.unitId, maxUses: body.maxUses, expiresAt: day(body.expiresInDays), createdAt: day(0) })
      state.invites.unshift(added)
      return json({ ...added, token }, 201)
    }],
    [/^DELETE \/t\/alpha\/Invites\/(.+)$/, (_request, _url, id) => {
      state.invites.find(i => i.id === id)!.status = 'Revoked'
      return noContent()
    }],
  ]
  return { state, routes }
}

const made = (calls: Call[]) => calls.filter(c => c.key === 'POST /t/alpha/Invites').map(c => c.body)
const item = (page: Page, status: string) => page.locator(`li[data-status="${status}"]`)

test.describe('what was made', () => {
  test('each link says how it is, how many joined, and as what', async ({ page, goto, api }) => {
    await api(organization({
      invites: [
        invite('i1', 'Active', { uses: 3, unitId: 'u2' }),
        invite('i2', 'Expired', { expiresAt: day(-1) }),
        invite('i3', 'Revoked', { role: 'Admin' }),
        invite('i4', 'UsedUp', { uses: 10 }),
      ],
      requireApproval: true,
    }).routes)

    await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

    await expect(item(page, 'Active')).toContainText('Works')
    await expect(item(page, 'Expired')).toContainText('Ran out of time')
    await expect(item(page, 'Revoked')).toContainText('Stopped')
    await expect(item(page, 'UsedUp')).toContainText('Used up')
    await expect(item(page, 'Active')).toContainText('3 of 10 joined')
    await expect(item(page, 'Active')).toContainText('Member, Bravo')
    await expect(item(page, 'Revoked')).toContainText('Admin')
    // Only one that works can be stopped
    await expect(page.getByRole('button', { name: /^Stop the link made/ })).toHaveCount(1)
    await expect(item(page, 'Active').getByRole('button', { name: /^Stop/ })).toHaveCount(1)
    // That people wait to be let in is said, as it is set
    await expect(page.getByText('They then wait for you to let them in.')).toHaveCount(1)
  })

  test('none is said, and that people are in at once, as it is set', async ({ page, goto, api }) => {
    await api(organization({ requireApproval: false }).routes)

    await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

    await expect(page.getByText('No links yet.')).toBeVisible()
    await expect(page.getByText('They join straight away.')).toHaveCount(1)
  })
})

test('a link made is shown once, where people join, and can be copied', async ({ page, goto, api }) => {
  const { state, routes } = organization()
  const calls = await api(routes)
  await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Make link' }).click()

  await expect(page.getByTestId('new-link')).toBeVisible()
  // What is sent is what is asked for, from the start
  expect(made(calls)).toEqual([expect.objectContaining({ role: 'Member', unitId: null, expiresInDays: 7, maxUses: 10 })])
  const link = page.getByLabel('The link', { exact: true })
  await expect(link).toHaveValue(new URL(`/join/${token}`, page.url()).href)
  await expect(page.getByTestId('new-link')).toContainText('Joins as member. Works until')
  await expect(item(page, 'Active')).toHaveCount(1)
  expect(state.invites).toHaveLength(1)

  await page.getByRole('button', { name: 'Copy' }).click()
  await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible()
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(await link.inputValue())

  await page.reload()
  await expect(item(page, 'Active')).toBeVisible()
  await expect(page.getByTestId('new-link')).toHaveCount(0)
  await expect(page.getByText(token)).toHaveCount(0)
})

test('a link for an admin, in a unit, for a time and a number of people, says so', async ({ page, goto, api }) => {
  const calls = await api(organization().routes)
  await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

  await page.getByRole('combobox', { name: 'They join as' }).click()
  await page.getByRole('option', { name: 'Admin' }).click()
  await page.getByRole('combobox', { name: 'In the unit' }).click()
  await page.getByRole('option', { name: 'Bravo' }).click()
  await page.getByLabel('Works for (days)').fill('3')
  await page.getByLabel('People who can join').fill('1')
  await page.getByRole('button', { name: 'Make link' }).click()

  await expect(page.getByTestId('new-link')).toContainText('Joins as admin, in Bravo.')
  await expect(page.getByTestId('new-link')).toContainText('for up to 1 person.')
  expect(made(calls)).toEqual([expect.objectContaining({ role: 'Admin', unitId: 'u2', expiresInDays: 3, maxUses: 1 })])
})

test('a time and a number of people that are not allowed are said, and not sent', async ({ page, goto, api }) => {
  const calls = await api(organization().routes)
  await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

  await page.getByLabel('Works for (days)').fill('31')
  await page.getByLabel('People who can join').fill('0')
  await page.getByRole('button', { name: 'Make link' }).click()

  await expect(page.getByText('It can work for 1 to 30 days.')).toBeVisible()
  await expect(page.getByText('Between 1 and 100 people can join with it.')).toHaveCount(1)
  expect(made(calls)).toHaveLength(0)
})

test('the limit is said, and stopping a link makes room for another', async ({ page, goto, api }) => {
  await api(organization({ invites: [invite('i1', 'Active'), invite('i2', 'Active')], limit: 2 }).routes)
  await goto('/t/alpha/admin/invites', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Make link' }).click()
  await expect(page.getByText('There can be 2 invite links at a time.')).toBeVisible()
  await expect(page.getByTestId('new-link')).toHaveCount(0)
  await expect(page.locator('li[data-status]')).toHaveCount(2)

  await item(page, 'Active').first().getByRole('button', { name: /^Stop/ }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Stop the link' }).click()
  await expect(item(page, 'Revoked')).toBeVisible()
  await expect(item(page, 'Active')).toHaveCount(1)

  await page.getByRole('button', { name: 'Make link' }).click()
  await expect(page.getByTestId('new-link')).toBeVisible()
})
