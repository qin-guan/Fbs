// Learning to book: the walkthrough of the booking page, help offered to whoever looks stuck picking a time, the page on how to book, and
// where a newcomer is pointed to it from.
import type { Page } from '@playwright/test'
import { expect, json, test } from './support'
import type { Table } from './support'

test.use({ viewport: { width: 1200, height: 900 } })

const member = { memberId: 'm1', displayName: 'CPT Sam', role: 'Member', notificationScope: 'None', phone: '91234567' }
const org = (role = 'Member') => ({ slug: 'alpha', name: 'Alpha Company', timeZone: 'Asia/Singapore', defaultCountryCode: '65', slotMinutes: 30, me: { ...member, role } })

const table = (role = 'Member'): Table => ({
  'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [{ tenantSlug: 'alpha', tenantName: 'Alpha Company', role, status: 'Active', displayName: 'CPT Sam' }] }),
  'GET /t/alpha': () => json(org(role)),
  'GET /t/alpha/Facilities/Bookable': () => json([{ id: 'f-hall', name: 'Hall', group: 'Indoor' }, { id: 'f-gym', name: 'Gym', group: 'Indoor' }]),
  'GET /t/alpha/Bookings': () => json([]),
  // An admin's setup checklist asks for these, and the organization is set up
  'GET /t/alpha/Facilities': () => json([{ id: 'f-hall', name: 'Hall', group: 'Indoor', openToAll: true, unitIds: [] }]),
  'GET /t/alpha/Units': () => json([]),
  'GET /t/alpha/Invites': () => json([{ id: 'i1' }]),
  'GET /t/alpha/Members': () => json([]),
})

const remember = (page: Page, items: Record<string, string>) => page.addInitScript((items) => {
  for (const [key, value] of Object.entries(items)) {
    localStorage.setItem(key, value)
  }
}, items)

/** Where to press in the Hall column, tomorrow at ten in the morning, where nothing has passed and nothing is booked. */
async function tomorrowAtTen(page: Page) {
  await page.locator('.vuecal__nav--next').first().click()
  await page.waitForTimeout(500)
  const tenOClock = page.locator('.vuecal__time-cell', { hasText: '10:00' }).first()
  await tenOClock.scrollIntoViewIfNeeded()
  await page.waitForTimeout(300)
  const cell = (await page.locator('.vuecal__schedule--cell').first().boundingBox())!
  const ten = (await tenOClock.boundingBox())!
  return { x: cell.x + cell.width / 2, y: ten.y + 5 }
}

async function drag(page: Page, from: { x: number, y: number }, pixels: number) {
  await page.mouse.move(from.x, from.y)
  await page.mouse.down()
  await page.mouse.move(from.x, from.y + pixels / 2, { steps: 6 })
  await page.mouse.move(from.x, from.y + pixels, { steps: 6 })
  await page.mouse.up()
}

const popover = (page: Page) => page.locator('.driver-popover')

test.describe('the walkthrough', () => {
  test('starts on a first visit, waits for a time to be dragged out, and isn\'t shown again once closed', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/book', { waitUntil: 'hydration' })

    await expect(popover(page)).toContainText('Facility types')
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(popover(page)).toContainText('Pick a day')
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(popover(page)).toContainText('Now pick a time')
    // It can be skipped, but goes on by itself once a time is picked, which it shows how to pick on the calendar
    await expect(page.getByRole('button', { name: 'Skip' })).toBeVisible()
    await expect(page.getByTestId('booking-ghost')).toBeVisible()

    // A click picks nothing, and it says why
    const ten = await tomorrowAtTen(page)
    await page.mouse.click(ten.x, ten.y)
    await expect(popover(page)).toContainText('A click doesn\'t pick a time')

    await drag(page, ten, 80)
    await expect(popover(page)).toContainText('Change it if you need to')
    await expect(page.getByTestId('booking-ghost')).toHaveCount(0)
    await expect(page.locator('#booking-summary')).toContainText('10:00 am – 11:00 am')

    // It can be stretched while it is pointed at, past what is pointed at, and again after that
    const stretch = async () => {
      const resizer = (await page.locator('.vuecal__event.new-booking .vuecal__event-resizer').boundingBox())!
      await drag(page, { x: resizer.x + resizer.width / 2, y: resizer.y + resizer.height / 2 }, 40)
    }
    await stretch()
    await expect(page.locator('#booking-summary')).toContainText('10:00 am – 11:30 am')
    await page.waitForTimeout(300)
    await stretch()
    await expect(page.locator('#booking-summary')).toContainText('10:00 am – 12:00 pm')
    await expect(popover(page)).toContainText('Change it if you need to')

    await page.getByRole('button', { name: 'Close' }).click()
    await expect(popover(page)).toHaveCount(0)
    // Nothing was booked, and the time picked is still there to go on with
    await expect(page.getByRole('button', { name: 'Confirm selection' })).toBeEnabled()

    await page.reload()
    await expect(page.locator('.vuecal__schedule--cell').first()).toBeVisible()
    await page.waitForTimeout(500)
    await expect(popover(page)).toHaveCount(0)
  })

  test('skipping picking a time skips what is about the time picked', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/book', { waitUntil: 'hydration' })

    await expect(popover(page)).toContainText('Facility types')
    await page.getByRole('button', { name: 'Next' }).click()
    await page.getByRole('button', { name: 'Next' }).click()
    await expect(popover(page)).toContainText('Now pick a time')
    await page.getByRole('button', { name: 'Skip' }).click()
    await expect(popover(page)).toContainText('Several at once')
    await page.getByRole('button', { name: 'Previous' }).click()
    await expect(popover(page)).toContainText('Now pick a time')
  })

  test('can be started again from the help menu', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true' })
    await goto('/t/alpha/book', { waitUntil: 'hydration' })
    await expect(page.locator('.vuecal__schedule--cell').first()).toBeVisible()
    await expect(popover(page)).toHaveCount(0)

    await page.getByRole('button', { name: 'Help' }).click()
    await page.getByRole('menuitem', { name: 'Show me around' }).click()

    await expect(popover(page)).toContainText('Facility types')
  })
})

test.describe('help for whoever looks stuck', () => {
  test('a newcomer whose clicks pick nothing is offered it, and shown on the calendar', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true' })
    await goto('/t/alpha/book', { waitUntil: 'hydration' })

    const ten = await tomorrowAtTen(page)
    await page.mouse.click(ten.x, ten.y)
    await page.waitForTimeout(500)
    await expect(page.getByTestId('booking-hint')).toHaveCount(0)
    await page.mouse.click(ten.x, ten.y + 40)

    const hint = page.getByTestId('booking-hint')
    await expect(hint).toContainText('Picking a time?')
    await expect(hint).toContainText('A click doesn\'t pick a time')

    await hint.getByRole('button', { name: 'Show me' }).click()
    await expect(hint).toHaveCount(0)
    await expect(popover(page)).toContainText('Now pick a time')
  })

  test('somebody who has picked times before is only offered it after a few more misses', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true', 'fbs:picked-a-time': 'true' })
    await goto('/t/alpha/book', { waitUntil: 'hydration' })

    const ten = await tomorrowAtTen(page)
    for (const offset of [0, 40]) {
      await page.mouse.click(ten.x, ten.y + offset)
      await page.waitForTimeout(450)
    }
    await expect(page.getByTestId('booking-hint')).toHaveCount(0)

    await page.mouse.click(ten.x, ten.y + 80)
    await expect(page.getByTestId('booking-hint')).toBeVisible()
  })

  test('can choose the times from a list instead, and goes once a time is picked', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true' })
    await goto('/t/alpha/book', { waitUntil: 'hydration' })

    const ten = await tomorrowAtTen(page)
    await page.mouse.click(ten.x, ten.y)
    await page.waitForTimeout(450)
    await page.mouse.click(ten.x, ten.y + 40)
    await page.getByTestId('booking-hint').getByRole('button', { name: 'Pick times from a list' }).click()
    await expect(page.getByText('Book multiple facilities or days')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page.getByTestId('booking-hint')).toHaveCount(0)

    // Offered again, then a drag that works takes it away
    await page.mouse.click(ten.x, ten.y)
    await page.waitForTimeout(450)
    await page.mouse.click(ten.x, ten.y + 40)
    await expect(page.getByTestId('booking-hint')).toBeVisible()
    await drag(page, ten, 80)
    await expect(page.getByTestId('booking-hint')).toHaveCount(0)
    expect(await page.evaluate(() => localStorage.getItem('fbs:picked-a-time'))).toBe('true')
  })
})

test.describe('how to book', () => {
  test('a member new to booking is pointed to it, and it leads to the walkthrough', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha', { waitUntil: 'hydration' })

    const welcome = page.getByTestId('welcome')
    await expect(welcome).toContainText('New here?')
    await welcome.getByRole('link', { name: 'How to book' }).click()

    await expect(page).toHaveURL(/\/t\/alpha\/guide$/)
    await expect(page.getByRole('heading', { name: 'Drag out the time' })).toBeVisible()
    // Shown for a mouse, and for a touch screen when asked
    await expect(page.getByText('keep the button down')).toBeVisible()
    await page.getByRole('button', { name: 'Touch screen' }).click()
    await expect(page.getByText('hold for half a second')).toBeVisible()

    await page.getByRole('link', { name: 'Walk me through it' }).first().click()
    await expect(page).toHaveURL(/\/t\/alpha\/book(\?|$)/)
    await expect(popover(page)).toContainText('Facility types')
  })

  test('leads to the walkthrough for whoever has been through it, once', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true' })
    await goto('/t/alpha/guide', { waitUntil: 'hydration' })

    await page.getByRole('link', { name: 'Walk me through it' }).first().click()
    await expect(popover(page)).toContainText('Facility types')
    await page.getByRole('button', { name: 'Close' }).click()
    await expect(popover(page)).toHaveCount(0)

    // It was asked for once: coming back to the page doesn't ask again
    await page.reload()
    await expect(page.locator('.vuecal__schedule--cell').first()).toBeVisible()
    await page.waitForTimeout(500)
    await expect(popover(page)).toHaveCount(0)
    // And the page kept what it puts in the address
    await expect(page).toHaveURL(/facility-type=Indoor/)
  })

  test('is in the sidebar', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha/timeline', { waitUntil: 'hydration' })

    await page.getByRole('link', { name: 'How to book' }).click()

    await expect(page).toHaveURL(/\/t\/alpha\/guide$/)
  })

  test('isn\'t pointed to once hidden', async ({ page, goto, api }) => {
    await api(table())
    await goto('/t/alpha', { waitUntil: 'hydration' })
    await page.getByTestId('welcome').getByRole('button', { name: 'Hide this' }).click()
    await expect(page.getByTestId('welcome')).toHaveCount(0)

    await page.reload()
    await expect(page.getByPlaceholder('Keyword search')).toBeVisible()
    await page.waitForTimeout(500)
    await expect(page.getByTestId('welcome')).toHaveCount(0)
  })

  test('isn\'t pointed to for whoever has been through the walkthrough', async ({ page, goto, api }) => {
    await api(table())
    await remember(page, { 'fbs:booking-tour-done': 'true' })
    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByPlaceholder('Keyword search')).toBeVisible()
    await page.waitForTimeout(500)
    await expect(page.getByTestId('welcome')).toHaveCount(0)
  })

  test('isn\'t pointed to for an admin, who has the setup checklist instead', async ({ page, goto, api }) => {
    await api(table('Admin'))
    await goto('/t/alpha', { waitUntil: 'hydration' })

    await expect(page.getByPlaceholder('Keyword search')).toBeVisible()
    await page.waitForTimeout(500)
    await expect(page.getByTestId('welcome')).toHaveCount(0)
  })
})
