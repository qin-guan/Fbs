// Deleting an organization, being told that it is to be deleted and taking that back, and downloading copies of data. The API is kept by
// the test, so what is shown after a change is what the change made. Run by e2e/run.mjs.
import { readFileSync } from 'node:fs'
import { base, check, finish, json, noContent, problem, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = role => ({
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
function api({ role = 'Admin', tenantStatus = 'Active', exportLimited = false } = {}) {
  const state = { tenantStatus, deleteAfter: tenantStatus === 'PendingDeletion' ? deleteAfter() : null, asked: [] }
  const refusal = code => ({
    ...json({ title: 'Not available', status: 403, detail: 'This organisation is not available.', code }, 403),
    contentType: 'application/problem+json',
  })
  const usable = handler => () => (state.tenantStatus === 'PendingDeletion' ? refusal('pending-deletion') : state.tenantStatus === 'Suspended' ? refusal('unavailable') : handler())
  const table = {
    'GET /Me': () => json({
      id: 'a',
      name: 'Sam Lee',
      email: 'sam@example.com',
      memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam', tenantStatus: state.tenantStatus, deleteAfter: state.deleteAfter }],
    }),
    'GET /t/alpha': usable(() => json(org(role))),
    'GET /t/alpha/Settings': usable(() => json(settings)),
    'GET /t/alpha/Bookings': usable(() => json([])),
    'GET /t/alpha/Facilities/Bookable': usable(() => json([])),
    'GET /t/alpha/Facilities': usable(() => json([])),
    'GET /t/alpha/Units': usable(() => json([])),
    'GET /t/alpha/Invites': usable(() => json([])),
    'GET /t/alpha/Members': usable(() => json([])),
    'GET /t/alpha/Export': () => (exportLimited ? problem(429, [{ name: 'x', reason: 'slow down', code: 'rate-limited' }]) : json({ organization: { slug: 'alpha', name: 'Alpha Company' }, members: [{ displayName: 'CPT Sam', phone: '+6591234567' }], bookings: [] })),
    'GET /Me/Export': () => (exportLimited ? problem(429, [{ name: 'x', reason: 'slow down', code: 'rate-limited' }]) : json({ account: { name: 'Sam Lee', email: 'sam@example.com' }, memberships: [], bookings: [] })),
    'POST /Tenants/alpha/Deletion': (r) => {
      const body = JSON.parse(r.postData())
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

// 1. Deleting it, seeing that it is to be, and taking that back
{
  const { state, table } = api()
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByRole('button', { name: 'Delete this organization' }).click()
  const dialog = page.getByRole('dialog')
  const confirmButton = dialog.getByRole('button', { name: 'Delete the organization' })
  check('it can\'t be confirmed until its address is typed', await confirmButton.isDisabled())
  await shot(page, 'delete-organization')
  await dialog.getByLabel('The address of the organization').fill('alph')
  check('nor with some of it', await confirmButton.isDisabled())
  await dialog.getByLabel('The address of the organization').fill('alpha')
  check('with all of it it can', await confirmButton.isEnabled())
  await confirmButton.click()
  await page.waitForURL('**/orgs')
  check('what is sent is the address that was typed', state.asked.length === 1 && state.asked[0].confirm === 'alpha', JSON.stringify(state.asked))
  await page.getByText('To be deleted').first().waitFor()
  check('the list of organizations says it is to be deleted, and until when', (await page.getByText(/To be deleted, on .* at the earliest/).count()) === 1)
  await shot(page, 'orgs-to-be-deleted')

  // Going to it says why it can't be used, and an admin can restore it
  // The whole card is the link
  const href = await page.getByRole('link', { name: /Alpha Company/ }).getAttribute('href')
  check('it leads to where they are told why', href === '/t/alpha', href ?? '')
  await page.goto(`${base}${href}`)
  await page.getByText('This organization is to be deleted').waitFor()
  check('an admin is told, and can restore it', (await page.getByRole('button', { name: 'Restore it' }).count()) === 1 && (await page.getByText('but you can restore it').count()) === 1)
  await shot(page, 'to-be-deleted')
  await page.getByRole('button', { name: 'Restore it' }).click()
  await page.getByPlaceholder('Keyword search').waitFor()
  check('restored, it is used as it was', state.tenantStatus === 'Active' && log.some(l => l.key === 'DELETE /Tenants/alpha/Deletion'))
  await page.close()
}

// 2. What the API says when it isn't right
{
  const { table } = api()
  table['POST /Tenants/alpha/Deletion'] = () => problem(409, [{ name: 'generalErrors', reason: 'It can\'t be deleted now: it is to be deleted already, or has been suspended.', code: 'not-active' }])
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByRole('button', { name: 'Delete this organization' }).click()
  await page.getByRole('dialog').getByLabel('The address of the organization').fill('alpha')
  await page.getByRole('dialog').getByRole('button', { name: 'Delete the organization' }).click()
  await page.getByText('It can\'t be deleted now').waitFor()
  check('a refusal is said in the modal, which stays', (await page.getByRole('dialog').isVisible()) && page.url().includes('/admin/settings'))
  await page.close()
}

// 3. A member can't restore it, and is told who can
{
  const { table } = api({ role: 'Member', tenantStatus: 'PendingDeletion' })
  const page = await context.newPage()
  const log = await stub(page, table)
  await page.goto(`${base}/t/alpha`)
  await page.getByText('This organization is to be deleted').waitFor()
  // It once asked for the organization again each time it was shown, and it is shown because it can't be had: so it never stopped
  await page.waitForTimeout(1500)
  check('the organization is not asked for over and over', log.filter(l => l.key === 'GET /t/alpha').length <= 2, String(log.filter(l => l.key === 'GET /t/alpha').length))
  check('a member is not offered to restore it, and is told any admin can', (await page.getByRole('button', { name: 'Restore it' }).count()) === 0 && (await page.getByText('any admin of it can restore it').count()) === 1)
  await page.close()
}

// 4. Paused organizations are said to be
{
  const { table } = api({ tenantStatus: 'Suspended' })
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/orgs`)
  await page.getByText('Paused', { exact: true }).first().waitFor()
  check('an organization that has been paused is said to be', (await page.getByText('Paused', { exact: true }).count()) >= 1)
  await page.close()
}

// 5. Downloading copies
{
  const { table } = api()
  const page = await context.newPage()
  await stub(page, table)
  await page.goto(`${base}/account`)
  const [mine] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Download my data' }).click()])
  const mineText = readFileSync(await mine.path(), 'utf8')
  check('a person gets their data as a file', mine.suggestedFilename() === 'my-data.json' && JSON.parse(mineText).account.name === 'Sam Lee', mine.suggestedFilename())

  await page.goto(`${base}/t/alpha/admin/settings`)
  const [theirs] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Download a copy' }).click()])
  const theirText = readFileSync(await theirs.path(), 'utf8')
  check('an admin gets the organization\'s as a file named for it', theirs.suggestedFilename() === 'alpha-data.json' && JSON.parse(theirText).members[0].phone === '+6591234567', theirs.suggestedFilename())
  await page.close()

  const limited = api({ exportLimited: true })
  const busy = await context.newPage()
  await stub(busy, limited.table)
  await busy.goto(`${base}/account`)
  await busy.getByRole('button', { name: 'Download my data' }).click()
  await busy.getByText('Too many tries', { exact: true }).waitFor()
  check('being limited is said', true)
  await busy.close()
}

await finish(browser)
