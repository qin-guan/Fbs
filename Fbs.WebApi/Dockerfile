FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Fbs.WebApi/Fbs.WebApi.csproj", "Fbs.WebApi/"]
RUN dotnet restore "Fbs.WebApi/Fbs.WebApi.csproj"
COPY . .
WORKDIR "/src/Fbs.WebApi"
RUN dotnet build "Fbs.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Fbs.WebApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN mkdir /app/keys

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Data Protection key ring (see Program.cs). Mount a persistent volume here to keep users logged in
# across redeploys. Owned by the app user so a new volume mounted over it is writable too.
COPY --from=publish --chown=$APP_UID:$APP_UID /app/keys ./keys
ENV DataProtection__KeysPath=/app/keys
ENTRYPOINT ["dotnet", "Fbs.WebApi.dll"]
