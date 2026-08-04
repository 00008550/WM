# WM Worker — build from the repo root:  docker build -f src/Worker/WM.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY nuget.config Directory.Build.props ./
COPY src/PluginSdk/WM.Plugins.Abstractions/*.csproj src/PluginSdk/WM.Plugins.Abstractions/
COPY src/Licensing/WM.Licensing/*.csproj            src/Licensing/WM.Licensing/
COPY src/Worker/WM.Worker/*.csproj                  src/Worker/WM.Worker/
RUN dotnet restore src/Worker/WM.Worker/WM.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Worker/WM.Worker/WM.Worker.csproj -c Release -o /app --no-restore

# Plugins are mounted at runtime (see compose), not baked into the image, so a
# customer can add a payroll/connector plugin without rebuilding.
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app

# ICU + tzdata: payroll periods and schedules are timezone- and culture-sensitive.
RUN apk add --no-cache icu-libs icu-data-full tzdata

RUN adduser -D -u 64198 wm && mkdir -p /app/plugins /app/exports && chown -R wm:wm /app
USER wm

COPY --from=build --chown=wm:wm /app .

ENV DOTNET_ENVIRONMENT=Production \
    Plugins__Directory=/app/plugins

VOLUME ["/app/plugins", "/app/exports"]

ENTRYPOINT ["dotnet", "WM.Worker.dll"]
