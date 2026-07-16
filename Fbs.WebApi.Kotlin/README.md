# Fbs.WebApi.Kotlin

Kotlin Spring Boot rewrite of `Fbs.WebApi` (FastEndpoints C#), preserving route paths and response/request JSON shapes as closely as possible.

## Run

```bash
cd Fbs.WebApi.Kotlin
./gradlew bootRun
```

## Build

```bash
cd Fbs.WebApi.Kotlin
./gradlew build
```

## Configuration

Set these in `src/main/resources/application.yml` or environment variables:

- `fbs.google.service-account-json-credential` (base64 service-account JSON)
- `fbs.google.spreadsheet-id`
- `fbs.google.calendar-id`
- `fbs.google.carbon-copy-calendar-id`
- `fbs.telegram.token`
- `fbs.telegram.webhook-url`
- `fbs.auth-cookie.secret`

## Endpoints

Includes equivalent endpoints for:

- Auth: `/Auth/Login`, `/Auth/Verify`, `/Auth/Logout`, `/Auth/Me`
- Booking: `/Booking`, `/Booking/{id}`
- Facility: `/Facility`, `/Facility/{name}/TimeSlots`
- Nominal roll: `/NominalRoll`
- Bot webhook: `/Bot`
- Cache purge: `/Cache/Purge`
- Admin: `/Admin/Bookings`, `/Admin/Bookings/{id}`, `/Admin/Users`, `/Admin/Users/{phone}/Admin`
