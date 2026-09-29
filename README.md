# Facility Booking System

## Get started

You need:

* .NET 10
* Node.js and pnpm
* DevTunnel
* Telegram Bot Token

Create a tunnel:

```powershell
devtunnel create fbs -a -d "Facility Booking System";
devtunnel port create fbs -p 5204 --protocol http;
devtunnel port create fbs -p 7172 --protocol https;
devtunnel host fbs;
```

Visit the URL provided and grant access.

In future runs, you only need to start the tunnel:

```powershell
devtunnel host fbs;
```

Take note of the URL provided and set it as environment variables as show below.

```powershell
cd ./Fbs.WebApi;
dotnet user-secrets set "Telegram:Token" "<YOUR_TOKEN_HERE>";
dotnet user-secrets set "Telegram:WebhookUrl" "<YOUR_DEVTUNNEL_HERE>/Bot";
dotnet user-secrets set "Telegram:WebhookSecret" "<A_RANDOM_STRING>";
```

`Telegram:WebhookSecret` is registered with Telegram and checked on every update to `/Bot`, so that only
Telegram can talk to the bot. Use 16 to 256 characters of `A-Z`, `a-z`, `0-9`, `_` or `-`, for example the
output of `openssl rand -hex 32`. The API won't start without it.

You need to provide the service account with access to the facility spreadsheet.

Start the AppHost project, which runs the API and the web app (on http://localhost:3000) and prints a link to the
Aspire dashboard:

```powershell
dotnet run --project ./Fbs.AppHost/Fbs.AppHost.csproj;
```

Or, with the [Aspire CLI](https://aspire.dev):

```powershell
aspire run;
```

## Database

Bookings are kept in Google Calendar, and users, facilities, the roster and login codes in Google Sheets. The database
is where they are moving to (see `docs/adr/`), on [TiDB](https://www.pingcap.com/tidb/) through SqlSugar. Its schema is
applied with the migrator, which the API never does itself:

```powershell
$env:ConnectionStrings__db = "Server=<host>;Port=4000;User ID=<user>;Password=<password>;Database=fbs;SslMode=VerifyFull";
dotnet run --project ./Fbs.DbMigrator -- diff;                        # what would change; exits 2 if anything would
dotnet run --project ./Fbs.DbMigrator -- apply;                       # make the tables match the entities
dotnet run --project ./Fbs.DbMigrator -- apply --create-database;     # the same, on a fresh server
dotnet run --project ./Fbs.DbMigrator -- apply --allow-destructive;   # also drop columns that are no longer used
```

`apply` stops before dropping a column unless `--allow-destructive` is given, so read the `diff` first. Applying
also puts back a declared index that has gone missing, which SqlSugar's own comparison doesn't notice.

Once `ConnectionStrings:db` is set, the API checks at startup that the database has every table, column and index it
needs, and refuses to start if not, so a version deployed before its schema was applied never takes traffic. Extra
columns and indexes are fine, so the previous version keeps working after the next one has been applied. Turn the
check off with `Startup:ValidateDatabaseSchema=false`.

Set `Storage:Provider=Database` to keep bookings, users, facilities, the roster and login codes in the database instead,
and to stop using Google Calendar and Sheets altogether. It needs `ConnectionStrings:db`, and the API refuses to start
without it. They belong to the tenant named by `Storage:TenantSlug` (`3sib` by default), which has to exist. Google
stays the default until the data has been imported.

A facility is never booked twice, including by requests at the same moment: booking, or moving a booking onto a
facility, first locks the facility's row, then looks for a clash with a locking read. Cancelling keeps the booking,
marked as cancelled. Whoever made a booking never changes; changing one records who did, and the point of contact is
stored with the booking.

### Moving from Google

`import-legacy` copies the Users, Facilities and Nominal Roll sheets and the bookings in the calendar into the database, and
`verify-legacy` checks that the database has all of it, as it is. Both need the `Google__*` settings the API has, and
`ConnectionStrings__db`.

```powershell
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --name "3 SIB" --dry-run;   # what would happen, keeping none of it
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --name "3 SIB";             # import it, and make the tenant if it isn't there
dotnet run --project ./Fbs.DbMigrator -- verify-legacy --tenant 3sib;                            # exits 2 if anything is different
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --overwrite;                # replace what an earlier import saved, for the last one
```

It keeps the IDs bookings have, so the events that go with them stay the same, and people who have never signed in with an account
are kept as members waiting to claim their place. Rows that can't be imported, such as a phone number that is on the Users
sheet twice (the first counts, as it always has), are left out and listed as warnings, and so are bookings by people no longer
on the sheet (kept as by a member who has left), of facilities no longer on the sheet (kept, and nobody can book them), and
bookings that overlap another. Everything is done in one transaction, so it is all imported or none of it.

Running it again only adds what is new, so it can be run days ahead, and again just before switching. `--overwrite` replaces
what was imported with what Google has now, and is refused once anything has been done in the database, as Google is by then
out of date.

### Outbox

Work that follows a change, such as telling people about it, is written to the `OutboxMessage` table in the same
transaction as the change, so it is neither lost when the change is kept nor left behind when it isn't. A background
dispatcher (`OutboxDispatcher`) takes the messages that are due, has the handler for their type do them, and puts back the
ones that fail, 5 seconds later the first time and twice as long each time up to 15 minutes, until they have been tried
`Outbox:MaxAttempts` times (8), when they are kept as `Dead`, with the error, rather than tried again. Handlers have to be
safe to repeat.

TiDB has no `SKIP LOCKED`, so messages are taken with a lease, in a single statement: any number of API instances can run
at once, as they do while a new version starts, and a message that an instance died holding is taken over when its lease
(`Outbox:LeaseDuration`, 2 minutes) runs out. Only whoever holds the lease can say the message is done or failed. Messages
that are done are removed after `Outbox:Retention` (7 days). `Outbox:PollInterval` (10 seconds) is how long an idle
dispatcher waits before looking, for messages written by another instance, and `Outbox:TenantId` limits it to one tenant.

With `Storage:Provider=Database`, telling people about bookings on Telegram goes through the outbox: whoever made the booking,
plus those who asked to hear about everyone's bookings or their unit's, once each. Bookings made together are one message
that lists them, not one for each. People with no Telegram are left out, and so are those who have blocked the bot; a
Telegram outage is tried again later without telling twice the people who were told already. Times are in the tenant's
time zone. Sending is kept to `Telegram:MessagesPerSecond` (25) across everything, as Telegram allows a bot about 30.

### Google Calendar

A tenant can have a Google Calendar its bookings are copied to, one way: what is in the calendar is never read back. The
`CalendarConnection` table says which (`CalendarId`, which the service account in `Google:ServiceAccountJsonCredential`
must have been given access to). While its `Status` is `Active`, every booking made, changed or cancelled writes an
outbox message, and `CalendarBookingSync` sends the booking as it is when the message is handled, so retries and messages
handled out of order can't send old news. The event's ID is the booking's ID without dashes, as it was when bookings
were kept in the calendar, so events already there are updated rather than added again, and cancelling removes them.

Google refusing the calendar (401, 404, or 403 for anything but the rate limit) marks the connection `Failed`, with the
error in `LastError`, and stops sending to it until someone puts it right and sets it `Active` again. Anything else, such
as a rate limit or a problem at Google, is tried again like any outbox message. `BookingCalendarEvent` records which
version of each booking was sent. `CalendarReconciler` finds bookings that aren't in the calendar as they are now, and
sends them, every `CalendarSync:Interval` (24 hours), which is also how a calendar connected after bookings were made gets
them. Bookings that ended over a week ago are left alone.

## Testing

The API tests use [TUnit](https://tunit.dev) and run the API in memory, with Google and Telegram faked, so they need
no secrets. The tests that use the database run against [TiDB](https://www.pingcap.com/tidb/), as production does,
because it reads inside transactions differently to MySQL. The AppHost starts it in a container, so all they need is Docker,
and every run creates, and drops, a database of its own on it.

```powershell
dotnet test;
```

## Deploying

The API answers `GET /health` with `Healthy` in every environment. Point the host's health check at it (port
8080). The image includes `curl` because hosts like Coolify run the health check inside the container, and Coolify
only replaces the old container once it passes.

Set `Telegram__WebhookSecret` (see above) in the environment before deploying. The API registers it with
Telegram on startup and rejects bot updates that don't carry it.

Login cookies are encrypted with ASP.NET Core Data Protection keys. The Docker image keeps them in `/app/keys`, so
mount a persistent volume at that path or every redeploy generates new keys and logs everyone out.
