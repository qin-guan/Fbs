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

## Checks

```bash
pnpm lint
pnpm build                      # the build that is deployed until the switch
pnpm test:e2e                   # the pages for accounts in a browser, with the API answered by the test
pnpm exec playwright install chromium   # once, for the last
```

`pnpm test:e2e` builds for `test`, serves it, and runs `e2e/smoke.mjs`, which answers the API's calls itself. `SCREENSHOTS=<folder>` keeps pictures of the pages.

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
