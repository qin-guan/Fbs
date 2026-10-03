// What an admin is shown when an organization has just been made: what is left to do, and that it goes when done or when they say so.
// Run by e2e/run.mjs.
import { base, check, finish, json, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = role => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const me = { id: 'm1', displayName: 'CPT Sam', phone: null, unitId: null, role: 'Admin', notificationScope: 'None', status: 'Active', hasAccount: true }
const facility = { id: 'f1', name: 'Hall', group: null, availableToAll: true, unitIds: [] }
const invite = { id: 'i1', role: 'Member', unitId: null, expiresAt: new Date(Date.now() + 5 * 86_400_000).toISOString(), maxUses: 10, uses: 0, status: 'Active', createdAt: new Date().toISOString() }

/** An organization with what is in it given, and what was asked of it noted. */
const table = ({ role = 'Admin', facilities = [], units = [], invites = [], members = [me] } = {}) => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Bookings': () => json([]),
  'GET /t/alpha/Facilities/Bookable': () => json(facilities),
  'GET /t/alpha/Facilities': () => json(facilities),
  'GET /t/alpha/Units': () => json(units),
  'GET /t/alpha/Invites': () => json(invites),
  'GET /t/alpha/Members': () => json(members),
})

const list = page => page.getByTestId('setup-checklist')
const step = (page, id) => list(page).locator(`li[data-step="${id}"]`)

// 1. Nothing done
{
  const page = await context.newPage()
  await stub(page, table())
  await page.goto(`${base}/t/alpha`)
  await list(page).waitFor()
  check('all three are still to do', (await list(page).locator('li[data-done="false"]').count()) === 3)
  check('each goes to where it is done', (await step(page, 'facilities').getByRole('link', { name: 'Add facilities' }).getAttribute('href')) === '/t/alpha/admin/facilities'
  && (await step(page, 'units').getByRole('link', { name: 'Add units' }).getAttribute('href')) === '/t/alpha/admin/units'
  && (await step(page, 'people').getByRole('link', { name: 'Make a link' }).getAttribute('href')) === '/t/alpha/admin/invites')
  check('units are said to be optional', (await step(page, 'units').innerText()).includes('Optional'))
  check('it is not said twice that there are no facilities', (await page.getByText('There are no facilities yet').count()) === 0)
  await shot(page, 'setup-checklist')
  await page.close()
}

// 2. Some done
{
  const page = await context.newPage()
  await stub(page, table({ facilities: [facility] }))
  await page.goto(`${base}/t/alpha`)
  await list(page).waitFor()
  check('what is done is shown to be, and has no button', (await step(page, 'facilities').getAttribute('data-done')) === 'true' && (await step(page, 'facilities').getByRole('link').count()) === 0)
  check('what is not is still to do', (await step(page, 'people').getAttribute('data-done')) === 'false' && (await step(page, 'units').getAttribute('data-done')) === 'false')
  await page.close()

  // Somebody else being in is inviting them, whichever way
  const joined = await context.newPage()
  await stub(joined, table({ facilities: [facility], members: [me, { ...me, id: 'm2', displayName: 'SGT Lee' }] }))
  await joined.goto(`${base}/t/alpha`)
  await joined.getByText('Add the first').count()
  await joined.getByTestId('setup-checklist').waitFor({ state: 'detached', timeout: 3000 }).catch(() => {})
  check('somebody else being in is the people being invited, and units are not needed', (await list(joined).count()) === 0)
  await joined.close()
}

// 3. Done
{
  const page = await context.newPage()
  await stub(page, table({ facilities: [facility], invites: [invite] }))
  await page.goto(`${base}/t/alpha`)
  await page.getByPlaceholder('Keyword search').waitFor()
  await page.waitForTimeout(500)
  check('it goes when it is done', (await list(page).count()) === 0)
  await page.close()
}

// 4. Hidden by them, for the organization, and kept
{
  const page = await context.newPage()
  await stub(page, table())
  await page.goto(`${base}/t/alpha`)
  await list(page).waitFor()
  await page.getByRole('button', { name: 'Hide this' }).click()
  await list(page).waitFor({ state: 'detached' })
  await page.reload()
  await page.getByPlaceholder('Keyword search').waitFor()
  await page.waitForTimeout(500)
  check('it can be hidden, and stays hidden', (await list(page).count()) === 0)
  await page.close()
}

// 5. Not for members, who are not asked for what is for admins
{
  const page = await context.newPage()
  const log = await stub(page, table({ role: 'Member' }))
  await page.goto(`${base}/t/alpha`)
  await page.getByText('You can\'t book anything yet').waitFor()
  check('a member is told to ask an admin', true)
  check('and is not shown the list, or asked for what is only for admins', (await list(page).count()) === 0
  && !log.some(l => ['GET /t/alpha/Invites', 'GET /t/alpha/Members', 'GET /t/alpha/Units', 'GET /t/alpha/Facilities'].includes(l.key)), JSON.stringify(log.map(l => l.key)))
  await page.close()
}

await finish(browser)
