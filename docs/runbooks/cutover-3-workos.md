# Cutover 3: from Clerk to WorkOS

Today people sign in with Clerk, with an email address and password or with Google. After this cutover they sign in with WorkOS
(AuthKit) instead, **with the same email address and password, or the same Google account**, and they are the same account here, in the
same organisations, with the same bookings, Telegram link and settings. Nobody signs up again, claims anything, or is sent a link. The
only thing anyone notices is that they sign in once more, on WorkOS's page. See [ADR 0002](../adr/0002-clerk-to-workos.md).

## How nothing is lost

| What | How it moves |
|---|---|
| Passwords | Clerk exports each password as a bcrypt hash, and WorkOS takes the hash as it is, so the same password works and neither we nor WorkOS ever know it. |
| Google | WorkOS signs somebody in with Google as the WorkOS user with the same verified email address, which is the one the import made for them. Use the same Google OAuth client as Clerk (below) and Google doesn't even ask again. |
| Who they are | `import-clerk-users` makes each Clerk user a WorkOS user with their Clerk user ID as its external ID, and joins their account here (`UserAccount.WorkOSUserId`) to it. Everything here belongs to the account (`UserAccount.Id`), not to Clerk's ID or WorkOS's, so nothing else changes. |
| Anybody the import didn't join | The first time they sign in with WorkOS, their token carries their Clerk user ID (`clerk_user_id`, from the JWT template), and their account is joined then. |
| Names and email addresses | Copied to WorkOS, and kept up to date here from WorkOS's tokens from then on. |
| Deleting accounts | While both are on, a user deleted in Clerk who has moved is left as they are, so tidying up Clerk erases nobody. Deleting the user in WorkOS erases them here, as deleting them in Clerk did. |

What doesn't move:

- **Sessions.** Everybody signs in once more. A tab that was open with Clerk keeps working until it is reloaded, as long as Clerk is on in the API.
- **Authenticator apps**, if anybody set one up as a second step with Clerk. The import counts them. They sign in with their password, and set it up again
  if WorkOS asks.
- **Passwords that aren't bcrypt** (only somebody brought into Clerk from somewhere else would have one), and passwords of anybody whose address Clerk lists as
  never verified. The import names each one by Clerk user ID. They use "Forgot password" on WorkOS's page, or Google.
- **Other email addresses.** WorkOS keeps one address for a user, the Clerk primary one. Somebody who signs in with Google using another address of theirs is
  somebody new. The import names everybody with more than one.

## Before

1. **A WorkOS environment for production** (and one for staging, to rehearse), with, in the WorkOS dashboard (names as of writing; check them there):
   - Authentication: **Email + Password** on, and **Google** on, with **email verification** on (the default).
   - Google: use the **same Google OAuth client ID and secret** that Clerk's production instance uses, and add WorkOS's redirect URI (WorkOS shows it) to that
     client's authorised redirect URIs in Google Cloud. Then Google sees the same app, and doesn't ask anybody to agree again. (If Clerk used its shared
     development credentials, make a Google OAuth client for production.)
   - Redirects: the redirect URI `https://<app>/callback`, the sign-in endpoint `https://<app>/sign-in` (where WorkOS sends somebody to start signing
     in, as after resetting a password), and `https://<app>` as where to go after signing out.
   - The app's origin, `https://<app>`, allowed to call WorkOS from the browser (CORS).
   - A **custom authentication domain**, such as `auth.<domain>`, which AuthKit needs in production to keep the session in a cookie. Without one it keeps it in
     the browser's storage, which is only for `localhost`.
   - A **JWT template** for access tokens (Authentication, Sessions, Configure JWT Template):

     ```
     {
       "email": {{ user.email }},
       "given_name": {{ user.first_name }},
       "family_name": {{ user.last_name }},
       "clerk_user_id": {{ user.external_id }}
     }
     ```

     Check with the template's preview that what it makes is valid JSON, and quote the values if it isn't. `clerk_user_id` is what lets somebody the import
     didn't join be joined when they sign in. Without it they would be somebody new.
   - A **webhook endpoint** `https://<api>/webhooks/workos`, for `user.deleted`. Keep its secret.
2. **Apply the schema**, which adds `UserAccount.WorkOSUserId` and lets `ClerkUserId` be empty. It only adds, so the API that is running keeps working:

   ```sh
   dotnet Fbs.DbMigrator.dll diff
   dotnet Fbs.DbMigrator.dll apply
   ```

3. **Deploy the API with WorkOS on as well as Clerk.** Keep every `Clerk__*` setting, and add `WorkOS__ClientId` (`client_...`) and `WorkOS__WebhookSecret`.
   Nothing changes for anybody yet. Then sign in on staging with WorkOS, decode the access token (it is the `Authorization` header the app sends), and check:
   - `iss` is `https://api.workos.com/user_management/<client ID>`. If it is anything else, as it may be with the custom domain, set `WorkOS__Issuer` to it.
   - `email`, `given_name`, `family_name` and `clerk_user_id` are there.
4. **Export the users from Clerk**: the dashboard's user export, as CSV. Check that `password_digest` has something in it for people who have a password, and
   that `password_hasher` says `bcrypt`. If the digests are empty, ask Clerk's support for an export with them. **The export is everybody's email address and
   password hash**: keep it off laptops, chat and the repository, and delete it when this is over.
5. **Rehearse on staging**, with a copy of the production database, the staging WorkOS environment and its API key:

   ```sh
   export ConnectionStrings__db='...'
   export WorkOS__ApiKey='sk_test_...'
   dotnet Fbs.DbMigrator.dll import-clerk-users --export users.csv --dry-run
   dotnet Fbs.DbMigrator.dll import-clerk-users --export users.csv
   ```

   Then build the app for staging with WorkOS (below), and sign in as somebody with a password and as somebody who used Google: each should be in the
   organisations they were in.
6. **A dry run against production**, with production's API key. It reads WorkOS and the database and changes neither. Read every warning and error (they name
   people by Clerk user ID, never by email address).

## Switch

1. **Export the users from Clerk again**, as late as you can.
2. **Import them**:

   ```sh
   dotnet Fbs.DbMigrator.dll import-clerk-users --export users.csv
   ```

   It exits with 0 when everybody was moved and joined, and with 1 when somebody couldn't be; the errors say who, and what to do is below. It can be run as many
   times as needed: nobody is made twice.
3. **Deploy the app built with WorkOS**: `NUXT_PUBLIC_WORKOS_CLIENT_ID` and `NUXT_PUBLIC_WORKOS_API_HOSTNAME` set. Leaving Clerk's key set doesn't matter, as
   WorkOS comes first. Whoever loads the app from now on signs in with WorkOS.
4. **Export and import once more**, straight after, for anybody who signed up, or changed their password or name, with Clerk while the app was being deployed.
   Somebody who hasn't signed in with WorkOS yet is brought up to date with Clerk, password included. Somebody who has is left as they are, so a password they
   set with WorkOS is never put back to their old one.
5. **Watch**, for the first day:
   - `fbs.auth.failures{provider="workos"}`: `issuer` or `audience` is the API's WorkOS settings not matching the tokens. Nobody can sign in until it is put right.
   - `fbs.accounts.created`: a jump is people being made new accounts instead of being joined to theirs. Check the JWT template.
   - `fbs.accounts.moved{when="sign_in"}`: people the import didn't join, joined as they signed in. A few is somebody who signed up during the switch.

## Afterwards

1. **About two weeks later**, when nobody has had the old app open for a while, turn Clerk off in the API: remove `Clerk__Issuer` (and the other `Clerk__*`
   settings). Clerk's tokens are then refused, and `/webhooks/clerk` is gone.
2. Then, in Clerk, **delete the webhook endpoint first**, and only after that delete users or the instance, if you want to.
3. Delete the export.
4. Somebody who wants their account deleted: delete their user in WorkOS's dashboard, and the webhook erases them here, as Clerk's did. (Clerk's menu let people
   do it themselves; the app's own menu doesn't yet.)
5. In a later change, once going back is no longer wanted: remove the Clerk build of the app and `@clerk/*`, `Fbs.WebApi/Auth/Clerk`, its webhook and settings,
   and, with `apply --allow-destructive`, `UserAccount.ClerkUserId`.

## Going back

Until Clerk is turned off in the API, going back is building the app without `NUXT_PUBLIC_WORKOS_CLIENT_ID` and deploying it. Accounts are joined both ways,
so whatever anybody did after the switch is in the account they get with Clerk too. What doesn't go back: passwords changed with WorkOS, and people who signed
up with WorkOS after the switch, who have no Clerk user. Moving forward again is running the import and deploying the WorkOS build again.

## If something goes wrong

- **"the WorkOS user ... with their email address is another Clerk user's"**: a WorkOS user with that address already has a different Clerk user ID. Two
  Clerk users can't have one address, so this is an export from a different Clerk instance than before, or a WorkOS environment used for something else.
  Find out which is right, put the right external ID on the WorkOS user (or delete it if it shouldn't be there), and run the import again.
- **"... has an account here of its own ..., made when they signed in with WorkOS before this joined them"**: they signed in with WorkOS before they were
  joined, without `clerk_user_id` in their token (the JWT template didn't have it yet, or they signed up afresh), so they were made a new account. If that
  account belongs to no organisation (`SELECT * FROM TenantMember WHERE UserId = '<its id>'` finds nothing, and nothing else refers to it: `TelegramLink`,
  `MemberClaimToken`, `Tenant.CreatedByUserId`), delete it (`DELETE FROM UserAccount WHERE Id = '<its id>'`) and run the import again, and they are joined to
  the account they had. If it belongs to something, move that to the account they had first.
- **"their account here is joined to another WorkOS user"**: their account was joined to one WorkOS user and the export now names another. Find out which is
  theirs before changing anything.
- **"accounts here are of Clerk users who aren't in the export"**: people who signed in here and are no longer in Clerk, so would be somebody new with WorkOS.
  Either they deleted their Clerk account and the webhook didn't arrive (erase them in the database as the webhook would have), or the export is from the wrong
  Clerk instance (stop, and export from the right one).
- **Somebody can't sign in with their password**: it wasn't moved (the import named them) or they changed it with Clerk after the last import. "Forgot
  password" on WorkOS's page, or Google, gets them in, as the same account.
- **Somebody signed in with Google and is in no organisation**: they used an address other than their Clerk primary one, and WorkOS made a new user. Delete
  that WorkOS user, and their account here as above; they sign in with Google with their primary address, or an admin adds them again.
