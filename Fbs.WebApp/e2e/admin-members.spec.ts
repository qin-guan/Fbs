// The people of an organization, for its admins: who is waiting, letting in and removing, adding by phone number and changing, and what the API
// says when it can't. The API is kept by the test, so what is shown again after a change is what the change made.
import type { Page } from '@playwright/test'
import { expect, json, problem, test } from './support'
import type { Call, Routes } from './support'

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}
const units = [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }]

const member = (id: string, displayName: string, extra = {}) => ({
  id, displayName, phone: null as string | null, unitId: null as string | null, role: 'Member', notificationScope: 'None', status: 'Active', hasAccount: true, ...extra,
})
type Member = ReturnType<typeof member>
const people = () => [
  member('m1', 'CPT Sam', { role: 'Admin', unitId: 'u1', phone: '+6591234567', notificationScope: 'All' }),
  member('m2', 'SGT Lee', { unitId: 'u2', phone: '+6598765432', notificationScope: 'Unit' }),
  member('m3', 'LTA Chan', { status: 'Pending', unitId: 'u2', notificationScope: 'Unit' }),
  member('m4', 'PTE Wong', { status: 'Pending' }),
  member('m5', 'CPL Ong', { status: 'Unclaimed', hasAccount: false, phone: '+6590000001' }),
  member('m6', 'REC Goh', { status: 'Removed' }),
]

/** An organization with its people kept, and answered from as the API does, with the rules it has. */
function organization(members: Member[] = people()) {
  const state = { members }
  const phoneOf = (typed: string) => {
    const t = typed.trim()
    if (!/^\+?[\d\s-]{7,}$/.test(t)) {
      return undefined
    }

    const digits = t.replace(/\D/g, '')
    return t.startsWith('+') ? `+${digits}` : `+65${digits}`
  }
  const lastAdmin = 'The organisation needs an admin, so the last one can\'t be made a member or removed.'
  const routes: Routes = [
    [/^GET \/Me$/, () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] })],
    [/^GET \/t\/alpha$/, () => json(org)],
    [/^GET \/t\/alpha\/Units$/, () => json(units)],
    [/^GET \/t\/alpha\/Members$/, (_request, url) => json(state.members.filter(m => url.searchParams.get('includeRemoved') === 'true' || m.status !== 'Removed'))],
    [/^POST \/t\/alpha\/Members$/, (request) => {
      const body = request.postDataJSON()
      const phone = phoneOf(body.phone ?? '')
      if (!phone) {
        return problem(400, [{ name: 'phone', reason: 'That is not a phone number.', code: 'phone-invalid' }])
      }

      if (state.members.some(m => m.phone === phone)) {
        return problem(409, [{ name: 'phone', reason: 'Someone in this organisation has that number already.', code: 'phone-taken' }])
      }

      const added = { ...member(`new-${state.members.length}`, body.displayName, { status: 'Unclaimed', hasAccount: false }), ...body, phone, status: 'Unclaimed' }
      state.members.push(added)
      return json(added, 201)
    }],
    [/^PUT \/t\/alpha\/Members\/(.+)$/, (request, _url, id) => {
      const body = request.postDataJSON()
      const who = state.members.find(m => m.id === id)!
      const status = body.membership === 'Removed' ? 'Removed' : who.hasAccount ? 'Active' : 'Unclaimed'
      const phone = body.phone?.trim() ? phoneOf(body.phone) : null
      if (body.phone?.trim() && !phone) {
        return problem(400, [{ name: 'phone', reason: 'That is not a phone number.', code: 'phone-invalid' }])
      }

      if (status === 'Unclaimed' && !phone) {
        return problem(400, [{ name: 'phone', reason: 'Someone who hasn\'t signed in yet is found by their phone number, so it can\'t be left out.', code: 'phone-needed' }])
      }

      const admins = state.members.filter(m => m.role === 'Admin' && m.status === 'Active')
      if (admins.length === 1 && admins[0]!.id === who.id && !(status === 'Active' && body.role === 'Admin')) {
        return problem(409, [{ name: 'generalErrors', reason: lastAdmin, code: 'last-admin' }])
      }

      if (phone && state.members.some(m => m.phone === phone && m.id !== who.id)) {
        return problem(409, [{ name: 'phone', reason: 'Someone in this organisation has that number already.', code: 'phone-taken' }])
      }

      Object.assign(who, { displayName: body.displayName, phone, unitId: body.unitId, role: body.role, notificationScope: body.notificationScope, status })
      return json(who)
    }],
  ]
  return { state, routes }
}

/** What was sent to add somebody, or to change `id` */
const sent = (calls: Call[], method: 'POST' | 'PUT', id?: string) => calls.filter(c => c.key === `${method} /t/alpha/Members${id ? `/${id}` : ''}`).map(c => c.body)
// The list, and not the name of the admin in the sidebar
const row = (page: Page, name: string) => page.locator('li[data-status]', { has: page.getByText(name, { exact: true }) })

test.describe('who is there', () => {
  test('those waiting are first, and said to be, and who was removed is not shown', async ({ page, goto, api }) => {
    await api(organization().routes)

    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await expect(row(page, 'CPT Sam')).toBeVisible()
    await expect(page.getByTestId('member-name').nth(0)).toHaveText('LTA Chan')
    await expect(page.getByTestId('member-name').nth(1)).toHaveText('PTE Wong')
    await expect(page.getByText('2 people are waiting to be let in')).toHaveCount(1)
    await expect(row(page, 'REC Goh')).toHaveCount(0)
  })

  test('each says their number and unit, and whether they are an admin or have signed in', async ({ page, goto, api }) => {
    await api(organization().routes)

    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await expect(row(page, 'CPT Sam')).toContainText('+6591234567 · Alpha')
    await expect(row(page, 'CPT Sam').getByText('Admin', { exact: true })).toHaveCount(1)
    await expect(row(page, 'SGT Lee').getByText('Admin', { exact: true })).toHaveCount(0)
    await expect(row(page, 'CPL Ong').getByText('Unclaimed')).toHaveCount(1)
  })

  test('removed ones are asked for when shown, and can be let back in', async ({ page, goto, api }) => {
    const calls = await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('switch', { name: 'Show removed' }).click()

    await expect(row(page, 'REC Goh')).toBeVisible()
    expect(calls.filter(c => c.key === 'GET /t/alpha/Members').at(-1)!.query.includeRemoved).toBe('true')
    await expect(page.getByRole('button', { name: 'Let REC Goh back in' })).toHaveCount(1)
  })

  test('who is looked for is found by their unit, and nobody found is said', async ({ page, goto, api }) => {
    await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })
    await expect(row(page, 'CPT Sam')).toBeVisible()

    await page.getByLabel('Search people').fill('bravo')
    await expect(row(page, 'CPT Sam')).toHaveCount(0)
    await expect(page.locator('li[data-status]')).toHaveCount(2)
    await expect(row(page, 'SGT Lee')).toHaveCount(1)
    await expect(row(page, 'LTA Chan')).toHaveCount(1)

    await page.getByLabel('Search people').fill('zzz')
    await expect(page.getByText('Nobody matches.')).toBeVisible()
  })

  test('nobody is said, with what to do', async ({ page, goto, api }) => {
    await api(organization([]).routes)

    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await expect(page.getByText('There is nobody yet.')).toBeVisible()
  })
})

test.describe('letting in', () => {
  test('somebody let in is sent with everything else as it was, and is then in', async ({ page, goto, api }) => {
    const { state, routes } = organization()
    const calls = await api(routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Let LTA Chan in' }).click()

    await expect(page.getByText('2 people are waiting')).toHaveCount(0)
    expect(sent(calls, 'PUT', 'm3')).toEqual([expect.objectContaining({ membership: 'In', displayName: 'LTA Chan', role: 'Member', unitId: 'u2', notificationScope: 'Unit' })])
    expect(state.members.find(m => m.id === 'm3')!.status).toBe('Active')
    await expect(row(page, 'LTA Chan').getByText('Active')).toHaveCount(1)
    // Who is left waiting is said to be
    await expect(page.getByText('Somebody is waiting to be let in')).toHaveCount(1)
  })

  test('somebody turned away can be let back in', async ({ page, goto, api }) => {
    const { state, routes } = organization()
    const calls = await api(routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Turn PTE Wong away' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Turn away', exact: true }).click()
    await expect(row(page, 'PTE Wong')).toHaveCount(0)
    expect(sent(calls, 'PUT', 'm4')[0].membership).toBe('Removed')
    expect(state.members.find(m => m.id === 'm4')!.status).toBe('Removed')

    await page.getByRole('switch', { name: 'Show removed' }).click()
    await page.getByRole('button', { name: 'Let PTE Wong back in' }).click()

    await expect(row(page, 'PTE Wong').getByText('Active')).toBeVisible()
    expect(sent(calls, 'PUT', 'm4')[1].membership).toBe('In')
  })
})

test.describe('changing somebody', () => {
  test('starts from what they are, and what was changed is sent and shown', async ({ page, goto, api }) => {
    const calls = await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change SGT Lee' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('SGT Lee')
    await expect(dialog.getByLabel('Phone number')).toHaveValue('+6598765432')

    await dialog.getByRole('combobox', { name: 'Role' }).click()
    await page.getByRole('option', { name: 'Admin' }).click()
    await dialog.getByRole('combobox', { name: 'Unit' }).click()
    await page.getByRole('option', { name: 'Alpha' }).click()
    await dialog.getByRole('combobox', { name: 'Tell them about' }).click()
    await page.getByRole('option', { name: 'Every booking' }).click()
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(dialog).toHaveCount(0)
    expect(sent(calls, 'PUT', 'm2')).toEqual([expect.objectContaining({ role: 'Admin', unitId: 'u1', notificationScope: 'All', membership: 'In', phone: '+6598765432' })])
    await expect(row(page, 'SGT Lee').getByText('Admin', { exact: true })).toHaveCount(1)
  })

  test('a number somebody else has is said by the number', async ({ page, goto, api }) => {
    await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change SGT Lee' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Phone number').fill('+65 9123 4567')
    // Saved from the field rather than with the button: once a field has been left, the form looks at it again 300 ms after it was typed in,
    // which puts away what the API said of it when the API answers sooner than that, as it does here
    await dialog.getByLabel('Phone number').press('Enter')

    await expect(dialog.getByText('Someone in this organisation has that number already.')).toBeVisible()
  })

  test('somebody who has not signed in has to have a number, and none is not sent', async ({ page, goto, api }) => {
    const calls = await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change CPL Ong' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Phone number').fill('')
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(dialog.getByText('A phone number is needed')).toBeVisible()
    expect(sent(calls, 'PUT', 'm5')).toHaveLength(0)
  })

  test('the last admin is said to be needed, in the form', async ({ page, goto, api }) => {
    await api(organization().routes)
    await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change CPT Sam' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Role' }).click()
    await page.getByRole('option', { name: 'Member' }).click()
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(dialog.getByText('The organisation needs an admin')).toBeVisible()
  })
})

test('adding somebody needs a name and a number, and they are listed as not yet signed in', async ({ page, goto, api }) => {
  const { state, routes } = organization()
  const calls = await api(routes)
  await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Add', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()
  await expect(dialog.getByText('The name has to be between 1 and 200 characters.')).toBeVisible()
  await expect(dialog.getByText('A phone number is needed')).toHaveCount(1)
  expect(sent(calls, 'POST')).toHaveLength(0)

  await dialog.getByLabel('Name', { exact: true }).fill('ME Tan')
  await dialog.getByLabel('Phone number').fill('abc')
  // Added from the field rather than with the button, so that what the API says of the number isn't put away (see when a number is taken)
  await dialog.getByLabel('Phone number').press('Enter')
  // What is not a phone number is said by the API, and what the form said before is gone
  await expect(dialog.getByText('That is not a phone number.')).toBeVisible()
  await expect(dialog.getByText('The name has to be between 1 and 200 characters.')).toHaveCount(0)
  await expect(dialog.getByText('A phone number is needed')).toHaveCount(0)

  await dialog.getByLabel('Phone number').fill('9123 0000')
  await dialog.getByLabel('Phone number').press('Enter')

  await expect(dialog).toHaveCount(0)
  expect(sent(calls, 'POST').at(-1)).toMatchObject({ displayName: 'ME Tan', phone: '9123 0000', unitId: null, role: 'Member' })
  await expect(row(page, 'ME Tan').getByText('Unclaimed')).toHaveCount(1)
  expect(state.members.some(m => m.phone === '+6591230000')).toBe(true)
})

test('somebody removed is gone from the list, and the last admin can\'t be', async ({ page, goto, api }) => {
  const { state, routes } = organization()
  const calls = await api(routes)
  await goto('/t/alpha/admin/members', { waitUntil: 'hydration' })

  await page.getByRole('button', { name: 'Remove SGT Lee' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove', exact: true }).click()
  await expect(row(page, 'SGT Lee')).toHaveCount(0)
  expect(sent(calls, 'PUT', 'm2')[0].membership).toBe('Removed')
  expect(state.members.find(m => m.id === 'm2')!.status).toBe('Removed')

  await page.getByRole('button', { name: 'Remove CPT Sam' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove', exact: true }).click()

  await expect(page.getByRole('dialog').getByText('The organisation needs an admin')).toBeVisible()
  await expect(row(page, 'CPT Sam')).toHaveCount(1)
})
