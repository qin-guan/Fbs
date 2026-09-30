// The people of an organization, for its admins: who is waiting, letting in and removing, adding by phone number and changing, and what the API
// says when it can't. The API is kept by the test, so what is shown again after a change is what the change made. Run by e2e/run.mjs.
import { base, check, finish, json, problem, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}
const units = [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }]

const member = (id, displayName, extra = {}) => ({ id, displayName, phone: null, unitId: null, role: 'Member', notificationScope: 'None', status: 'Active', hasAccount: true, ...extra })
const people = () => [
  member('m1', 'CPT Sam', { role: 'Admin', unitId: 'u1', phone: '+6591234567', notificationScope: 'All' }),
  member('m2', 'SGT Lee', { unitId: 'u2', phone: '+6598765432', notificationScope: 'Unit' }),
  member('m3', 'LTA Chan', { status: 'Pending', unitId: 'u2', notificationScope: 'Unit' }),
  member('m4', 'PTE Wong', { status: 'Pending' }),
  member('m5', 'CPL Ong', { status: 'Unclaimed', hasAccount: false, phone: '+6590000001' }),
  member('m6', 'REC Goh', { status: 'Removed' }),
]

/** An organization with its people kept, and answered from as the API does, with the rules it has. */
function api() {
  const state = { members: people(), requests: [] }
  const phoneOf = (typed) => {
    const t = typed.trim()
    if (!/^\+?[\d\s-]{7,}$/.test(t)) {
      return undefined
    }

    const digits = t.replace(/\D/g, '')
    return t.startsWith('+') ? `+${digits}` : `+65${digits}`
  }
  const lastAdmin = 'The organisation needs an admin, so the last one can\'t be made a member or removed.'
  const table = new Proxy({}, {
    get(_, key) {
      const [method, pathname] = String(key).split(' ')
      if (method === 'GET' && pathname === '/Me') {
        return () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role: 'Admin', status: 'Active', displayName: 'CPT Sam' }] })
      }
      if (method === 'GET' && pathname === '/t/alpha') {
        return () => json(org)
      }
      if (method === 'GET' && pathname === '/t/alpha/Units') {
        return () => json(units)
      }
      if (method === 'GET' && pathname === '/t/alpha/Members') {
        return (_r, url) => json(state.members.filter(m => url.searchParams.get('includeRemoved') === 'true' || m.status !== 'Removed'))
      }
      if (method === 'POST' && pathname === '/t/alpha/Members') {
        return (r) => {
          const body = JSON.parse(r.postData())
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
        }
      }
      const put = /^\/t\/alpha\/Members\/(.+)$/.exec(pathname ?? '')
      if (method === 'PUT' && put) {
        return (r) => {
          const body = JSON.parse(r.postData())
          const who = state.members.find(m => m.id === put[1])
          const status = body.membership === 'Removed' ? 'Removed' : who.hasAccount ? 'Active' : 'Unclaimed'
          const phone = body.phone?.trim() ? phoneOf(body.phone) : null
          if (body.phone?.trim() && !phone) {
            return problem(400, [{ name: 'phone', reason: 'That is not a phone number.', code: 'phone-invalid' }])
          }

          if (status === 'Unclaimed' && !phone) {
            return problem(400, [{ name: 'phone', reason: 'Someone who hasn\'t signed in yet is found by their phone number, so it can\'t be left out.', code: 'phone-needed' }])
          }

          const admins = state.members.filter(m => m.role === 'Admin' && m.status === 'Active')
          if (admins.length === 1 && admins[0].id === who.id && !(status === 'Active' && body.role === 'Admin')) {
            return problem(409, [{ name: 'generalErrors', reason: lastAdmin, code: 'last-admin' }])
          }

          if (phone && state.members.some(m => m.phone === phone && m.id !== who.id)) {
            return problem(409, [{ name: 'phone', reason: 'Someone in this organisation has that number already.', code: 'phone-taken' }])
          }

          Object.assign(who, { displayName: body.displayName, phone, unitId: body.unitId, role: body.role, notificationScope: body.notificationScope, status })
          return json(who)
        }
      }
    },
  })
  return { state, table }
}

const sent = (log, method, id) => log.filter(l => l.key === `${method} /t/alpha/Members${id ? `/${id}` : ''}`).map(l => JSON.parse(l.body))
// The list, and not the name of the admin in the sidebar
const row = (page, name) => page.locator('li[data-status]', { has: page.getByText(name, { exact: true }) })

// 1. Who is there
{
  const { table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await row(page, 'CPT Sam').waitFor()

  const order = await page.getByTestId('member-name').allInnerTexts()
  check('those waiting are first', order.slice(0, 2).join() === 'LTA Chan,PTE Wong', order.join())
  check('and said to be', (await page.getByText('2 people are waiting to be let in').count()) === 1)
  check('who was removed is not shown', (await row(page, 'REC Goh').count()) === 0)
  check('the phone number and unit are shown', (await row(page, 'CPT Sam').innerText()).includes('+6591234567 · Alpha'), await row(page, 'CPT Sam').innerText())
  check('admins are said to be', (await row(page, 'CPT Sam').getByText('Admin', { exact: true }).count()) === 1 && (await row(page, 'SGT Lee').getByText('Admin', { exact: true }).count()) === 0)
  check('who has not signed in is said to be', (await row(page, 'CPL Ong').getByText('Unclaimed').count()) === 1)
  await shot(page, 'admin-members')

  await page.getByRole('switch', { name: 'Show removed' }).click()
  await row(page, 'REC Goh').waitFor()
  check('removed ones are asked for, and can be let back in', log.filter(l => l.key === 'GET /t/alpha/Members').at(-1).query.includeRemoved === 'true' && (await page.getByRole('button', { name: 'Let REC Goh back in' }).count()) === 1)

  await page.getByLabel('Search people').fill('bravo')
  await row(page, 'CPT Sam').waitFor({ state: 'detached' })
  check('who is looked for is found by their unit', (await page.locator('li[data-status]').count()) === 2 && (await row(page, 'SGT Lee').count()) === 1 && (await row(page, 'LTA Chan').count()) === 1)
  await page.getByLabel('Search people').fill('zzz')
  await page.getByText('Nobody matches.').waitFor()
  check('nobody found is said', true)
  await page.close()
}

// 2. Letting in, turning away, and letting back in
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await page.getByRole('button', { name: 'Let LTA Chan in' }).click()
  await page.getByText('2 people are waiting').waitFor({ state: 'detached' })
  const body = sent(log, 'PUT', 'm3')[0]
  check('somebody is let in, with everything else as it was', body.membership === 'In' && body.displayName === 'LTA Chan' && body.role === 'Member' && body.unitId === 'u2' && body.notificationScope === 'Unit', JSON.stringify(body))
  check('and are then in', state.members.find(m => m.id === 'm3').status === 'Active' && (await row(page, 'LTA Chan').getByText('Active').count()) === 1)
  check('who is left waiting is said to be', (await page.getByText('Somebody is waiting to be let in').count()) === 1)

  await page.getByRole('button', { name: 'Turn PTE Wong away' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Turn away', exact: true }).click()
  await row(page, 'PTE Wong').waitFor({ state: 'detached' })
  check('somebody is turned away', sent(log, 'PUT', 'm4')[0].membership === 'Removed' && state.members.find(m => m.id === 'm4').status === 'Removed')

  await page.getByRole('switch', { name: 'Show removed' }).click()
  await page.getByRole('button', { name: 'Let PTE Wong back in' }).click()
  await row(page, 'PTE Wong').getByText('Active').waitFor()
  check('and can be let back in', sent(log, 'PUT', 'm4')[1].membership === 'In')
  await page.close()
}

// 3. Changing somebody
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await page.getByRole('button', { name: 'Change SGT Lee' }).click()
  const dialog = page.getByRole('dialog')
  check('what they are now is what is shown', (await dialog.getByLabel('Name', { exact: true }).inputValue()) === 'SGT Lee' && (await dialog.getByLabel('Phone number').inputValue()) === '+6598765432')

  await dialog.getByRole('combobox', { name: 'Role' }).click()
  await page.getByRole('option', { name: 'Admin' }).click()
  await dialog.getByRole('combobox', { name: 'Unit' }).click()
  await page.getByRole('option', { name: 'Alpha' }).click()
  await dialog.getByRole('combobox', { name: 'Tell them about' }).click()
  await page.getByRole('option', { name: 'Every booking' }).click()
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await dialog.waitFor({ state: 'detached' })
  const body = sent(log, 'PUT', 'm2')[0]
  check('what was changed is sent', body.role === 'Admin' && body.unitId === 'u1' && body.notificationScope === 'All' && body.membership === 'In' && body.phone === '+6598765432', JSON.stringify(body))
  check('and shown', (await row(page, 'SGT Lee').getByText('Admin', { exact: true }).count()) === 1)

  // A number somebody else has
  await page.getByRole('button', { name: 'Change SGT Lee' }).click()
  await dialog.getByLabel('Phone number').fill('+65 9123 4567')
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('Someone in this organisation has that number already.').waitFor()
  check('a number that is taken is said by the number', await dialog.isVisible())
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await dialog.waitFor({ state: 'detached' })

  // Somebody who has not signed in has to have a number
  await page.getByRole('button', { name: 'Change CPL Ong' }).click()
  await dialog.getByLabel('Phone number').fill('')
  const before = sent(log, 'PUT', 'm5').length
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await page.getByText('A phone number is needed').waitFor()
  check('a number is needed by somebody who has not signed in, and it is not sent', sent(log, 'PUT', 'm5').length === before)
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await dialog.waitFor({ state: 'detached' })

  // The last admin
  state.members.find(m => m.id === 'm2').role = 'Member'
  await page.getByRole('button', { name: 'Change CPT Sam' }).click()
  await dialog.getByRole('combobox', { name: 'Role' }).click()
  await page.getByRole('option', { name: 'Member' }).click()
  await dialog.getByRole('button', { name: 'Save', exact: true }).click()
  await dialog.getByText('The organisation needs an admin').waitFor()
  check('the last admin is said to be needed, in the form', await dialog.isVisible())
  await page.close()
}

// 4. Adding by phone number
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await page.getByRole('button', { name: 'Add', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()
  await dialog.getByText('The name has to be between 1 and 200 characters.').waitFor()
  check('a name and a number are needed, and nothing is sent without', (await dialog.getByText('A phone number is needed').count()) === 1 && sent(log, 'POST').length === 0)

  await dialog.getByLabel('Name', { exact: true }).fill('ME Tan')
  await dialog.getByLabel('Phone number').fill('abc')
  // The form looks at a field when it is left, and is not pressed on until it has
  await dialog.getByLabel('Phone number').press('Tab')
  await dialog.getByText('The name has to be between 1 and 200 characters.').waitFor({ state: 'detached' })
  await dialog.getByText('A phone number is needed').waitFor({ state: 'detached' })
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()
  await page.getByText('That is not a phone number.').waitFor()
  check('what is not a phone number is said by the API', true)

  await dialog.getByLabel('Phone number').fill('9123 0000')
  await page.getByText('That is not a phone number.').waitFor({ state: 'detached' })
  await dialog.getByRole('button', { name: 'Add', exact: true }).click()
  await dialog.waitFor({ state: 'detached' })
  const body = sent(log, 'POST').at(-1)
  check('somebody is added by their number', body.displayName === 'ME Tan' && body.phone === '9123 0000' && body.unitId === null && body.role === 'Member', JSON.stringify(body))
  check('and are listed, not yet signed in', (await row(page, 'ME Tan').getByText('Unclaimed').count()) === 1 && state.members.some(m => m.phone === '+6591230000'))
  await page.close()
}

// 5. Removing
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await page.getByRole('button', { name: 'Remove SGT Lee' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove', exact: true }).click()
  await row(page, 'SGT Lee').waitFor({ state: 'detached' })
  check('somebody is removed, and gone from the list', sent(log, 'PUT', 'm2')[0].membership === 'Removed' && state.members.find(m => m.id === 'm2').status === 'Removed')

  await page.getByRole('button', { name: 'Remove CPT Sam' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Remove', exact: true }).click()
  await page.getByRole('dialog').getByText('The organisation needs an admin').waitFor()
  check('the last admin can\'t be removed, and is said to be needed', (await row(page, 'CPT Sam').count()) === 1)
  await page.close()
}

// 6. Nobody
{
  const { state, table } = api()
  state.members = []
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/members`)
  await page.getByText('There is nobody yet.').waitFor()
  check('nobody is said, with what to do', true)
  await page.close()
}

await finish(browser)
