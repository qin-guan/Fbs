# Facility Booking web app

A Nuxt 4 app for the API in `Fbs.WebApi`, whose pages are drawn in the browser, but for the home page, which is drawn when it is built (see
[The home page, and search engines](#the-home-page-and-search-engines)). Its API client is generated from the API's OpenAPI document with
[Kubb](https://kubb.dev): start the API, then `pnpm codegen` (or `OPENAPI_URL=<file or address> pnpm codegen`), and commit what it writes to `api/`.

## How people sign in

This is decided when the app is **built**, from the environment (see `nuxt.config.ts`):

| Build | Set | Sign in | Pages |
|---|---|---|---|
| `legacy` | nothing | phone number and a code on Telegram | the old pages: `/booking`, `/profile` and so on |
| `clerk` | `NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY` | Clerk, with the token sent to the API as a bearer | pages for accounts and organizations: `/sign-in`, `/orgs`, `/onboarding`, `/join/:token`, `/claim/:slug`, `/account`, `/t/:slug/...` |
| `test` | `NUXT_PUBLIC_AUTH_MODE=test` | none: always signed in, with a token the API would refuse | as `clerk`, for `pnpm test:e2e` |

The old pages are still what is deployed until the switch to Clerk, and each build only reaches its own pages: the others go back to `/`. What decides
where somebody goes once they are signed in is `GET /Me`: straight into their organization if they have one, to pick from if they have several, and to get started
if they have none. Everything that belongs to an organization is under `/t/:slug`, so what is cached for one can't be shown in another.

`@clerk/nuxt` is only added to a build with a key, and the app reads what Clerk says through `composables/session.ts` and `plugins/clerk-session.client.ts`, so
nothing else in the app, or in the download for the old pages, depends on it. Clerk's session token needs `email` and `name` as custom claims (see the
[Cutover 2 runbook](../docs/runbooks/cutover-2-accounts.md)).

## The home page, and search engines

Every page is drawn in the browser (`'/**': { ssr: false }` in `routeRules`), as there is no server to check a session on, but the home page, which is
drawn when the app is built (`'/': { prerender: true }`). So what it says, its metadata, and its structured data are in its HTML, for search engines and
link previews that don't run JavaScript. It is the same page for everyone: what depends on who is signed in (the buttons, in `composables/home.ts`, and
taking somebody signed in to their organization, `components/home/redirect.vue`) is only looked at once it is in the browser, after the page drawn there
has caught up with the HTML. Keep anything that reads the session, `localStorage` or the API on that page behind `useMounted` or `<ClientOnly>`, or it
won't match the HTML (and the build will call the API).

| What | Where |
|---|---|
| The name, title, description and the picture's alt text | `lib/site.ts` |
| What every page starts with: description, Open Graph, Twitter cards, icons, the manifest | `app.head` in `nuxt.config.ts` |
| The canonical link and `og:url` of each page, and `Page \| Book A Space` titles | `app.vue` |
| The home page's title, `robots`, and structured data (Organization, WebSite, WebPage, WebApplication, FAQPage) | `pages/index.vue` |
| `robots.txt` and `sitemap.xml`, written when the app is built | `server/routes/` |
| `X-Robots-Tag: noindex` for the pages behind a sign in, and keeping crawlers out of them | `privatePaths` in `lib/site.ts` |
| The picture link previews show (1200 by 630), the icons, and the manifest | `public/og-image.png`, `public/icon-*.png`, `public/site.webmanifest` |

The addresses given to search engines and previews start with `NUXT_PUBLIC_SITE_URL` (`https://bookaspace.app` when it isn't set), when the app is built:
set it for a build deployed anywhere else, such as staging. `e2e/home.spec.ts` checks what is served, as a crawler sees it.

## Pages of an organization

Everything under `/t/:slug`, in the organization's time zone (not the browser's: a day, or a slot on the half hour, is one where the facilities are).

| Address | For | What |
|---|---|---|
| `/t/:slug` | everyone in it | the bookings in a window of time, filtered by facility, and only mine |
| `/t/:slug/bookings/:id` | everyone in it | one booking, changed or cancelled by whoever may |
| `/t/:slug/book`, `/t/:slug/book/confirm` | everyone in it | pick a slot on the calendar, or several facilities and days with the builder, then confirm with a point of contact |
| `/t/:slug/timeline` | everyone in it | a row for each facility, along the days of a window |
| `/t/:slug/guide` | everyone in it | how to book, with the walkthrough of the booking page a press away |
| `/t/:slug/admin/settings` | admins | name, time zone, calling code, shortest booking, whether people wait to be let in, and turning off claiming from before accounts |
| `/t/:slug/admin/units`, `.../facilities` | admins | what people belong to, and what they can book |
| `/t/:slug/admin/members` | admins | letting in, turning away, changing, removing and adding by phone number |
| `/t/:slug/admin/invites` | admins | links to join with, shown once when they are made |
| `/t/:slug/admin/audit` | admins | the history: what has been done to the organization, the latest first, a page at a time |
| `/t/:slug/admin/settings` (lower down) | admins | a copy of the organization's data as a file, and deleting it: by typing its address, and taken back until it is deleted for good |
| `/account` | everyone | the name on their account (changed with Clerk) and what each organization calls them (changed there), a copy of what is kept about them as a file, and connecting Telegram |

An admin who opens an organization with nothing in it is shown a checklist (`components/tenant/setup-checklist.vue`). A member who reaches an admin page by its address
is told it is for admins (`components/tenant/admin-gate.vue`), and nothing is asked of the API for them.

Learning to book (`composables/booking-help.ts`, `components/booking-help/`, and `pages/t/[slug]/guide.vue`):

- The booking page walks through itself the first time it is opened, and again from its help menu: at one step it asks for a time to be dragged out on the real
  calendar, and goes on by itself once it is. It is driver.js, and the steps find the page by `#facility-type`, `#booking-calendar`, `#booking-summary`,
  `#book-multiple` and `#confirm-selection`, so keep those ids.
- It watches presses on the calendar (`lib/struggle.ts`), and offers help to whoever looks stuck: clicks or taps that pick nothing, a finger let go after holding
  without dragging (vue-cal waits half a second before a finger can drag), or somebody who has never picked a time not picking one for a while.
- A member new to booking is pointed to the guide from the bookings page (`components/tenant/welcome.vue`). What has been seen is kept in the browser
  (`fbs:booking-tour-done`, `fbs:picked-a-time`, `fbs:welcome-hidden`). The old build's booking page has the walkthrough and the help too.

Where the code is:

- `lib/slots.ts` and `lib/timeline.ts` are the time zone logic, and have no Nuxt in them. They are in `lib/` and not `utils/` **on purpose**: everything in `utils/` and
  `composables/` is auto-imported, and these names (`planSlots`, `findClashes`, `wholeDay` ...) are those of the pages of the old build, which would be handed the
  wrong ones. Import them by name.
- `composables/tenant.ts` has what every page of an organization asks of it (`useTenant`, the formatters for its zone, `invalidateUnder`).
- `composables/api.ts` reads what the API says when it refuses: `getErrorReasons`, `getErrorCodes`, and `getFieldErrors` for forms (put in with `UForm`'s `setErrors`).

## Checks

```bash
pnpm lint
pnpm test                       # the time zone logic, with no browser
pnpm build                      # the build that is deployed until the switch
pnpm test:e2e                   # the pages for accounts in a browser, with the API answered by the tests
pnpm exec playwright install chromium   # once, for the last
```

The browser tests use [Nuxt's test utils with the Playwright test runner](https://nuxt.com/docs/getting-started/testing#testing-with-playwright-test-runner).
`pnpm test:e2e` builds the app for `test` and serves it once (`webServer` in `playwright.config.ts`), and the tests in `e2e/*.spec.ts` go to it with
`goto(path, { waitUntil: 'hydration' })` from `@nuxt/test-utils/playwright`. Each test answers the API's calls itself with the `api` fixture in
`e2e/support.ts`, which also lists the calls the app made:

```ts
test('somebody in one organization goes straight in', async ({ page, goto, api }) => {
  const calls = await api({ 'GET /Me': () => json({ id: 'a', name: 'Sam', email: null, memberships: [/* ... */] }) })
  await goto('/', { waitUntil: 'hydration' })
  await expect(page).toHaveURL(/\/t\/alpha$/)
})
```

`pnpm test:e2e e2e/smoke.spec.ts` runs one file, `--ui` opens them in Playwright's UI, and a test that fails keeps a trace and a screenshot in
`test-results/` (`pnpm exec playwright show-trace <trace.zip>`).

There is a file of tests for each part: `smoke` (signing in, and making, joining and claiming an organization), `tenant` (the bookings), `booking` (making
them), `timeline`, and one for each of the admin pages. Those of the admin pages keep what they are told in a small fake of the API with its rules in it (the last
admin, names that are taken, and so on), and answer the paths with an id in them by pattern (`Routes` in `e2e/support.ts`).

Some of them put the browser in a time zone that isn't the organization's (`test.use({ timezoneId: 'America/Los_Angeles' })`, when it is in Singapore), and that
is the point: check what is drawn, and what is sent to the API, is by the organization's clock. When you write one, know that:

- `UForm` looks at a field again 300 ms after it is typed in, once the field has been left. A person can't press a button that soon after typing, but a test can,
  and the form is then a step behind: an error from before is still there, or what the API said of the field is put away when the form catches up, as the API
  answered by the test is quicker than that. Wait for the earlier error to go before pressing the button; and to see what the API says of a field, type in it and
  press `Enter` there, without leaving it first.
- While a modal closes, the page behind it is hidden from queries by role: wait for it to be gone (`await expect(dialog).toHaveCount(0)`) first.
- `USelectMenu` doesn't put its `aria-label` on anything a test can find: click its placeholder. `USelect` does.

---

# Nuxt Minimal Starter

Look at the [Nuxt documentation](https://nuxt.com/docs/getting-started/introduction) to learn more.

## Setup

Make sure to install dependencies:

```bash
# npm
npm install

# pnpm
pnpm install

# yarn
yarn install

# bun
bun install
```

## Development Server

Start the development server on `http://localhost:3000`:

```bash
# npm
npm run dev

# pnpm
pnpm dev

# yarn
yarn dev

# bun
bun run dev
```

## Production

Build the application for production:

```bash
# npm
npm run build

# pnpm
pnpm build

# yarn
yarn build

# bun
bun run build
```

Locally preview production build:

```bash
# npm
npm run preview

# pnpm
pnpm preview

# yarn
yarn preview

# bun
bun run preview
```

Check out the [deployment documentation](https://nuxt.com/docs/getting-started/deployment) for more information.
