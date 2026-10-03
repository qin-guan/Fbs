# Facility Booking web app

A Nuxt 4 single page app (`ssr: false`) for the API in `Fbs.WebApi`. Its API client is generated from the API's OpenAPI document with
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

## Pages of an organization

Everything under `/t/:slug`, in the organization's time zone (not the browser's: a day, or a slot on the half hour, is one where the facilities are).

| Address | For | What |
|---|---|---|
| `/t/:slug` | everyone in it | the bookings in a window of time, filtered by facility, and only mine |
| `/t/:slug/bookings/:id` | everyone in it | one booking, changed or cancelled by whoever may |
| `/t/:slug/book`, `/t/:slug/book/confirm` | everyone in it | pick a slot on the calendar, or several facilities and days with the builder, then confirm with a point of contact |
| `/t/:slug/timeline` | everyone in it | a row for each facility, along the days of a window |
| `/t/:slug/admin/settings` | admins | name, time zone, calling code, shortest booking, whether people wait to be let in, and turning off claiming from before accounts |
| `/t/:slug/admin/units`, `.../facilities` | admins | what people belong to, and what they can book |
| `/t/:slug/admin/members` | admins | letting in, turning away, changing, removing and adding by phone number |
| `/t/:slug/admin/invites` | admins | links to join with, shown once when they are made |
| `/t/:slug/admin/audit` | admins | the history: what has been done to the organization, the latest first, a page at a time |

An admin who opens an organization with nothing in it is shown a checklist (`components/tenant/setup-checklist.vue`). A member who reaches an admin page by its address
is told it is for admins (`components/tenant/admin-gate.vue`), and nothing is asked of the API for them.

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
pnpm test:e2e                   # the pages for accounts in a browser, with the API answered by the test
pnpm exec playwright install chromium   # once, for the last
```

`pnpm test:e2e` builds for `test`, serves it, and runs each of `e2e/*.mjs` named in `e2e/run.mjs`: `smoke` (signing in, and making, joining and claiming an
organization), `tenant` (the bookings), `booking` (making them), `timeline`, and one for each of the admin pages. They answer the API's calls themselves, and those of
the admin pages keep what they are told in a small fake of the API with its rules in it (the last admin, names that are taken, and so on). Add `--no-build` to run
them against the last build. `SCREENSHOTS=<folder>` keeps pictures of the pages.

Some of them put the browser in a time zone that isn't the organization's (Los Angeles, when it is in Singapore), and that is the point: check what is drawn, and
what is sent to the API, is by the organization's clock. When you write one, know that:

- `UForm` looks at a field as it is left, and a person can't press a button in the same instant they type, but a test can: leave the field (`press('Tab')`), or wait for
  the earlier error to go, before pressing the button.
- While a modal closes, the page behind it is hidden from queries by role: wait for it to be `detached` first.
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
