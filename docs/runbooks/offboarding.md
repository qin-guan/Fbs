# Offboarding: deleting an organisation, and erasing what is asked to be erased

An admin can delete their organisation, and it is deleted for good some days later. See
[ADR 0001](../adr/0001-clerk-multitenancy-tidb.md) for why it is in two steps.

## What happens

1. An admin asks (`POST /Tenants/{slug}/Deletion`, from the settings page), typing the address of the organisation. It becomes
   **pending deletion**: nobody in it can use it, its invite links stop working, and nothing is deleted. The people in it are told, and any
   admin of it can restore it. It is written in its history.
2. `Limits__DeletionGraceDays` (30) days later it is **due**. Nothing happens by itself: `purge-tenants` deletes the ones that are due, and
   everything of them (bookings, people, facilities, units, links, the history, the outbox, the calendar connection). Accounts stay, and
   so do the chats people connected, which are theirs. What was copied to somebody's Google Calendar stays there: it is theirs.
3. Until it has been purged, an admin can still restore it, even after it was due.

## Run the purge on a schedule

Once a day is enough. On Coolify, a scheduled task in the API's container, or the migrator's:

```bash
dotnet Fbs.DbMigrator.dll purge-tenants
```

It logs what it deleted, by organisation and by kind, and "No organisation is due to be deleted." when there is none. It is safe to run twice, or
at the same time as somebody restoring: an organisation is locked and looked at again before it is deleted, so one that was just restored stays.

To see what it would do first:

```bash
dotnet Fbs.DbMigrator.dll purge-tenants --dry-run
```

## Somebody has to be erased sooner

For a request to have an organisation's data erased before the days are up (a request under the PDPA, say):

1. Have an admin of the organisation ask for it to be deleted in the app. Nothing is deleted for an organisation that hasn't been asked to be.
2. `dotnet Fbs.DbMigrator.dll purge-tenants --tenant <slug> --dry-run`, to see what will go.
3. `dotnet Fbs.DbMigrator.dll purge-tenants --tenant <slug> --early`.

It exits with 1 if there is no such organisation, or it isn't to be deleted, or (without `--early`) it isn't due yet.

There is no purge of one person apart from their Clerk account being deleted, which erases their name, email and chat but keeps the bookings they
made as a former member's (see the README).

## Things to know

- The address is free to be used again once it is purged, by anybody. Nobody from before is in the new organisation.
- The purge is a deletion of rows by the organisation's ID, in one transaction for each organisation. If a new table with a `TenantId` is
  added and left out of `TenantPurges.TenantOwned`, a test fails.
- Nothing is kept of a purged organisation but the log lines. Take a backup first if it may be wanted again: there is no undoing it.
- To stop an organisation being used without deleting it, suspend it instead (`suspend --tenant <slug>`, see the README).
