# Cutover 1: from Google Sheets and Calendar to the database

This moves bookings, users, facilities and the nominal roll from Google Sheets and Calendar into TiDB. Sign-in is
unchanged (Telegram codes); moving it to Clerk is Cutover 2. See [ADR 0001](../adr/0001-clerk-multitenancy-tidb.md).

Nothing here needs downtime longer than a redeploy, and everything up to the last two steps can be done days ahead
and repeated.

## What changes when it is switched

- Bookings, users, facilities, the roster and login codes are read from and written to the database. Google Sheets and
  Google Calendar are no longer the store.
- Telegram messages about bookings are sent from the outbox: a batch of bookings is one message, times are in the tenant's
  time zone, and a Telegram outage is retried rather than lost.
- Whoever made a booking no longer changes when someone else edits it, and cancelled bookings are kept.
- **The Users, Facilities and Nominal Roll sheets are still where people are added and facilities changed**, until there are
  screens for it, and are read every few minutes (`ReferenceData__Sheets__Enabled=true`). Who is an admin, whom someone
  hears about, and which Telegram chat is theirs are kept in the database from then on, so editing those columns on the sheet
  has no effect: use the app.
- The calendar becomes an optional one-way copy of bookings. Changes made to events in the calendar are not read back.

## Before

1. A TiDB database (Serverless or self-hosted) and its connection string. In Coolify, set it as a secret:
   `ConnectionStrings__db=Server=<host>;Port=4000;User ID=<user>;Password=<password>;Database=fbs;SslMode=VerifyFull`.
   The user needs to create and alter tables, as the migrator applies the schema.
2. Everything the API has today (`Google__*`, `Telegram__*`) stays set: the import reads Google, and the calendar copy and
   the sheets sync use it afterwards.
3. Deploy the version that has this in it **without** `Storage__Provider`, so it still runs on Google. It is unchanged
   until the switch.

## Apply the schema

The migrator is in the image. In Coolify, run it in the running container (Execute command), or as a pre-deployment command:

```sh
dotnet Fbs.DbMigrator.dll diff
dotnet Fbs.DbMigrator.dll apply --create-database
```

`diff` exits 2 while there is something to apply. `apply` refuses to drop anything unless `--allow-destructive` is given.
The API also checks at startup that the schema has what it needs, and refuses to start if not, so a version that was
deployed before its schema was applied never takes traffic.

## Import

```sh
dotnet Fbs.DbMigrator.dll import-legacy --tenant 3sib --name "3 SIB" --dry-run
```

Read the warnings. They are about the data, not the tool: a phone number on the Users sheet twice (the first row counts, as
it always has), a facility with bookings that is no longer on the Facilities sheet (kept, and nobody can book it), bookings by
someone no longer on the Users sheet (kept as by a member who has left), and bookings that overlap another (imported as they
are). Fix what is worth fixing on the sheet, and run the dry run again. When it says what is expected:

```sh
dotnet Fbs.DbMigrator.dll import-legacy --tenant 3sib --name "3 SIB"
dotnet Fbs.DbMigrator.dll verify-legacy --tenant 3sib
```

`verify-legacy` exits 0 when the database has everything that is in Google, as it is, and 2 with the differences when not.
Both can be run again at any time before the switch: importing only adds what is new.

## Switch

Do these together, as a short window:

1. Stop changes, so nothing is made in Google after the last import: set `Maintenance__ReadOnly=true` and redeploy. Everything
   can still be read; anything that changes something answers 503 (with `Retry-After`), including what Telegram sends the
   bot, which Telegram delivers again later.
2. Import once more, replacing what an earlier import saved with what Google has now, and check it:

   ```sh
   dotnet Fbs.DbMigrator.dll import-legacy --tenant 3sib --overwrite
   dotnet Fbs.DbMigrator.dll verify-legacy --tenant 3sib
   ```

   `--overwrite` is refused once anything has been done in the database, so it can't undo real work by mistake.
3. Set, and redeploy, removing `Maintenance__ReadOnly`:

   ```
   Storage__Provider=Database
   Storage__TenantSlug=3sib
   ReferenceData__Sheets__Enabled=true
   ```

4. Coolify's health check (`GET /health`) passes once the API has started and the database answers. It will not start if
   the schema is out of date, and while it doesn't pass Coolify keeps the old container.

## Check it worked

- Sign in with a Telegram code as someone who is on the Users sheet.
- Make a booking, change it, and cancel it. The booker, their unit, and everyone who asked to hear about everything, get one
  message each time.
- Add someone to the Users sheet. Within `ReferenceData__Sheets__Interval` (5 minutes) they can sign in.
- `GET /Cache/Purge` as an admin is now a no-op for bookings, and only drops the caches of the sheets.

## Copy bookings to the calendar (optional)

If people look at the calendar, keep it as a one-way copy. Bookings keep the IDs they had, so the events already in the
carbon copy calendar are updated in place, and cancelling removes them. The service account must be able to edit the
calendar, which it can for the carbon copy calendar today.

```sql
INSERT INTO CalendarConnection (Id, TenantId, CalendarId, Status, CreatedAt)
SELECT UUID(), Id, '<the carbon copy calendar id>', 1, UTC_TIMESTAMP() FROM Tenant WHERE Slug = '3sib';
```

Bookings are sent as they change, and every 24 hours anything out of date is sent, which is
also how the existing bookings are brought up to date. If Google refuses the calendar (it isn't shared with the service
account any more), the connection becomes `Failed` with the error in `LastError`, and nothing more is sent until it is put
right and `Status` is set back to 1.

The old storage calendar is no longer read or written. Keep it, read only, as a backup for a while.

## If it goes wrong

Going back loses nothing if the bookings made in the database since the switch are written back first. In the container:

```sh
dotnet Fbs.DbMigrator.dll export-legacy --tenant 3sib --dry-run
dotnet Fbs.DbMigrator.dll export-legacy --tenant 3sib
```

This writes what was booked, changed and cancelled since to Google Calendar, in the form the old version reads (in both
calendars, with the IDs bookings have in the database), and says what it could not write: a booking by someone since taken off
the Users sheet, or with more written on it than an event can hold. It exits 2 when there is anything to say, and running it
again writes only what is still different. Put `Maintenance__ReadOnly=true` on first, as for the switch, so that nothing is
made while it runs.

Then set `Storage__Provider` back (or remove it) and redeploy: the API reads Google again, as before. Only bookings are written
back. The sheets are where users and facilities are edited, so they are as they were, but who linked a Telegram chat, was made
an admin, or changed whom they hear about, since the switch, is not. After going back, `import-legacy --overwrite` will refuse to
run over the database once it has been used, which is as it should be.

## Afterwards

Once it has run for a while and the switch is not going to be undone, the code that stores things in Google (and this
importer) can be deleted. That is a separate change so that this one can be undone by configuration alone.
