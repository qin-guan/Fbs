# Sunset: removing the phone number sign-in and Google as the store

After [Cutover 1](cutover-1-database.md) (the database is the store) and [Cutover 2](cutover-2-accounts.md) (people sign in with Clerk and have taken
over their places), what is left of the old version is code nobody uses, and a way back nobody should need. This is how to remove it, in small
changes, and when it is safe to. See [ADR 0001](../adr/0001-clerk-multitenancy-tidb.md), whose last Phase 1 item this is.

**Step 3 is done, before 1 and 2**, as the sheets were no longer used: Google is not a store any more, and the sheets, the import and
`export-legacy` have gone, with `/Cache/Purge` from step 2, which only emptied what had been read from Google. Going back is no longer a
matter of configuration. The rest is not done yet. Until a step is done, going back from it is a matter of deploying what was there
before, and each has a gate.

## Before any of it

All of these, and written down as done:

1. **Both cutovers have been in production for at least four weeks** without going back, and the old build of the web app has had no visits in the
   last two (look at the access logs for `/booking`, `/profile` and `/auth/*`).
2. **Everybody who is still coming has taken their place.** Count who hasn't, for each organisation that came from before:

   ```sql
   -- Status 3 is Unclaimed
   SELECT COUNT(*) FROM TenantMember WHERE TenantId = '<the tenant id>' AND Status = 3;
   ```

   Anyone left is either not coming (remove them from the people page) or has to be told to claim. Then turn claiming off in the organisation's
   settings (it can't be turned on again), or have it done from the database.
3. **Nothing calls the old API.** No requests to `/Auth/*`, `/Booking/*`, `/Facility/*`, `/NominalRoll`, `/Admin/*` or `/Cache/Purge` for two weeks. `/Bot` stays:
   it is how Telegram is linked, and how claims were opened.
4. **A backup of the database, and the old Sheets and Calendar kept, read only, as an archive** for as long as bookings have to be kept (ask
   whoever decides that). Deleting the code is not deleting them.
5. The [rollback](#rolling-back) below is understood by whoever is doing this, and the step that ends it is chosen, not stumbled on.

## The steps

Each is a change of its own that can be reviewed, built, and deployed before the next. Run the whole test suite, and the browser tests of the web
app, on each, and look at staging by hand: signing in, booking, the admin pages, a Telegram notification.

### 1. The web app: only the pages for accounts

- Remove the `legacy` build, and the pages only it reaches: `pages/auth`, `pages/booking`, `pages/profile`, `pages/faqs`, `pages/changelog`, the old build's
  buttons on the home page (in `composables/home.ts`, with its `/Auth/Me` and `/auth/`, `/booking` and `/profile` in `privatePaths` in `lib/site.ts`), the `app` layout (and `landing`, if nothing else uses it then), and the composables only they use (`booking.ts`,
  `booking-slots.ts`, `nominal-roll.ts`, the ones for a session by cookie). `lib/` and `composables/tenant.ts` are the new pages' and stay.
- `authMode` is then `clerk` (and `test`, for the browser tests); `NUXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is required.
- Regenerate the API client after step 2, not before: this still calls what is there.

The last thing that can be undone by deploying the old build. Everything after it needs the code back from version control.

### 2. The API: the old endpoints and the phone number sign-in

- Remove `Endpoints/Auth`, `Endpoints/Booking`, `Endpoints/Facility`, `Endpoints/NominalRoll`, `Endpoints/Admin` and `Endpoints/Cache`, the cookie
  and phone number authentication, `OtpAttemptTracker`, `IOtpRepository` (and its two implementations, `OtpRepository` and `DatabaseOtpRepository`), and the parts of
  `Endpoints/Bot` that answer a shared contact (linking, and the claim link, stay).
- Remove the events: `Events`, `EventHandlers`, `BackgroundPublisher`, which only the old endpoints raise. What tells people about bookings is the outbox.
- Remove their tests: `AuthTests`, `Booking*Tests`, `CachePurgeTests`, the phone number parts of `BotTests`.
- Regenerate the API client for the app, and check the diff is only what was removed.

### 3. The API: Google as the store (done)

**The point of no return for going back by configuration.** Take a backup first, and do not do this in the same week as step 2.

- Remove the Google implementations of the repositories, `BookingCache`, `BookingWriteLock`, `CacheRefreshService`, and `Storage:Provider` (the database is then
  the only store, and needs `ConnectionStrings:db`).
- Remove `Legacy/` (the importer, verifier, exporter and the Sheets reference sync), and `import-legacy`, `verify-legacy` and `export-legacy` from the migrator.
  Keep what the outbound calendar sync uses of Google: only Sheets goes.
- Remove `Google:` settings that are for Sheets (`Google__SpreadsheetId`, `Google__CalendarId`, `Google__CarbonCopyCalendarId`, and `Storage__Provider`,
  `ReferenceData__Sheets__*`), and the service account's access to the spreadsheet.
- Remove `LegacyImporterTests`, `LegacyExporterTests`, `SheetsCallsTests`, `SheetsReferenceSyncTests`, `CalendarBookingTests` (the Google-as-store ones), and the
  Google-mode parts of the others.

### 4. The claim flow, and what only it used

- Remove `Claims/`, `Endpoints/Claims`, the page at `/claim/:slug`, and `ClaimsTests`. Keep `MemberPromotions` and `promote-admin`: the first admin of an
  organisation that came from before still has to be made by somebody.
- Remove the setting for turning claiming off from the settings page and the API.

### 5. The schema

Only now, and by the migrator, which will not drop anything without being told to:

```bash
dotnet Fbs.DbMigrator.dll diff
dotnet Fbs.DbMigrator.dll apply --allow-destructive
```

What goes: the `LoginOtp` and `RosterEntry` and `MemberClaimToken` tables, and `TenantMember.LegacyChatId` and `Tenant.LegacyClaimEnabled`. Read the diff first,
and take a backup before it is applied: dropping is not undone by the code coming back.

### 6. The words

Remove the old sections from the README, tick the ADR, and move this runbook and the two cutovers into an archive folder, so that what is in `docs/runbooks`
is what somebody running the system needs now.

## Rolling back

| After | To go back |
|---|---|
| Nothing done | `Storage__Provider=Google`, the old build of the web app, and `export-legacy` for what was booked in the database since. |
| Step 1 | As above, and the old build of the app comes from version control. |
| Step 2 | The old endpoints come back from version control, and then as above. |
| Step 3 | **Not by configuration.** Restore the code from version control, and the Sheets and Calendar from the archive, and import what was booked since by hand. Better not to. |
| Step 5 | The tables and columns are gone: restore from the backup. |

## When somebody asks why it is still there

Because until the last step nothing can be undone by the people who run it without a developer, and the cost of leaving code in is only that it is in.
