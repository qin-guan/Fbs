# Cutover 2: from Telegram codes to Clerk accounts

Today people sign in with their phone number and a code the bot sends them on Telegram. After this cutover they sign in with
a Clerk account instead. Run it after [Cutover 1](cutover-1-database.md), with `Storage__Provider=Database`. See
[ADR 0001](../adr/0001-clerk-multitenancy-tidb.md).

## What claiming is for

Cutover 1 imported everyone from the old version as a member of the `3sib` organisation, with status `Unclaimed`. That
member row keeps their name, phone number, unit and notification settings, but has no Clerk account attached (`UserId` is
null). It also keeps `LegacyChatId`: the Telegram chat the old version sent their login codes to.

**Claiming attaches a person's new Clerk account to their imported member row**, so they keep their unit and settings
instead of being added again by hand. It works like this:

1. They sign in with Clerk and open `/claim/3sib` in the app.
2. The app calls `POST /Claims/3sib/Start` and shows the link it returns: `https://t.me/<bot>?start=claim_<token>`. The
   link works once, for 10 minutes.
3. They open the link in Telegram, so their Telegram account sends `/start claim_<token>` to the bot from its private chat.
4. The bot looks for the unclaimed member in `3sib` whose `LegacyChatId` is that chat. If there is exactly one, it attaches
   the Clerk account to it, makes it `Active`, and replies "Welcome back". That chat also becomes where they get booking
   notifications, unless their account already has a chat connected.

Opening the link from the chat is the proof of identity: only the person with that Telegram account can send from it. They
never type a phone number, so knowing someone's number is not enough to claim their row.

**A claim never makes someone an admin**, even if they were an admin in the old version. The old bot let anyone link their
chat to someone else's phone number (finding 1 in the ADR), so `LegacyChatId` is good enough to identify a member, but not
to hand out admin rights. You make the admins yourself, from the command line (step 3 below).

## Before

1. A Clerk instance for production, on a domain whose DNS is ours, with `email` and `name` added to the session token as custom
   claims (`{{user.primary_email_address}}`, `{{user.full_name}}`).
2. In Clerk, add a webhook endpoint `https://<api>/webhooks/clerk` subscribed to `user.deleted`, and copy its signing secret.
   Set on the API: `Clerk__WebhookSecret` (the `whsec_...`), `Clerk__Issuer` (Clerk's Frontend API), `Clerk__AuthorizedParties__0` (the origin of the app), and
   `Telegram__BotUsername` (the bot's name without the @, or leave it out and the API asks Telegram once).
3. The app that uses Clerk deployed, with a page at `/claim/3sib` that asks for `GET /Claims/3sib` and then
   `POST /Claims/3sib/Start`, and shows the link it returns.

## Switch

1. Check that `3sib` can be claimed: `GET /t/3sib/Settings` returns `legacyClaimEnabled: true` (the import turns it on), and
   the members have status `Unclaimed`.
2. Message everyone through the bot: go to `/claim/3sib`, sign in, and open the link you are given **from the Telegram
   account you used to get login codes**.
3. Once the admins have claimed, make them admins again:

   ```sh
   dotnet Fbs.DbMigrator.dll promote-admin --tenant 3sib --phone +6591234567
   ```

   The phone number can be written as in the old sheet (`6591234567`), with a `+`, or as a local number (`9123 4567`). The
   command fails if that member hasn't claimed yet. From then on, admins can add, approve and manage people in the app.
4. If someone's link says "This chat is not linked to anyone", they opened it from a different Telegram account than the
   one the old version knew. An admin can add them by phone number (`POST /t/3sib/Members`) or send them an invite link.

## Afterwards

- **About three months later**, turn claiming off: an admin sets `legacyClaimEnabled` to `false` in the settings (or run
  `UPDATE Tenant SET LegacyClaimEnabled = 0`). The app can only turn it off, never back on, so someone who has taken over
  an admin's account can't reopen claiming. People who never claimed can still be added by phone number like anyone else.
- When the app no longer uses it, turn off the phone number and Telegram code sign-in.

## If something goes wrong

- **The wrong person claimed a member.** An admin removes them (`PUT /t/3sib/Members/{id}` with `membership: Removed`).
  To let the right person claim it instead, set that row's `UserId` to null and `Status` to `Unclaimed` in the database.
- **Two members share a `LegacyChatId`.** Neither can be claimed, because the bot can't tell which one the person is.
  Correct the `LegacyChatId` of the wrong row in the database.
