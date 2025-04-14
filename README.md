# Facility Booking System

## Get started

You need:

* .NET 9
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
```

You need to provide the service account with access to the facility spreadsheet.

Start the AppHost project:

```powershell
dotnet run --project ./Fbs.AppHost/Fbs.AppHost.csproj;
```
