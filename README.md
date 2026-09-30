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
