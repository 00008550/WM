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
| Backend | .NET 9, C# 13, ASP.NET Core Minimal APIs, module system (`IModule`) |
| ORM | EF Core 9 + Npgsql; Dapper for hot read paths |
| Database | **PostgreSQL 17** (schema per module) |
| Object storage | MinIO (dev) / S3 or Azure Blob (prod) — documents, exports, report output |
| Cache / realtime backplane | Redis |
| Commands & jobs | **RabbitMQ** via **MassTransit** (retry, outbox, sagas) |
| Event streaming | **Kafka** (Confluent.Kafka) — punch/clocking log, audit, notifications, integration feed |
| Realtime to UI | SignalR (WebSockets) — live boards, notification centre, mustering |
| Frontend | Angular 19 (standalone, signals, zoneless), Tailwind, "Control Room" design system |
| Charts/viz | ECharts |
| Mobile (later) | Flutter, same OpenAPI + SignalR + FCM push |
| Reporting | QuestPDF + ClosedXML (DevExpress optional later) |
| Observability | OpenTelemetry, Serilog |
| CI/CD | GitHub Actions, Docker, Trivy scans |

> Node note: dev box is Node 20.17, so Angular 19 until Node ≥20.19 unlocks Angular 20.

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
- **Authorization**: Roles → fine-grained permissions (`employees.manage`, `payroll.export`, `absence.approve`…) via ASP.NET Core policies. **Row-level scope** on the site/department hierarchy (TLW SiteStructure) so a manager sees only their subtree, via EF Core global query filters on scope claims. **Multi-tenancy-ready** (tenant id + filter).
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

Custom roles (bundles of fine-grained permissions) layer on top of these — an Administrator can define e.g. a "Payroll Officer" role.

### Self-service (`/api/me/*`)
An employee-linked user with limited permissions gets a **self-service surface** scoped to *their own* employee id (never an arbitrary one — the id comes from their token, not the request):
- `GET /api/me` — profile + linked employee summary + permissions.
- `GET /api/me/timesheet` — **their own** timesheet.
- `GET /api/me/punches` — their own recent punches; `POST /api/me/punch` — punch themselves in/out (geofenced on mobile).
- Later: `/api/me/absences`, `/api/me/documents` (view + e-sign), `/api/me/expenses`.

This is what makes WM "usable as TLW": create an employee, create a user linked to that employee with the Employee role, and that person logs in to a self-service portal showing only their data. Managers get the team view; admins get everything. The **web UI and Flutter app share these same `/api/me/*` endpoints.**

### User management (`/api/users/*`, permission `users.manage`)
Admin CRUD: list/search users, create (optionally linked to an employee, with roles), edit roles/active state/employee link, reset password, deactivate. **User action logging** (TLW `UserActionLogsService`) feeds the audit stream. Password reset and invite emails go through the Notifications hub (§9).

### Access enforcement
- **Roles → permissions → policies** (already live in WM) for *what actions* a user may perform.
- **Row-level scope** for *which records* — a Manager's queries are filtered to their managed departments/employees; an Employee's to their own id. Implemented as EF Core query filters keyed on scope claims (successor to TLW `RoleBasedEmployeeFilterService` + `SiteItemPermission` + `FormAccess`).
- **2FA** (TOTP + email code — TLW `TwoFactorAuthenticationService`/`TwoFactorEmailCodeSender`) at sign-in for privileged users.

---

## 5. Licensing

- **License = signed JSON document** (ECDSA P-256; WM already implements this). Fields: customer, edition, **feature flags** (`scheduling`, `absence`, `documents`, `expenses`, `visitors`, `plugin:payroll.*`…), **limits** (max employees, max sites, max users), validity window, grace period.
- Private key only in the Licensing Service; products embed the public key → **offline verification**, no phone-home to boot.
- **Online activation** binds a license to an installation id (optional machine fingerprint) enabling revocation/floating counts; **offline activation** file exchange for air-gapped sites.
- Feature gates: server-side `ILicenseFeature` checks **and** token claims so the UI hides unlicensed modules (UI hiding is UX; server always re-checks).
- **Per-plugin licensing**: each plugin id is a licensable feature — mirrors selling payroll plugins per customer.

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
| Clockings / swipes / swipe processing | TimeAttendance | ✅ built (web/mobile punches) |
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
| Users / user management (`Logic/Users`) | Identity (user mgmt) | ▢ planned (building now) |
| User ↔ employee linking (`UserFromEmployeeIdProvider`) | Identity + People | ◐ field exists; UI/self-service building now |
| Employee self-service portal (`EmployeeSchedulingPortal`) | `/api/me/*` + portal | ▢ building now |
| User types Administrator/Employee/Manager (`BuiltinRoles`) | Identity roles | ◐ roles live; Manager scoping planned |
| Manager row-level scope (`RoleBasedEmployeeFilterService`) | Identity/People query filters | ▢ planned |
| User action logging (`UserActionLogsService`) | Admin + `wm.audit` | ▢ planned |
| SSO | Identity | ▢ planned (local JWT live) |
| 2FA (`TwoFactorAuthenticationService`) | Identity | ▢ planned |
| Localization (multi-language) | Admin + i18n | ▢ planned |
| Tip management / job sheets | Admin (or small modules) | ▢ planned |
| **Device comms (Suprema/SyFace/Salto/ANPR/thermal/fingerprint)** | — | ⏹ **dropped by decision (phone-only)** |
| EPOS / cashless till | — | ⏹ deprioritized |
| Student registration | — | ⏹ deprioritized (schools vertical) |
| Card printing | — | ⏹ dropped (was device-adjacent) |

Legend: ✅ done · ◐ partial / foundation laid · ▢ planned · ⏹ intentionally out of scope.

---

## 14. Roadmap (v3 — rebased on the measured legacy scope)

> **Read `TLW-INVENTORY.md` first.** An exhaustive scan of the legacy product (317 projects,
> 59 services, 247 entities, ~230 screens, 67 reports, ~70 notification types, ~150 fields on
> the Daily Template alone) showed the earlier estimate was badly wrong. WM today is
> roughly **3–5%** of the legacy functional surface. This roadmap is rebased on that reality.

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
| **1b — Core depth** | Employee contracts, custom fields, positions/qualifications, corrections, period locking, employee groups & population groups | 4–6 wks | ▢ next |
| **2 — Rules engine** ⭐ | Daily templates (shifts, breaks, core hours, rounding policy, exceptions, shift matching, split/multi-shift), weekly models, counters, flexi balances, pay categories, cost-centre allocation, recalculation & replay | **10–16 wks** | ▢ |
| **3 — Absence & Accruals** | Absence types, requests + multi-level approval (absence managers per employee/department), blocked dates, holidays, entitlements, accruals incl. length-of-service, recaps | 8–10 wks | ▢ |
| **4 — Notifications & Documents** | Notification hub (in-app + email + SMS, 4 manager-assignment roles, ~70 typed notifications, templates, per-user preferences), Documents + categories + expiry, e-signature, object storage | 8–10 wks | ▢ |
| **5 — Scheduling** | Rotas, planning board, auto-planning, roster calendar, schedule requests, thresholds, timetables | 8–12 wks | ▢ |
| **6 — Reporting & payroll economics** | Report engine + **designer**, scheduled delivery with recipient scoping, favourites; **generic payroll export builder** + 2 pilot formats; licensing service; plugin admin | 10–14 wks | ▢ |
| **7 — Expenses & Field service** | Expenses + mileage (full cancel-request workflow), activities/job costing, scheduled activities, job sheets, clients, travel tracking | 10–12 wks | ▢ |
| **8 — Access, Safety & Visitors** | Logical access control (groups, calendars, doors, cards), mustering & fire roll-call, guard screen, visitors + pre-registration + deliveries | 8–10 wks | ▢ |
| **9 — Mobile & enterprise** | Flutter app (punch, rota, absence, documents + sign, push), 2FA, SSO, localisation, audit browser, page-level permissions & data scopes | 8–12 wks | ▢ |

**Realistic total for the retained scope: ~18–30 months** (one developer at the low end of parallelism; ~15–24 months with a small team). Explicitly **excluded**: devices (dropped), EPOS/catering (~25 screens), student registration (~12 screens), vehicle/ANPR (~5 screens).

### Three honest options
1. **Parity** — accept the multi-year timeline above.
2. **Defensible core** (recommended) — Phases 1b–6 only: time, rules, absence, notifications/documents, scheduling, reporting + payroll. That is a genuinely sellable T&A product in roughly **12–18 months**, with everything else added on demand.
3. **Customer-led** — instrument which features your actual customers use and build only those. Legacy has ~230 screens; a typical customer touches a fraction.

Migration: old TLW keeps running. A `TlwLegacyConnectorPlugin` reading the existing SQL Server database bridges data during transition — worth building early (Phase 2–3) so WM can run alongside on real data.

---

## 15. Open Decisions

1. **Scope option** — parity vs. defensible core vs. customer-led (see above). **This is the decision that shapes everything else.**
2. **Multi-tenancy migration** — legacy is **database-per-customer** with shared services iterating all DBs; WM plans tenant-id columns. Confirm the target and the migration path.
3. **Generic export builder vs. per-customer plugins** — recommended: builder first, plugins only for genuinely exotic formats.
4. **Object storage** for dev — MinIO (recommended, S3-compatible) vs. local disk.
5. **Email/SMS providers** for the Notifications hub.
6. **Report designer** — build one, or accept DevExpress licensing.
7. **Which legacy features are actually used** — the highest-value question; ideally answered with data from live customers before committing to Phases 5–9.
