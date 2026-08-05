# WM — Architecture & TLW Coverage Plan

**Workforce Management Platform — .NET 9 + Angular rebuild of TLW**
*v2.0 — 2026-07-20 (supersedes the TlwNext draft)*

> This document describes the **target architecture** for WM.
>
> For **what the legacy product actually does**, read **[`TLW-INVENTORY.md`](./TLW-INVENTORY.md)** —
> an exhaustive, measured inventory (317 projects, 59 services, 247 entities, ~230 screens,
> 67 reports, ~70 notification types). It is the source of truth for scope; this document is
> the source of truth for design. Section 13 here is the coverage matrix, section 14 the
> roadmap rebased on the measured scope.

---

## 0. What changed in v2

- **Renamed** TlwNext → **WM**; project lives at `E:\Work\GitProjects\WM` (private repo `github.com/00008550/WM`).
- **Physical devices dropped** (user decision 2026-07-20): no Suprema / SyFace / Salto / ANPR / thermal-face / fingerprint / biometric hardware, and **no Device Gateway**. The **phone app is the only "device"**. Punches come from web + the future Flutter app (geofenced, offline-queued).
- **Three modules promoted from footnotes to first-class**, because the TLW rescan showed they are core, not cosmetic:
  - **Documents** (employee docs, company docs, onboarding packs, e-signature) — TLW's "HR" is largely a document system.
  - **Notifications** — a real, event-driven hub (in-app via SignalR + email with templates & per-user preferences + mobile push). See §9.
  - **Expenses** (mileage & claims with approval).
- Domain modules expanded with everything the rescan surfaced: cost centres, tariffs/pay rates, tips, job sheets, flexi balances, holidays, population groups, rounding rules, pay periods, corrections, exception setup, daily/weekly shift models, auto-planning, QR punching, geolocation, employee custom fields, emergency contacts, fire-marshal/mustering.

---

## 1. Goals

Rebuild TLW (Time & Labour Workforce) as a modern, API-first platform:

- **.NET 9 Web API + Angular SPA** — replaces ASP.NET MVC / Razor / Silverlight.
- **API-first** so a future **Flutter mobile app** consumes the exact same API as the web UI.
- **Security from day one** — modern auth, RBAC + fine-grained permissions, audit, OWASP hardening.
- **Licensing** — signed license keys with feature packages & limits (successor to `Licensing` + `WebLicenseManager`).
- **Plugin architecture** — payroll exports, connectors, custom reports, notification channels as installable plugins (TLW's business model: ~60 per-customer payroll plugins).
- **Message-driven core** — RabbitMQ for commands/jobs, Kafka for the event stream (punches, clockings, audit, notifications).
- **A "wow" UI** — custom design system on Tailwind, motion design, real-time dashboards.

---

## 2. High-Level Architecture

**Modular monolith API + a few real services.** Not 40 microservices — that one-Windows-service-per-integration sprawl is what made old TLW painful to run. Modules live in one deployable API, strictly isolated (own schema, talk only via contracts/events); only things that must scale or fail independently are separate services.

```
                        ┌───────────────────────────────┐
   Angular SPA ───────► │        API Gateway (YARP)     │ ◄─────── Flutter app (later)
   (web)                └──────────────┬────────────────┘
                                       │
        ┌──────────────────┬───────────┴─────────┬────────────────────┐
        ▼                  ▼                     ▼                    ▼
  ┌───────────┐     ┌────────────┐        ┌────────────┐       ┌────────────┐
  │ Identity  │     │  Core API  │        │  Worker    │       │ Licensing  │
  │ (auth,    │     │  (modular  │        │ (jobs +    │       │  Service   │
  │  tokens,  │     │  monolith) │        │  plugins + │       │ (keys,     │
  │  2FA, SSO)│     │            │        │  notifiers)│       │  features) │
  └───────────┘     └─────┬──────┘        └─────┬──────┘       └────────────┘
                          │                     │
              RabbitMQ ◄──┴──► Kafka ◄──────────┘
             (commands,      (event stream:
              jobs, retry,    punches, clockings,
              notify)         audit, notifications,
                              integration)
        ▼                  ▼                     ▼
  ┌───────────┐     ┌────────────┐        ┌────────────┐
  │ PostgreSQL│     │  Redis     │        │ Object     │
  │ + read    │     │ (cache,    │        │ storage    │
  │  replicas │     │  SignalR   │        │ (documents,│
  │           │     │  backplane)│        │  exports)  │
  └───────────┘     └────────────┘        └────────────┘
```

### Deployables

1. **ApiGateway** — YARP: routing, rate limiting, TLS, WebSocket pass-through.
2. **Identity** — OpenIddict OAuth2/OIDC. JWT access + rotating refresh tokens, TOTP 2FA, external SSO (Entra ID / Google / SAML — replaces TLW `Logic/SSO`), password policy, lockout. *(WM today ships a self-contained JWT module; OpenIddict is the Phase-3 upgrade.)*
3. **CoreApi** — the modular monolith (see §8 for the module list).
4. **Worker** — background jobs (Quartz.NET/MassTransit scheduler) + plugin execution (payroll exports, connectors, report generation) + **notification dispatch** (email/push). Consumes RabbitMQ, produces/consumes Kafka.
5. **LicensingService** — issue/validate/revoke + customer portal (successor to `WebLicenseManager.*`).

All containerized (Docker Compose for dev, Kubernetes-ready for prod). **Object storage** (MinIO in dev, S3/Azure Blob in prod) is added for the Documents module and generated report/export files.

### Tech stack

| Concern | Choice |
|---|---|
| Backend | **.NET 10 (LTS)**, C# 14, ASP.NET Core Minimal APIs, module system (`IModule`) |
| ORM | **EF Core 10** + Npgsql. **No Dapper** — see §14 decision 4; raw SQL where measured, via `FromSql`/`SqlQuery<T>`/`ExecuteUpdate` |
| Database | **PostgreSQL 17** (schema per module) |
| Object storage | MinIO (dev) / S3 or Azure Blob (prod) — documents, exports, report output |
| Cache / realtime backplane | Redis |
| Commands & jobs | **RabbitMQ** via **MassTransit** (retry, outbox, sagas) |
| Event streaming | **Kafka** (Confluent.Kafka) — punch/clocking log, audit, notifications, integration feed |
| Realtime to UI | SignalR (WebSockets) — live boards, notification centre, mustering |
| Frontend | Angular 22 (standalone, signals, zoneless), Tailwind, "Control Room" design system |
| Charts/viz | ECharts |
| Mobile (later) | Flutter, same OpenAPI + SignalR + FCM push |
| Reporting | QuestPDF + ClosedXML (DevExpress optional later) |
| Observability | OpenTelemetry, Serilog |
| CI/CD | GitHub Actions, Docker, Trivy scans |

> **Runtime support (2026-08-04):** .NET 9 is STS and its support window closed around May 2026.
> WM ships **on-prem to customer servers**, where upgrades are infrequent and outside our control,
> so an unsupported runtime is not acceptable. .NET 10 is LTS with a 3-year window that matches the
> deployment model. The dev box already carries SDK 10.0.302 / runtime 10.0.10.
>
> Node note: dev box is Node **24.18**, so no Angular ceiling. (The old "Node 20.17 limits us to
> Angular 19" note is obsolete.)

---

## 3. Solution Layout (target)

```
WM/
├─ src/
│  ├─ Api/WM.Api                      host: DI, auth, module bootstrap, SignalR hubs
│  ├─ SharedKernel/WM.SharedKernel    IModule, Entity, events, security, multitenancy
│  ├─ Modules/
│  │  ├─ WM.Modules.Identity          ✅ built
│  │  ├─ WM.Modules.People            ✅ built (employees, sites, departments)
│  │  ├─ WM.Modules.TimeAttendance    ✅ built (punches, live presence, timesheets)
│  │  ├─ WM.Modules.Scheduling        ▢ planned (rotas, shift models, auto-planning)
│  │  ├─ WM.Modules.Absence           ▢ planned (requests, approvals, entitlements, holidays)
│  │  ├─ WM.Modules.Rules             ▢ planned (hours calc, OT, accruals, counters, tariffs)
│  │  ├─ WM.Modules.Documents         ▢ planned (employee/company docs, onboarding, e-sign)
│  │  ├─ WM.Modules.Notifications     ▢ planned (in-app + email + push hub) — see §9
│  │  ├─ WM.Modules.Expenses          ▢ planned (mileage, claims, approval)
│  │  ├─ WM.Modules.Reporting         ▢ planned (definitions, scheduled exports, plugin reports)
│  │  ├─ WM.Modules.Safety            ▢ planned (mustering, fire roll-call, fire marshals)
│  │  ├─ WM.Modules.Visitors          ▢ planned (later phase)
│  │  └─ WM.Modules.Admin             ▢ planned (settings, audit browser, licenses, plugins)
│  ├─ Worker/WM.Worker                ✅ built (MassTransit + Kafka + plugin host)
│  ├─ Licensing/
│  │  ├─ WM.Licensing                 ✅ built (ECDSA codec + validation)
│  │  └─ WM.Licensing.Generator       ✅ built (keygen/sign/verify CLI)
│  └─ PluginSdk/
│     ├─ WM.Plugins.Abstractions      ✅ built (payroll/connector/notification contracts)
│     └─ WM.Plugins.TestKit           ▢ planned (author harness w/ fake data)
├─ plugins/
│  ├─ payroll/WM.Plugins.DemoPayroll  ✅ built (reference CSV export)
│  ├─ payroll/…, connectors/…, reports/…   ▢ planned
├─ frontend/portal                    ✅ Angular app (login, shell, dashboard, employees)
├─ mobile/                            ▢ future Flutter app
├─ deploy/                            ✅ docker-compose (postgres, redis, rabbitmq, kafka)
└─ docs/ARCHITECTURE.md               ← this file
```

Legend: ✅ built · ▢ planned.

---

## 4. Security

- **Auth**: OAuth2/OIDC (Auth Code + PKCE for SPA/mobile; client credentials for integrations). Short-lived JWT access tokens + rotating refresh tokens with **reuse detection** (family revocation on theft). TOTP 2FA, WebAuthn/passkeys, lockout, breached-password check, strong hashing. Enterprise SSO (Entra ID, Google, SAML). *WM today: self-contained JWT + refresh rotation + lockout are live; OpenIddict/2FA/SSO are the Phase-3 upgrade.*
- **Authorization**: **one object, one membership.** A **security group** carries per-screen rights (none / read / edit), employee data scope, a mode (`Normal` / `SelfOnly`) and `CanEditOwnRecord`. **A user belongs to exactly one group**, non-null — the built-in `Employee` group is the floor. This mirrors TLW, where `/Groups` edits a single `dbo.Role` holding rights and all managed dimensions together, and where a user holds exactly one role (`AuthorizationService.cs:161` selects with `SingleOrDefault`; `AddUserToRole` deletes the prior assignment). Scope is applied as query filters, never per endpoint. See [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md) for the measured legacy model and [`SCREEN-TREE.md`](./SCREEN-TREE.md) for the tree. **Multi-tenancy-ready** (tenant id + filter).
  > **Why exclusivity is part of the design, not an accident of it** (decided 2026-08-05). Legacy can put rights and scope in one object *because* membership is exclusive: there is never a union to resolve, no precedence to remember, and `IsSelfOnly` is a mode rather than an override that must beat a grant. Conflating the objects while allowing several memberships is the worst of the available shapes — it forces WM to answer questions legacy never poses, and every answer becomes a rule somebody has to hold in their head. If exclusivity later proves too rigid, a group may be **composed from another at edit time** (copy-on-write, as TLW's `DuplicateGroup` does) without reintroducing runtime resolution.
  > **Rights resolve fail-closed**: a new group denies everything, `edit` implies `read`, and an ancestor branch gates its children. Legacy's deny-overrides-allow loop exists but is dead in practice, because exclusivity means at most one role ever matches.
  > Naming note: TLW's `SecurityGroup` is a *different, physical* concept — which employees may open which door readers. That is out of scope for WM and the name is deliberately not reused. Likewise TLW's `SiteStructure` is the application's page tree, not the site/department hierarchy.
- **Audit**: every mutation emits an audit event to Kafka `wm.audit`; an append-only, hash-chained store makes it tamper-evident and queryable (replaces `Logic/AuditTrail`). Admin module ships an audit browser.
- **Hardening**: CSP, HSTS, secure cookies, gateway rate limiting, FluentValidation, secrets in env/Key Vault, field-level encryption for special-category data (GDPR), dependency + SAST scanning in CI.

---

## 4A. Users, Access & Self-Service

This is how TLW is actually *used*: a person signs in as a **user account**, and what they can do depends on their **user type** and whether that user is **linked to an employee record**. WM reproduces this exactly (TLW: `Logic/Users`, `Logic/UserProviders`, `Logic/Security/AccessControl`).

### The user ↔ employee link
- A **User** (login, credentials, roles) optionally carries an **`EmployeeId`** linking it to one **Employee** (people record). WM's `Identity.User` already has this field. (TLW: `User.EmployeeId`, `UserFromEmployeeIdProvider`, `GetUserByEmployeeId`, `UpdateUserNameFromEmployee`.)
- **Not every employee has a login** (shop-floor staff may only badge in), and **not every user is an employee** (a head-office admin need not be). The link is optional and one-to-one.
- Creating a user *from* an employee pre-fills name/email and sets the link (TLW `UpdateUserNameFromEmployee`).

### User types (built-in roles) — TLW `BuiltinRoles`
| Type | Sees / does |
|---|---|
| **Administrator** | Everything: users, settings, all employees, licensing, plugins. |
| **Manager** | Scoped to the employees/departments they manage (row-level scope on the site hierarchy). Approves absences/timesheets, views team dashboards. TLW: `ManagedDepartmentsAuthorizer`, `ManagedRelatedEmployeeRecordsAuthorizer`, `RoleBasedEmployeeFilterService`. |
| **Employee** | **Self-service on their own linked record only**: their timesheet, punches, absence requests, documents to sign, expenses. Cannot see other employees. |

These three are **built-in security groups**, not a separate "role" concept — see §4. An Administrator can define custom groups (e.g. "Payroll Officer") carrying their own screen rights and data scope. A user belongs to **exactly one** group; there is no layering, because there is no union to resolve. To give someone the Payroll Officer rights *and* northern-region scope, you author a group that carries both — copy an existing group and edit it, rather than granting two.

### Self-service (`/api/me/*`)
An employee-linked user with limited permissions gets a **self-service surface** scoped to *their own* employee id (never an arbitrary one — the id comes from their token, not the request):
- `GET /api/me` — profile + linked employee summary + permissions.
- `GET /api/me/timesheet` — **their own** timesheet.
- `GET /api/me/punches` — their own recent punches; `POST /api/me/punch` — punch themselves in/out (geofenced on mobile).
- Later: `/api/me/absences`, `/api/me/documents` (view + e-sign), `/api/me/expenses`.

This is what makes WM "usable as TLW": create an employee, create a user linked to that employee with the Employee role, and that person logs in to a self-service portal showing only their data. Managers get the team view; admins get everything. The **web UI and Flutter app share these same `/api/me/*` endpoints.**

### User management (`/api/users/*`, permission `users.manage`)
Admin CRUD: list/search users, create (optionally linked to an employee, assigned to one security group), edit group/active state/employee link, reset password, deactivate. **User action logging** (TLW `UserActionLogsService`) feeds the audit stream. Password reset and invite emails go through the Notifications hub (§9).

### Access enforcement
- **Group → screen rights → policies** for *what actions* a user may perform. (Permissions are live in WM today as a per-role claim set; plan 005 merges roles into the group and plan 004 replaces the flat permission list with tri-state screen rights.)
- **Row-level scope** for *which records* — a Manager's queries are filtered to their managed departments/employees; an Employee's to their own id. Implemented as EF Core query filters keyed on the resolved scope (successor to TLW `RoleBasedEmployeeFilterService` and the `dbo.AccessControlEntry` rights store — note `FormAccess` and `SiteItemPermission` are C# types, not tables, and `dbo.WebPages` is localization).
- **2FA** (TOTP + email code — TLW `TwoFactorAuthenticationService`/`TwoFactorEmailCodeSender`) at sign-in for privileged users.

---

## 5. Licensing

- **License = signed JSON document** (ECDSA P-256; WM already implements this). Fields: customer, edition, **feature flags** (`scheduling`, `absence`, `documents`, `expenses`, `visitors`, `plugin:payroll.*`…), **limits** (max employees, max sites, max users), validity window, grace period.
- Private key only in the Licensing Service; products embed the public key → **offline verification**, no phone-home to boot.
- **Online activation** binds a license to an installation id (optional machine fingerprint) enabling revocation/floating counts; **offline activation** file exchange for air-gapped sites.
- Feature gates: server-side `ILicenseFeature` checks **and** token claims so the UI hides unlicensed modules (UI hiding is UX; server always re-checks).
- **Per-plugin licensing**: each plugin id is a licensable feature — mirrors selling payroll plugins per customer.

### Modules are the licensing unit

**A navigation branch is a licensable module** (see [`SCREEN-TREE.md`](./SCREEN-TREE.md)), so a customer can be shipped exactly what they need. An unlicensed branch is **absent** — missing from the navigation, refused by the API, and not offered in the group editor so nobody can grant rights to something the customer has not bought.

This turns the module rule from a design preference into a constraint that has to hold: **if Scheduling is licensable, no core branch may hard-depend on Scheduling types.** Cross-module access stays on contracts and events, and anything shared moves to SharedKernel. Getting this wrong is only discovered when a customer buys a subset, which is the worst time to find out — so it is checked as modules are built, not after.

---

## 6. Plugin Architecture

TLW's commercial core. Backend plugins (.NET) load in their own collectible **`AssemblyLoadContext`** in the **Worker only** (a crashing plugin can't take the API down), verified against signature + license before load. Manifest (`plugin.json`): id, version, type, required core range, required license feature, JSON-Schema config (→ auto-generated Angular settings UI, which is what keeps 60 payroll plugins cheap).

Contracts in `WM.Plugins.Abstractions` (✅ the payroll/connector/notification ones exist today):
- `IPayrollExportPlugin` — calculated timesheets → payroll file/API (replaces ~60 `*PayrollPlugin`).
- `IConnectorPlugin` — scheduled two-way sync with external HR/scheduling systems (replaces the `Horio*IntegrationService` zoo: RotaGeek, HotSchedules, SageHR, Staffology, Lucca, Cegid, ADP, PeopleHR, Evalu8, PeopleFirst…).
- `IReportPlugin` — custom report datasets/templates (replaces `*CustomReportPlugin`).
- `INotificationChannelPlugin` — email/SMS/Teams/webhook/push channels used by the Notifications module (§9).

Frontend plugins that need real screens: Angular lazy remote modules via Native Federation; the shell reads routes/menu entries from the manifest at runtime.

---

## 7. Messaging: RabbitMQ + Kafka

**RabbitMQ (MassTransit) — "do this"**: commands & jobs (run payroll export, generate report, **send notification**, sync connector), retry/DLQ, saga orchestration (payroll close: lock period → calculate → export → notify), transactional outbox.

**Kafka — "this happened"** (topics):
- `wm.punches` — every punch (web/mobile). ✅ produced today. Partitioned by site; retained for replay/reprocess.
- `wm.clockings` — processed clocking pairs; consumed by Rules, dashboards, connectors.
- `wm.audit` — immutable audit stream.
- `wm.notifications` — domain events that may raise a notification (see §9); the Notifications consumer decides who/how.
- `wm.integration` — public event feed for connectors/customers (`employee.created`, `absence.approved`…).

**Realtime UX**: Kafka consumers fan out to SignalR groups → live attendance board, **notification centre**, mustering roll-call, all sub-second. ✅ punch→SignalR is live today.

---

## 7A. Why each broker earns its place

The test applied to each: *what does this do that Postgres alone would do badly?* If a component can't answer that, it shouldn't ship.

### RabbitMQ (via MassTransit) — work that must survive and retry

Every job that is slow, fails for external reasons, or must not be lost:

| Job | Why not just a DB table |
|---|---|
| Payroll export run | Minutes long, plugin-hosted, must retry on transient failure without blocking a request |
| Report generation & scheduled delivery | Same; plus fan-out to many recipients |
| **Notification dispatch** (~70 types × email/SMS/push) | External SMTP/SMS providers fail constantly; needs backoff, DLQ, and per-message retry state |
| Connector sync runs | Third-party APIs rate-limit and time out |
| Recalculation jobs | Large, long-running, cancellable |

What we'd otherwise rebuild by hand: retry with exponential backoff, dead-letter queues, scheduled redelivery, concurrency limits, and **sagas** for multi-step workflows (payroll close = lock period → calculate → export → notify, with compensation if a step fails). MassTransit gives all of that plus a **transactional outbox**, so a database commit and its message can never diverge. Hand-rolling this on Postgres is a known multi-week detour that ends in a worse version of MassTransit.

### Kafka — the replayable event log *(this is the load-bearing one)*

The justification is **Phase 2, the rules engine**, and it is not theoretical — it is legacy's single biggest operational pain.

In legacy, changing a daily template meant *recalculating history*: `HorioService` recalculates the previous day for every employee nightly, and there is an entire `Reprocessor` project for re-running swipes. That work mutates data in place, which makes it slow, hard to verify, and frightening to run.

With an append-only log:

- `wm.punches` is the **immutable record of what happened**. Calculation output is a *projection* of it.
- Changing a rule becomes: *replay punches for the affected employees/period through the new rule version and rebuild the projection.* No in-place mutation, no guesswork.
- Recalculating three months for one site is a bounded, resumable, verifiable operation — and you can diff old vs. new output before committing to it.
- Partitioned by site, so replay for one customer site doesn't touch others.

That capability is worth a container on its own. The other topics ride along for free:

| Topic | Consumers |
|---|---|
| `wm.punches` | rules engine, live dashboard, **replay/recalculation** |
| `wm.clockings` | rules output → dashboards, connectors, reporting |
| `wm.audit` | append-only, hash-chained audit store |
| `wm.notifications` | notification hub decides recipients + channels |
| `wm.integration` | public event feed for connectors (`employee.created`, `absence.approved`…) |

**Retention policy is a hard requirement, not a default** (see §13A): `wm.punches` sized to cover the longest realistic recalculation window (suggest 12–18 months for a typical site), everything else far shorter.

### What we deliberately do *not* use them for
- **Not** for request/response — that is plain HTTP.
- **Not** as a database — Postgres remains the system of record; Kafka is the event log.
- **Not** as a hard dependency — a broker outage degrades background work; it never breaks the UI.

---

## 8. Domain Modules

Each module: `Domain` / `Application` / `Infrastructure` (EF) / `Endpoints`, own schema, event-based comms.

1. **People** ✅ — employees, **contracts** (hourly rate, currency, cost-centre), **custom fields** (extensible per-customer employee data — TLW `EmployeeCustomFieldService`), **emergency contacts**, skills/qualifications, photos, org hierarchy (sites/departments), **population groups** (arbitrary employee groupings for rules/reports). *Today: employees + sites + departments; the rest is planned expansion.*
2. **TimeAttendance** ✅ — punch ingestion (Kafka), clocking pairing, **corrections**, **anomaly/exception detection** (missed punch, muted exceptions — TLW `Scores/Abnormality`), manual timesheets, geolocated mobile punch, **QR punching**, approvals.
3. **Scheduling** ▢ — shift patterns / **daily & weekly models** (TLW `DailyModel`/`WeeklyModel`/`MasterDailyModel`), rotas, drag-&-drop planning board, **auto-planning**, open-shift offers, working-time-directive validation, demand vs. coverage, **schedule thresholds**.
4. **Absence** ▢ — requests, multi-level/delegated approvals (**absence managers** per employee/department), entitlements & **holidays**, accrual integration, team calendars, absence-related documents (links to Documents).
5. **Rules** ▢ — the calculation engine: hours classification (regular/OT/premium/night), **rounding rules**, **tariffs/pay rates**, accruals, **counters & flexi-balances** (TLW `Counters`/`FlexiBalances`), **cost-centre allocation**, **pay periods**, **formula evaluator** (TLW `FormulaEvaluator`). Deterministic, versioned, replayable over `wm.clockings`.
6. **Documents** ▢ — **NEW, see §10.**
7. **Notifications** ▢ — **NEW, see §9.**
8. **Expenses** ▢ — expense claims, **mileage**, receipts (Documents), approval workflow, export to payroll.
9. **Reporting** ▢ — report definitions, **favourites**, scheduled execution in Worker (successor of `HorioReportExecutorService`), export to xlsx/pdf/csv, plugin reports.
10. **Safety** ▢ — **mustering & fire roll-call** (live via SignalR — TLW `Mustering`/`OnlineFireReport`), **fire-marshal assignment**, emergency events. *(Software only; no hardware.)*
11. **Visitors** ▢ — visitor pre-registration, sign-in/out, host notification, visitor emails (later phase).
12. **Admin** ▢ — settings/**software options**, **localization management** (TLW is multi-language), audit browser, license status, plugin management, health dashboard, **tip management** & **job sheets** (hospitality/job-costing add-ons) surfaced here or as their own small modules.

---

## 9. Notifications — the hub (expanded)

TLW has a real notification subsystem (`Logic/Notifications`, `EmailTemplateService`, `EmailPreferenceService`, `HRDocumentNotificationService`, `ESignatureNotifierService`, geofence "outside office" alerts, license alerts, popups). WM reproduces this as **one Notifications module** that is channel-agnostic and event-driven.

### How it flows
1. A domain module publishes a **domain event** (to Kafka `wm.notifications` or in-process) — e.g. `AbsenceRequested`, `DocumentAwaitingSignature`, `PunchOutsideAllowedArea`.
2. The **Notifications consumer** resolves **recipients** (the actor, their manager, a role, a population group) and **rules** (which notification types each user wants, per channel — TLW `EmailPreferenceService`).
3. For each recipient×channel it writes an **in-app notification** row and/or enqueues a **RabbitMQ send-job** for email/push.
4. **Delivery**:
   - **In-app** → pushed instantly over **SignalR** to a per-user group; the UI shows a bell badge + **notification centre** (mark read, filter, deep-link to the item). Persisted so it survives reloads and offline periods.
   - **Email** → Worker renders an **email template** (per-type, localized) and sends via an `INotificationChannelPlugin` (SMTP/SendGrid/etc.). Supports digests.
   - **Push** (later) → FCM to the Flutter app.
5. Per-user **preferences** decide channel per type (in-app always on; email/push opt-in), with quiet hours and digest options.

### Notification catalogue (initial — "what we show")
| Event | Who is notified | In-app | Email | Push |
|---|---|---|---|---|
| Punch outside allowed geofence | Employee's manager | ✅ | ✅ | ✅ |
| Missed punch / attendance anomaly | Manager (+ employee) | ✅ | optional | — |
| Absence request submitted | Approver(s) | ✅ | ✅ | ✅ |
| Absence approved / rejected | Requesting employee | ✅ | ✅ | ✅ |
| Document awaiting your signature | Employee | ✅ | ✅ | ✅ |
| New company document / policy | Target population group | ✅ | optional | — |
| Onboarding task/document assigned | New employee | ✅ | ✅ | ✅ |
| Expense claim submitted / decided | Approver / claimant | ✅ | ✅ | — |
| Schedule published / shift changed | Affected employees | ✅ | optional | ✅ |
| Open shift available | Eligible employees | ✅ | — | ✅ |
| License expiring / limit reached | Admins (dismissible — TLW `LicenseAlert`) | ✅ | ✅ | — |
| Report finished / export ready | Requesting user | ✅ | optional | — |
| Payroll export completed / failed | Payroll admins | ✅ | ✅ | — |

The current live punch feed on the dashboard is the first, simplest instance of this pipeline (event → SignalR → UI); the Notifications module generalizes it to typed, addressed, persisted, multi-channel messages.

---

## 10. Documents module (NEW)

In TLW, "HR" is largely document management (`CompanyDocumentsService`, `EmployeeDocument`, `EmployeeOnboardingDocument`, `HRDocumentTagsService`, `AbsenceRequestDocumentsService`, `ClockingDocumentsService`, `ESignatureDocumentsService`). WM captures this as a **Documents** module backed by **object storage** with metadata in Postgres.

- **Document types**: employee documents (contracts, IDs, certificates), **company documents** organized by **category** (policies, handbooks) targeted at population groups, **onboarding packs** (a checklist of documents/tasks for new hires), and documents **attached to** other records (absence requests, clockings, expenses).
- **Tags** (TLW `HRDocumentTags`) for classification/search; **expiry tracking** (e.g. certificate/visa expiry → raises a Notification).
- **E-signature** (`ESignatureDocumentsService`): request a signature, token-based signer access (works for employees without full accounts), optional signing **qualification** requirement, tamper-evident signed content, audit trail, and email/notification to the signer (`ESignatureNotifierService`). Signed PDFs stored immutably.
- **Access control**: documents scoped by employee/site/role; special-category docs encrypted.
- **Storage**: binaries in object storage (MinIO/S3/Blob) with virus scanning on upload (TLW `FileVirusScanning`); metadata, tags, versions, signature state in Postgres.
- **Employee self-service** (web + Flutter): "My documents" — view, download, and **sign** documents; receive them via notification/email.

---

## 11. Frontend — "Control Room" design system

Angular + Tailwind, custom system (no stock Material look). Dark-first tokens (light theme too), Space Grotesk / IBM Plex / mono numerals, mint accent. ✅ Live today: login, app shell (nav, theming, realtime status, sign-out), live attendance dashboard (KPI band, quick-punch, live feed), employees list — all with skeleton loaders, empty states, inline SVG icons, a11y (aria-live/labels/focus rings), per-person avatar hues.

Signature interactions to build: planning board (drag-&-drop, conflict highlight), mustering roll-call (full-screen live), notification centre (bell + panel), document signing flow, count-up KPIs. Quality bars: WCAG 2.2 AA, keyboard-first, i18n (Transloco + ICU — TLW is multilingual), Lighthouse ≥95.

---

## 12. Flutter Mobile (designed-for now, built later)

Same OpenAPI client + OIDC PKCE; refresh token in secure storage. Employee self-service: geofenced punch in/out (offline queue → syncs to `wm.punches`, the phone is just another punch source), rota view, absence requests, **my documents + e-sign**, **notifications via FCM push**, timesheet view. Manager: approvals, live team presence, open-shift broadcast.

---

## 13. TLW → WM Coverage Matrix

The completeness check. Every meaningful TLW capability, where it lands in WM, and status.

| TLW area (source) | WM home | Status |
|---|---|---|
| Swipe capture (individual punches) | TimeAttendance | ✅ built (web/mobile punches) |
| **`Clockings` — the daily aggregate (249 cols)** | TimeAttendance + Rules | ▢ **not started** — see [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md) |
| Pay categories (`CPTN01..20`, 20 fixed slots) | Rules | ▢ planned — hard ceiling of 20 in legacy |
| Clocking generation ("calendar" job, nightly) | Worker | ▢ not started |
| Swipe→day allocation (night shift, offset, prev/next day) | TimeAttendance | ▢ not started |
| Clocking change audit (`ClockingsLog`, 97 cols) | Admin + `wm.audit` | ▢ planned |
| **Tariffs / rates (`TariffValues`: 20 rate + 20 charge-rate, date-versioned)** | Rules | ▢ **not started** — the hours→money path |
| **Global calculation settings (`Calculations`, 45 cols)** | Rules | ▢ **not started** — incl. the 8-window day-boundary matrix |
| **Per-install options (`SoftwareMainOptions`, 227 cols)** | Admin | ▢ **not started** — period locking, recalc control, QR toggles |
| Employee contracts + thresholds (`…Effective` resolution) | People + Rules | ▢ not started |
| Cost-centre counter split (`CostCentreCounters`) | Rules | ▢ not started |
| Manual timesheets (`ManualTimesheets`, own `BadgeTime1..12`) | TimeAttendance | ▢ not started |
| Manual timesheets / corrections | TimeAttendance | ◐ partial (timesheet read; corrections planned) |
| Exceptions / abnormalities (`Scores`) | TimeAttendance | ▢ planned |
| QR punching (`QrSetup`) | TimeAttendance | ▢ planned |
| Geolocation tracking | TimeAttendance | ◐ punch coords captured; tracking view planned |
| Planning / PlanningControl / daily & weekly models | Scheduling | ▢ planned |
| Auto-planning | Scheduling | ▢ planned |
| Hours calculation / rounding / tariffs | Rules | ▢ planned |
| Accruals / counters / flexi-balances | Rules | ▢ planned |
| Cost centres / pay periods / formulas | Rules | ▢ planned |
| Absences / holidays / approvals / absence managers | Absence | ▢ planned |
| HR documents / company docs / onboarding / tags | Documents | ▢ planned (NEW) |
| E-signature | Documents | ▢ planned (NEW) |
| Expenses / mileage | Expenses | ▢ planned (NEW) |
| Notifications / email templates / preferences / alerts | Notifications | ◐ punch→SignalR live; full hub planned (NEW) |
| Personnel / contracts / custom fields / emergency contacts | People | ◐ partial (employees/sites/depts; rest planned) |
| Population groups | People | ▢ planned |
| Reports / custom reports / favourites / scheduled | Reporting (+ report plugins) | ▢ planned |
| Mustering / online fire report / fire marshals | Safety | ▢ planned |
| Visitors | Visitors | ▢ planned |
| Access control (logical) | Admin/People permissions | ◐ partial (RBAC live; zones N/A w/o devices) |
| Payroll plugins (~60) | Payroll export plugins | ◐ SDK + demo built; real plugins planned |
| Integration services (RotaGeek, SageHR, …) | Connector plugins | ◐ SDK contract built; connectors planned |
| Licensing / WebLicenseManager | Licensing (lib + service) | ◐ lib+CLI built; service/portal planned |
| Audit trail | Admin + `wm.audit` | ◐ topic designed; store+browser planned |
| Users / user management (`Logic/Users`) | Identity (user mgmt) | ✅ built — list/search, create, edit roles + employee link, reset password, lockout (`UserEndpoints.cs`, `UserManagementService.cs`) |
| User ↔ employee linking (`UserFromEmployeeIdProvider`) | Identity + People | ✅ built — `User.EmployeeId`, one-to-one enforced, `/api/me/employee` |
| Employee self-service portal (`EmployeeSchedulingPortal`) | `/api/me/*` + portal | ◐ partial — punches/timesheet/profile live; absences, documents, expenses follow their phases |
| User types Administrator/Employee/Manager (`BuiltinRoles`) | Identity groups | ◐ roles live; **both `Role.IsSelfOnly` and `CanModifySelf` adopted 2026-08-05** — `SelfOnly` as `ScopeRuleKind.Self` (a mode is a rule kind under exclusivity), `CanEditOwnRecord` as a group flag defaulting to `true`; built in plan **005 P1**. `IsDepartmentOnly` is dead code in legacy — do not model it. See `TLW-AUTHORIZATION-MODEL.md` §6 |
| **One role per user** (`UsersInRoles`, `GetUserRole`) | Identity | ◐ **decided, not yet built.** Legacy users hold **exactly one** role (`AuthorizationService.cs:1265-1280` `SingleOrDefault`; `:1008-1041` replaces on assign). WM has many roles *and* many groups per user; §4's old "union, mirroring TLW" was never TLW. **User ruled 2026-08-05: option A — one object, one membership**; §4 rewritten. The refactor is plan **005** (`User.Roles` → `User.SecurityGroupId`, `Role`/`RolePermission` merged into `SecurityGroup`, 6 portions) |
| Manager row-level scope (`RoleBasedEmployeeFilterService`) | Identity/People query filters | ◐ partial (shipped PR #7; composed multi-dimension scope in plan 001) — **department scope is unreachable from the UI and employee writes are unscoped**, see `PHASE-AUDIT.md` A2/A4 |
| Version rollout to servers (`AutoSiteUpdater`, `AutoScriptExecutor` — **claimed at §13A `:685` with no `file:line`, i.e. a hypothesis**) | Deploy (`deploy/`, `.github/`) | ◐ **partial.** Containers + `docker-compose.prod.yml` + migrate-on-start built; **CI** merged 2026-08-05 (#22). No CD, no registry (`WM_REGISTRY` defaults to the literal `wm`), no arm64 build, no hosted instance — plan **006** |
| Hosted public demo (no legacy equivalent — WM addition) | Deploy + `src/Demo` | ▢ **not started.** Demo data is Development-only (`Program.cs:89-95`), so a Production boot has no employees, no punches and no `manager` login. Plan **006 P1** moves the seeders out of the shipping assemblies entirely |
| Realtime punch feed (no legacy equivalent — WM addition) | Api SignalR hub | ✅ **scoped** — 003 P1 merged 2026-08-05 (`3389525`, #18): the feed addresses SignalR groups derived from resolved scope, the hub requires `attendance.view`, and a scope change re-groups open sockets within the session. **One follow-up:** group fan-out is *union* semantics and a composed rule is an *intersection*, so a multi-dimension group would leak — settled by plan **005 P5** (evaluate the rule per open connection); 005 P4 fails that arm closed in the interim |
| API/integration auth (`ApiKeys`, `RsaKeys`, `SynergyAppAuthenticationTokens`) | Identity | ▢ **not started** — legacy authenticates the mobile app as an *employee* with a revocable per-device token, not as a user; plan 003 P4 |
| Screen-level rights (**`dbo.AccessControlEntry`** — 6 cols, ~1,000 rows/role) | Identity groups | ▢ **not started** — measured 2026-08-05: 49 branches / 382 forms / 75 tabs, tri-state none·read·edit, **deny-overrides-allow, default deny**. See `TLW-AUTHORIZATION-MODEL.md` §4 and plan 004 |
| Navigation icons / dashboard categories per role (`RoleNavigationIcons`, `RoleDashboardCategories`) | Identity groups | ▢ **not started** — presentation allow-lists on the group, never a security boundary (`AuthorizationService.cs:511-526, 1423-1432`) |
| HR document-type rights (`RoleHrDocumentSecurity`, `Deny`/`ReadOnly`/`ReadWrite`) | Documents | ▢ **not started** — a *third* rights vocabulary in legacy; WM should have one (`RoleHrDocumentSecurityService.cs:64-87`) |
| ~~`WebPages`~~ | — | ⏹ **not an authorization table.** `WebPage(PageId, Name, Description)` + `LocalizationKeys` — it is localization (`HorioDB.designer.cs:27642-27654`). Previously listed here in error |
| ~~`FormAccess`, `SiteItemPermission`~~ | — | ⏹ **not tables.** A static helper class and an in-memory DTO (`FormAccess.cs:7`, `SiteItemPermission.cs:5-23`); both persist as `AccessControlEntry`. Previously listed as schema in error |
| Access deny-lists (`AccessRightsExclusions` + 2 child tables) | — | ⏹ dropped by design — WM narrows by removing group membership, never by deny rules (`ScopeModel.cs:176-180`). **The requirement it met is unmet:** hiding named individuals' Salary/Bank/Disciplinary tabs from managers who legitimately hold the tab (`GroupsController.cs:463-466`) — open question 4 in `TLW-AUTHORIZATION-MODEL.md` |
| User action logging (`UserActionLogsService`) | Admin + `wm.audit` | ▢ planned |
| SSO | Identity | ▢ planned (local JWT live) |
| 2FA (`TwoFactorAuthenticationService`) | Identity | ▢ planned |
| Localization (multi-language) | Admin + i18n | ▢ planned |
| Tip management / job sheets | Admin (or small modules) | ▢ planned |
| **Device comms (Suprema/SyFace/Salto/ANPR/thermal/fingerprint)** | — | ⏹ **dropped by decision (phone-only)** |
| EPOS / cashless till | — | ⏹ deprioritized |
| Student registration | — | ⏹ deprioritized (schools vertical) |
| Card printing | — | ⏹ dropped (was device-adjacent) |

Legend: ✅ done · ◐ partial / foundation laid · ▢ planned · ⏹ intentionally out of scope ·
⚠️ built but defective.

> **Audited 2026-08-04.** Every `✅` and `◐` in this table was checked against the code, not
> against the previous revision of this table. Four rows were stale ("building now" for work
> merged in PR #3), and four rows were missing entirely — the realtime feed, the API-auth
> surface, screen-level rights and the deny-list drop. Findings, evidence and disposition:
> [`PHASE-AUDIT.md`](./PHASE-AUDIT.md).
>
> **Authorization surface re-measured 2026-08-05.** The `▢ not started` screen-rights row named
> four things, and **two of them were not authorization at all** (`WebPages` is localization;
> `FormAccess`/`SiteItemPermission` are C# types, not tables). The real table is
> `dbo.AccessControlEntry`. A new row records the one-role-per-user divergence, which §4 had
> backwards. Full measurement and citations:
> [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md).

---

## 14. Roadmap (v3 — rebased on the measured legacy scope)

> **Read `TLW-SCHEMA-SWEEP.md` and `COVERAGE-AUDIT.md` first.** An exhaustive scan of the legacy
> product (317 projects, 59 services, **578 database tables / 8,173 columns**, ~230 screens,
> 67 reports, ~70 notification types, ~150 fields on the Daily Template alone) showed the earlier
> estimate was badly wrong. This roadmap is rebased on that reality.
>
> **Corrected 2026-08-04.** This note previously said "247 entities" and "roughly 3–5% of the
> legacy functional surface". Both were superseded by `COVERAGE-AUDIT.md` and never updated here.
> 247 counts hand-written entity classes, not tables; the measured schema is **578 distinct
> tables** (579 `TableAttribute` mappings). Against the ~457 tables WM retains after the device
> and EPOS drops, WM's 8 aggregates are **under 2%** of the retained data model. Never size a
> plan from the optimistic number.

### Sequencing principle
Order by **dependency, not visibility**. Everything commercially valuable — timesheets,
payroll export, absence balances, scheduling compliance — sits on the **rules/calculation
engine**. It is the deepest, least glamorous piece and it has to come first. The pretty
boards are cheap once the engine underneath is right.

### Leverage principle
Legacy grew **44 payroll plugins, 17 report plugins and per-vendor screens** because
everything customer-specific became code. WM must ship a **generic export builder**, a
**report designer**, and **rules as configurable data** so new customers are configuration.
This single decision is worth more than any individual feature.

| Phase | Delivers | Est. | Status |
|---|---|---|---|
| **0 — Foundations** | Solution skeleton, Docker infra, Identity, app shell, design system | — | ✅ done |
| **1 — People, Time & Users** | People, punch pipeline (Kafka), live dashboard, timesheets, users + employee linking + self-service | — | ✅ done |
| **1b — Access model** ⭐ | **Groups** (per-screen read/edit + employee scope in one object), several groups per user combining as a union, scope applied as query filters, diagnostics screen. *Moved early: every later query depends on scope being right.* | 4–6 wks | ◐ **in progress — plans 001 + 003** |
| **1c — Core depth** | Employee contracts, custom fields, positions & qualifications, corrections, period locking, employee/population groups | 4–5 wks | ▢ |
| **2 — Rules engine** ⭐ | Daily templates (shifts, breaks, core hours, rounding policy, exceptions, shift matching, split/multi-shift), weekly models, counters, flexi balances, pay categories, cost-centre allocation, recalculation & replay, **safe rules expression language** (replaces legacy per-template custom SQL) | **10–16 wks** | ▢ |
| **3 — Absence & Accruals** | Absence types, requests + multi-level approval (absence managers per employee/department), blocked dates, holidays, entitlements, accruals incl. length-of-service, recaps | 8–10 wks | ▢ |
| **4 — Notifications & Documents** | Notification hub (in-app + email + SMS, 4 manager-assignment roles, ~70 typed notifications, templates, per-user preferences), Documents + categories + expiry, e-signature, object storage | 8–10 wks | ▢ |
| **5 — Scheduling** | Rotas, planning board, auto-planning, roster calendar, schedule requests, thresholds, timetables | 8–12 wks | ▢ |
| **6 — Semantic layer, Assistant & payroll** | Semantic model, **AI assistant** (§16), ~6–10 core reports, saved/versioned report definitions, scheduled delivery; **generic payroll export builder** + 2 pilot formats; licensing service + packaging | 10–12 wks | ▢ |
| **6b — Emergency & Safety** ⭐ | Emergency trigger, live broadcast, live roll call, self-mark-safe, muster points, fire marshals, incident report (§17) | **3–4 wks** | ▢ *(cheap — reuses live presence; strong differentiator)* |
| **7 — Expenses & Field service** | Expenses + mileage (full cancel-request workflow), activities/job costing, scheduled activities, job sheets, clients, travel tracking | 10–12 wks | ▢ |
| **8 — Visitors** | Pre-registration, invitations, check-in/out, host notices, deliveries, auto sign-out | 4–5 wks | ▢ |
| **9 — Mobile & enterprise** | Flutter app (punch, rota, absence, documents + sign, **mark-safe**, push), 2FA, SSO, localisation, audit browser | 8–12 wks | ▢ |

**Running workstream (not a phase):** on-prem rollout tooling — versioned migrations, self-updating installer with rollback, version/health reporting, per-customer backup/restore (§13A).

### Customer feature usage (2026-07-20, from the field)
Ranked by what customers actually use — this is what justifies the phase order:

| Feature | Usage | Phase |
|---|---|---|
| Time & attendance | **all customers, most use all of it** | ✅ 1 / 1c |
| Daily templates | heavily used | 2 |
| Reporting (simple → advanced) | **all customers** | 6 (assistant + core reports) |
| Absences | all *(some still on the "legacy absences" variant — needs clarification)* | 3 |
| Accruals | widely used | 3 |
| Planning module | widely used | 5 |
| Expenses | used | 7 |
| Emergency / fire marshal | **reframed as software-only — new capability** | 6b |
| Physical access control | ⏹ dropped (no devices) | — |

**Chosen scope (2026-07-20): defensible core** — phases 1b → 6, roughly **12–18 months**, producing a sellable T&A product that is *better* than legacy (AI assistant instead of a report designer, configuration instead of 44 plugins). Phases 7–9 follow on demand.

Explicitly **excluded**: devices (phone-only), EPOS/catering, student registration, vehicle/ANPR.

### Confirmed decisions (2026-07-20)
1. **AI assistant replaces the reporting stack** — no 67-report port, no designer, no 17 report plugins. See §16.
2. **Free/open-weight model by default**, provider-agnostic, on-prem-safe; hosted API optional. Assistant is a metered licensed feature.
3. **Everything is a licensable feature** so packaging/pricing is configuration, not code.
4. **EF Core everywhere** — no Dapper. Optimise measured hot paths only (compiled queries, projections, `FromSql` as a last resort); never split the stack pre-emptively.
5. **Groups** adopted from legacy `/Groups` (one object carrying per-screen read/edit *and* employee scope, richer than flat roles) and **moved to the front** of the queue. WM makes scope explicit rather than legacy's fail-open "empty list means everything".

   > **Corrected 2026-08-05.** This decision read *"WM allows several groups per user"*. The user
   > ruled the opposite on 2026-08-05 — **one object, one membership** (§4:157-160) — and the
   > sentence is superseded rather than merely stale: multi-membership was the half of the design
   > nobody chose deliberately, and it is what forced the union, the precedence questions and the
   > "vague idea about groups" the survey was commissioned to answer. The refactor is plan 005.

   **WM deliberately inverts all three of legacy's fail-open behaviours** (measured in `RoleBasedEmployeeFilterService`, 2026-08-03):
   - an **empty managed list applies no filter** in legacy, so a misconfigured role sees every employee — in WM a dimension present with zero ids matches **nothing**;
   - `EmployeeLocationId == null ||` makes **location-less employees visible to every role** — in WM a null discriminator never widens visibility;
   - the `default:` branch **returns the query unfiltered** and merely logs, commented as intentional "to avoid an error getting to the user" — WM fails closed and returns nothing.

   Legacy also supports an explicit **`ByEmployees`** managed-employee list, which WM did not model at all until plan 001.

   **Audit addenda (2026-08-04) — measured, and not previously recorded anywhere:**
   - There is a **fourth** fail-open of the same shape: `if (managedEmployees.Any())`
     (`RoleBasedEmployeeFilterService.cs:50, 89, 128`) means a `ByEmployees` role with an empty
     list also sees everyone. WM's `ScopeRule.Constrained` already covers it; the doc did not.
   - The rule is implemented **four times**, not three: the three C# marker-interface overloads,
     plus the T-SQL function `dbo.EmployeeIdsManagedByRole`
     (`E:\Tlw\Database\Versioning\80.V5.26.0.0.sql:880-934`), which is what
     `IsEmployeeManagedByRole` and `GetPermittedEmployeeIdsForRole` actually call. **The two
     implementations disagree on the unknown-`ManagementType` case**: C# returns the query
     unfiltered (fail-open), SQL returns an empty table (fail-closed). WM's fail-closed choice
     matches legacy's own SQL, not just our preference.
   - `dbo.Role` has only **six columns** and three of them are scope modifiers plan 001 did not
     model: `IsSelfOnly`, `IsDepartmentOnly`, `CanModifySelf`. `IsSelfOnly` is a hard
     **narrowing override** that beats the managed lists entirely
     (`AuthorizationService.cs:220-224, 236-240, 275-279`); `CanModifySelf` is a read/write
     asymmetry on one's own record (`AuthorizationService.cs:281-286`). WM's `Self` is a
     *unioned* rule, i.e. widening. See `PHASE-AUDIT.md` B3.
   - Legacy's **`Buildings` are not an employee attribute.** `dbo.Employees` (153 cols) carries
     `DepartmentId`, `EmployeeLocationId`, `CostCentreId` — and no `BuildingId`. `BuildingId`
     appears only on devices, doors, ANPR, EPOS tills and muster points. A building scope
     dimension over employees would be a WM invention, not a legacy behaviour. See
     `PHASE-AUDIT.md` B4.

   **Authorization-survey addenda (2026-08-05) — measured, and three of these correct the
   addenda above.** Full evidence: [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md).
   - **There are twelve fail-opens of this shape in the in-scope surface, not four** — the four
     already recorded, plus `FilterDepartments`, `FilterLocations`, `FilterBuildings`,
     `FilterClockingActivities`, `FilterClockingScheduledActivities`,
     `CanViewDailyPeriodicTemplate` (commented `//no departments to manage - allow all`) in
     `WebSite/Controllers/AuthorizingControllerBase.cs:101-114, 216-221, 280-322, 444-454`;
     `UserHasLimitedBySelfPermissions` returning `false` from a `catch` (`:572-585`); and
     `FormAccess.cs:64, 131, 199, 231`, where an **unlicensed** tab layer grants every tab.
     Ten more sit in dropped verticals. The pattern is uniform: *no configuration means no
     restriction*. WM's inversion is the single largest behavioural difference between the
     products.
   - **`AccessType` is `Read`/`Edit`, not `View`/`Edit`** (`Enums.cs:9-13`), it has exactly three
     call sites, and it changes the answer in exactly **one** case: your own record when
     `CanModifySelf = false` (`AuthorizationService.cs:281-286`). **Legacy's read scope and write
     scope are otherwise the same set.**
   - **`Role.IsDepartmentOnly` is dead**, settled: written by `UpdateRole` (`:866`), localized,
     rendered as a checkbox — and never read, in `Logic`, `WebSite` or SQL. WM must not model it.
     Supersedes the "unverified" in `PHASE-AUDIT.md` B3.
   - **Legacy does not expand the department tree when filtering employees.**
     `dbo.EmployeeIdsManagedByRole` matches `e.DepartmentId = dr.DepartmentId` exactly
     (`80.V5.26.0.0.sql:911`). WM's `IncludeDescendants` has no legacy precedent on this path;
     it is a WM improvement and should be labelled as one.
   - **`ByDepartments` and `ByEmployees` are mutually exclusive**, enforced on save —
     `UpdateRole:872-883` wipes the other lists. Legacy cannot say "these departments *and also*
     these named people". WM's intersecting employee-list dimension is an improvement without a
     legacy analogue (affects plan 001 P4's wording).
6. **No per-template custom SQL** — replaced by a safe rules expression language, so WM does not recreate legacy's un-dismantlable core.
7. **No deny rules** *(recorded 2026-08-04 by the phase audit; the decision itself is older)*.
   Legacy's `AccessRightsExclusions` + `AccessRightsExclusionEmployees` +
   `AccessRightsExclusionResources` let a role's access be *subtracted* per resource, wired
   through `AuthorizationService.SaveExclusionListForRole`. WM narrows only by removing group
   membership. This was previously stated only in a code comment (`ScopeModel.cs:176-180`); it is
   a decision and belongs here.

   > **Correction (2026-08-05).** This decision was justified here, and in three other WM
   > documents, by the claim that *"deny rules interacting across several groups are what forced
   > legacy to ship a diagnostics subsystem to explain its own answers."* **That claim is false.**
   > `DataAccessScopeDiagnostics` counts LINQ-to-SQL `DataContext` creation and disposal to find
   > connection leaks — `TotalScopesCreated`, `ActiveScopes`, `TotalDisposalErrors`,
   > `AutoDisposalRate` (`Logic/DAL/DataContextTracking/DataAccessScopeDiagnostics.cs:10-55`,
   > `WebSite/Controllers/DataAccessScopeDiagnosticsController.cs:70-87`). It has nothing to do
   > with authorization, and **legacy ships no tool that explains an access decision at all.**
   > The decision to drop deny rules may still be right, but it no longer has this evidence
   > behind it. Two measured facts that do bear on it: legacy's deny rows are only reachable
   > because a user holds exactly one role, so deny-over-allow never actually arbitrates between
   > two grants; and the *requirement* the exclusion list met — hiding named individuals'
   > sensitive Personnel/HR tabs — is currently unmet in WM.
   > See [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md) §11 C2.

Migration: old TLW keeps running. A `TlwLegacyConnectorPlugin` reading the existing SQL Server database bridges data during transition — worth building early (Phase 2–3) so WM can run alongside on real data.

---

## 13A. Deployment model — on-prem, database per customer

**Decision (2026-07-20):** WM is deployed **on the customer's own server**, with a **database per customer**, matching legacy. Rationale: customers want their data to stay with them, and it makes migration from legacy far simpler (no data merging).

### The full stack ships by default *(revised 2026-07-20)*

An earlier draft proposed stripping RabbitMQ and Kafka out of the default profile on operational-risk grounds. **That was over-cautious and is reversed.** With Docker Compose the customer runs one file and gets the whole stack; the marginal cost of two more containers on a 32 GB server is noise (~1 GB for Kafka in KRaft mode, ~200 MB for RabbitMQ), and maintaining *two* supported topologies costs more engineering and test effort than running one well.

**Default stack, shipped as a single `docker compose up -d`:**

```
postgres · redis · rabbitmq · kafka · wm-api · wm-worker   (+ minio if object storage is local)
```

More importantly, both brokers have **specific jobs that Postgres does not do as well** — see §7A. Kafka in particular is load-bearing for the Phase-2 rules engine, not decoration.

### Where the real operational risk lives

The risk was never "too many containers" — it is **unattended long-run behaviour on a server we cannot reach**. These are requirements, not optional polish:

| Risk | Mitigation (must ship with v1) |
|---|---|
| Kafka disk growth fills the volume | **Bounded retention** on every topic (size *and* time), sized from employee count; volume separate from the OS disk |
| Consumer lag / stuck consumer group | Lag exported to health endpoint; alert surfaced in the Admin health dashboard |
| Broker briefly down → user-facing errors | **Graceful degradation** — publish failures never fail a user request (already implemented: `KafkaEventStreamProducer` logs and continues); jobs queue and retry |
| Startup ordering after a customer reboot | Compose health checks + dependency ordering; API tolerates brokers arriving late |
| Customer IT upgrades/blocks a component | Version pinning in compose; documented firewall/port requirements |

### Keeping the broker-agnostic abstraction anyway

The `IEventStreamProducer` / MassTransit abstractions stay — **but the justification changes**. Not for deployment flexibility, for two cheaper reasons:

1. **Dev and CI speed** — unit and integration tests run without brokers; the inner loop stays fast.
2. **Graceful degradation** — the app must keep serving when a broker blips, which requires the seam to exist regardless.

### Ship as Linux containers — the legacy constraints are already gone

An earlier draft worried about "Windows Server + IIS + SQL Server" sites. **That is a legacy constraint WM has already escaped:** WM is .NET 10 (Kestrel — no IIS) on PostgreSQL (no SQL Server), with an Angular SPA served by nginx. Nothing in the stack requires Windows.

**Target: a Linux host running Docker (or Podman). The host OS is otherwise an implementation detail** — the same Linux images run on Windows Server via Docker if a customer's IT insists.

This is also a **commercial advantage worth stating in the sales conversation**: no Windows Server licence, no SQL Server licence. SQL Server Standard alone is a per-core cost that WM removes entirely.

**Shipped artefacts** (all built from the repo, see `deploy/docker-compose.prod.yml`):

| Image | Base | Size | Notes |
|---|---|---|---|
| `wm/wm-api` | `dotnet/aspnet:10.0-alpine` | ~194 MB | non-root, health-checked, migrates on start |
| `wm/wm-worker` | `dotnet/runtime:10.0-alpine` | ~165 MB | non-root; plugins mounted at runtime, not baked in |
| `wm/wm-portal` | `nginx:1.27-alpine` | ~49 MB | serves the SPA, proxies `/api` + `/hubs` |

> **Corrected 2026-08-05.** The base images read `9.0-alpine` here long after
> [#14](https://github.com/00008550/WM/pull/14) moved the repo to .NET 10; the Dockerfiles say
> `10.0-alpine` (`src/Api/WM.Api/Dockerfile:2,18`, `src/Worker/WM.Worker/Dockerfile:2,16`). Sizes
> are the pre-.NET-10 measurements and have not been re-measured. **None of the three Dockerfiles
> pins a platform**, so they build for whatever the builder is — plan 006 P4 adds `linux/arm64`.

Deployment properties this buys us:
- **Single origin** — nginx proxies the API and SignalR, so there is no CORS and no API URL baked into the JS bundle.
- **Only the portal is exposed**; API, worker, database and brokers stay on the internal network.
- **Versioned rollout and instant rollback** — pin `WM_VERSION` per customer, `docker compose pull && up -d` to upgrade, repin to roll back.
- **Schema upgrades itself** — the API applies EF Core migrations on start in every environment, which is how an on-prem install stays current without us reaching it.
- **Secrets are required by the image itself, not only by the compose file** — the image carries no signing key and no administrator password, and outside Development the host **refuses to start** without a `Jwt:SigningKey` of its own (`JwtOptions.DescribeSigningKeyFault`, enforced at `IdentityModule.cs:57-63`). `docker-compose.prod.yml:99,101` still refuses to start without `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY` and `WM_ADMIN_PASSWORD`, but it is now the second line of defence rather than the only one.
- **Readiness is separate from liveness** — `/health` answers "this process is answering" and cannot fail, which is what an orchestrator should restart on; `/api/health/ready` answers "connected and migrated" per module database and is what the container `HEALTHCHECK` and any `depends_on: service_healthy` use. It sits under `/api/` because that is the only prefix the portal's nginx proxies, so an external smoke check reaches it as a visitor would. It reports a build identity **only when `Build__Id` is set**, so a customer install discloses nothing.

> **Corrected 2026-08-05, closed 2026-08-06 by plan 006 P2.** This bullet used to end *"There is no
> hard-coded administrator password in a production build."* **That was false**:
> `src/Api/WM.Api/appsettings.json` shipped a working `Jwt:SigningKey` and a
> `Bootstrap:AdminPassword` inside the `wm-api` image (`.dockerignore` excludes only
> `appsettings.Development.json`), and nothing refused to boot on them — a container started
> without `Jwt__SigningKey` ran on a key published in this repository, and anyone who could read it
> could mint an administrator token. **006 P2 removed both values from that file** (they now live
> in `appsettings.Development.json`, which is not in the image) **and made the host fail closed**:
> missing or under-32-byte keys are refused everywhere, and every key this repository publishes is
> refused outside Development. The claim above is now true, and asserted by
> `WM.Api.Tests/Security/SigningKeyGuardTests.cs`.

Two Alpine-specific requirements learned by actually running it: **ICU must be installed** (`icu-libs`, `icu-data-full`) because WM is multi-language and invariant globalization is not acceptable; and container health checks must target **`127.0.0.1`, not `localhost`**, since `localhost` resolves to IPv6 first and nginx binds IPv4.

### Consequence: rollout tooling is a first-class deliverable

Legacy needed `AutoSiteUpdater`, `AutoScriptExecutor`, WinSCP and a pile of batch scripts to push versions to N servers. WM needs an equivalent from early on, or upgrades become the bottleneck:

- **Versioned EF Core migrations** applied automatically on startup (already the case in dev).
- **Self-updating installer / container bundle** per customer, with rollback.
- **Version + health reporting** back to the vendor (which customer is on which build, are services healthy).
- Per-customer **backup/restore** and **licence status** surfaced in Admin.

This is added to the roadmap as a running workstream rather than a phase.

---

## 14A. Module inventory (double-checked against `TLW-INVENTORY.md` §3)

Every retained legacy functional area maps to exactly one module. Checked area-by-area so nothing falls between modules.

| # | Module | Covers (legacy area) | Status |
|---|---|---|---|
| 1 | **Identity** | users, roles, login, 2FA, SSO, self-service surface | ✅ built |
| 2 | **Access** ⭐ | **Groups**: per-screen read/edit + employee scope, several per user, diagnostics. See `SCREEN-TREE.md` | ◐ **in progress — foundational** |
| 3 | **People** | employees, org (sites/departments), contracts, hourly rates, custom fields, contact & emergency info, groups, population groups, cost centres, positions | ◐ basics built |
| 4 | **HR** | appraisals, disciplinaries, objectives, remunerations, certificates, **qualifications + expiry**, onboarding, fixed-term & probation, leavers, anniversaries | ▢ *(split from People: different sensitivity + permissions)* |
| 5 | **TimeAttendance** | punches, clockings, pauses, corrections, manual timesheets, daily browser, geolocation, QR punch, period locking | ◐ core built |
| 6 | **Rules** ⭐ | daily/weekly templates, shifts, breaks, core hours, rounding policy, exceptions, shift matching, split/multi-shift, counters, flexi balances, pay categories, cost-centre allocation, recalculation & replay | ▢ **deepest piece** |
| 7 | **Scheduling** | rotas, planning board, auto-planning, roster calendar, schedule requests, thresholds, timetables | ▢ |
| 8 | **Absence** | absence types, requests + approvals, blocked dates, holidays, school holidays, entitlements, accruals (+ length-of-service), recaps | ▢ |
| 9 | **Documents** | company/employee documents, categories, onboarding packs, expiry, **e-signature**, virus scan | ▢ |
| 10 | **Notifications** | hub: ~70 typed notifications, templates, per-user preferences, channels (in-app/email/SMS/push), **4 manager-assignment roles** | ▢ |
| 11 | **Expenses** | claims, mileage + periods, types/rates/categories, vehicle types, cancel-request workflow | ▢ |
| 12 | **Activities** | work-activity hierarchy, clients, scheduled activities, support members, job sheets, travel tracking, job costing | ▢ *(was missing entirely)* |
| 13 | ~~AccessControl~~ | physical access control (doors, readers, cards) | ⏹ **dropped — device-dependent.** Locations/zones retained in People for presence & mustering |
| 14 | **Emergency & Safety** ⭐ | software-only emergency: trigger, live broadcast, **live roll call**, self-mark-safe, muster points, fire marshals, incident report + archive (§17) | ▢ *(high value, low cost — builds on existing live presence)* |
| 15 | **Visitors** | pre-registration, invitations, check-in/out, host notices, deliveries, auto sign-out | ▢ |
| 16 | **Reporting** | core reports, saved/versioned definitions, scheduled delivery with recipient scoping, favourites, export builder | ▢ |
| 17 | **Assistant** ⭐ | semantic layer + AI agent: ad-hoc questions, report authoring, in-app help | ▢ *(replaces report designer + 17 report plugins)* |
| 18 | **Integrations** | connector host + settings, legacy-DB bridge connector | ▢ |
| 19 | **Admin** | settings/software options, **localization** (multi-culture + custom keys), audit browser, user action logs, licence status, plugin management, health | ▢ |

Cross-cutting (SharedKernel, not modules): audit event emission, multi-tenancy, outbox, event stream contracts.

**Excluded by decision:** devices/biometrics (phone-only), EPOS/catering (~25 screens), student registration (~12), vehicle/ANPR (~5).

---

## 16. AI Assistant — architecture & decision record

**Decision:** replace the legacy report designer, 67 static reports and 17 report plugins with a **semantic layer + AI assistant**, keeping only ~6–10 hand-built core reports.

### 16.1 The permission boundary (non-negotiable)

The model **never touches data**. It emits a **query specification** (validated JSON) against a curated semantic model. That spec is executed by the normal data layer, through the **same Access-module scope filters as any other request**.

```
user question
     ↓
[semantic model: entities, fields, measures, relationships the user may see]
     ↓
LLM  →  QuerySpec (JSON, schema-constrained decoding)
     ↓
[validator: schema + field allow-list + scope check]   ← rejects anything out of bounds
     ↓
[query executor: EF Core + Access data-scope filters]  ← same path as the rest of the app
     ↓
results → rendered table/chart, optionally saved as a report definition
```

Rules:
- **No raw SQL from the model, ever.** A crafted employee name or document must never become a query. Treat all model output as untrusted input.
- The semantic model presented to the LLM is **already filtered** to what the caller may see — an employee-role user's assistant cannot even describe other employees' fields.
- Every assistant query is **audited** (prompt, spec, rows returned) to `wm.audit`.
- Assistant results carry the same **row-level scope** as the UI. Scope is enforced in the executor, never in the prompt.

### 16.2 Authoring vs. execution (determinism)

| Mode | Behaviour |
|---|---|
| **Ad-hoc question** | Model → spec → results. Fine for exploration; results are not guaranteed reproducible. |
| **Saved report** | Model *authors* a definition; once saved it is **versioned and deterministic** — the engine executes it identically every time, no model involved. |

Anything payroll-adjacent or statutory must be a saved definition or a hand-built core report. An assistant that returns a slightly different number each run is unacceptable there.

### 16.3 Model strategy (provider-agnostic)

**The property that makes this tractable on customer hardware: the model never sees customer data.**
It receives the *semantic model* (entity/field/measure names the caller may see) and the *question*, and returns a query spec. Rows are fetched afterwards, locally, by our executor. So employee records **never leave the customer's server**, even when the model itself is remote.

Residual exposure to be honest about: the question text can contain a person's name ("show me Elena's absences"), and the schema reveals structure. That is a very different risk class from shipping data rows to a third party — but it is not zero, hence the strict tier below.

Legacy's servers are **Windows Server + IIS + SQL Server with no GPU**. WM must not require customers to buy hardware to adopt it.

| Tier | Model | Hardware | Data leaving site |
|---|---|---|---|
| **Managed** *(default)* | Hosted API (Claude / OpenAI / Gemini) | **None** — no server change | Question + schema only |
| **Strict / air-gapped** | Small open-weight (Gemma 4 12B class or small Qwen) via **Ollama**, CPU-only, schema-constrained decoding | Existing server, +8–16 GB RAM. ~5–15 s per query — acceptable for report *authoring*, not for chat | **Nothing** |
| **Local fast** | Mid-size Qwen 3.5 (Apache 2.0) or GLM 5.1 (MIT) via **vLLM** | One modest GPU | **Nothing** |

`IAssistantModel` abstracts all three; the tier is a per-customer configuration. Licence preference **Apache 2.0 / MIT** for anything shipped on-prem. The assistant is a **licensed feature with usage metering** — unlike every other feature it carries real marginal cost.

**Baseline server target** (Standard profile + Strict assistant): 8 cores, 32 GB RAM, SSD — comfortably within what legacy sites already run.

### 16.4 Why this is viable with a small model
The task is narrow (workforce data), the output is **schema-constrained** (the model cannot emit invalid structure), and every spec is **deterministically validated** before execution — invalid specs are rejected and retried rather than guessed at. Reliability comes from the harness, not from model size.

### 16.5 Sequencing constraint
The assistant is a **multiplier on a well-modelled domain, not a shortcut past building one**. Pointed at a half-finished schema it will be confidently wrong. It therefore lands *after* Rules, Absence, Scheduling — never before.

---

## 17. Emergency & Safety — software-only, better than legacy

Legacy needed an **Adam Fire Link hardware device** to trigger a fire report. Without devices we can do this *better* in software, and it costs little because the **live presence pipeline already exists**.

### Flow
1. **Trigger** — an authorised user (or any employee, for a panic alert) raises an emergency from web or mobile: *fire, evacuation, medical, security, lockdown,* or *drill*.
2. **Broadcast** — every connected user sees it instantly (SignalR, already built), plus push to mobile and email/SMS to fire marshals and managers.
3. **Live roll call** — derived automatically from attendance data:
   | Status | Derived from |
   |---|---|
   | **Presumed on site** | signed in and not signed out |
   | **Safe** | employee self-marked from their phone, or a marshal marked them accounted for |
   | **Unaccounted** | presumed on site, not yet marked safe ← *the list that matters* |
   | **Not on site** | never signed in today |
4. **Marshal dashboard** — per muster point: counts and names, updating live as people mark themselves safe. Fire marshals are assigned to muster points (legacy `FireMarshalMusterPoint`, `EmployeeMusterPoint`).
5. **Stand down** → immutable **incident report**: who was on site, who was accounted for and when, who was never located, full timeline. Archived (legacy `EmergencyEventsArchive`).

### Why this is a differentiator
- **Zero hardware** — works at any site from day one.
- Turns attendance data into a **life-safety** capability, which is a far stronger sell than "we log hours".
- Self-mark-safe from a phone is something legacy could not do at all.
- Drill mode lets customers rehearse and produces a compliance record.

**Caveat to state plainly to customers:** the roll call is only as accurate as the attendance data. Someone who forgot to sign out appears as unaccounted. That is the safe direction to fail (over-report rather than under-report), but it must be documented, and drills will expose sites with sloppy punching — which is itself useful.

---

## 15. Open Decisions

1. ~~Scope option~~ — **decided: defensible core** (phases 1b–6, plus 6b).
2. ~~Multi-tenancy~~ — **decided: database per customer, deployed on the customer's own server** (§13A). Consequence: infrastructure profiles, and rollout tooling becomes a deliverable.
3. ~~Export builder vs. plugins~~ — **decided: generic builder**, plugins only for exotic formats.
4. ~~Report designer~~ — **decided: AI assistant + saved definitions** (§16).
5. ~~Assistant hardware floor~~ — **decided: must not require new hardware.** Managed API default; CPU-only strict tier for air-gapped sites; GPU optional (§16.3).
6. ~~Physical access control~~ — **dropped** (device-dependent); emergency/mustering reframed as software-only (§17).
7. **Object storage** — MinIO (recommended, S3-compatible, runs on-prem) vs. plain filesystem. On-prem favours filesystem simplicity; MinIO favours a future cloud move.
8. **Email/SMS providers** — on-prem customers usually have their own SMTP relay; SMS needs a provider account per customer or a vendor-brokered one.
9. **"Legacy absences"** — some customers reportedly use an older absence variant. Need to identify what that is in the legacy code before building Phase 3, or we may model the wrong thing.
10. **Which of phases 7–9 to actually build** — usage data suggests expenses yes, visitors low priority. Revisit before Phase 7.
