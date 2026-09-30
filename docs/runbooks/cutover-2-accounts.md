# Cutover 2: from Telegram codes to accounts with Clerk

This moves sign-in from a code sent on Telegram to Clerk, and lets people take over the places they were carried over with.
It comes after [Cutover 1](cutover-1-database.md), and needs `Storage__Provider=Database`. See
[ADR 0001](../adr/0001-clerk-multitenancy-tidb.md).

## What changes when it is switched

- People sign in with Clerk, and belong to organisations by what they are in the database: members, and admins.
- Everyone who was in the old version has a place already (imported as **unclaimed**), and takes it over by opening a link in
  the Telegram chat that was linked to them. That is what shows the place is theirs.
- Whoever takes over a place is a **member**, even if they were an admin: the chat that was linked to a number can't be relied
  on for that. The first admin is made from the command line, below.

## Before

1. A Clerk instance for production, on a domain whose DNS is ours, with `email` and `name` added to the session token as custom
   claims (`{{user.primary_email_address}}`, `{{user.full_name}}`).
2. Set on the API: `Clerk__Issuer` (Clerk's Frontend API), `Clerk__AuthorizedParties__0` (the origin of the app), and
   `Telegram__BotUsername` (the bot's name without the @, or leave it out and the API asks Telegram once).
3. The app that uses Clerk deployed, with a page at `/claim/3sib` that asks for `GET /Claims/3sib` and then
   `POST /Claims/3sib/Start`, and shows the link it returns.

## Switch

1. Check the organisation is set up to be claimed: `GET /t/3sib/Settings` says `legacyClaimEnabled: true` (the import turns it on) and the
   members are `Unclaimed`.
2. Tell everyone, in the chats the bot already has, to go to `/claim/3sib`, sign up or in, and open the link they are given
   **from the same Telegram account as before**. The bot says who they are now, and they can sign in.
3. When the first people have taken over their places, make the admins. They have to have taken their places first:

   ```sh
   dotnet Fbs.DbMigrator.dll promote-admin --tenant 3sib --phone +6591234567
   ```

   The number can be written as it was on the sheet (`6591234567`), with a plus, or as it would be typed in the country.
   From then on an admin can let others in, add people, and manage facilities and units in the app.
4. Somebody who says their link doesn't work was probably in another chat than the one they were linked to. An admin can add them by phone number
   (`POST /t/3sib/Members`) or send them an invite link.

## Afterwards

- **About three months on**, turn claiming off: an admin sets `legacyClaimEnabled` to `false` in the settings (or it is
  `UPDATE Tenant SET LegacyClaimEnabled = 0`). It can't be turned on from the app, so it can't be turned back on by somebody who has taken over an
  admin's account. People who never claimed are found by phone number as any other member added by an admin is.
- Nothing is written to the old places any more once the people who matter have taken over: the phone number and Telegram code sign-in can be turned off
  when the app no longer uses it.

## If something goes wrong

- A place claimed by the wrong person: an admin removes them (`PUT /t/3sib/Members/{id}` with `membership: Removed`), adds the right person by phone number and lets them claim again by
  setting `UserId` to null and `Status` to `Unclaimed` (a database change, as it should be rare).
- A chat that is linked to two places is refused, so nobody is given a place that might not be theirs. Fix the `LegacyChatId` of the wrong one.
