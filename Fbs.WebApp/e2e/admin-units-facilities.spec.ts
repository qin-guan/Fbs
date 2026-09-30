// The units and facilities of an organization, for its admins: adding, changing and deleting them, and what the API says when it
// can't. The API is kept by the test, so what is shown again after a change is what the change made.
import type { Page } from '@playwright/test'
import { expect, json, noContent, problem, test } from './support'
import type { Call, Routes } from './support'

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}

interface Unit { id: string, name: string }
interface Facility { id: string, name: string, group: string | null, availableToAll: boolean, unitIds: string[] }

/** An organization with what is in it kept, and answered from. */
function organization({ units = [], facilities = [], unitLimit = 50, facilityLimit = 100 }: { units?: Unit[], facilities?: Facility[], unitLimit?: number, facilityLimit?: number } = {}) {
  const state = { units: [...units], facilities: [...facilities], deleted: [] as string[] }
  let made = 0
  const routes: Routes = [
    [/^GET \/Me$/, () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] })],
    [/^GET \/t\/alpha$/, () => json(org)],
    [/^GET \/t\/alpha\/Units$/, () => json(state.units)],
    [/^POST \/t\/alpha\/Units$/, (request) => {
      const { name } = request.postDataJSON()
      if (state.units.length >= unitLimit) {
        return problem(403, [{ name: 'generalErrors', reason: `An organisation can have ${unitLimit} units. Delete one that isn't needed.`, code: 'unit-limit' }])
      }

      if (state.units.some(u => u.name === name)) {
        return problem(409, [{ name: 'name', reason: 'There is a unit with that name already.', code: 'unit-exists' }])
      }

      const unit = { id: `new-${++made}`, name }
      state.units.push(unit)
      return json(unit, 201)
    }],
    [/^PUT \/t\/alpha\/Units\/(.+)$/, (request, _url, id) => {
      const { name } = request.postDataJSON()
      if (state.units.some(u => u.name === name && u.id !== id)) {
        return problem(409, [{ name: 'name', reason: 'There is a unit with that name already.', code: 'unit-exists' }])
      }

      const unit = state.units.find(u => u.id === id)!
      unit.name = name
      return json(unit)
    }],
    [/^DELETE \/t\/alpha\/Units\/(.+)$/, (_request, _url, id) => {
      if (id === 'u-busy') {
        return problem(409, [{ name: 'generalErrors', reason: 'Move the people in it to another unit first, and a unit that bookings were made for can\'t be deleted.', code: 'unit-in-use' }])
      }

      state.units = state.units.filter(u => u.id !== id)
      state.deleted.push(id!)
      return noContent()
    }],
    [/^GET \/t\/alpha\/Facilities$/, () => json(state.facilities)],
    [/^POST \/t\/alpha\/Facilities$/, (request) => {
      const body = request.postDataJSON()
      if (state.facilities.length >= facilityLimit) {
        return problem(403, [{ name: 'generalErrors', reason: `An organisation can have ${facilityLimit} facilities. Delete one that isn't needed.`, code: 'facility-limit' }])
      }

      if (state.facilities.some(f => f.name === body.name)) {
        return problem(409, [{ name: 'name', reason: 'There is a facility with that name already.', code: 'facility-exists' }])
      }

      const facility = { id: `new-${++made}`, ...body }
      state.facilities.push(facility)
      return json(facility, 201)
    }],
    [/^PUT \/t\/alpha\/Facilities\/(.+)$/, (request, _url, id) => {
      const body = request.postDataJSON()
      if (state.facilities.some(f => f.name === body.name && f.id !== id)) {
        return problem(409, [{ name: 'name', reason: 'There is a facility with that name already.', code: 'facility-exists' }])
      }

      const facility = state.facilities.find(f => f.id === id)!
      Object.assign(facility, body)
      return json(facility)
    }],
    [/^DELETE \/t\/alpha\/Facilities\/(.+)$/, (_request, _url, id) => {
      if (id === 'f-busy') {
        return problem(409, [{ name: 'generalErrors', reason: 'Something has been booked on it, so it can\'t be deleted.', code: 'facility-in-use' }])
      }

      state.facilities = state.facilities.filter(f => f.id !== id)
      state.deleted.push(id!)
      return noContent()
    }],
  ]
  return { state, routes }
}

/** What was sent in the calls `key` */
const sent = (calls: Call[], key: string) => calls.filter(c => c.key === key).map(c => c.body)

test.describe('units', () => {
  const someUnits = () => organization({ units: [{ id: 'u2', name: 'Bravo' }, { id: 'u1', name: 'Alpha' }, { id: 'u-busy', name: 'Busy' }] })
  const names = (page: Page) => page.locator('li span.font-medium')

  test('are listed by name', async ({ page, goto, api }) => {
    await api(someUnits().routes)

    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await expect(names(page)).toHaveText(['Alpha', 'Bravo', 'Busy'])
  })

  test('one is added and listed, and the box is emptied for the next', async ({ page, goto, api }) => {
    const calls = await api(someUnits().routes)
    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await page.getByLabel('Name of the new unit').fill('Charlie')
    await page.getByRole('button', { name: 'Add', exact: true }).click()

    await expect(names(page)).toContainText(['Charlie'])
    expect(sent(calls, 'POST /t/alpha/Units')).toEqual([{ name: 'Charlie' }])
    await expect(page.getByLabel('Name of the new unit')).toHaveValue('')
  })

  test('a name that is blank is said, and not sent, and one that is taken is said', async ({ page, goto, api }) => {
    const calls = await api(someUnits().routes)
    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await page.getByLabel('Name of the new unit').fill('  ')
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByText('The name has to be between 1 and 100 characters.')).toBeVisible()
    expect(sent(calls, 'POST /t/alpha/Units')).toHaveLength(0)

    await page.getByLabel('Name of the new unit').fill('Alpha')
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByText('There is a unit with that name already.')).toBeVisible()
  })

  test('one is renamed, and a new name that is taken is said in its row', async ({ page, goto, api }) => {
    const { state, routes } = someUnits()
    const calls = await api(routes)
    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Rename Bravo' }).click()
    await page.getByLabel('New name').fill('Bravo Platoon')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByText('Bravo Platoon', { exact: true })).toBeVisible()
    expect(sent(calls, 'PUT /t/alpha/Units/u2')).toEqual([{ name: 'Bravo Platoon' }])
    expect(state.units.find(u => u.id === 'u2')!.name).toBe('Bravo Platoon')

    await page.getByRole('button', { name: 'Rename Busy' }).click()
    await page.getByLabel('New name').fill('Alpha')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.locator('li', { has: page.getByLabel('New name') }).getByText('There is a unit with that name already.')).toBeVisible()
  })

  test('one that is in use says why it stays, and one that isn\'t is deleted', async ({ page, goto, api }) => {
    const { state, routes } = someUnits()
    await api(routes)
    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Delete Busy' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText('Move the people in it to another unit first')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Keep it' }).click()
    await expect(names(page)).toContainText(['Busy'])

    await page.getByRole('button', { name: 'Delete Alpha' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(names(page)).toHaveText(['Bravo', 'Busy'])
    expect(state.deleted).toEqual(['u1'])
  })

  test('none is said to be all right', async ({ page, goto, api }) => {
    await api(organization().routes)

    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await expect(page.getByText('There are no units yet.')).toBeVisible()
  })
})

test.describe('facilities', () => {
  const someFacilities = () => organization({
    units: [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }],
    facilities: [
      { id: 'f-hall', name: 'Hall', group: 'Indoor', availableToAll: true, unitIds: [] },
      { id: 'f-gym', name: 'Gym', group: 'Indoor', availableToAll: false, unitIds: ['u1'] },
      { id: 'f-busy', name: 'Range', group: null, availableToAll: false, unitIds: [] },
    ],
  })
  const row = (page: Page, name: string) => page.locator('li', { has: page.getByText(name, { exact: true }) })

  test('each says who can book it, under the kind it is, and those with none under other', async ({ page, goto, api }) => {
    await api(someFacilities().routes)

    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await expect(row(page, 'Hall')).toContainText('Everyone')
    await expect(row(page, 'Gym')).toContainText('Alpha')
    await expect(row(page, 'Range')).toContainText('Admins only')
    await expect(page.getByRole('heading', { name: 'Indoor' })).toHaveCount(1)
    await expect(page.getByRole('heading', { name: 'Other' })).toHaveCount(1)
  })

  test('one is added for the units it is given to, and listed under its kind', async ({ page, goto, api }) => {
    const calls = await api(someFacilities().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Add', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Name', { exact: true }).fill('Field')
    await dialog.getByLabel('Group').fill('Outdoor')
    // Units are not asked for while it is for everyone
    await expect(dialog.getByRole('combobox', { name: 'Units that can book it' })).toHaveCount(0)
    await dialog.getByRole('switch', { name: 'Everyone can book it' }).click()
    await dialog.getByText('No unit', { exact: true }).click()
    await page.getByRole('option', { name: 'Alpha' }).click()
    await page.getByRole('option', { name: 'Bravo' }).click()
    await page.keyboard.press('Escape')
    await dialog.getByRole('button', { name: 'Add', exact: true }).click()

    // While it closes, the page behind it is hidden from what is looked for by role
    await expect(dialog).toHaveCount(0)
    expect(sent(calls, 'POST /t/alpha/Facilities')).toEqual([expect.objectContaining({ name: 'Field', group: 'Outdoor', availableToAll: false, unitIds: ['u1', 'u2'] })])
    await expect(row(page, 'Field')).toContainText('Alpha, Bravo')
    await expect(page.getByRole('heading', { name: 'Outdoor' })).toHaveCount(1)
  })

  test('one is changed from what it is, and made for everyone is sent without units, which the API doesn\'t take with it', async ({ page, goto, api }) => {
    const calls = await api(someFacilities().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change Gym' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('Gym')
    await expect(dialog.getByLabel('Group')).toHaveValue('Indoor')
    await dialog.getByRole('switch', { name: 'Everyone can book it' }).click()
    await dialog.getByRole('button', { name: 'Save', exact: true }).click()

    await expect(row(page, 'Gym').getByText('Everyone')).toBeVisible()
    expect(sent(calls, 'PUT /t/alpha/Facilities/f-gym')).toEqual([expect.objectContaining({ availableToAll: true, unitIds: [] })])
  })

  test('a name that is taken is said by the name, and a group emptied is sent as none', async ({ page, goto, api }) => {
    const calls = await api(someFacilities().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change Hall' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Group').fill('')
    await dialog.getByLabel('Name', { exact: true }).fill('Gym')
    // Saved from the field rather than with the button: once a field has been left, the form looks at it again 300 ms after it was typed in,
    // which puts away what the API said of it when the API answers sooner than that, as it does here
    await dialog.getByLabel('Name', { exact: true }).press('Enter')
    await expect(dialog.getByText('There is a facility with that name already.')).toBeVisible()

    await dialog.getByLabel('Name', { exact: true }).fill('Hall B')
    await dialog.getByLabel('Name', { exact: true }).press('Enter')

    await expect(page.getByText('Hall B', { exact: true })).toBeVisible()
    expect(sent(calls, 'PUT /t/alpha/Facilities/f-hall').at(-1)).toMatchObject({ name: 'Hall B', group: null })
  })

  test('no name is said, and not sent', async ({ page, goto, api }) => {
    const calls = await api(someFacilities().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Change Hall' }).click()
    await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill('')
    await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click()

    await expect(page.getByText('The name has to be between 1 and 100 characters.')).toBeVisible()
    expect(sent(calls, 'PUT /t/alpha/Facilities/f-hall')).toHaveLength(0)
  })

  test('one that has been booked says why it stays, and one that hasn\'t is deleted', async ({ page, goto, api }) => {
    const { state, routes } = someFacilities()
    await api(routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Delete Range' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText('Something has been booked on it, so it can\'t be deleted.')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Keep it' }).click()
    await expect(row(page, 'Range')).toHaveCount(1)

    await page.getByRole('button', { name: 'Delete Gym' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText('Gym', { exact: true })).toHaveCount(0)
    expect(state.deleted).toEqual(['f-gym'])
  })

  test('with none, and no units to give one to, says so', async ({ page, goto, api }) => {
    await api(organization().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })
    await expect(page.getByText('Nothing can be booked until there is a facility.')).toBeVisible()

    await page.getByRole('button', { name: 'Add the first' }).click()
    await page.getByRole('dialog').getByRole('switch', { name: 'Everyone can book it' }).click()

    await expect(page.getByText('There are no units yet, so only admins can book it.')).toBeVisible()
  })
})

test.describe('as many as there can be', () => {
  const full = () => organization({
    units: [{ id: 'u1', name: 'Alpha' }],
    facilities: [{ id: 'f1', name: 'Hall', group: null, availableToAll: true, unitIds: [] }],
    unitLimit: 1,
    facilityLimit: 1,
  })

  test('a limit on facilities is said in the form, which stays for what was typed, and not the next time', async ({ page, goto, api }) => {
    await api(full().routes)
    await goto('/t/alpha/admin/facilities', { waitUntil: 'hydration' })

    await page.getByRole('button', { name: 'Add', exact: true }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Name', { exact: true }).fill('Gym')
    await dialog.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(dialog.getByText('An organisation can have 1 facilities.')).toBeVisible()
    await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('Gym')

    await dialog.getByRole('button', { name: 'Cancel' }).click()
    await expect(dialog).toHaveCount(0)
    await page.getByRole('button', { name: 'Add', exact: true }).click()

    await expect(dialog.getByLabel('Name', { exact: true })).toBeVisible()
    await expect(dialog.getByText('An organisation can have 1 facilities.')).toHaveCount(0)
  })

  test('a limit on units is said by the box', async ({ page, goto, api }) => {
    await api(full().routes)
    await goto('/t/alpha/admin/units', { waitUntil: 'hydration' })

    await page.getByLabel('Name of the new unit').fill('Bravo')
    await page.getByRole('button', { name: 'Add', exact: true }).click()

    await expect(page.getByText('An organisation can have 1 units.')).toBeVisible()
  })
})
