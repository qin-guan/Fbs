// The settings of an organization, for its admins: what is shown, what is sent, and what the API says when it can't. Run by e2e/run.mjs.
import { base, check, finish, json, problem, shot, start, stub } from './support.mjs'

const { browser, context } = await start()

const org = (role = 'Admin') => ({
  slug: 'alpha',
  name: 'Alpha Company',
  timeZone: 'Asia/Singapore',
  defaultCountryCode: '65',
  slotMinutes: 30,
  me: { memberId: 'm1', displayName: 'CPT Sam', role, notificationScope: 'None', phone: null },
})
const settings = (extra = {}) => ({ name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, requireApproval: false, legacyClaimEnabled: true, ...extra })

const table = (role = 'Admin', extra = {}) => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Settings': () => json(settings()),
  ...extra,
})

const put = log => JSON.parse(log.filter(l => l.key === 'PUT /t/alpha/Settings').at(-1).body)

// 1. What is shown, and what is sent
{
  const page = await context.newPage()
  const log = await stub(page, table('Admin', {
    'PUT /t/alpha/Settings': r => json(settings(JSON.parse(r.postData()))),
  }))
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByLabel('Name', { exact: true }).waitFor()
  check('the settings are shown', (await page.getByLabel('Name', { exact: true }).inputValue()) === 'Alpha Company')
  check('the address is shown, and can\'t be changed', (await page.getByLabel('Address').isDisabled()) && (await page.getByLabel('Address').inputValue()) === '/t/alpha')
  check('nothing to save until something is changed', await page.getByRole('button', { name: 'Save' }).isDisabled())
  await shot(page, 'admin-settings')

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  check('saving is possible once something has', await page.getByRole('button', { name: 'Save' }).isEnabled())
  await page.getByRole('button', { name: 'Undo' }).click()
  check('undo puts it back', (await page.getByLabel('Name', { exact: true }).inputValue()) === 'Alpha Company' && (await page.getByRole('button', { name: 'Save' }).isDisabled()))

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  await page.getByRole('combobox', { name: 'Shortest booking' }).click()
  await page.getByRole('option', { name: '15 minutes' }).click()
  await page.getByRole('switch', { name: 'An admin lets people in' }).click()
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('Saved', { exact: true }).first().waitFor()
  const sent = put(log)
  check('what was changed is sent', sent.name === 'Alpha Coy' && sent.slotMinutes === 15 && sent.requireApproval === true && sent.timeZone === 'Asia/Singapore' && sent.defaultCountryCode === '65', JSON.stringify(sent))
  check('claiming is left as it is when it is not turned off', sent.legacyClaimEnabled === undefined, JSON.stringify(sent))
  check('it is saved, and there is nothing more to save', await page.getByRole('button', { name: 'Save' }).isDisabled())
  await page.close()
}

// 2. Turning claiming off, which is all that can be done to it
{
  const page = await context.newPage()
  const log = await stub(page, table('Admin', {
    'PUT /t/alpha/Settings': r => json(settings({ ...JSON.parse(r.postData()), legacyClaimEnabled: JSON.parse(r.postData()).legacyClaimEnabled ?? true })),
  }))
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByRole('switch', { name: 'Stop people claiming their place from before' }).click()
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('Saved', { exact: true }).first().waitFor()
  check('turning claiming off is sent', put(log).legacyClaimEnabled === false, JSON.stringify(put(log)))
  await page.close()

  const off = await context.newPage()
  await stub(off, table('Admin', { 'GET /t/alpha/Settings': () => json(settings({ legacyClaimEnabled: false })) }))
  await off.goto(`${base}/t/alpha/admin/settings`)
  await off.getByText('Claiming a place from before accounts is off.').waitFor()
  check('it can\'t be turned on again', (await off.getByRole('switch', { name: 'Stop people claiming their place from before' }).count()) === 0)
  await off.close()
}

// 3. What is wrong is said before anything is sent, and after
{
  const page = await context.newPage()
  const log = await stub(page, table('Admin', {
    'PUT /t/alpha/Settings': () => problem(400, [{ name: 'timeZone', reason: 'That is not a time zone this server knows.', code: 'invalid' }]),
  }))
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByLabel('Name', { exact: true }).fill('A')
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('The name has to be between 2 and 100 characters.').waitFor()
  check('a name that is too short is said, and not sent', log.filter(l => l.key === 'PUT /t/alpha/Settings').length === 0)

  await page.getByLabel('Name', { exact: true }).fill('Alpha Coy')
  // The form looks at what was typed as it is typed, and is not pressed on until it has
  await page.getByText('The name has to be between 2 and 100 characters.').waitFor({ state: 'detached' })
  await page.getByRole('button', { name: 'Save' }).click()
  await page.getByText('That is not a time zone this server knows.').waitFor()
  check('what the API says is shown by the field', true)
  await page.close()
}

// 4. Only for admins
{
  const page = await context.newPage()
  const log = await stub(page, table('Member'))
  await page.goto(`${base}/t/alpha/admin/settings`)
  await page.getByText('Only admins can do this').waitFor()
  check('a member is told it is for admins', true)
  check('and nothing of the settings is asked for', log.filter(l => l.key === 'GET /t/alpha/Settings').length === 0)
  check('no admin links in the sidebar', (await page.getByRole('link', { name: 'Settings' }).count()) === 0)
  await page.close()
}

await finish(browser)
