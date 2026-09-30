// The invite links of an organization, for its admins: making one, which is the only time the link is known, and stopping one. The API is
// kept by the test, so what is listed again after a change is what the change made. Run by e2e/run.mjs.
import { base, check, finish, json, noContent, problem, shot, start, stub } from './support.mjs'

const { browser, context: ordinary } = await start()
await ordinary.close()
// The link is copied to the clipboard, which a page is only let read and write if it has been given the right to
const context = await browser.newContext({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true, permissions: ['clipboard-read', 'clipboard-write'] })

const org = {
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role: 'Admin', notificationScope: 'None', phone: null },
}
const units = [{ id: 'u1', name: 'Alpha' }, { id: 'u2', name: 'Bravo' }]
const day = offset => new Date(Date.now() + offset * 86_400_000).toISOString()
const invite = (id, status, extra = {}) => ({ id, role: 'Member', unitId: null, expiresAt: day(5), maxUses: 10, uses: 0, status, createdAt: day(-2), ...extra })
const token = 'Kq3xT9mZ0pLw2VbN7sRfYc4uHdEa8JgXoI1tA5nBvMk'

/** An organization with its links kept, and answered from as the API does, with the limit it has. */
function api({ invites = [], requireApproval = false, limit = 5 } = {}) {
  const state = { invites: [...invites], requireApproval }
  let made = 0
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
      if (method === 'GET' && pathname === '/t/alpha/Settings') {
        return () => json({ name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, requireApproval: state.requireApproval, legacyClaimEnabled: false })
      }
      if (method === 'GET' && pathname === '/t/alpha/Invites') {
        return () => json(state.invites)
      }
      if (method === 'POST' && pathname === '/t/alpha/Invites') {
        return (r) => {
          const body = JSON.parse(r.postData())
          if (state.invites.filter(i => i.status === 'Active').length >= limit) {
            return problem(403, [{ name: 'generalErrors', reason: `There can be ${limit} invite links at a time. Revoke one that isn't needed.`, code: 'invite-limit' }])
          }

          const made1 = invite(`new-${++made}`, 'Active', { role: body.role, unitId: body.unitId, maxUses: body.maxUses, expiresAt: day(body.expiresInDays), createdAt: day(0) })
          state.invites.unshift(made1)
          return json({ ...made1, token }, 201)
        }
      }
      const del = /^\/t\/alpha\/Invites\/(.+)$/.exec(pathname ?? '')
      if (method === 'DELETE' && del) {
        return () => {
          state.invites.find(i => i.id === del[1]).status = 'Revoked'
          return noContent()
        }
      }
    },
  })
  return { state, table }
}

const sent = (log, method) => log.filter(l => l.key === `${method} /t/alpha/Invites`).map(l => JSON.parse(l.body))
const item = (page, status) => page.locator(`li[data-status="${status}"]`)

// 1. What was made
{
  const { table } = api({
    invites: [
      invite('i1', 'Active', { uses: 3, unitId: 'u2' }),
      invite('i2', 'Expired', { expiresAt: day(-1) }),
      invite('i3', 'Revoked', { role: 'Admin' }),
      invite('i4', 'UsedUp', { uses: 10 }),
    ],
    requireApproval: true,
  })
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/invites`)
  await item(page, 'Active').waitFor()
  check('each link says how it is', (await item(page, 'Active').innerText()).includes('Works') && (await item(page, 'Expired').innerText()).includes('Ran out of time') && (await item(page, 'Revoked').innerText()).includes('Stopped') && (await item(page, 'UsedUp').innerText()).includes('Used up'))
  check('how many joined, and the unit, are said', (await item(page, 'Active').innerText()).includes('3 of 10 joined') && (await item(page, 'Active').innerText()).includes('Member, Bravo'), await item(page, 'Active').innerText())
  check('a link that was for an admin says so', (await item(page, 'Revoked').innerText()).includes('Admin'))
  check('only one that works can be stopped', (await page.getByRole('button', { name: /^Stop the link made/ }).count()) === 1 && (await item(page, 'Active').getByRole('button', { name: /^Stop/ }).count()) === 1)
  check('that people wait to be let in is said, as it is set', (await page.getByText('They then wait for you to let them in.').count()) === 1)
  await shot(page, 'admin-invites')
  await page.close()

  const open = api({ requireApproval: false })
  const other = await context.newPage()
  await stub(other, open.table)
  await other.goto(`${base}/t/alpha/admin/invites`)
  await other.getByText('There are none yet.').waitFor()
  check('none is said, and that they are in at once, as it is set', (await other.getByText('They are in at once.').count()) === 1)
  await other.close()
}

// 2. Making one, and its link
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/invites`)
  await page.getByRole('button', { name: 'Make link' }).click()
  await page.getByTestId('new-link').waitFor()
  const body = sent(log, 'POST')[0]
  check('what is sent is what is asked for, from the start', body.role === 'Member' && body.unitId === null && body.expiresInDays === 7 && body.maxUses === 10, JSON.stringify(body))

  const link = await page.getByLabel('The link', { exact: true }).inputValue()
  check('the link is where people join, with the token', link === `${base}/join/${token}`, link)

  await page.getByRole('button', { name: 'Copy' }).click()
  await page.getByRole('button', { name: 'Copied' }).waitFor()
  check('it is copied', (await page.evaluate(() => navigator.clipboard.readText())) === link)
  check('it is one that is listed', (await item(page, 'Active').count()) === 1 && state.invites.length === 1)
  check('what it is said to be', (await page.getByTestId('new-link').innerText()).includes('Joins as member. Works until'))

  await page.reload()
  await item(page, 'Active').waitFor()
  check('and is not shown again', (await page.getByTestId('new-link').count()) === 0 && (await page.getByText(token).count()) === 0)
  await page.close()
}

// 3. Another kind, and what is not allowed
{
  const { table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/invites`)
  await page.getByRole('combobox', { name: 'They join as' }).click()
  await page.getByRole('option', { name: 'Admin' }).click()
  await page.getByRole('combobox', { name: 'In the unit' }).click()
  await page.getByRole('option', { name: 'Bravo' }).click()
  await page.getByLabel('Works for (days)').fill('3')
  await page.getByLabel('People who can join').fill('1')
  await page.getByRole('button', { name: 'Make link' }).click()
  await page.getByTestId('new-link').waitFor()
  const body = sent(log, 'POST')[0]
  check('an admin, in a unit, for a time, for a number of people', body.role === 'Admin' && body.unitId === 'u2' && body.expiresInDays === 3 && body.maxUses === 1, JSON.stringify(body))
  check('and it says so', (await page.getByTestId('new-link').innerText()).includes('Joins as admin, in Bravo.') && (await page.getByTestId('new-link').innerText()).includes('for up to 1 person.'), await page.getByTestId('new-link').innerText())
  await page.close()

  const bad = await context.newPage()
  const badLog = await stub(bad, api().table)
  await bad.goto(`${base}/t/alpha/admin/invites`)
  await bad.getByLabel('Works for (days)').fill('31')
  await bad.getByLabel('People who can join').fill('0')
  await bad.getByRole('button', { name: 'Make link' }).click()
  await bad.getByText('It can work for 1 to 30 days.').waitFor()
  check('a time and a number of people that are not allowed are said, and not sent', (await bad.getByText('Between 1 and 100 people can join with it.').count()) === 1 && sent(badLog, 'POST').length === 0)
  await bad.close()
}

// 4. As many as there can be
{
  const { table } = api({ invites: [invite('i1', 'Active'), invite('i2', 'Active')], limit: 2 })
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/invites`)
  await page.getByRole('button', { name: 'Make link' }).click()
  await page.getByText('There can be 2 invite links at a time.').waitFor()
  check('the limit is said, and there is no new link', (await page.getByTestId('new-link').count()) === 0 && (await page.locator('li[data-status]').count()) === 2)

  await item(page, 'Active').first().getByRole('button', { name: /^Stop/ }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Stop the link' }).click()
  await item(page, 'Revoked').waitFor()
  check('a link is stopped, and is then said to be', (await item(page, 'Active').count()) === 1)
  await page.getByRole('button', { name: 'Make link' }).click()
  await page.getByTestId('new-link').waitFor()
  check('and there is room for another', true)
  await page.close()
}

await finish(browser)
