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

The web app is built to sign people in with a phone number and a code on Telegram (what is deployed until the switch to accounts), or with Clerk, which
gives it the pages for accounts and organizations, `/t/{slug}`, for bookings, the timeline, and the admins of an organization. Which is decided when it
is built: see [`Fbs.WebApp/README.md`](Fbs.WebApp/README.md), which also says how its pages are checked in a browser.

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

### Signing in with Clerk

With `Storage:Provider=Database` and `Clerk:Issuer` (Clerk's Frontend API, such as `https://example.clerk.accounts.dev`) set, the
API also accepts Clerk session tokens as `Authorization: Bearer` tokens, alongside the Telegram code and cookie the app uses
today. `Clerk:AuthorizedParties` lists the origins of the apps that may use one, checked against the token's `azp` (it is
required), so a token issued to another app on the same Clerk instance is refused. The keys come from
`{Issuer}/.well-known/jwks.json` and are kept for an hour (`Clerk:JwksUrl` if they are elsewhere). Only RS256 is accepted, and a
token has to be signed, unexpired, from that issuer, and for someone.

Whoever signs in gets an account (`UserAccount`), made the first time they are seen from their token's `sub`, `email` and `name`
(add `email` and `name` as custom claims in Clerk's session token settings), kept up to date from it, and belonging to no
organisation until they are a member of one. `GET /Me` says who is signed in and which organisations they belong to. Enums are
written in words in JSON.

### Organisations

Anyone signed in with Clerk can make an organisation with `POST /Tenants` (`name`, `slug`, and optionally `timeZone`, an IANA
name that is UTC if left out, and `defaultCountryCode`), and becomes its admin. The slug is its address, `/t/{slug}`: lower case
letters, digits and hyphens, 3 to 63 of them, starting and ending with a letter or digit. Some words, such as `api` and `admin`,
are kept back. Nobody can make more than `Limits:MaxTenantsPerUser` (3) themselves, and a slug that is taken is a 409.

Everything that belongs to an organisation is under `/t/{slug}`, and the organisation is found from the address, never from what
is sent. Only an active member gets in. Someone who isn't a member, or has left, is told there is no such organisation, the same as
for an address that isn't one, so addresses can't be tried to find which exist; someone waiting to be let in gets a 403 saying so,
and so does anyone in an organisation that has been suspended. `GET /t/{slug}` says what the organisation is and who the caller is
in it. `GET` and `PUT /t/{slug}/Settings` are for admins: name, time zone, calling code, the length of a slot (15, 30 or 60
minutes) and whether people who join with an invite wait to be approved (they do unless it's turned off).

Admins manage what the organisation is made of, all under `/t/{slug}`:

- Units: `GET /Units` (every member, as it is what people pick from), and `POST /Units`, `PUT /Units/{id}`, `DELETE /Units/{id}`
  for admins. A unit can't be deleted while people are in it or bookings were made for it; one that goes takes the access it
  gave to facilities with it.
- Facilities: `GET /Facilities`, `POST /Facilities`, `PUT /Facilities/{id}` (which replaces who can book it), `DELETE
  /Facilities/{id}`. A facility is available to everyone, or to the units listed, which have to be the organisation's own. One
  that anything was booked on (cancelled or not) can't be deleted.
- Members: `GET /Members` (`?includeRemoved=true` for those who have left), `POST /Members` and `PUT /Members/{id}`.
  Adding someone is by phone number, and they are unclaimed until they sign in. A phone number with a plus (or 00) is taken
  as it is, and one without is in the organisation's country. `PUT` sets their name, phone, unit, role (`Member` or `Admin`),
  whose bookings they hear about (`None`, `Unit`, `All`), and `membership`: `In` lets them in (or lets someone waiting in),
  `Removed` takes them out. The organisation always keeps an admin: the change that would take the last one is a 409.

Members book under `/t/{slug}` too:

- `GET /Facilities/Bookable` is what the caller can book: what is available to everyone, and what their unit has been given
  (admins can book anything).
- `POST /Bookings` books one or more `slots` (`facilityId`, `startDateTime`, `endDateTime`; up to 50) with the same
  `conduct`, `description` and point of contact (`pocName`, `pocPhone`, written on the booking as they are). All of them, or
  none if any clashes (a 409 that says which slot, and with what). Times have to be in the future and on the organisation's
  slot length (15, 30 or 60 minutes) in its time zone, whichever offset they are sent in.
- `GET /Bookings` lists those that share any time with a window, earliest first: `from` and `to` (from the start of today
  for 31 days if left out; at most 93 days), `facilityId`, `bookedBy`, and `mine=true`. `GET /Bookings/{id}` reads one.
- `PUT /Bookings/{id}` changes what it says and, if both are given, its time; `DELETE /Bookings/{id}` cancels it, and the
  booking is kept. Their booker, anyone in the booker's unit, and admins can. A booking that is over can't be moved, and one
  that has started can only have its end changed. Whoever made a booking never changes.

A facility is never booked twice at once, whichever way it is booked: booking, and moving a booking, lock the facility's row
and then look for a clash with a locking read (see Database). When a lot of requests are after the same facility, TiDB can turn
one away ("pessimistic lock retry limit reached", or a deadlock), so a booking that was turned away is run again, up to five times.

People join with a link an admin shares in a chat, as there is no email:

- `POST /t/{slug}/Invites` makes one (`role`, `unitId`, `expiresInDays` from 1 to 30, `maxUses` from 1 to 100), and returns its
  `token` **once**: only a SHA-256 of it is kept, so it can't be shown again. `GET /t/{slug}/Invites` lists the latest 100 and
  whether each is still `Active`, `Expired`, `Revoked` or `UsedUp`, and `DELETE /t/{slug}/Invites/{id}` stops one. An
  organisation can have `Limits:MaxActiveInvites` (20) going at once.
- Whoever has the token signs in and `GET /Invites/{token}` says where it leads. A link that doesn't work, for any reason, is a
  404, so the reasons can't be told apart. `POST /Invites/{token}/Accept` joins (`displayName` if they want another than
  their name with Clerk). With `requireApproval` (the default) they are `Pending` and get a 403 from `/t/{slug}` until an admin lets
  them in (`PUT /t/{slug}/Members/{id}` with `membership: In`); otherwise they are `Active` at once. A use is taken in the
  statement that checks there is one left, so a link lets in no more than `maxUses` however many join together. Joining again
  changes nothing, and somebody an admin removed can't come back with a link, only be let back in by an admin.

Telegram is where a person is told about bookings, connected to their account from the app:

- `POST /Me/Telegram/Link` makes a link to open in Telegram (`https://t.me/<bot>?start=<token>`), which works once, for ten
  minutes; a new one replaces it, and the chat that is connected stays until the new one is opened. Opening it starts the bot, which is
  told whose account it is by the token, and the chat is then theirs. `GET /Me/Telegram` says whether one is connected, and
  `DELETE /Me/Telegram` disconnects it. The bot only takes a token in a private chat. A chat belongs to one account: connecting it
  to another takes it from the first. `Telegram:BotUsername` names the bot in the link, and is asked of Telegram if it isn't set.
- A member is told in the chat their account connected, and otherwise in the one their phone number was linked to before, so
  people carried over from before are told as they were. Somebody waiting to be let in, or who has left, is told nothing.
  Somebody who is in more than one organisation is told which one a booking is in.

**Claiming** moves people from the old sign-in (phone number and a Telegram code) to Clerk accounts. `import-legacy` creates
them as `Unclaimed` members with no account, and keeps the Telegram chat the old version sent their codes to
(`LegacyChatId`). A signed-in user claims their member row like this:

1. `GET /Claims/{slug}` checks they can (404 if claiming is off, or they are a member already).
2. `POST /Claims/{slug}/Start` returns `https://t.me/<bot>?start=claim_<token>`, valid once for ten minutes.
3. They open it in Telegram. The bot gives their account the unclaimed member whose `LegacyChatId` is the chat it came from,
   and connects that chat for notifications.

The member keeps its phone, unit and notification scope, but its role is always **member**, even for old admins: the old bot
let anyone link their chat to another person's number, so the chat is not trusted with admin rights. Make the admins with
`dotnet Fbs.DbMigrator.dll promote-admin --tenant 3sib --phone +6591234567` once they have claimed.
`Tenant.LegacyClaimEnabled` is on for organisations the importer made, and can be turned off but not back on. See
[Cutover 2](docs/runbooks/cutover-2-accounts.md).

When somebody deletes their Clerk account, Clerk tells `POST /webhooks/clerk` (subscribe it to `user.deleted`, and set the
endpoint's signing secret as `Clerk:WebhookSecret`, the `whsec_...` value). Webhooks are signed with Svix: one without a valid signature
over its ID, time and body, or older than five minutes, is a 401, and none are accepted without the secret (503). The account is
erased: its name and email are cleared, its places are made former members (no name, phone or chat, not an admin, not told
anything), and its Telegram link is removed. Bookings stay, with who made them shown as "Former member". The point of contact
typed on a booking is part of the booking and stays. Being told twice, or about someone who never signed in, changes nothing. If it
was the only admin of an organisation, the API logs a warning, and `promote-admin` makes another.

### What admins have done

`GET /t/{slug}/Audit` (admins only) says what has been done to an organisation, the latest first: its settings changed, units and facilities added, changed
and deleted, invite links made and stopped, people added by phone number, joining with a link, being let in or turned away, changed (name, phone number, unit,
role, whose bookings they are told about), removed and let back in, or taking over their places from before accounts, and it being made, and suspended or made
available again by whoever runs the system. Each says who did it, what was done in words, and what it was done to. `limit` (50, at most 200) and `before` (the `at` of the last one seen) page back through it. Nothing is written for
a change that was refused or that changed nothing.

An entry has no name of a person in it: who did it, and who it was done to, are kept as who they are in the organisation, and turned into names when it is
read, so that when somebody's account is erased they are a former member here too, and what they did stays.

### Deleting an organisation

An admin of an organisation can delete it, in two steps so that a mistake can be undone. `POST /Tenants/{slug}/Deletion` with `{ "confirm": "<the slug>" }`
(typing its address, to say they mean it) makes it **pending deletion**: nobody in it can use it (a 403 with the code `pending-deletion`, and its invite
links look as if they were never there), nothing is deleted, and `GET /Me` tells the people in it, in `tenantStatus` and `deleteAfter`, that it is to be deleted and
when. Any admin of it can take it back with `DELETE /Tenants/{slug}/Deletion` until it has been deleted for good, even after the time is up, and it is as it was.
`Limits__DeletionGraceDays` (30) is how long from asking until it can be deleted for good. Only an organisation that is active can be asked to be deleted (not one that
is suspended, or that is to be already), and one that is to be deleted can't be suspended: `suspend` says so and exits with 1. Both are written in its history.

Nothing is deleted for good until `purge-tenants` is run, which deletes the organisations that are due, and everything of them (accounts stay), by
their ID, in a transaction for each; run it on a schedule. `--dry-run` says what it would delete, `--tenant <slug>` is for one, and `--early` with it deletes one
that an admin has asked to be deleted before the time is up, for when somebody has to be erased sooner. See [the runbook](docs/runbooks/offboarding.md).

### Getting a copy of the data

Anybody signed in can download what is kept about them, `GET /Me/Export`, as a JSON file: their account (name, email), the Telegram chat they connected, every
organisation they are, or were, or asked to be in (with the name, phone number and unit they have there), and every booking they made, cancelled ones and
the point of contact written on each included. What they changed or cancelled of somebody else's booking is only said to have been, and when: what it was for
is not theirs. Nothing about anybody else is in it.

An admin can download everything of their organisation, `GET /t/{slug}/Export`: its settings, units, facilities, everybody in it (removed ones and phone numbers
included), every booking (cancelled ones, and who to contact, included), the invite links that were made (never the links, only a hash of each is kept),
and its history. As it has everybody's phone number, taking a copy is written in its history. Both are limited (`export`, above), and neither deletes anything:
deleting is [offboarding](docs/runbooks/offboarding.md).

### Running the system: looking at and suspending organisations

Anybody can make an organisation, so whoever runs the system can stop one being used. From the migrator, which is in the image:

```bash
dotnet Fbs.DbMigrator.dll list-tenants            # slug, name, status, how many people, how many bookings in the last day
dotnet Fbs.DbMigrator.dll suspend --tenant some-org
dotnet Fbs.DbMigrator.dll unsuspend --tenant some-org
```

Nobody in a suspended organisation can use it, its admins included: everything under `/t/{slug}` is a 403 with the code `unavailable` (the web app says it
has been paused, and to ask its admins), and it looks as if its invite links were never there, so nobody can join. Nothing is deleted, and `unsuspend`
puts it back as it was. Doing either twice changes nothing. Both exit with 1 if there is no such organisation.

**Nothing is sent for it either**, and the same goes for an organisation that is to be deleted. The outbox doesn't take messages of an organisation that isn't
active: Telegram notifications about it are skipped (marked `Skipped`, and not sent later if it is made active again, as they are about what happened then), and
calendar messages wait, untried, and go when it is active again, so its calendar catches up. Nothing is added to its calendar by the daily check either. A message
that the dispatcher had taken at the moment it was suspended is still stopped before Telegram is asked. (Replies to somebody who asks the bot something, such as
connecting Telegram, are not about an organisation, and are still answered.)

### Moving from Google

`import-legacy` copies the Users, Facilities and Nominal Roll sheets and the bookings in the calendar into the database, and
`verify-legacy` checks that the database has all of it, as it is. Both need the `Google__*` settings the API has, and
`ConnectionStrings__db`.

```powershell
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --name "3 SIB" --dry-run;   # what would happen, keeping none of it
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --name "3 SIB";             # import it, and make the tenant if it isn't there
dotnet run --project ./Fbs.DbMigrator -- verify-legacy --tenant 3sib;                            # exits 2 if anything is different
dotnet run --project ./Fbs.DbMigrator -- import-legacy --tenant 3sib --overwrite;                # replace what an earlier import saved, for the last one
dotnet run --project ./Fbs.DbMigrator -- export-legacy --tenant 3sib;                            # going back: write what was booked in the database back to Google Calendar
```

It keeps the IDs bookings have, so the events that go with them stay the same, and people who have never signed in with an account
are kept as members waiting to claim their place. Rows that can't be imported, such as a phone number that is on the Users
sheet twice (the first counts, as it always has), are left out and listed as warnings, and so are bookings by people no longer
on the sheet (kept as by a member who has left), of facilities no longer on the sheet (kept, and nobody can book them), and
bookings that overlap another. Everything is done in one transaction, so it is all imported or none of it.

`export-legacy` is for going back after the switch: it writes bookings made, changed and cancelled in the database back to
Google Calendar in the form the old version reads (in both calendars, keeping their IDs), lists any it can't, and writes only
what is still different when run again.

Running `import-legacy` again only adds what is new, so it can be run days ahead, and again just before switching. `--overwrite` replaces
what was imported with what Google has now, and is refused once anything has been done in the database, as Google is by then
out of date.

### Sheets after the switch

Until there are screens to manage people and facilities in, the Users, Facilities and Nominal Roll sheets can stay where they
are edited: `ReferenceData:Sheets:Enabled=true` (with `Storage:Provider=Database` and the `Google:*` settings) reads them every
`ReferenceData:Sheets:Interval` (5 minutes) and keeps the database in step. The sheet decides who is a member, their name and
unit, and every facility and roster entry. The database keeps what is changed in the app (who is an admin, whom someone hears
about, which Telegram chat is theirs), so those columns of the sheet no longer have any effect.

Someone taken off the Users sheet is marked as having left, and comes back by being put back on it; a facility taken off can no
longer be booked; what they made, and what was booked, is kept. An empty sheet, or taking more than
`ReferenceData:Sheets:MaxRemovedFraction` (half) of the members off at once (when there are at least five), is taken to be a
mistake, and removes nobody. Problems with rows (a phone number twice, none at all) are logged once, until they change.

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

### Metrics

The API sends metrics with OpenTelemetry to wherever `OTEL_EXPORTER_OTLP_ENDPOINT` points (with `OTEL_EXPORTER_OTLP_HEADERS` and
`OTEL_SERVICE_NAME` as usual, for a hosted collector); locally, the Aspire dashboard has them. There are those for requests (rate, duration, how many failed), the
HTTP clients and the runtime (garbage collection, the thread pool), and the ones of this system, in the meter `Fbs.WebApi`, which are the ones below. In Prometheus
a name has its dots as underscores, a unit on the end and `_total` on counters: `fbs.outbox.messages` is `fbs_outbox_messages_total`, and `fbs.db.query.duration`
is `fbs_db_query_duration_seconds`.

None is for one organisation, so the number of lines doesn't grow as they do, and the tags are ours (a type of message, an outcome), never what somebody typed. To find out
which organisation it is, look in the audit log, at the `OutboxMessage` table, or at `list-tenants`.

| Metric | Tags | What it says |
| --- | --- | --- |
| `fbs.outbox.pending` (gauge) | `type` | Messages due and not handled yet |
| `fbs.outbox.oldest_due.age` (gauge, seconds) | `type` | How long ago the oldest of them should have been handled. Growing means what is sent isn't getting there |
| `fbs.outbox.dead` (gauge) | `type` | Messages given up on and kept to see what failed (`LastError`). Anything but 0 needs looking at |
| `fbs.outbox.held` (gauge) | | Messages waiting because their organisation is suspended, or is to be deleted |
| `fbs.outbox.messages` | `type`, `outcome` | What became of each message: `done`, `retry`, `dead` (`type` is `unknown` for one given up on because its dispatcher kept stopping) or `skipped` |
| `fbs.outbox.handle.duration` (seconds) | `type` | How long a message took to handle, whether or not it worked |
| `fbs.telegram.messages` | `result` | One for each person: `sent`, `blocked` (they stopped the bot, which is normal) or `failed` (tried again later) |
| `fbs.telegram.send.duration` (seconds) | | How long Telegram took to take a message, not counting waiting to keep to its limit |
| `fbs.calendar.connections.failed` | | Calendars Google refused, which stopped being sent to |
| `fbs.calendar.connections` (gauge) | `status` | Calendars by status: `Active`, `Failed`, `Disabled` |
| `fbs.db.query.duration` (seconds) | `operation` | How long a statement took: `select`, `insert`, `update`, `delete`, `begin`, `commit`, `rollback` or `other` |
| `fbs.db.query.errors` | `operation` | Statements that failed |
| `fbs.db.transaction.retries` | `reason` | A transaction run again because the database turned it away: `lock_retry_limit`, `deadlock`, `lock_wait_timeout` or `write_conflict` |
| `fbs.db.transaction.given_up` | `reason` | The same, still turned away after the last go, which is somebody who got an error |
| `fbs.bookings.made`, `.changed`, `.cancelled` | | Bookings, one for each slot |
| `fbs.bookings.clashes` | | Requests to book or move refused because somebody else has the time |
| `fbs.quota.refusals` | `limit` | An organisation at a limit: `unit-limit`, `facility-limit`, `member-limit` or `booking-limit` |
| `fbs.auth.failures` | `reason` | Session tokens that were sent and refused: `expired`, `not_yet_valid`, `signature`, `unknown_key`, `issuer`, `azp`, `subject` or `invalid`. Having none isn't a failure |
| `fbs.webhooks.received` | `type`, `result` | Clerk's webhooks: `accepted`, `invalid_signature` or `not_configured`. A type that isn't one of Clerk's is `other`, and one that wasn't signed is `unknown` |
| `fbs.rate_limit.rejections` | `policy` | Requests refused for being too many, by which limit |
| `fbs.tenants` (gauge) | `status` | Organisations: `Active`, `Suspended`, `PendingDeletion` |
| `fbs.members` (gauge) | `status` | People in organisations, by status |
| `fbs.tenants.created`, `fbs.accounts.created`, `fbs.accounts.erased` | | Organisations made, people who signed in for the first time, and accounts erased after Clerk said they were deleted |
| `fbs.invites.used` | `outcome` | Joining with a link: `joined`, `waiting` (for an admin), `already_in`, `full`, `unusable` (ended, revoked or used up) or `not_found` |
| `fbs.claims.completed`, `fbs.telegram.linked` | | People who took over their place from before accounts, and Telegram chats connected to an account |

What is worth being told about:

- **`fbs.outbox.dead` above 0**, or **`fbs.outbox.oldest_due.age` over five minutes**, which is Telegram or Google being down, or the API not running its dispatcher. Sending is
  the part nobody sees fail.
- **`fbs.db.transaction.given_up` going up at all** is somebody who got an error, and **`fbs.db.transaction.retries` going up a lot** is the database being fought over.
  Along with **`fbs.db.query.errors`** and the 99th percentile of **`fbs.db.query.duration`** (over a second), this is TiDB not coping.
- **`fbs.calendar.connections{status="Failed"}` going up**: an organisation's calendar was refused, and stays that way until somebody puts it right.
- **`fbs.telegram.messages{result="failed"}` as a share of what is sent**. `blocked` is people who left, and isn't a problem.
- **`fbs.auth.failures{reason="unknown_key"}` or `{reason="azp"}`**: Clerk's keys aren't being fetched, or the web app is at an address `Clerk:AuthorizedParties` doesn't have. `expired` is
  normal, as people leave tabs open. **`fbs.webhooks.received{result="invalid_signature"}`** is the wrong `Clerk:WebhookSecret`, or someone trying.
- **`fbs.rate_limit.rejections` and `fbs.quota.refusals`** suddenly high: somebody is trying to fill the system, or a limit is too low for what people do.
- For whether it is being used: the rate of **`fbs.bookings.made`**, **`fbs.tenants.created`** and **`fbs.accounts.created`**, and **`fbs.tenants{status="Suspended"}`**.

The gauges are read from the database every `Metrics__DatabaseGauges__Interval` (30 seconds, and `00:00:00` turns it off), not when they are scraped, so a scrape isn't a
query, and they are the same for every instance, so with more than one, take the largest of them and not the sum. The counters are for one instance each, so add them up.
A gauge says nothing until the first look, and keeps the last one when the database can't be reached, which is what the health check is for.

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

When the database is configured, `/health` also checks that it answers, so a version that can't reach it is not put in place of
the one that is running, and says nothing more than `Healthy` or `Unhealthy`. The image carries the migrator too, so a hook that
runs in the container can apply the schema before the version starts: `dotnet Fbs.DbMigrator.dll apply`.

Nothing is sent anywhere until `OTEL_EXPORTER_OTLP_ENDPOINT` is set to a collector, such as Grafana Cloud's or one of your own, with `OTEL_EXPORTER_OTLP_HEADERS` for
whatever it needs to let it in. Then the metrics in [Metrics](#metrics) are sent, and are worth putting alerts on before the first organisation other than 3SIB is let in.

Behind a proxy such as Coolify's, the client's address and scheme come in `X-Forwarded-For` and `X-Forwarded-Proto`, which are only
believed from a proxy in a private range (`10/8`, `172.16/12`, `192.168/16`, loopback and `fc00::/7`), and only the last hop of them
(`ForwardedHeaders__ForwardLimit`, 1). Somebody who reaches the API directly can't say they are somebody else with a header. Change the
ranges with `ForwardedHeaders__TrustedNetworks__0`, `__1`, and so on, for example when a proxy is somewhere else, and raise the limit to 2 if there are
two proxies in front, such as Cloudflare and then Coolify's.

Some things are limited, so that being reachable by anyone doesn't make them a way to fill the database or to guess: making an
organisation (10 an hour, for each person), looking at and using links to join or to claim a place (30 in 10 minutes, for each person), making links for
connecting Telegram (20 in 10 minutes, for each person), downloading a copy of data (5 in 10 minutes, for each person) and Clerk's webhooks (120 a minute, for each address). More than that is a 429 with `Retry-After`.
Change one with `RateLimits__Limits__join__PermitLimit` and `RateLimits__Limits__join__WindowSeconds` (the names are
`create-organization`, `join`, `link-telegram`, `export` and `webhook`), or turn them all off with `RateLimits__Enabled=false`. People at one address, such as a whole unit
on one network, are not one person: what is for a person is counted for their account.

What an organisation can have, and make in a day, is limited too, so that one can't use up what all of them share: `Limits__MaxUnits` (50),
`Limits__MaxFacilities` (100), `Limits__MaxMembers` (500, counting those waiting to be let in and those added by phone number, and not those removed) and
`Limits__MaxBookingsPerDay` (1000 made in the last 24 hours, counting those cancelled, and counting each slot of a batch). Going over is a 403 whose error code says
which (`unit-limit`, `facility-limit`, `member-limit` or `booking-limit`), and a batch is made whole or not at all. Somebody who is refused joining with a link,
or letting somebody back in, doesn't use up the link. They are looked at just before something is made, so a number made at the same moment can go a little over.

`Maintenance__ReadOnly=true` makes the API read only: everything can be read and anything that changes something answers 503.
It is for moving the data, see [the runbook](docs/runbooks/cutover-1-database.md), which has the steps for switching from Google
to the database.

Set `Telegram__WebhookSecret` (see above) in the environment before deploying. The API registers it with
Telegram on startup and rejects bot updates that don't carry it.

Login cookies are encrypted with ASP.NET Core Data Protection keys. The Docker image keeps them in `/app/keys`, so
mount a persistent volume at that path or every redeploy generates new keys and logs everyone out.
