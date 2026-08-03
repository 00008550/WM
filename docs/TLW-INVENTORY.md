# TLW / Synergy Workforce — Complete System Inventory

**Source of truth for what the legacy product actually does.**
*Compiled 2026-07-20 from an exhaustive scan of `E:\Tlw` — source, database, and the internal Obsidian documentation.*

> Purpose: the earlier architecture drafts under-counted the legacy scope badly. This
> document is the full inventory, measured rather than estimated, so the WM roadmap can
> be planned against reality. Nothing here is aspirational — every item was found in the
> legacy source, schema, or docs.

---

## 0. The numbers

| Measure | Count |
|---|---|
| Top-level projects in `Source/` | **317** |
| Windows services | **59** |
| Web services / API endpoints | 14 |
| Device & comms projects | 72 |
| Payroll export plugins | **44** |
| Custom report plugins | 17 |
| Import plugins | 5 |
| Websites / UI apps | 18 |
| Domain entity classes | **247** |
| **Database tables (LINQ-to-SQL model)** | **579** *(measured 2026-08-04 — the 247 above counts hand-written entity classes, not tables)* |
| **Mapped database columns** | **8,173** |
| **Table associations** | **1,147** |
| Web UI screen folders (Views) | **~230** |
| Standard DevExpress reports | **67** |
| Distinct email notification types | **~70** |
| Daily Template configuration fields | **~150** |
| **Columns on `dbo.Clockings`** | **249** — the widest table WM needs |
| **Columns on `dbo.Employees`** | **153** |
| **Pay category slots (`CPTN01..20`)** | **20, fixed** — hard ceiling |

> **Correction (2026-08-04).** "247 entity classes" is roughly half the real table count. The
> authoritative schema is the LINQ-to-SQL model at
> `Source\Logic\Entities\HorioDB.designer.cs` (6.1 MB, 240,262 lines): **579 tables, 8,173
> columns**. The single most important omission from earlier drafts is the **`Clockings` daily
> aggregate** — see [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md).

Product naming: **Synergy Workforce (SWF)**, previously **Horio**, previously **TLW (Time Log Web)**. All three names appear throughout the source.

---

## 1. Deployment & tenancy model (important)

From `Documentation/High Level Architecture.md`:

- **Database per customer** — `Horio_[Customer]`, `Horio_[Country]_[Customer]`, or `Horio_[Distributor][Customer]`.
- **Web application per customer** — a physical copy of the site per customer on the server.
- **Windows services are shared** — a *single* instance of each service runs on a server and iterates **all customer databases** on that server.
- Device web services identify the tenant by **device IP + device ID** combination.
- Architecture style is SOA: independent services sharing a database.

**Implication for WM:** WM's planned tenant-id-column multi-tenancy is a *different* model. Migration must account for N databases → one (or per-tenant schemas). This is a real decision, not a detail.

---

## 2. Service map — what actually runs

### 2.1 Platform / core services

| Service | Responsibility |
|---|---|
| **HorioService** | Overnight maintenance: purge old logs; **reset balance & flexi balance**; **recalculate previous day for all employees**; **pre-create future Clockings rows**; **allocate cost centres per schedule**; **disable expired cards**; purge old T&A/AC data; **delete inactive employees & leavers** (retention); **auto sign-out visitors** |
| **HorioNotifierService** | All email/SMS + in-app notification generation (see §5) |
| **HorioCalculationService** | Runs calculation **after every swipe**, invoked async by the swipe library to keep response times low |
| **HorioReportExecutorService** | Scheduled/queued report generation and delivery |
| **HorioWatchdogService** | Monitors the other services |
| **HorioWebLicenseManagerService** | Licensing |
| **FilePurgeService**, **DbBackupSenderService**, **DbRestoreService**, **TimeSyncService**, **EmployeeListDistributionService** | Housekeeping / ops |
| **Swipe processing libraries** | `Horio.SyServer.DLL` (T&A) and `AccessControlDLL` (AC) — dynamically loaded by every service that processes swipes, so processing runs in the caller's scope |

### 2.2 Integration services (non-device)

| Service | External system |
|---|---|
| HorioIntegrationService | **Fourth**, **Health Roster**, **People HR** (employee sync, absence import, swipe export, lateness & no-swipe exception export) |
| HorioRotageekIntegrationService | Rotageek |
| HorioSageHrIntegrationService | Sage HR |
| HorioStaffologyHrIntegrationService | Staffology HR |
| HorioLuccaIntegrationService | Lucca |
| HorioPeopleFirstIntegrationService | People First |
| HorioEvalu8IntegrationService | Evalu-8 |
| HorioHotSchedulesIntegrationService | HotSchedules |
| HorioReflexisIntegrationService | Reflexis |
| HorioStaffTrackerIntegrationService | StaffTracker |
| HorioCegidService | Cegid (+ contract-change/bad-file notifications) |
| HorioADPGestilService, AdpDataExchangeService | ADP / ADP Gestil |
| TopazService | Topaz (employee sync + swipe export) |
| HorioSIMSIntegrationService, HorioISAMSSyncService, HorioTribalEbsIntegrationService | School MIS: SIMS, iSAMS, Tribal EBS |
| HorioImportService | FTP imports: absences, leavers, **Cascade** absences/employees, **activities**, **scheduled activities**, **activity clients** |
| HorioExportService | FTP exports: **IQX** swipes; **Cascade** (WorkdayList, AbsenceList, Workdayload, Timesheetimport, payrollexport, WorkdaySwipesList); **Ascendia** activity swipes |
| CdgDataExchangeService, HorioDataExchangeService, JHIImportService, HorioAbsenceImportService | Misc data exchange |
| Documented but not separate services | IQDox, Skiply Ubiqod (Smilio + mustering) |

### 2.3 EPOS / cashless catering services

HorioDailyTopUpService, HorioFreeMealsBalanceService, HorioParentPayService, HorioSquidService, HorioTillSyncService — daily top-ups, free-meal balances, ParentPay/Squid/WisePay payment providers, till sync.

### 2.4 Safety / vehicle / industrial

| Service | Purpose |
|---|---|
| HorioFireLinkListenerService, HorioFireLinkService | Listen for **Adam Fire Link** alarm → auto-send Fire Report |
| HorioLapiImportService / HorioLapiExportService | **LAPI** vehicle drive-through logs in/out |
| HorioANPRService / HorioANPRClientService | ANPR number-plate recognition |
| HorioPCVUEService | PcVue **SCADA** integration |
| HorioAMPMService | AM/PM registration (schools) |

### 2.5 Device services — **out of scope for WM** (user decision)

SupremaControllerService, SupremaNewControllerService, SyFaceService, HorioNewSyFaceService, ThermalFaceService, HorioSyControllerService, HorioIrControllerService, IrCollectService, SALTOHostService, HorioSaltoSpaceClientService, HorioAutomodeService, HorioSynergyMonitoringService, HorioSynergyNeedSyncService, plus 72 device/comms projects and the Synergy A / SY Face / Synergy Touch web services.

**Retained concept:** the *phone* replaces all of these as the punch source.

### 2.6 Client applications

- **Synergy App** — existing **native iOS + Android** app (published to both stores; has emergency push-notification work). WM's Flutter app is the successor.
- **Synergy Touch** — Android tablet kiosk app with its own API and update mechanism.
- **EposTillApplication** — WPF till.
- **TabletKiosk**, **FingerprintCapturer**, **MasterGUI**, Silverlight Planning apps.

---

## 3. Functional inventory by module

Derived from ~230 web screens + 247 entities. **Bold** = not previously in the WM plan.

### 3.1 Time & Attendance
Clockings, swipes, **clocking actions**, **clocking allocations**, **auto-pauses**, **virtual pauses**, **clocking pauses**, corrections, **daily browser**, **transaction report**, **virtual terminal** (+ its employee assignment), manual timesheets (submit → approve/reject), **geolocation tracking**, **QR setup / QR terminal punching**, **work locations on swipes**, **lock data / data locking** (period lock).

### 3.2 Rules & Calculation — *the deepest part of the product*
- **Daily Templates** (~150 fields): template types; **shift 1 & 2** IN/OUT with limits and calc-before/after; **core hours** (+2nd half); **12+ exception types** (early/late entry, exit, break start, break end, long break, short break, expected-hours-exceeded, short day, no swipes); breaks (lunch rules, **delta breaks**, **break half-day**, multi-break with paid/pay-category, min/expected duration, max deduction, grace, rounding, cut-off); **debit/credit** full/half day; **rounding policy** with per-swipe **Prime/Enter/Exit/Final score** rounding + corrections; **schedule thresholds** (block swipes outside window); **daily calculation order** by time-bands or duration with **hour counters per weekday and absences**; **shift matching** rules; **split-shifts**; **multi-shift**; night-shift handling, **day/night hours distribution**, **offset transaction to next day**, allocate to previous/next day, **lost premia percentage**; and a **custom SQL statement per template** with reprocess-after-execute.
- **Periodic Templates**, **Weekly Models** (band + hour counters), **Master Daily Model** assignment, **Calendar day models**, **Day types**.
- **Rounding rules** (global + per template), **hours calculation**, **counters** & **weekly counters**, **flexi balance** + **flexi balance tracking**, **contract hours limits**, **balance reset**, **periods**, **cost centre allocation**, **tariffs**, **pay categories**, **scores / abnormalities** with **muted exceptions**, **exceptions setup**, **blocked exception rules**.

### 3.3 Scheduling
Planning, **Planning Control**, **Auto-Planning**, **Roster** + **Roster Calendar** + **roster notification templates**, **employee schedule requests**, **global schedule thresholds**, **Timetables / Timetable Creator** (+ settings).

### 3.4 Absence & Leave
Absence, absences, **absence allocation**, **absence authorization**, **absence requests** (create/update/review/approve/reject/**block**/cancel + cancellation approval), **absence managers setup** (per employee **and** per department), **predefined absences**, holidays, **school holidays**, **period absences**, **recaps** (recap periods, recap absences), **accruals** incl. **accrual employee allocation**, **length-of-service bonus**, **accrual adjustments**, per-employee accrual calculations with change history.

### 3.5 People & HR — *much deeper than modelled*
Personnel, **Personnel HR**, personnel setup, **positions** + **position qualifications**, **qualifications (with expiry)**, **perks**, employee contracts + **contract assignment** + contract weekly/monthly/flexi counters, **hourly rates**, **employee groups**, **population groups**, cost centres, **custom fields (+ change log)**, **contact info**, **emergency contacts**, **employee images**, **appraisals**, **disciplinaries**, **objectives**, **remunerations**, **certificates**, **onboarding documents**, **fixed-term end dates**, **probation due dates**, **leavers**, **anniversaries**, **delete personnel data** (GDPR retention).

### 3.6 Documents & E-Signature
Company document **categories**, manage/view company documents, employee documents by category (**Remunerations, Disciplinary, Appraisals, Objectives, Certificates**), onboarding documents, documents attached to **absence requests** and **clocking activities**, **document expiry tracking**, **e-signature** (send to sign, token signer access, signed-document storage, reminders daily 09:00), **virus scanning on upload**.

### 3.7 Activities / Job Costing / Field Service — **entirely missing from the WM plan**
Work activities (+ **hierarchy**), activity machines, **activity documents**, **activity notes**, daily activity, **default activity assignment**, **global activity exceptions**, **global activity rounding rules**, **clients**, **job sheets**, **scheduled activities** with **assigned employee + support member**, **travel/driving tracking** ("long driving delay"), activity late-start / early-end / over-run detection, **job costing actual-vs-estimated**, activities by client / cost centre / employee.

### 3.8 Expenses
Expense claims, **expense dashboard**, **mileage claims** + **mileage periods**, expense **types/groups**, **rates**, **payment categories**, **vehicle types & groups**, **expense managers**, full submit → approve/reject → **cancel-request** → approve/reject cancellation workflow.

### 3.9 Access Control (logical parts retained; hardware dropped)
AC calendar, holidays, periods, **year view**, **dashboard**, **presence panel**, settings, **door control**, **door status**, **buildings**, **locations**, **security groups** + **smart security groups**, **reader security groups**, **timezones** (+details), **proximity cards**, **card management**, **card templates**, **card printing**, **card expiry**, **external access log**, **AC event notifications**.

> **`SecurityGroup` belongs here, not to user permissions.** It assigns *employees* to *door readers* over dated periods and is synced to hardware (`Logic/AccessControl/SecurityGroupService.cs`, `SupremaControllerService/SyncSecurityGroups.cs`). With devices dropped it is out of scope for WM, and the name is not reused — user visibility is handled by **groups** (§3.14).

### 3.10 Safety & Emergency
Fire report, **custom fire report**, **online fire report**, **Fire Link** (Adam device alarm → auto report), **mustering dashboard**, **muster points** (employee + **fire marshal**), **emergency events archive**, **guard screen** (with its own notification records).

### 3.11 Visitors
Visitor activity screen, visitor settings, **pre-registration + invitations**, check-in/out, **host notifications**, **early/late arrival alerts**, **auto sign-out overnight**, **delivery/parcel notifications**, visitor reports, **visitor mobile UI**.

### 3.12 Notifications (see §5 for the full catalogue)
Notification setup, **attendance notifications**, **custom notifications**, **report notifications**, **system notifications**, **email settings**, **SYQR notifications**, **EPOS stock notifications**, **student-registration absence alerts**.

### 3.13 Reporting
**67 standard reports**, **custom reports** (17 plugin projects), **Report Builder**, **Web Report Designer v1 & v2**, **statistical** reports, **favourites**, **scheduled report delivery** with recipient-scoped data (Employees / Users / Notification Managers) and per-report data windows.

### 3.14 Security & Administration
Users, **groups** (`/Groups` — a group *is* a role: per-form none/read/edit **plus** managed departments/locations/employees/buildings/cost-centres/work-activities; exposed in the API as `SoftwareAccessGroupDto`), **per-form and per-tab permissions** (`SiteStructure` = the application's page tree of `SiteBranch`/`SiteForm`/`SiteFormTab`; `WebPage` entity; `AccessType` = Read/Edit; `PersonnelTab`), **data access scope** + diagnostics screen, software options, **localization management** + **custom localization** (multi-culture, custom keys), **audit trail** (insert/update/delete interfaces on entities), **user action logs**, **system login reports**, **2FA (email code)**, **new-IP sign-in detection**, password reset, **delete data** tooling, **navigation icons / display settings**.

### 3.15 EPOS / Cashless Catering — ~25 screens (deprioritised for WM)
Tills, till designs, till groups, products, product groups/types, **tally groups**, meals, **free meal settings**, **diners** + diner defaults, **caterers**, sales + **sale corrections**, payments, **VAT settings**, **tariffs**, **inventory**, **receipt status**, **tip management**, **Squid cards**, **ParentPay/WisePay** integration, EPOS reports, **daily reconciliation**.

### 3.16 Student Registration / Schools — deprioritised for WM
Lesson registration (+ bulk, retro), settings, **AM/PM registration** (+ mark/service settings), student dashboard, search, reports, **consecutive & percentage absence alerts**, **mentor/teacher alert reports**, MIS integrations (SIMS, iSAMS, Tribal EBS).

### 3.17 Vehicle / ANPR — deprioritised
ANPR events, car plates, cameras, LAPI security groups & timezones, **swipe data on map**.

---

## 4. Payroll exports — 44 plugins

Dedicated UI screens exist per payroll vendor, so this is not purely a back-end plugin: ADP, Arno HR, Cascade, Cegid, Cegid CBRH, Ciel, Earnie, Fulll Paie, **Generic**, Opera Pegasus, **Other**, Pegasus, Persee, Pixid, Sage, Sage Pastel, SAP, Silae, Thesaurus — plus ~25 further customer-specific plugin projects (Atmos, Boxway, DeLaRue, Delta, IHG, Micropay, NewLook, NSK, Olam, Prima, Rota, RFU, Sprintdata CCH, WCS Care, Wren Kitchens, Zep, …).

**Design implication:** WM needs a **generic payroll export builder** (field mapping + format) so most customers are configuration, not code — otherwise this is 44 plugins to port.

---

## 5. Notification catalogue — ~70 emails, 19 categories

Channels: **email**, **SMS**, **in-app (SWF notifications)**, and **mobile push** (Synergy App).

Recipient resolution uses **four distinct manager assignments per employee** — a key model WM was missing:
**Absence Managers** (per employee *and* per department) · **Expense Managers** · **Timesheet Managers** · **Notification Managers**.

| Category | Notifications |
|---|---|
| **Absence requests** | ~18: created (manager / additional), approved (employee / manager / admin-approved / additional / **with blocked dates**), rejected (employee w/ reason / by admin / additional), blocked (employee / manager), updated, reviewed, cancellation submitted/approved/rejected, **absence manager changed** |
| **Expenses** | 11: mileage submitted/approved/rejected + cancel-request submitted/approved/rejected; expense submitted/rejected + cancel-request submitted/approved/rejected |
| **Activities** | 7: **job finished (with job sheet attached)**, finished earlier, running later, **long driving delay**, scheduled activity assigned, support-member assigned, cancelled |
| **Visitors** | 8: host guest-arrived / guest-left, visitor checked-in / checked-out, early arrival, late arrival, invitation, **parcel delivery** |
| **E-signature** | 4: document to sign, signed (with attachment), pending-signing (to manager), **daily 09:00 reminder** |
| **Manual timesheets** | 3: pending, approved, rejected |
| **HR** | **fixed-term end within 6 weeks**, **probation due**, **qualifications expiring** (email/SMS), **HR documents expiring** (email/SMS) |
| **Employee documents** | uploaded (to employee), per-category notification (to notification managers) |
| **Attendance** | **did not swipe at daily model start** (grace-aware), **swipe outside office** (geofence), **working from home** |
| **Security** | **new IP/device sign-in**, new user welcome, **2FA code**, password reset |
| **Reports** | scheduled report delivery, recipient-scoped, with retry on failure |
| **Access control** | per-event-type configurable |
| **EPOS** | low stock alert |
| **Student registration** | consecutive absence, percentage absence, mentor/teacher alert reports |
| **Thermal camera** | 9 variants (dropped with devices) |
| **System / integrations** | device diagnostics, Synergy Touch device logs, Cegid import issues (contract dates changed, empty file, bad lines, no file) |

---

## 6. Honest gap assessment vs WM today

WM currently implements: Identity + users + employee linking + self-service, People (basic), Time & Attendance (punches/live/timesheet), Licensing lib, Plugin SDK + demo, Worker, Angular shell + 4 pages.

**That is roughly 3–5% of the legacy functional surface.**

| Legacy area | Screens (approx.) | WM status |
|---|---|---|
| Rules & calculation (Daily/Weekly templates, counters, flexi) | 20+ | ▢ not started — **largest single piece** |
| Scheduling / rostering / timetables | 14 | ▢ not started |
| Absence, accruals, holidays | 18 | ▢ not started |
| People & HR depth (appraisals, quals, contracts, custom fields) | 15 | ◐ basics only |
| Documents & e-signature | 5 | ▢ not started |
| Activities / job costing / field service | 10 | ▢ **not even in the plan until now** |
| Expenses | 9 | ▢ not started |
| Access control (logical) | 17 | ▢ not started |
| Safety / mustering / fire | 7 | ▢ not started |
| Visitors | 2 (+ mobile UI) | ▢ not started |
| Notifications (~70 types, 4 manager roles) | 9 | ◐ punch→SignalR only |
| Reporting (67 reports + designer) | 8 | ▢ not started |
| Security/admin (page perms, scopes, localisation, audit) | 12 | ◐ RBAC only |
| Payroll exports | 19 vendor screens, 44 plugins | ◐ SDK + demo |
| Integrations | 18 settings screens, ~22 services | ◐ contract only |
| EPOS / catering | ~25 | ⏹ deprioritised |
| Student registration | 12 | ⏹ deprioritised |
| Vehicle / ANPR | 5 | ⏹ deprioritised |
| Devices | 72 projects | ⏹ **dropped (phone-only)** |

---

## 7. What this means for the roadmap

Three conclusions I'd draw from the measured scope:

1. **The original 8–12 month estimate was wrong.** A faithful rebuild of the *retained* scope (excluding EPOS, schools, vehicle, devices) is realistically **2.5–4 years** at one developer, or ~15–24 months with a small team. The honest options are (a) accept that, (b) cut scope to a defensible core product, or (c) target specific customers' used features rather than parity.

2. **Configuration must replace code.** Legacy grew 44 payroll plugins, 17 report plugins and per-vendor screens. WM should ship a **generic export builder**, a **report designer**, and **rule templates as data** so new customers are configuration. This is the single highest-leverage architectural decision.

3. **Sequence by dependency, not by visibility.** Everything valuable (timesheets, payroll, absence balances, scheduling compliance) sits on the **rules/calculation engine**. It is the deepest and least glamorous piece, and it must come first.

A revised phase plan is in `ARCHITECTURE.md` §14.

---

## 8. Sources

- `E:\Tlw\Source` — 317 projects, `Logic/Entities` (247 entities), `WebSite/Views` (~230 screens)
- `E:\Tlw\Database` — DevExpressReports (67), MenuInfo, Notifications, Localization, Setup
- `E:\Tlw\Documentation` — High Level Architecture, Daily/Periodic Templates, Email Notifications Triggers & Behavior, Access Control, Visitor Management, Absences Configuration, Blocked Exception Rules, 15 integration docs, Synergy App/Touch docs
