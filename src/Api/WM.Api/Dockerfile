# WM API — build from the repo root:  docker build -f src/Api/WM.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Restore first so the layer caches on source-only changes.
COPY nuget.config Directory.Build.props ./
COPY src/SharedKernel/WM.SharedKernel/*.csproj              src/SharedKernel/WM.SharedKernel/
COPY src/Modules/Identity/WM.Modules.Identity/*.csproj      src/Modules/Identity/WM.Modules.Identity/
COPY src/Modules/People/WM.Modules.People/*.csproj          src/Modules/People/WM.Modules.People/
COPY src/Modules/TimeAttendance/WM.Modules.TimeAttendance/*.csproj src/Modules/TimeAttendance/WM.Modules.TimeAttendance/
COPY src/Licensing/WM.Licensing/*.csproj                    src/Licensing/WM.Licensing/
COPY src/Api/WM.Api/*.csproj                                src/Api/WM.Api/
RUN dotnet restore src/Api/WM.Api/WM.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/WM.Api/WM.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Alpine ships without ICU. WM is multi-language (localisation is a first-class
# requirement), so full globalization is required — invariant mode is not an option.
RUN apk add --no-cache icu-libs icu-data-full tzdata

# Run unprivileged — the image never needs root.
RUN adduser -D -u 64198 wm && chown -R wm:wm /app
USER wm

COPY --from=build --chown=wm:wm /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "WM.Api.dll"]
