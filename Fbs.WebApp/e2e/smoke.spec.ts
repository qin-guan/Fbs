// The pages for accounts: getting started, making, picking, joining and claiming an organization, and the account page.
import { expect, json, noContent, problem, test } from './support'

const membership = (tenantSlug: string, tenantName: string, role = 'Member', status = 'Active') => ({ tenantSlug, tenantName, role, status, displayName: 'CPT Sam' })
const me = (memberships: ReturnType<typeof membership>[] = [], name = 'Sam Lee', email: string | null = null) => ({ id: 'a', name, email, memberships })

test.describe('getting started', () => {
  test('somebody in no organization is sent to get started, and their token is sent as a bearer', async ({ page, goto, api }) => {
    const calls = await api({ 'GET /Me': () => json(me()) })

    await goto('/', { waitUntil: 'hydration' })

    await expect(page).toHaveURL(/\/onboarding$/)
    expect(calls).toContainEqual(expect.objectContaining({ key: 'GET /Me', auth: 'Bearer test-token' }))
  })

  test('the address follows the name', async ({ page, goto, api }) => {
    await api({ 'GET /Me': () => json(me()) })
    await goto('/onboarding', { waitUntil: 'hydration' })

    await page.getByLabel('Name').first().fill('Alpha Company')

    await expect(page.getByPlaceholder('alpha-company')).toHaveValue('alpha-company')
  })

  test('making an organization says when the address is taken, and goes into it once made', async ({ page, goto, api }) => {
    const calls = await api({
      'GET /Me': () => json(me()),
      'POST /Tenants': (request) => {
        const body = request.postDataJSON()
        return body.slug === 'taken'
          ? problem(409, [{ name: 'slug', reason: 'That address is taken.', code: 'slug-taken' }])
          : json({ slug: body.slug, name: body.name, timeZone: body.timeZone }, 201)
      },
    })
    await goto('/onboarding', { waitUntil: 'hydration' })

    await page.getByLabel('Name').first().fill('Taken One')
    await page.getByPlaceholder('alpha-company').fill('taken')
    await page.getByRole('button', { name: 'Make organization' }).click()
    await expect(page.getByText('That address is taken')).toBeVisible()

    await page.getByPlaceholder('alpha-company').fill('bravo-company')
    // The message going away moves the button, so this submits from the field rather than chasing it
    await page.getByPlaceholder('alpha-company').press('Enter')

    await expect(page).toHaveURL(/\/t\/bravo-company$/)
    const sent = calls.filter(c => c.key === 'POST /Tenants').at(-1)?.body
    expect(sent).toMatchObject({ name: 'Taken One', slug: 'bravo-company', timeZone: expect.any(String), defaultCountryCode: expect.stringMatching(/^\d+$/) })
  })
})

test.describe('picking an organization', () => {
  test('somebody in one goes straight in', async ({ page, goto, api }) => {
    await api({ 'GET /Me': () => json(me([membership('alpha', 'Alpha Company', 'Admin')], 'Sam')) })

    await goto('/', { waitUntil: 'hydration' })

    await expect(page).toHaveURL(/\/t\/alpha$/)
  })

  test('somebody in several picks one, and one they are waiting for is marked', async ({ page, goto, api }) => {
    await api({
      'GET /Me': () => json(me([
        membership('alpha', 'Alpha Company', 'Admin'),
        membership('bravo', 'Bravo Club'),
        membership('charlie', 'Charlie Team', 'Member', 'Pending'),
      ], 'Sam')),
    })

    await goto('/', { waitUntil: 'hydration' })

    await expect(page).toHaveURL(/\/orgs$/)
    await expect(page.getByText('Waiting for approval').first()).toBeVisible()
  })
})

test.describe('joining with a link', () => {
  const token = 'goodtokengoodtokengoodtoken'

  test('asks to join with the name of the account, and says they are waiting for approval', async ({ page, goto, api }) => {
    const calls = await api({
      'GET /Me': () => json(me()),
      [`GET /Invites/${token}`]: () => json({ organizationName: 'Alpha Company', requiresApproval: true }),
      [`POST /Invites/${token}/Accept`]: () => json({ slug: 'alpha', organizationName: 'Alpha Company', status: 'Pending' }),
    })
    await goto(`/join/${token}`, { waitUntil: 'hydration' })

    await expect(page.getByText('Join Alpha Company')).toBeVisible()
    await expect(page.getByLabel('Your name')).toHaveValue('Sam Lee')
    await page.getByRole('button', { name: 'Ask to join' }).click()

    await expect(page.getByText('You asked to join Alpha Company')).toBeVisible()
    expect(calls.find(c => c.key === `POST /Invites/${token}/Accept`)?.body).toMatchObject({ displayName: 'Sam Lee' })
  })

  test('a link that doesn\'t work says so', async ({ page, goto, api }) => {
    await api({ 'GET /Me': () => json(me()) })

    await goto('/join/badtokenbadtokenbadtokenbad', { waitUntil: 'hydration' })

    await expect(page.getByText('This link doesn\'t work')).toBeVisible()
  })

  test('a link pasted when getting started goes to it', async ({ page, goto, api }) => {
    await api({ 'GET /Me': () => json(me()) })
    await goto('/onboarding', { waitUntil: 'hydration' })

    await page.getByLabel('Link to join with').fill(`https://app.example/join/${token}`)
    await page.getByRole('button', { name: 'Join', exact: true }).click()

    await expect(page).toHaveURL(new RegExp(`/join/${token}$`))
  })
})

test.describe('claiming a member imported from the old version', () => {
  test('shows the Telegram link to open, and goes in by itself once claimed', async ({ page, goto, api }) => {
    let claimed = false
    await api({
      'GET /Me': () => json(me(claimed ? [membership('3sib', '3SIB')] : [], 'Sam')),
      'GET /Claims/3sib': () => json({ organizationName: '3SIB' }),
      'POST /Claims/3sib/Start': () => json({ url: 'https://t.me/fbs_bot?start=claim_abc', expiresAt: new Date(Date.now() + 600_000).toISOString() }),
    })
    await goto('/claim/3sib', { waitUntil: 'hydration' })

    await expect(page.getByText('Take over your place in 3SIB')).toBeVisible()
    await page.getByRole('button', { name: 'Get my link' }).click()
    await expect(page.getByRole('link', { name: 'Open in Telegram' })).toHaveAttribute('href', 'https://t.me/fbs_bot?start=claim_abc')

    // As the bot would, once the link is opened in Telegram
    claimed = true
    await expect(page).toHaveURL(/\/t\/3sib$/)
  })

  test('says when there is nothing to claim', async ({ page, goto, api }) => {
    await api({ 'GET /Me': () => json(me()) })

    await goto('/claim/other', { waitUntil: 'hydration' })

    await expect(page.getByText('There is nothing to take over here')).toBeVisible()
  })
})

test('the account page connects and disconnects Telegram', async ({ page, goto, api }) => {
  let linked = false
  await api({
    'GET /Me': () => json(me([], 'Sam Lee', 'sam@example.com')),
    'GET /Me/Telegram': () => json({ linked, linkedAt: linked ? new Date().toISOString() : null }),
    'POST /Me/Telegram/Link': () => json({ url: 'https://t.me/fbs_bot?start=abc', expiresAt: new Date(Date.now() + 600_000).toISOString() }),
    'DELETE /Me/Telegram': () => {
      linked = false
      return noContent()
    },
  })
  await goto('/account', { waitUntil: 'hydration' })

  await expect(page.getByText('sam@example.com').or(page.locator('input[value="sam@example.com"]')).first()).toBeVisible()
  await page.getByRole('button', { name: 'Connect Telegram' }).click()
  await expect(page.getByText('Press Start in Telegram')).toBeVisible()

  // As the bot would, once Start is pressed
  linked = true
  await expect(page.getByText('Connected').first()).toBeVisible()

  await page.getByRole('button', { name: 'Disconnect' }).click()
  await expect(page.getByRole('button', { name: 'Connect Telegram' })).toBeVisible()
})

test('the old phone number pages can\'t be reached', async ({ page, goto, api }) => {
  await api({ 'GET /Me': () => json(me()) })

  await goto('/booking', { waitUntil: 'hydration' })

  await expect(page).toHaveURL(/\/onboarding$/)
})
