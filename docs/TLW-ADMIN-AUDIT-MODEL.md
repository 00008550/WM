# TLW admin / config / audit — measured

Surveyed 2026-08-31. Sources opened: `E:\Tlw\Source\Logic`, `E:\Tlw\Database\Versioning`,
`E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs`. Every claim carries a `file:line`. WM's own
documents were treated as claims to verify, not evidence.

This is the survey of **everything else** in `COVERAGE-AUDIT.md:58`'s *Admin / config / audit*
bucket (16 tables / 376 columns) after `SoftwareMainOptions` (227 cols) was measured in plan 012
(`TLW-GLOBAL-OPTIONS.md`). It exists because the 2026-08-18 ruling (`ARCHITECTURE.md §0a`) made the
audit trail load-bearing: the Daily Browser becomes editable, so *who changed a calculated value*
stops being hygiene.

---

## 1. The measurement

### 1.1 The bucket's real footprint — base tables vs report views

**WM's claim: 16 tables / 376 columns.** Re-derived against the schema, the count is essentially
correct **and — unlike the Activities and Scheduling buckets — it is not inflated by report views.**
The over-counting caveat does not bite here: the bucket is ~99% base tables.

| Member | Cols | Kind | Note |
|---|---:|---|---|
| `dbo.SoftwareMainOptions` | 227 | base (singleton) | measured in plan 012 — excluded from this survey |
| `dbo.ClockingsLog` | 97 | base | **86% of the non-SMO footprint sits here.** Clocking change-audit; see §2. Arguably a T&A-bucket table |
| `dbo.AuditTrailLogs` | 9 | base | the generic field-level change log; see §3 |
| `dbo.EmailSettings` | 7 | base (singleton) | SMTP config, **plaintext password** |
| `dbo.UserActionLogs` | 7 | base | login / session audit; see §4 |
| `dbo.SystemNotifications` | 7 | base | admin broadcast banners |
| `dbo.DisplayModelsSettings` | 5 | base | dashboard/display config |
| `dbo.ExternalAccessLogs` | 4 | base | API request log |
| `dbo.SystemNotificationsLogs` | 4 | base | per-user read receipts for the banners |
| `dbo.LogDeleteStatus` | 4 | base | retention-job progress tracker |
| `dbo.DisplaySettings` / `dbo.DisplayEmployeesSettings` | 4 / 4 | base | display config |
| `dbo.AuditTrailLogsView` | 4 | **view** | report projection over `AuditTrailLogs` |
| `dbo.DefaultDisplaySettings` / `dbo.sy_dbsettings` | 3 / 3 | base / base | `sy_*` is access-control DB config → **Drop** |
| `dbo.EmployeesForLogCleaner` | 2 | base | staging table for the cleaner job |

**Both numbers, as asked:** ~**372 base-table columns + ~4 report-view columns** ≈ 376. Two tables
(`SoftwareMainOptions` 227 + `ClockingsLog` 97 = **324, or 86%**) are the bucket. The remaining ~52
columns are ~13 small config/audit tables. **This is a two-table bucket wearing a sixteen-table
costume**, and one of those two (`ClockingsLog`) is really the audit shadow of the T&A core.

### 1.2 Classification — 0 unclassified

| Table | Class | Reason |
|---|---|---|
| `ClockingsLog` | **Improve** | Right idea (change history of the central aggregate); wrong shape — a 94-column denormalised full-row mirror with `calc_*` and `CPTN01..20` slots duplicated. WM's `wm.audit` + replay design replaces it. |
| `AuditTrailLogs` | **Improve** | Right idea (one field-level change store); wrong implementation — written by three uncoordinated mechanisms with a hand-maintained `TableId`/`ColumnId` integer map in two places. |
| `AuditTrailLogsView` | **Drop** (as a table) | A report projection; WM's audit browser queries the store directly. |
| `UserActionLogs` | **Keep** | Login/action audit is genuine domain truth; folds into `wm.audit`. |
| `ExternalAccessLogs` | **Improve** | API access log; WM gets this from structured request logging/observability (plan 011), not a domain table. |
| `SystemNotifications` + `SystemNotificationsLogs` | **Keep** | Admin broadcast banner with per-user dismissal — a real admin feature; belongs to Notifications/Admin. |
| `EmailSettings` | **Improve/Invert** | SMTP config is Keep; **storing the password in plaintext (`NVarChar(50)`) is Invert** — WM uses a secret store. |
| `LogDeleteStatus`, `EmployeesForLogCleaner` | **Improve** | Retention-job machinery; WM replaces with a scheduled worker + `wm.audit` retention policy (§16). |
| `Display*Settings` (4 tables) | **Improve** | Per-install/per-role display defaults; fold into Admin settings, not four tables. |
| `sy_dbsettings` | **Drop** | Access-control (`sy_*`) database config — devices are out of scope (CLAUDE.md §3). |
| Licensing (no table) | **Keep** | See §5 — runtime signed key, already has a WM home (`src/Licensing`). |

---

## 2. `dbo.ClockingsLog` — the 97 columns

**It is a fixed-shape, whole-row mirror of `dbo.Clockings`, not a field-level diff.**
`HorioDB.designer.cs:49104`. The first 94 columns are `Clockings`' own columns —
`ClockingId`, `EmployeeId`, `DailyModelID`, the twelve `BadgeTime1..12` / `DeviceBadgeTime1..12` /
`BadgeTimeNGeneratedBy` slots, `DurationTheoretic`, every `calc_*` field
(`calc_grossAttendance`, `calc_netAttendance`, `calc_actualWork`, `calc_nightHours`,
`calc_balance`, …) and the `CPTN01..20` + `CPTT01` counter slots — reproduced verbatim. Three audit
columns are appended:

| Column | Type | Meaning |
|---|---|---|
| `DateUpdated` | `DateTime NOT NULL` | when the row was captured |
| `Operation` | `NVarChar(50) NOT NULL` | `INSERT` / `UPDATE` / `DELETE` |
| `UpdatedBy` | `VarChar(250)` | actor |

**Written by a SQL trigger, not by C#.** `Clocking_Update_Trigger`
(`Database\Versioning\30.V3.0.0.sql:608`) and its insert/delete siblings copy the whole affected
`Clockings` row into `ClockingsLog` on every change. A row is written **per operation, per
clocking** — so it is a **snapshot mirror**, one row = the full post-image (or pre-image on delete),
tagged with the operation. It is **not** a before/after pair and **not** a per-column diff: to see
what changed you diff consecutive `ClockingsLog` rows yourself.

**Consequences for WM's audit design (and plan 002):**
- The audit shadow of the central aggregate carries the same smells the aggregate does —
  `calc_*` stored values and `CPTN01..20` fixed slots — so every defect in `Clockings` is
  duplicated into its log. WM's replay (plan 002) removes the need to *store* the log of a
  recalculated value at all: the audit records the **inputs and the human overrides**, and the
  `calc_*` outputs are derived.
- `Clockings` is `249/249 UpdateCheck.Never` (recorded in `STATE.md`, 2026-08-29): legacy
  deliberately exempts the wholesale-recalculated aggregate from optimistic concurrency, and mirrors
  every write into `ClockingsLog` instead. That is the provenance question plan 002 asks — answered:
  **legacy's provenance for a clocking is a full-row snapshot trail, not a field diff.**

---

## 3. Is audit uniform? No — it is a four-mechanism patchwork

Legacy does **not** have one change-log mechanism. There are four, with different shapes and
different coverage. This is the single most important finding for WM, whose `wm.audit`
(`ARCHITECTURE.md §16:161`) is one uniform store — the correct **Improve/Invert** of this.

**Mechanism A — the generic field-level store `AuditTrailLogs` (9 cols).**
`Id, Date, ActionId, TableId, ColumnId, OldValue(MAX), NewValue(MAX), UserName, RowId`
(`HorioDB.designer.cs:86448`). `TableId` and `ColumnId` are **integer codes**, not names. It is
written by *three* uncoordinated paths into the same table:
- **A.1 — C# ChangeSet interceptor.** `HorioDataContext.SubmitChanges` override
  (`Entities\HorioDataContext.cs:105-124`) walks `GetChangeSet()`; entities implementing
  `IAuditInsertEntity` / `IAuditUpdateEntity` / `IAuditDeleteEntity` emit an `AuditTrailLog` via
  `GetLog(...)`. **Opt-in — ~22 entity partials** under `Entities\AuditTrail\` implement it:
  `Employee`, `Device`, `DailyModel`, `Holiday`, `WeeklyShift`, `WorkActivity`, `ClockingActivity`,
  `ClockingPause`, `AccrualsEmployee`, `PeriodAbsence`, `RecapAbsence`, `EmployeeQualification`,
  `EmployeeRemuneration`, `EmployeeDisciplinary`, `EmployeeAppraisal`, `EmployeeCertificate`,
  `EmployeeDocument`, `EmployeeObjective`, `EmployeeOnboardingDocument`, `EmployeeSecurityGroup`,
  `Link_Employee_WeeklyShift`, `RecapPeriodEmployee`. HR/personnel-heavy. **For `Employee` the C#
  path logs only a summary string** (`"Firstname Lastname Code"`), not a column diff
  (`Entities\AuditTrail\Employee.cs:13-21`).
- **A.2 — manual C# calls.** `AuditTrailLogService` exposes hand-written loggers per concern —
  `AddAuditTrailLogForEmployee`, `...ForDailyModelBreaks`, `...ForCardManagement`,
  `...ForEmergencyControl`, `...ForAutomaticDeleteSettings`, `...ForPersonnelDataDelete`
  (`AuditTrail\AuditTrailLogService.cs:54-287`). Callers pass `columnId` by hand. **De-dupe rule:**
  a log is skipped if `OldValue == NewValue` unless it is a personnel-delete (`:30`).
- **A.3 — SQL triggers.** `Employees_Update_Trigger` (`Database\Versioning\30.V3.0.0.sql:582-604`)
  builds dynamic SQL from a **hard-coded `#ColumnKeys` map** (`ColumnName → ColumnId`, e.g.
  `TariffId=79`, `WTDOptOut=80`, `NotificationManager1..10 = 68..77`), inserting one
  `AuditTrailLogs` row per changed column, then stamps `Employees.UpdatedAt`. The map is maintained
  **in parallel** with the C# `AuditTrailLogsTables`/`...Columns` enums — two places, and the trigger
  carries a self-described *"in case columns got deleted and we are not aware"* safety delete
  (`:575-578`) proving the map drifts. This is the `Thing1..N` numbered-slot smell applied to an
  audit vocabulary.

**Mechanism B — full-row mirror tables per entity.** `ClockingsLog` (§2, trigger) and
`AbsenceRequestsLogs` (26 cols, `HorioDB.designer.cs:47082`) — a fixed-shape mirror of an absence
request, itself carrying `Manager1*/Manager2*` **numbered approver slots** (a two-approver ceiling).
Plus clocking-pause logs and others. Hand-rolled, one table per audited entity, no shared shape with
A.

**Mechanism C — login / session / security audit.** `UserActionLogs` (§4), `ExternalAccessLogs`
(API requests: `Date, Request, IP`), and the security-history tables `User2FAHistory` (3),
`UserPasswordHistory` (3).

**Mechanism D — nothing.** The vast majority of tables have no audit at all. `AuditTrailLogs`
coverage is exactly the ~22 opt-in entities plus whatever triggers exist; everything else changes
silently.

**Count of `*Log` / `*History` / `*Audit` tables in the schema: 24** (measured). Most are
domain-specific (`MobileAccessLogs`, `LapiAccessLogs`, `ReportNotificationLogs`,
`RosterNotificationLogs`, `HrNotificationLogs`, …) and belong to their own buckets — they are
notification/delivery logs, not change audits. The genuine **change-audit** mechanism is A+B above.

---

## 4. `UserActionLogs` — security/login audit

`Id, UserId, EventDateTime, UserAction(int), IP(15), Browser(MAX), Device(200)`
(`HorioDB.designer.cs:161590`). `UserAction` is an integer enum (login, logout, failed login, etc.).
This is the successor `ARCHITECTURE.md:194,468` already names. It is distinct from `AuditTrailLogs`
(data changes) and `ExternalAccessLogs` (raw API hits). WM folds all three into `wm.audit` with a
typed event kind.

---

## 5. Licensing — no table; a runtime signed key, fail-open

**There is no license table in the schema.** Licensing is a C# subsystem: `Horio.Logic.License`
(`License\LicenseInfo.cs`, `LicenseItems.cs`, `LicenseService.cs`) over a `Licensing` assembly.

**What it gates on** (`LicenseInfo.cs:10-23, 148-208`): counts of active/all employees, QR users,
virtual-terminal users, tablets, access-control doors, devices, **daily templates**, and per-module
document storage (documents/expenses/activities/visitors, in GB) — each with an `Allowed*` ceiling
and an over/near-limit check. Plus `ValidUntil` (expiry), `DbName` binding (a key is issued for one
database), and a `HardwareKey` (`HardwareInfo.GetUniqueId()`). **Feature packages** are
`ActivePackages: List<LicenseTypeDefinition>` keyed by `LicenseType` (`Full`, `Cashless`,
`TLWLAPIAccess` → ANPR, `ER2Registration` → student registration, …); capabilities resolve as
`IsEposEnabled`, `IsAnprEnabled`, `IsStudentRegistrationEnabled` (`:287-294`).

**Two fail-opens to Invert** (`LicenseInfo.cs`):
1. `ShouldUseLicensing` reads app-setting `EnableLicensing`; **absent or unparseable → `false`**
   (`:25-45`).
2. `CorruptedOrAbsentLicense` returns **`FullLicense` (allow-everything, unlimited, never expires)**
   whenever licensing is disabled (`:296-334, 47-88`). So the default posture of an un-configured
   install is *fully licensed forever*. WM's `src/Licensing` must **fail closed** — no key means no
   feature package, not all of them.

**What WM's `src/Licensing` must express** (it has the ECDSA codec + CLI already, per
`ARCHITECTURE.md:135-137`): the ceilings above as signed claims, `ValidUntil`, an install/tenant
binding (WM's equivalent of `DbName`), and the feature-package set — verified **offline** against the
embedded public key, failing closed.

---

## 6. Retention and data lifecycle — legacy DOES purge, on a schedule

Plan 012 found `NumberOfDaysToKeepLogs` and `RequireFullLogging` **dead** in `SoftwareMainOptions`.
They are dead because they were **superseded**, not because legacy never purges. The live mechanism
is the **Log Cleaner**, driven by a different set of `SoftwareMainOptions` columns
(`MaintenanceService.cs:218-283`):

| Setting | Role |
|---|---|
| `LogCleanerEnableAutoCleaner` | master on/off |
| `LogCleanerBeginDate`, `LogCleanerTime`, `LogCleanerPeriod` | schedule (start, time-of-day, recurrence) |
| `LogCleanerDaysToDelete` | retention window |
| `LogCleanerDeleteTAData`, `LogCleanerDeleteACData` | which data classes to purge (T&A vs access-control) |
| `DeleteInactiveEmployeesAndLeavers` (+`Days`) | leaver deletion |

Every change to these is itself audited via `AddAuditTrailLogForAutomaticDeleteSettings`
(`MaintenanceService.cs:228-245`). Execution runs a large stored proc that deletes per-employee,
per-data-class, tracking progress in `LogDeleteStatus` (`Id, LogDeleteId, LogStatus,
ProgressPercentage`) with a GUID job id and a running `ClearTransactionDataLogs` narrative
(`Database\Versioning\74.V5.20.0.0.sql:249-591`), staging targets in `EmployeesForLogCleaner`.

**So retention is real, scheduled, and configurable per data class** — a **Keep/Improve**: WM's
`wm.audit` retention (§16:289, "hard requirement, not a default") and a scheduled worker cover it,
but WM must not hard-code a single window — legacy exposes schedule, window, and per-class scope.

---

## 7. Corrections to WM's records (propose-only — consolidated pass)

Per the task boundary, I do **not** edit `ARCHITECTURE.md`, `COVERAGE-AUDIT.md` or `STATE.md`. These
are the changes owed:

1. **`COVERAGE-AUDIT.md:58`** — mark the bucket surveyed. The 376 is correct and **not
   view-inflated** (unlike Activities/Scheduling): ~372 base + ~4 view. But it is a **two-table
   bucket** — `SoftwareMainOptions` (227) + `ClockingsLog` (97) are 86% of it — and `ClockingsLog`
   is really the T&A core's audit shadow.
2. **`ARCHITECTURE.md §13:431`** — the row *"Column-level change history (`Employees_Update_Trigger`
   → `AuditTrailLogs`)"* is **correct** (the trigger exists, `30.V3.0.0.sql:582-604`) but
   **understated**: `AuditTrailLogs` is written by **three** paths (trigger + C# interceptor + manual
   calls), the C# `Employee` path logs only a summary string, and the `ColumnId` map is
   hand-maintained in two places. It is a patchwork, not one trigger.
3. **`ARCHITECTURE.md §13:395`** — the `ClockingsLog` row is correct; add that it is a **full-row
   snapshot mirror written by a SQL trigger**, not a field diff — which is the provenance answer
   plan 002 needs.
4. **`ARCHITECTURE.md §5 (Licensing)`** — record that legacy licensing has **no DB table**, gates on
   the ceilings in §5 above, and **fails open twice** (default-disabled → allow-all). WM must fail
   closed. `src/Licensing` already has the codec; it needs these claim fields.
5. **`ARCHITECTURE.md §13A` (retention)** — `SoftwareMainOptions.NumberOfDaysToKeepLogs` is dead but
   retention is **live** via the Log Cleaner (§6); WM's retention must expose schedule + window +
   per-class scope, not a single constant.

---

## 8. What I did NOT measure (numbered)

1. **The exact 16-table membership WM used for the 376.** `COVERAGE-AUDIT.md` has no per-table
   appendix; I re-derived the footprint (§1.1) but cannot prove which 16 rows WM summed. My set
   reproduces 376 within rounding; the two dominant tables are certain.
2. **The full `AuditTrailLogsTables` / `AuditTrailLogsColumns` integer enums.** The enum definitions
   live outside `Logic\` (a shared/`DataAccess` assembly the grep did not resolve); I confirmed their
   *use* and the parallel SQL `#ColumnKeys` map, not the full value list.
3. **Which entities beyond `Employee` and `Clocking` have SQL audit triggers**, vs relying only on
   the C# interceptor. I confirmed the two named triggers; a full trigger census of
   `Database\Versioning` (append-only, higher numbers redefine) was not done.
4. **`Display*Settings` semantics** — measured shape and count, classified by name; did not read
   their consumers.
5. **`sy_dbsettings` / `ExternalAccessLogs` retention interaction** with the access-control purge
   branch (out of scope — devices dropped).
6. **The Documents bucket** — explicitly reserved for the concurrent survey (plan 019).
