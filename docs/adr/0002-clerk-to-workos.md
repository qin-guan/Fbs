# ADR 0002: Move sign-in from Clerk to WorkOS, without anybody signing up again

- Status: Accepted
- Scope: `Fbs.WebApi`, `Fbs.DbMigrator`, `Fbs.WebApp`, deployment
- Amends: [ADR 0001](0001-clerk-multitenancy-tidb.md), where it says Clerk

## Context

Accounts are Clerk users who sign in with an email address and password, or with Google. ADR 0001 kept Clerk to login only: organisations,
memberships, roles and everything else are ours, and belong to `UserAccount.Id`, which is joined to Clerk only by `UserAccount.ClerkUserId`, the
`sub` of a Clerk session token. That is what makes moving to another provider a matter of who signs people in, not of what they have.

We are moving to WorkOS (AuthKit). It has to lose nobody's data, and nobody can be asked to sign up again or move their account themselves.

What WorkOS gives us (from its SDKs, `@workos-inc/node` and `@workos-inc/authkit-js`, and its documentation):

- Users can be made with a password **hash** (`password_hash`, `password_hash_type: bcrypt`), which is what Clerk exports, and an **external ID**.
- Signing in with Google is joined to the user with the same verified email address.
- Access tokens are RS256 JWTs from `https://api.workos.com/user_management/{clientId}`, with keys at `https://api.workos.com/sso/jwks/{clientId}`. They
  say only `sub`, `sid` and the like: anything else comes from a **JWT template**, which can use the user's email, names and external ID.
- Webhooks are signed `WorkOS-Signature: t=<ms>, v1=<hex HMAC-SHA256 of "t.body">`.
- `authkit-js` signs a single page app in on WorkOS's own pages with PKCE, and needs a custom authentication domain in production.

## Decisions

1. **Both providers at once, for a while.** The API accepts Clerk and WorkOS tokens side by side, each checked by its own JwtBearer scheme behind one
   policy scheme (`Account`), which picks by the token's issuer. Endpoints ask for `Account`, never for a provider. So the API goes first and changes
   nothing, the app switches when it is deployed, tabs left open with Clerk keep working, going back is deploying the Clerk build again, and turning
   Clerk off later is a setting.
2. **Import, with password hashes, before the switch.** `import-clerk-users` reads Clerk's CSV export, makes each Clerk user a WorkOS user with their email
   address (verified, unless Clerk lists it as unverified), names, bcrypt hash and Clerk user ID as external ID, and sets `UserAccount.WorkOSUserId`. It
   can be run again: it finds users by external ID, then by email, and makes nobody twice. Until somebody first signs in with WorkOS, Clerk is right about
   them and they are brought up to date, password included; after, WorkOS is, and they are left alone.
3. **The account is found by the provider's ID, and joined on first sight if the import didn't.** A WorkOS token is found by `WorkOSUserId`. Failing that,
   its `clerk_user_id` claim (the external ID, through the JWT template) joins it to the account with that `ClerkUserId`, but only one that isn't joined to
   another WorkOS user and wasn't erased. Only WorkOS can put that claim in a token, and only somebody with the API key can set it. Accounts are never
   joined by email address: that is WorkOS's to prove, not ours to trust.
4. **Name and email come from the token**, through the JWT template (`email`, `given_name`, `family_name`), as Clerk's custom claims did, so the API
   still never calls the provider.
5. **A Clerk deletion doesn't erase an account that has moved.** Otherwise emptying Clerk after the move would erase everybody. Deleting the user in
   WorkOS erases them, through `POST /webhooks/workos`.
6. **The app has a `workos` build**, which comes before `clerk` when both are configured, uses WorkOS's own pages for signing in and up, and loads
   `authkit-js` only in that build, as the `clerk` build does with Clerk. Where to go back to after signing in is only ever a path on this site.
7. **Schema: expand now, contract later.** `WorkOSUserId` is added (unique) and `ClerkUserId` may be empty. Dropping `ClerkUserId` waits until going back
   to Clerk is no longer wanted.

## Alternatives not taken

- **WorkOS's own importer** (`workos/migrate-clerk-users`) moves the users and passwords, but sets no external ID and knows nothing of our accounts, so they
  would have had to be joined by email address afterwards.
- **Moving people as they sign in** (asking Clerk to check a password at sign-in, and making the WorkOS user then) would mean keeping Clerk's sign-in
  working behind WorkOS's for months, and nobody who didn't sign in would ever be moved.
- **Joining by email address** in the API: an address that changed, or one that isn't theirs, would join somebody to someone else's account.
- **Switching the API and the app at once, with no overlap**: everybody signed in would be thrown out at the moment of the switch, and going back would
  be a redeploy of both.

## Consequences

- Everybody signs in once more after the switch. Sessions don't move.
- Authenticator apps set up with Clerk, passwords hashed other than with bcrypt, and other email addresses don't move (the import names whoever they are).
- Deleting your own account was in Clerk's menu. With WorkOS it is done in WorkOS's dashboard, until the app has it.
- The steps, the checks and what to do when something goes wrong are in [Cutover 3](../runbooks/cutover-3-workos.md).
