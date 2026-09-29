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

Bookings and users still come from Google. The database is where they are moving to (see `docs/adr/`), on
[TiDB](https://www.pingcap.com/tidb/) through SqlSugar. Its schema is applied with the migrator, which the API never
does itself:

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
