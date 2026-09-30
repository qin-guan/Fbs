// The units and facilities of an organization, for its admins: adding, changing and deleting them, and what the API says when it
// can't. The API is kept by the test, so what is shown again after a change is what the change made. Run by e2e/run.mjs.
import { base, check, finish, json, noContent, problem, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}

/** A table that answers by pattern, for the paths that have an id in them. */
const routes = list => new Proxy({}, {
  get(_, key) {
    for (const [pattern, handler] of list) {
      const match = pattern.exec(String(key))
      if (match) {
        return (request, url) => handler(request, url, ...match.slice(1))
      }
    }
  },
})

/** An organization with what is in it kept, and answered from. */
function api({ units = [], facilities = [] } = {}) {
  const state = { units: [...units], facilities: [...facilities], deleted: [] }
  let made = 0
  const table = routes([
    [/^GET \/Me$/, () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] })],
    [/^GET \/t\/alpha$/, () => json(org)],
    [/^GET \/t\/alpha\/Units$/, () => json(state.units)],
    [/^POST \/t\/alpha\/Units$/, (r) => {
      const { name } = JSON.parse(r.postData())
      if (state.units.some(u => u.name === name)) {
        return problem(409, [{ name: 'name', reason: 'There is a unit with that name already.', code: 'unit-exists' }])
      }

      const unit = { id: `new-${++made}`, name }
      state.units.push(unit)
      return json(unit, 201)
    }],
    [/^PUT \/t\/alpha\/Units\/(.+)$/, (r, _u, id) => {
      const { name } = JSON.parse(r.postData())
      if (state.units.some(u => u.name === name && u.id !== id)) {
        return problem(409, [{ name: 'name', reason: 'There is a unit with that name already.', code: 'unit-exists' }])
      }

      const unit = state.units.find(u => u.id === id)
      unit.name = name
      return json(unit)
    }],
    [/^DELETE \/t\/alpha\/Units\/(.+)$/, (_r, _u, id) => {
      if (id === 'u-busy') {
        return problem(409, [{ name: 'generalErrors', reason: 'Move the people in it to another unit first, and a unit that bookings were made for can\'t be deleted.', code: 'unit-in-use' }])
      }

      state.units = state.units.filter(u => u.id !== id)
      state.deleted.push(id)
      return noContent()
    }],
    [/^GET \/t\/alpha\/Facilities$/, () => json(state.facilities)],
    [/^POST \/t\/alpha\/Facilities$/, (r) => {
      const body = JSON.parse(r.postData())
      if (state.facilities.some(f => f.name === body.name)) {
        return problem(409, [{ name: 'name', reason: 'There is a facility with that name already.', code: 'facility-exists' }])
      }

      const facility = { id: `new-${++made}`, ...body }
      state.facilities.push(facility)
      return json(facility, 201)
    }],
    [/^PUT \/t\/alpha\/Facilities\/(.+)$/, (r, _u, id) => {
      const body = JSON.parse(r.postData())
      if (state.facilities.some(f => f.name === body.name && f.id !== id)) {
        return problem(409, [{ name: 'name', reason: 'There is a facility with that name already.', code: 'facility-exists' }])
      }

      const facility = state.facilities.find(f => f.id === id)
      Object.assign(facility, body)
      return json(facility)
    }],
    [/^DELETE \/t\/alpha\/Facilities\/(.+)$/, (_r, _u, id) => {
      if (id === 'f-busy') {
        return problem(409, [{ name: 'generalErrors', reason: 'Something has been booked on it, so it can\'t be deleted.', code: 'facility-in-use' }])
      }

      state.facilities = state.facilities.filter(f => f.id !== id)
      state.deleted.push(id)
      return noContent()
    }],
  ])
  return { state, table }
}

const sent = (log, key) => log.filter(l => l.key === key).map(l => JSON.parse(l.body))

// 1. Units
{
  const { state, table } = api({ units: [{ id: 'u2', name: 'Bravo' }, { id: 'u1', name: 'Alpha' }, { id: 'u-busy', name: 'Busy' }] })
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/units`)
  await page.getByText('Alpha', { exact: true }).waitFor()
  const names = async () => (await page.locator('li span.font-medium').allInnerTexts())
  check('the units are listed by name', JSON.stringify(await names()) === '["Alpha","Bravo","Busy"]', JSON.stringify(await names()))
  await shot(page, 'admin-units')

  await page.getByLabel('Name of the new unit').fill('Charlie')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByText('Charlie', { exact: true }).waitFor()
  check('a unit is added, and listed', sent(log, 'POST /t/alpha/Units')[0]?.name === 'Charlie' && (await names()).includes('Charlie'))
  check('the box is empty for the next', (await page.getByLabel('Name of the new unit').inputValue()) === '')

  await page.getByLabel('Name of the new unit').fill('  ')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByText('The name has to be between 1 and 100 characters.').waitFor()
  check('nothing is not a name, and not sent', sent(log, 'POST /t/alpha/Units').length === 1)

  await page.getByLabel('Name of the new unit').fill('Alpha')
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByText('There is a unit with that name already.').waitFor()
  check('a name that is taken is said', true)

  await page.getByRole('button', { name: 'Rename Bravo' }).click()
  await page.getByLabel('New name').fill('Bravo Platoon')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('Bravo Platoon', { exact: true }).waitFor()
  check('a unit is renamed', sent(log, 'PUT /t/alpha/Units/u2')[0]?.name === 'Bravo Platoon' && state.units.find(u => u.id === 'u2').name === 'Bravo Platoon')

  await page.getByRole('button', { name: 'Rename Charlie' }).click()
  await page.getByLabel('New name').fill('Alpha')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  // In the row that is being renamed: what was said of the box above is still there
  await page.locator('li', { has: page.getByLabel('New name') }).getByText('There is a unit with that name already.').waitFor()
  check('a rename to a name that is taken is said', true)
  await page.getByRole('button', { name: 'Cancel' }).click()

  await page.getByRole('button', { name: 'Delete Busy' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByText('Move the people in it to another unit first').waitFor()
  check('a unit that is in use says why it stays', (await names()).includes('Busy'))
  await page.getByRole('dialog').getByRole('button', { name: 'Keep it' }).click()

  await page.getByRole('button', { name: 'Delete Charlie' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByText('Charlie', { exact: true }).waitFor({ state: 'detached' })
  check('a unit is deleted, and gone from the list', state.deleted.includes('new-1') && !(await names()).includes('Charlie'))
  await page.close()
}

// 2. Facilities
{
  const { state, table } = api({
    units: [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }],
    facilities: [
      { id: 'f-hall', name: 'Hall', group: 'Indoor', availableToAll: true, unitIds: [] },
      { id: 'f-gym', name: 'Gym', group: 'Indoor', availableToAll: false, unitIds: ['u1'] },
      { id: 'f-busy', name: 'Range', group: null, availableToAll: false, unitIds: [] },
    ],
  })
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/facilities`)
  await page.getByText('Hall', { exact: true }).waitFor()
  const row = name => page.locator('li', { has: page.getByText(name, { exact: true }) })
  check('who can book each is said', (await row('Hall').innerText()).includes('Everyone') && (await row('Gym').innerText()).includes('Alpha') && (await row('Range').innerText()).includes('Admins only'))
  check('they are under the kind they are, and those with none under other', (await page.getByRole('heading', { name: 'Indoor' }).count()) === 1 && (await page.getByRole('heading', { name: 'Other' }).count()) === 1)
  await shot(page, 'admin-facilities')

  // Adding one for two units
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Name', { exact: true }).fill('Field')
  await dialog.getByLabel('Group').fill('Outdoor')
  check('units are not asked for while it is for everyone', (await dialog.getByRole('combobox', { name: 'Units that can book it' }).count()) === 0)
  await dialog.getByRole('switch', { name: 'Everyone can book it' }).click()
  await dialog.getByText('No unit', { exact: true }).click()
  await page.getByRole('option', { name: 'Alpha' }).click()
  await page.getByRole('option', { name: 'Bravo' }).click()
  await page.keyboard.press('Escape')
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByText('Field', { exact: true }).waitFor()
  // While it closes, the page behind it is hidden from what is looked for by role
  await dialog.waitFor({ state: 'detached' })
  const added = sent(log, 'POST /t/alpha/Facilities')[0]
  check('a facility is added with its units', added.name === 'Field' && added.group === 'Outdoor' && added.availableToAll === false && added.unitIds.join() === 'u1,u2', JSON.stringify(added))
  check('and listed under its kind', (await row('Field').innerText()).includes('Alpha, Bravo') && (await page.getByRole('heading', { name: 'Outdoor' }).count()) === 1, `${await row('Field').innerText()} / ${await page.getByRole('heading', { name: 'Outdoor' }).count()}`)

  // Making it for everyone drops its units, which the API doesn't take with it
  await page.getByRole('button', { name: 'Change Field' }).click()
  check('a facility is changed from what it is', (await dialog.getByLabel('Name', { exact: true }).inputValue()) === 'Field' && (await dialog.getByLabel('Group').inputValue()) === 'Outdoor')
  await dialog.getByRole('switch', { name: 'Everyone can book it' }).click()
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await row('Field').getByText('Everyone').waitFor()
  const changed = sent(log, 'PUT /t/alpha/Facilities/new-1')[0]
  check('for everyone is sent without units', changed.availableToAll === true && changed.unitIds.length === 0, JSON.stringify(changed))

  // A group emptied is none, and a name that is taken is said by the name
  await page.getByRole('button', { name: 'Change Hall' }).click()
  await dialog.getByLabel('Group').fill('')
  await dialog.getByLabel('Name', { exact: true }).fill('Gym')
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('There is a facility with that name already.').waitFor()
  check('a facility name that is taken is said by the name', await dialog.isVisible())
  await dialog.getByLabel('Name', { exact: true }).fill('Hall B')
  await page.getByText('There is a facility with that name already.').waitFor({ state: 'detached' })
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('Hall B', { exact: true }).waitFor()
  check('no group is sent as none', sent(log, 'PUT /t/alpha/Facilities/f-hall').at(-1).group === null)

  await page.getByRole('button', { name: 'Change Hall B' }).click()
  await dialog.getByLabel('Name', { exact: true }).fill('')
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('The name has to be between 1 and 100 characters.').waitFor()
  check('no name is said, and not sent', sent(log, 'PUT /t/alpha/Facilities/f-hall').length === 2)
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await page.getByRole('button', { name: 'Delete Range' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByText('Something has been booked on it, so it can\'t be deleted.').waitFor()
  check('a facility that has been booked says why it stays', (await row('Range').count()) === 1)
  await page.getByRole('dialog').getByRole('button', { name: 'Keep it' }).click()

  await page.getByRole('button', { name: 'Delete Field' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Delete', exact: true }).click()
  await page.getByText('Field', { exact: true }).waitFor({ state: 'detached' })
  check('a facility is deleted, and gone from the list', state.deleted.includes('new-1'))
  await page.close()
}

// 3. Nothing yet, and with no units to give it to
{
  const { table } = api()
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/facilities`)
  await page.getByText('Nothing can be booked until there is a facility.').waitFor()
  await page.getByRole('button', { name: 'Add the first' }).click()
  await page.getByRole('dialog').getByRole('switch', { name: 'Everyone can book it' }).click()
  await page.getByText('There are no units yet, so only admins can book it.').waitFor()
  check('with no units, the way to give a facility to them is said', true)
  await page.close()

  const units = await context.newPage()
  await stub(units, table)
  await units.goto(`${base}/t/alpha/admin/units`)
  await units.getByText('There are no units yet.').waitFor()
  check('no units is said to be all right', true)
  await units.close()
}

await finish(browser)
