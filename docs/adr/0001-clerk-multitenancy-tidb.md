# ADR 0001: Clerk login, DB-backed multi-tenancy, TiDB storage

- Status: Accepted (roadmap in progress, see [Delivery](#delivery))
- Scope: `Fbs.WebApi`, `Fbs.WebApp`, `Fbs.AppHost`, deployment

## Context

Fbs is a single-organisation (3SIB) facility booking system.

- **Storage.** Users, Facilities, Nominal Roll and OTPs are Google Sheet tabs. Bookings are
  MemoryPack blobs inside Google Calendar events (`extendedProperties.shared.Data`), mirrored into
  an in-memory `BookingCache` via sync tokens. A second "carbon copy" calendar is the
  human-readable one. A process-local `SemaphoreSlim` (`BookingWriteLock`) serialises overlap
  checks, so the API can only run as one instance.
- **Identity.** A phone number is both the identity and the foreign key (`Booking.UserPhone`).
  Login is a Telegram OTP followed by a cookie. "Unit" is a free-text string that drives facility
  access, cancel rights and notification groups.
- **Tenancy.** 3SIB is hard-coded in the UI, bot copy, CORS origins, `+65` phone masks, `en-SG`
  and the server `TZ`.
- **Operations.** Sheets are the admin tool: unit S3s whitelist users and edit facilities there.

We want Clerk for login, Telegram kept only as a notification channel, multiple self-serve
organisations, a real database (SqlSugar + TiDB, following `GeeksHacking/portal`), and Google
Calendar sync as an option on top.

## Decisions

1. **Tenants live in our database.** Clerk stores users and handles login only. We do not use
   Clerk Organizations. Membership, roles, invites and units are ours. Consequences: no Clerk
   B2B add-on, no 20-member org cap, no `o` claim, no `pending` sessions.
2. **Self-serve tenant creation.** Any signed-in user can create an organisation and becomes its
   admin. Abuse controls are described below.
3. **Storage is TiDB via SqlSugar**, using portal's conventions (singleton `SqlSugarScope`, UTC
   handling, Guid PKs, `[SugarIndex]`, a `DbMigrator` with `diff` / `apply`, startup schema
   validation).
4. **The database is the source of truth.** Google Calendar becomes an optional, per-tenant,
   one-way (outbound) projection driven by a transactional outbox. Manual edits in the calendar
   are not read back.
5. **Telegram is a per-user notification channel**, linked with a one-time deep-link token.
   Notification preferences live on the tenant membership.
6. **Booking creator is immutable.** `BookedByMemberId` never changes and edits set
   `UpdatedByMemberId`. PoC (`PocName`, `PocPhone`) is stored per booking as a snapshot, not a
   foreign key.
7. **Deployed as a Docker container on Coolify**, with TiDB as the database.
8. **Two production cutovers: storage first, identity second.** Each is small and reversible.

## Architecture

```
Browser: Nuxt SPA + Clerk JS
   | Authorization: Bearer <Clerk JWT: sub, email, name, azp>
   v
Fbs.WebApi (.NET 10, FastEndpoints)
   |- JwtBearer (Clerk JWKS) -> user; /t/{slug}/... group resolves tenant + membership
   |- BookingService: tx -> lock facilities -> overlap check (current read) -> insert + outbox
   |- POST /webhooks/clerk (user.deleted) | POST /bot (secret token)
   '- hosted: OutboxDispatcher -> Telegram notifier | Calendar sync worker
   v
TiDB (SqlSugar, TenantId on every tenant-owned row)
   Fbs.DbMigrator: diff | apply | import-legacy | verify-legacy
   Google Calendar (optional, per tenant, one-way)   Telegram (per-user link)
```

### Authentication (Clerk)

- The API validates Clerk session JWTs with `JwtBearer` against the Clerk JWKS: `iss`, `exp`/`nbf`,
  and `azp` against an allow-list of app origins. Tokens are sent as `Authorization: Bearer`, so
  cookie auth, `AllowCredentials` and the Data Protection key volume go away.
- Add `email` and `name` custom session claims so first-sight provisioning needs no Backend API
  call. The `User` row is upserted from claims on first request.
- Clerk config: Organizations off, sign-up Open, email verification required, disposable-email
  blocking and bot protection on.
- The only webhook is `user.deleted` (anonymise the user, remove their memberships and Telegram
  link). It is idempotent, so no dedupe table.
- Web: `@clerk/nuxt` (Nuxt >= 4) with `skipServerMiddleware: true`, since the app is `ssr: false`.
  A Kubb `interceptors.request` hook attaches the token.

### Tenancy

- Shared database and schema. `TenantId` is on every tenant-owned row and unique indexes lead with
  it.
- The tenant is selected by the URL: tenant-scoped endpoints live under `/t/{slug}/...` (a
  FastEndpoints endpoint group). A group pre-processor resolves the slug and the caller's
  `TenantMember` row, and returns 404 (not 403) to non-members so slugs cannot be enumerated. The
  SPA mirrors this as `/t/:slug/...`, so Vue Query keys include the slug and caches cannot bleed
  across tenants.
- Outside the group: `/Me`, `/Tenants`, `/Invites/{token}`, `/Telegram`, `/webhooks/clerk`,
  `/bot`, and the legacy claim endpoints.
- Roles are `Admin` and `Member`, stored in the DB and checked per request (lookup cached 10-30 s
  at most). The last admin cannot be demoted or removed.
- Scoping is enforced in one place: a SqlSugar `QueryFilter` on an `ITenantScoped` marker plus
  stamping `TenantId` on insert. Every endpoint needs a cross-tenant negative test. Fallback if
  filters do not re-evaluate per request under the singleton scope: a scoped `TenantDb` wrapper plus
  an architecture test banning raw `ISqlSugarClient` in endpoints.
- Per-tenant settings replace constants: time zone, default country code, slot size (30 min).
- Units stay in the app (Clerk has no nested orgs).

### Data model

```
Tenant(Slug uniq, Name, TimeZone, DefaultCountryCode, SlotMinutes, RequireApproval, Status,
       CreatedByUserId, DeletedAt, LegacyClaimEnabled)
User(ClerkUserId uniq, Name, Email)            TelegramLink(UserId, ChatId, Status)
TenantMember(TenantId, UserId?, DisplayName, Phone?, UnitId?, Role, NotificationScope,
             Status: Active|Pending|Unclaimed|Removed, LegacyChatId?)
TenantInvite(TenantId, TokenHash, Role, UnitId?, ExpiresAt, MaxUses, Uses, RevokedAt)
Unit(TenantId, Name)   Facility(TenantId, Name, Group, AvailableToAll)
FacilityUnitAccess(FacilityId, UnitId)   RosterEntry(TenantId, Name, Phone, UnitId?)
Booking(TenantId, FacilityId, StartUtc, EndUtc, Conduct, Description,
        PocName, PocPhone,                        -- per-booking PoC snapshot
        BookedByMemberId (immutable), UpdatedByMemberId, UnitId (snapshot),
        BatchId?, CancelledAt/By, Revision)
CalendarConnection(TenantId, CalendarId, Status)
BookingCalendarEvent(BookingId, EventId, SyncedRevision)
OutboxMessage(TenantId, Type, Payload, Status, Attempts, NextAttemptAt, LockedUntil)
LegacyImportMap(Source, LegacyKey, NewId)
```

Bookings are soft-cancelled. Phones are stored E.164 using the tenant's default country code.

### Booking concurrency (TiDB)

TiDB differs from MySQL for check-then-write, per its documentation:

- TiDB takes the transaction snapshot at `BEGIN`. MySQL takes it at the first plain `SELECT`. A
  plain re-read after acquiring a row lock can therefore miss rows committed by the transaction we
  waited on. Portal's `LockRowAsync` callers re-read with a plain query and its concurrency tests
  run on MySQL, so that pattern is unproven on TiDB.
- TiDB is snapshot isolation (write skew allowed), has no gap locks, and has no `SKIP LOCKED`.

Rule: `BookingService` is the only booking writer. One transaction locks the involved `Facility`
rows in id order, runs the overlap check as a **locking read** (`FOR UPDATE`, a current read),
then inserts bookings and outbox rows. The concurrency suite runs against TiDB, not only MySQL.
Optional hardening if ever needed: a `BookingSlot(FacilityId, SlotStart)` table with a unique
index.

### Calendar sync (optional per tenant, off by default)

- DB to Calendar only. The outbox row is a dirty flag; the worker reads the booking's current
  state and upserts or deletes the event, so retries and reordering are harmless.
- Event ID stays `booking.Id.ToString("N")`, so 3SIB's existing carbon-copy events are adopted, not
  duplicated. The old storage calendar is retired and archived read-only.
- One platform service account; each tenant shares its calendar with it. A calendar is bound only
  after a proof-of-control handshake (a code written into the calendar that the admin reads back),
  otherwise tenant A could point at tenant B's calendar.
- At most 4 concurrent calls per tenant plus a global limiter, backoff, and a dead-letter state
  whose error is visible in tenant settings. "Sync now" and a nightly reconcile replace
  `/Cache/Purge`. Claim outbox rows with `UPDATE ... SET LockedUntil` leases.

### Telegram

- One global bot, private chats only, webhook protected by a secret token. Remove `SetWebhook`
  from startup. Use separate bots for dev, staging and prod.
- Linking: a signed-in user calls `POST /Me/Telegram/Link` and gets `t.me/<bot>?start=<token>`
  (128-bit, hashed, single-use, 10 min, bound to the user).
- Handlers must tolerate users with no Telegram, format times in the tenant time zone, and show the
  tenant name for users in several tenants.
- Batch bookings get a `BatchId` and produce one summary message per recipient (not one per slot),
  and the dispatcher applies a global send throttle. Today a 50-slot batch to 50 subscribers can
  send about 2,500 messages.

### Self-serve onboarding and abuse controls

- Flow: sign up, then `/onboarding` offers "Create organization" or "Join with a link". The creator
  becomes Admin; a guided setup covers facilities, units, invites, optional calendar and Telegram.
- Joining: admins generate invite links (hashed token, role, optional unit, expiry, max uses) to
  share in WhatsApp or Telegram. No email infrastructure in v1. With `RequireApproval` (default
  on), joiners are `Pending` until an admin approves, so a leaked link exposes nothing.
- Controls: cap on tenants created per user (about 3), reserved slugs, ASP.NET rate limits per IP
  and user, per-tenant quotas (facilities, members, bookings per day; batch limit 50 already
  exists), and a `Suspended` status with a platform-admin switch.
- Offboarding: 30-day soft delete of an organisation, then a purge by `TenantId`. `user.deleted`
  anonymises the user.

### Scale (20-50 members, 5,000+ bookings per organisation)

- Indexes: `(TenantId, StartUtc)`, `(TenantId, FacilityId, StartUtc, EndUtc)`,
  `(TenantId, BookedByMemberId, StartUtc)`.
- `GET /Booking` currently returns every booking ever and the UI filters client-side. When the API
  contract changes, add `from`, `to`, `facilityId`, `mine` and cursor paging with a default window
  of about 30 days back plus the future.

### Deployment (Coolify + TiDB)

- Coolify runs its health check inside the container, so the image needs `curl` or `wget`. The
  chiseled runtime image has neither. Install `curl` and `tzdata` (tenant time zones need
  `tzdata`). Rolling updates need a passing health check. (From search results; coolify.io was not
  reachable. Verify.)
- Aspire `MapDefaultEndpoints` only maps `/health` and `/alive` in Development. Map a DB-ping
  `/health` in production.
- Behind Coolify's proxy, configure `ForwardedHeadersOptions`, otherwise `CreatedAtAsync` builds
  `http://` Location headers and IP rate limiting sees the proxy address.
- Set `Startup:ValidateDatabaseSchema=true` in production: a new container whose schema is behind
  fails its health check and never takes traffic. Rolling updates run old and new against the same
  schema briefly, so migrations must be backward-compatible (expand/contract).
- Migrations: either a GitHub Actions job (`diff`, approval, `apply`, then Coolify's deploy
  webhook, as portal does) or the migrator bundled in the image and run from a Coolify hook. Destructive
  changes stay manual with `--allow-destructive`.
- Secrets are Coolify environment variables (TiDB connection string, Clerk keys, Telegram token and
  webhook secret, Google service account). No persistent volume is needed once cookies and Data
  Protection keys are gone.
- TiDB needs TLS; allow-list the Coolify egress IP if the tier requires it; keep the Coolify server
  in the same region as the cluster.
- The SPA can stay on Cloudflare Pages or become a static Coolify site. Use one registrable domain
  for `app.` and `api.`, and read CORS origins and `azp` values from config. Clerk production needs
  a domain whose DNS we control.

## Migrating 3SIB

| Legacy | New |
|---|---|
| Users sheet | `Unit` rows plus unclaimed `TenantMember`s (E.164 phone, `LegacyChatId`) |
| Facilities sheet | `Facility` plus `FacilityUnitAccess` (`"All"` becomes `AvailableToAll`) |
| Nominal Roll | `RosterEntry`, deduped by phone |
| Storage-calendar blobs | `Booking` rows, **keeping the GUIDs** so Telegram "Ref:" and `/booking/{id}` links keep working |
| Carbon-copy events | `BookingCalendarEvent` links |
| OTPs sheet | dropped |

- The importer (`import-legacy`) is idempotent through `LegacyImportMap`, supports `--dry-run`, and
  delta runs reuse the calendar sync-token logic. `verify-legacy` reports counts per facility and
  month, overlaps, orphan bookers and duplicate phones. The legacy MemoryPack contract is
  positional, so the old `Booking` type is frozen and tested against a real blob.
- Legacy `UserPhone` was overwritten by whoever last edited a booking, so imported bookings take the
  last editor as creator. The true creator cannot be recovered.
- **Cutover 1 (storage)** doubles as the infrastructure move. The new API goes up on Coolify and
  TiDB while the old deployment stays as the rollback target. Steps: rehearse against a prod copy,
  full import the day before, maintenance mode (reads OK, writes 503) at T0, delta import and
  verify, switch the API URL, then re-point the bot webhook (stop the old API first, it calls
  `SetWebhook` at startup). Legacy OTP login keeps working against `TenantMember.Phone`, and Sheets
  stay a temporary inbound source for Users, Facilities and Roster until admin screens ship.
  Rollback within about 48 h: an `export-legacy` command writes post-T0 bookings back as legacy
  blobs.
- **Cutover 2 (identity)** flips web and API to Clerk in one release with no dual auth. Existing
  members claim their account: a broadcast to all legacy chat IDs links to `/claim/3sib`; after
  Clerk sign-in the bot deep link matches the chat to the unclaimed member's `LegacyChatId` and the
  server sets `TenantMember.UserId`. Claim endpoints sit outside the tenant group. Legacy admins are
  not carried over automatically (legacy chat bindings may have been tampered with, see below); a
  platform admin promotes them. The claim path is removed after about 3 months.

## Findings that shaped this decision

Fix-now items, independent of the rewrite and exploitable in production today:

1. **Telegram account takeover.** `POST /Bot` is anonymous, `SetWebhook` sets no secret token
   (`Program.cs`), and the handler trusts `contact.PhoneNumber` without checking it belongs to the
   sender. Anyone can bind their chat ID to a known member's phone, request that phone's OTP and
   sign in as them, admins included.
2. **OTPs never expire and have no attempt limit.** `Auth/Verify` never checks `CreatedAt`.
3. **`GET /Cache/Purge` is anonymous**, and admin endpoints use `AllowAnonymous()` with thrown
   exceptions rather than policies.

Clerk facts (from Clerk's docs): session tokens refresh every 60 s, so claims can be that stale;
webhooks can be late, duplicated or out of order; SMS sign-in is opt-in per country (default US and
Canada) and billed per message; production needs a domain whose DNS we control (or the Frontend API
proxy). Custom session claims (`{{user.primary_email_address}}`, `{{user.full_name}}`) cover the
email and name we need.

## Delivery

Delivered as small stacked PRs. Sizes are rough, for one developer.

| Phase | Scope | Size |
|---|---|---|
| 0 | Hotfixes above, Coolify prerequisites (image, `/health`, forwarded headers). Spikes: S1 `@clerk/nuxt` on `ssr:false` plus Bearer token to .NET; S2 SqlSugar tenant filter under singleton scope; S3 TiDB concurrency semantics (lock + plain read vs lock + `FOR UPDATE` read vs READ COMMITTED); S4 TiDB reachability from Coolify, health check, rolling update, migrator step | 3-4 days |
| 1 | Cutover 1: TiDB entities and migrator, `BookingService`, outbox, Telegram handlers, optional calendar sync, importer, Sheets as inbound reference data, test rewrite (real DB, one tenant per test, TiDB concurrency suite) | 3-4 weeks |
| 2 | Cutover 2: Clerk auth, `/t/{slug}` routing, self-serve creation, invites and approval, quotas, tenant admin screens, Telegram linking, claim flow, windowed lists, batch notifications | 4-5 weeks |
| 3 | Offboarding and deletion, PDPA export, per-tenant limits, audit log, legacy sunset | 1-2 weeks |

### Phase 0 stack

- [x] ADR (this document)
- [x] Telegram webhook: secret token and contact-ownership check
- [x] OTP: expiry and attempt limit
- [x] `/Cache/Purge`: admins only
- [ ] Coolify readiness: production `/health`, image with `curl` and `tzdata`
- [ ] Forwarded headers behind the Coolify proxy
- [ ] Spikes S1 to S4

## Open items

1. Web hosting (Cloudflare Pages or Coolify static) and a domain whose DNS we control for Clerk.
2. TiDB tier and region, and the Coolify server's region and egress IP.
3. Join policy default (approval required, as assumed, or open links).
4. Expected number of tenants (sizes quotas and throttles) and the freeze window 3SIB will accept at
   Cutover 1.
5. Whether Coolify's pre/post-deploy hooks can run the migrator (check in S4).

## Sources

- Clerk: [session tokens](https://clerk.com/docs/guides/sessions/session-tokens),
  [customize session tokens](https://clerk.com/docs/guides/sessions/customize-session-tokens),
  [manual JWT verification](https://clerk.com/docs/guides/sessions/manual-jwt-verification),
  [webhooks](https://clerk.com/docs/guides/development/webhooks/overview),
  [restricting access](https://clerk.com/docs/guides/secure/restricting-access),
  [production deployment](https://clerk.com/docs/guides/development/deployment/production)
- TiDB: [transaction overview](https://docs.pingcap.com/tidb/stable/transaction-overview),
  [pessimistic transactions](https://docs.pingcap.com/tidb/stable/pessimistic-transaction),
  [isolation levels](https://docs.pingcap.com/tidb/stable/transaction-isolation-levels),
  [MySQL compatibility](https://docs.pingcap.com/tidb/stable/mysql-compatibility)
- Coolify: [health checks](https://coolify.io/docs/knowledge-base/health-checks),
  [rolling updates](https://docs.coolify.codeon.cn/en/knowledge-base/rolling-updates)
- Reference implementation: `GeeksHacking/portal` (`Data/SqlSugarClientFactory.cs`,
  `Extensions/SqlSugarLockExtensions.cs`, `GeeksHackingPortal.DbMigrator`, `deploy-api.yml`)
