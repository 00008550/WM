# WM Workforce Management

Modern workforce management platform: **.NET 9 modular monolith API + Angular SPA**, with real-time attendance, signed licensing, and a plugin architecture. Architecture plan: `../TlwNext/docs/ARCHITECTURE.md`.

## Stack

- **API** (`src/Api`): ASP.NET Core 9 minimal APIs, module system (`IModule`), Serilog, Swagger, SignalR
- **Modules** (`src/Modules`): Identity (JWT + rotating refresh tokens, roles в†’ fine-grained permissions), People (sites/departments/employees), TimeAttendance (punches, live presence, timesheets)
- **Data**: EF Core 9 + PostgreSQL 17, one schema per module, per-module migrations
- **Messaging**: Kafka (`wm.punches` event stream) + RabbitMQ/MassTransit (Worker jobs)
- **Worker** (`src/Worker`): plugin host (collectible `AssemblyLoadContext`), payroll export consumer, Kafka punch consumer
- **Licensing** (`src/Licensing`): ECDSA P-256 signed license documents + generator CLI
- **Plugin SDK** (`src/PluginSdk`): `IPayrollExportPlugin`, `IConnectorPlugin`, `INotificationChannelPlugin` вЂ” see `plugins/payroll/WM.Plugins.DemoPayroll`
- **Frontend** (`frontend/portal`): Angular 19 standalone + signals, Tailwind, "Control Room" design system (dark-first tokens, Space Grotesk / IBM Plex), SignalR live dashboard

## Run it

```bash
# 1. infrastructure (PostgreSQL, Redis, RabbitMQ, Kafka)
cd deploy && docker compose up -d

# 2. API вЂ” applies migrations + seeds demo data in Development
dotnet run --project src/Api/WM.Api --launch-profile http   # http://localhost:5200 (Swagger at /swagger)

# 3. frontend
cd frontend/portal && npx ng serve                          # http://localhost:4200

# 4. worker (optional вЂ” needs RabbitMQ up)
dotnet run --project src/Worker/WM.Worker
```

Dev login: `admin` / `Admin!234567` (seeded, Development only вЂ” change it).

## License tooling

```bash
dotnet run --project src/Licensing/WM.Licensing.Generator -- keygen
dotnet run --project src/Licensing/WM.Licensing.Generator -- sign wm-license-private.pem spec.json
```

## Status

Phase 0/1 foundation (see architecture doc В§11): auth, people, punches, live dashboard, licensing lib, plugin SDK + demo plugin. Next: scheduling, absence workflows, rules engine, device gateway, Flutter app.
