# 007 — The person record: employment as a date, and three defects under it

Status: in-progress      <!-- draft → approved → in-progress → in-review → merged -->
Approved by user 2026-08-06, all 5 portions. **P1 merged 2026-08-17 as #61 (`ead3bfc`); P2 is next.**
P1 and P2 were ordered ahead of 003 P2b — the same "fix what is bleeding" rule applied to 006 P2/P3
— though in the event P2b shipped first, at the user's direction.
Roadmap: ARCHITECTURE.md §14 phase 1c ("Core depth"), and §13's newly-split People rows
Legacy sources surveyed: [`../TLW-PEOPLE-MODEL.md`](../TLW-PEOPLE-MODEL.md) — 76 tables / 631
columns measured, `dbo.Employees`' 153 columns classified one by one. Full file list in its §1.
Plus a **narrow Absence dependency check** for P1's `OnLeave` deletion, 2026-08-06 — §4.1a there,
scope and non-coverage recorded in [`../COVERAGE-AUDIT.md`](../COVERAGE-AUDIT.md) §2a.

> ### ⚠️ Correction to this survey, 2026-08-06 — the user caught a miss
> This plan originally stated that `EmployeeStatus.OnLeave` *"has no legacy counterpart"* and that
> leaving is only a date. **The leaver model in legacy is richer than that, and the survey missed
> two of the 153 columns it claimed to have classified, plus a whole lookup table:**
>
> | Legacy | |
> |---|---|
> | `Employees.DischargeDate` | `Date`, nullable (`HorioDB.designer.cs:29831`) |
> | `Employees.LeaveReasonId` | nullable FK, association `LeaveReason_Employee` (`:30707`, `:34148`) |
> | `Employees.AdditionalLeaverComments` | `nvarchar(500)` (`:32539`) |
> | `dbo.LeaveReason` | `Id, Name, IsActive` (`:53138-53148`) — a **customer-maintained lookup**, deactivatable rather than deleted |
>
> So leaving is **a date, a reason chosen from a maintained list, and optional comments**. WM's
> `Terminated` carries none of the three. P1 adopts all of them.
>
> What the survey got right: there is no *temporary* "on leave" state on the person. That is an
> **absence**, and it belongs to the Absence module. The two were being conflated. `OnLeave` is
> still dropped — but because absence is a different subsystem, not because leaving is unmodelled.

> ### ✅ Confirmation, 2026-08-06 — the `OnLeave` drop is now measured, not assumed
> The claim above ("that is an absence, in a module nobody has surveyed") was an *assumption*: no
> one had opened Absence. Since P1 deletes `EmployeeStatus.OnLeave` on it, a **narrow dependency
> check** was run before building — four questions only, **not a survey of Absence**
> (`TLW-PEOPLE-MODEL.md` §4.1a; scope recorded in `COVERAGE-AUDIT.md` §2). It lands in P1's favour:
>
> 1. **No temporary-non-availability state exists on `dbo.Employees`.** All 153 columns re-scanned;
>    every absence-shaped hit is configuration or an approver role — `AbsenceGroupId` (`:30555`),
>    `HolidayGroupId` (`:30731`), `HolidaysAmount`/`Period` (`:30667`, `:30687`),
>    `IsAbsenceManager`/`IsDeputyAbsenceManager` (`:31275`, `:31295`). The instance is dated
>    elsewhere: `dbo.AbsenceRequests(EmployeeId, StartDate, EndDate, AbsenceId)` (14 cols, `:46542`)
>    and, per day, `Clockings.MorningAbsenceID`/`AfternoonAbsenceID` (`:21310`, `:21334`).
>    Legacy's own person-status vocabulary is **three computed values** — active / leaver /
>    inactive, from `IsActive` × `dbo.IsActiveEmployment` (`PersonnelService.cs:1890-1904`).
>    **There is nothing for `OnLeave` to mirror. P1 is right.**
> 2. **The two reason vocabularies are genuinely different tables.** `dbo.LeaveReasons` (3 cols,
>    `:53137`) is a label under `Menu_Personnel_LeaveReasons`. The absence vocabulary is
>    **`dbo.Absence` — 35 columns** (`:8743`), a *rule-carrying type*: `Unit`, `Category`
>    (`Holiday | Sick | MaternityPaternity | OtherEvent`), `AllowOnDayOff`, `CounterId`,
>    `IsExcludedFromPayrollExport`, `BlockAbsenceRequestOnNegativeBalance` … **No collision — but a
>    naming risk. See open question 6.**
> 3. **Accruals confirm P1's shape independently.** `AccrualsCalculationRepository.cs:53-57` selects
>    `EnterDate, ContinuousServiceDate, DischargeDate, FinalEmploymentDate` and
>    `EmployeeAccrualCalculationsService.cs:728-741` intersects
>    `DateTimeInterval(EnterDate, employmentEnd)` with the accrual period to pro-rate entitlement.
>    The downstream consumer wants **exactly `[EmployedFrom, EmployedUntil]`**, not a status enum —
>    and reads dates only, never `IsActive`, which confirms keeping suspension separate.
>    ⚠️ One caveat P1 should record: legacy's window end is **`FinalEmploymentDate ?? DischargeDate`**,
>    a *second* leaving date with higher precedence. P1 adopts only `DischargeDate`; that is fine
>    now, but Phase 3 will meet it.
> 4. **Nothing in Absence writes `IsActive`/`DischargeDate`/`LeaveReasonId`, and nothing reads
>    `ActiveEmployeesView`.** `PlanningService.cs:32-57` re-derives employment itself — a *sixth*
>    copy of the predicate, evaluated against `DateTime.Now.Date` rather than the period being
>    planned. More weight behind P1's single `IsEmployedOn`.

---

## Ground truth

**`dbo.Employees` is the 6th-largest table in the legacy schema — 153 columns**
(`HorioDB.designer.cs:28594`, columns `:29583`–`:32703`), behind only `Devices` (268, dropped),
`Clockings` (249, plan 002), `SoftwareMainOptions` (227), `FireMarshalMusterPoints` (177) and one
report view. WM's `Employee` has **10 domain properties + 4 audit fields**
(`src/Modules/People/WM.Modules.People/Domain/Employee.cs:27-42`).

Classified against WM, all 153:

| | Cols |
|---|---:|
| ✅ Modelled, same meaning | **11** |
| 🔶 Modelled but diverges | 4 |
| ⏹ Dropped by an existing decision (devices 12, EPOS/schools 19) | 31 |
| ♻️ Stored calculated state, replaced by replay | 13 |
| ▢ Owned by a named later phase (Rules 26, other modules 35) | 61 |
| ❌ **No owner anywhere — not in a plan, a §13 row, a screen or a migration** | **33** |

**Corrections made to WM's records by the survey** (details in `TLW-PEOPLE-MODEL.md` §10):
`COVERAGE-AUDIT.md` §2's People/HR mark and queue; `ARCHITECTURE.md` §13 — one vague row became
sixteen checkable ones, and two shipped rows moved to `⚠️`; `TLW-AUTHORIZATION-MODEL.md` §1
(the authorization surface is 19 tables / 66 columns, not 18 / 59 — seven permission booleans live
on `dbo.[User]`), §10 (fourteen fail-opens, not twelve) and §15; `SCREEN-TREE.md` (a
self-contradiction about Sites vs Locations, plus which dimensions actually nest).

**This plan covers only the executable subset.** Most of the 33 are backlog. Three things are
**defects in running code**, and one is a foundation that plan 002 needs.

---

## Legacy behaviour (what we are replacing)

### Employment is two facts, one of them a date

```sql
-- E:\Tlw\Database\Versioning\76.V5.22.0.0.sql:25-38
CREATE OR ALTER FUNCTION dbo.IsActiveEmployment (@dischargeDate DATE, @referenceDate DATE)
RETURNS BIT AS BEGIN
    RETURN CASE WHEN @dischargeDate < @referenceDate THEN 0 ELSE 1 END;
END;
```

- **`IsActive`** (bit) — administratively suspended. Bulk-set with no date
  (`Logic/Personnel/PersonnelService.cs:93-114`, `:151-173`).
- **`DischargeDate`** (date) — the **last day of employment, inclusive**, always evaluated against
  a reference date. `SetEmployeesLeaver:122-145` writes `DischargeDate` + `LeaveReasonId` and
  deliberately does **not** touch `IsActive`.

Three states, filtered independently: active / leaver / inactive
(`PersonnelService.FilterEmployeesByStatus:1848-1871`, `FilterEmployeeIdsByStatus:1873-1922`,
`FilterEmployeesNonActiveFiredExclusive:1818-1846`; predicates in
`Logic/Extensions/EmployeeExtensions.cs:14-49, 251-284`).

### Legacy fails closed on employment at every write boundary

`dbo.ActiveEmployeesView` or `.ActiveNotFired()` gates device enrolment and badge access —
`ExternalAccess/KioskExternalAccessService.cs:152, 201`; `Devices/IrTemplatesService.cs:76`
(commented `--active and not fired`); `Personnel/FaceService.cs:176`;
`Devices/DevicesService.cs:1230` — and HR notification (`HR/HRService.cs:1450, 1487, 1524, 1561,
1598`, each commented `--active not leaver`). `Employees_Update_Trigger:1523-1528`
(`Database/Versioning/77.V5.23.0.0.sql`) pushes a **delete** task to every device the moment
`IsActive` goes 1→0.

> **And legacy got it wrong once, in the obvious way — then fixed it.**
> ~~`dbo.ActiveEmployeesView` (latest revision `Database/Versioning/78.V5.24.0.0.sql:74-80`)~~
> **Corrected 2026-08-14:** `78.V5.24.0.0.sql:74-80` is the **third of four** revisions, not the
> latest. `WHERE IsActive = 1 AND DischargeDate IS NULL OR DischargeDate >= CAST(GETDATE() AS date)`
> did mean `(IsActive = 1 AND DischargeDate IS NULL) OR (DischargeDate >= today)` between v5.22 and
> v5.24 — but **`79.V5.25.0.0.sql:728-734` added the parentheses** and no later script redefines the
> view. The shipping product is correct here. See `TLW-PEOPLE-MODEL.md` §4.1's 2026-08-14 correction
> block for the full four-revision table and the method note about citing `Versioning/`.
>
> The design argument is unaffected and does not rest on this bug: employment should be computed
> **once**, in one place, because legacy re-derives it in six and needed three releases to notice one
> typo in one of them.

### Identity: two uniqueness rules, both stricter than WM's

```sql
-- PersonnelService.IsEmployeeCodeFieldUnique:2697-2709
select case when right('0000000000' + @code, 10) in
    (select right('0000000000' + Code, 10) from Employees where Id != @employeeIdToExclude)
  then 0 else 1 end
```

Left-zero-padded to 10 — `42`, `0042` and `000042` are the same code — **on top of** SQL Server's
case-insensitive default collation (`SQL_Latin1_General_CP1_CI_AS`, visible at
`77.V5.23.0.0.sql:1425`). `IsEmployeeBadgeUnique:2726-2741` does the same for `Badge`.

### `DepartmentId` is `NOT NULL`; `EmployeeLocationId` is not

Legacy's department and location are **independent** columns on the employee
(`HorioDB.designer.cs:29687`, `:32599`). There is no site↔department relation on the person, so
there is nothing to be inconsistent. WM introduced that relation (`Department.SiteId`) — see
*Edge cases*.

---

## Keep / Improve / Invert / Drop

Full table in `TLW-PEOPLE-MODEL.md` §8. The entries this plan acts on:

| Structure | Class | Reason |
|---|---|---|
| Employment as **(administrative state × dated end)**, evaluated at a reference date | **Keep — adopt properly** | `IsActiveEmployment(dischargeDate, referenceDate)` is right, and WM's undated enum is a regression. Every historical question in the product depends on it. |
| `IsActive` and `DischargeDate` as two separate facts | **Keep** | Suspension ≠ leaving; `SetEmployeesLeaver` proves legacy treats them separately on purpose. |
| `EmployeeStatus.OnLeave` | **Drop** | WM invented it. Legacy's "on leave" is an *absence* — dated, a separate subsystem. Two systems would answer differently the day Absence ships. **Measured 2026-08-06 (see ✅ block above): no non-availability state exists on `dbo.Employees`, and legacy's person-status vocabulary has exactly three computed values. Confirmed.** |
| `dbo.Absence` (35 cols) vs `dbo.LeaveReasons` (3 cols) | **Keep both — they are not the same list** | Absence *types* carry pay, accrual and export rules; a leaving reason is a label. WM must not collapse them, and must not name them so they read as synonyms (open question 6). |
| **Leaving as date + reason + comments** (`DischargeDate`, `LeaveReasonId`, `AdditionalLeaverComments`) | **Keep — adopt all three** | *(Added 2026-08-06; this survey missed it.)* "Why did they leave?" is an HR question every customer asks, and legacy answers it. WM's `Terminated` is a bare enum value that discards the answer. |
| `dbo.LeaveReason` as a **customer-maintained lookup** with `IsActive` | **Keep** | Reasons are per-customer vocabulary (resignation, redundancy, TUPE, dismissal…), not a WM enum. `IsActive` retires a reason without orphaning the historical records that used it — the right pattern, and one WM should copy rather than hard-delete. |
| `ActiveEmployeesView`'s `AND … OR …` | **Invert** | A real legacy defect. Employment is computed once, in a single expression, tested. |
| Fail closed on employment at every write boundary | **Keep** | Legacy is right and WM is not (defect D1). |
| Zero-padding-insensitive code uniqueness | **Drop** | Compensates for a decades-old badge format. Carrying it forward would make `42` and `0042` the same person forever. Case-insensitivity is worth keeping; padding is not. *(User decision — open question 1.)* |
| Uniqueness enforced only in application code | **Invert** | WM's check and WM's index disagree. A rule the database does not hold is not a rule. |
| Employee↔department integrity | **Improve** | Legacy has no relation to break. WM created one and left it unenforced, on a **scope dimension**. |
| `KnownAs` / `MiddleName` / `Title` / personal contact / address | **Keep** | A person's preferred name is not cosmetic when it appears on every punch event. |
| Bank details, salary, HR documents | **Keep — but out of scope here** | They need field-group write rights first (`UpdateEmployeePermissions.cs:5-28`, 24 flags). See *Out of scope*. |

---

## Edge cases

Lifted where possible from legacy's own code rather than invented.

**Employment boundaries**

1. `DischargeDate = today` → **still employed.** `IsActiveEmployment` is `dischargeDate < referenceDate → 0`
   (`76.V5.22.0.0.sql:33-36`). The last day is inclusive. WM must not use `<=`.
2. `DischargeDate = NULL` → employed. In T-SQL `NULL < date` is `NULL`, so the `CASE` falls to
   `ELSE 1`. WM must reach the same answer explicitly, not by accident.
3. `DischargeDate` **in the future** → employed today, a leaver from that date. This is the case WM
   cannot represent at all today, and the most common one an HR user enters.
4. `IsActive = 0` with a **future** `DischargeDate` → legacy's view says active (the precedence
   defect). **WM must say not-employed:** administrative suspension wins, and a test must assert it.
5. Employment asked **as at a date in the past**, for a replay or a payroll re-run — the question
   plan 002 needs and WM currently cannot answer.
6. Hire date in the future (a pre-boarded starter) → not yet employed. Legacy's `EnterDate` is
   `NOT NULL` but nothing stops it being future-dated.

**Punch boundary**

7. A `Terminated` employee punches → must be rejected, with the same message shape as an unknown
   employee so the endpoint does not disclose employment status to an unauthorised caller.
8. A punch **back-dated** to a day the employee *was* employed, submitted after they left → the
   decision is made against the **punch timestamp**, not `UtcNow`. (`RecordAsync` already accepts
   `request.Timestamp`, `PunchService.cs:36`.)
9. A punch dated **before** the hire date → rejected.
10. Self-service punch by a leaver whose login still works → rejected. `AuthService` checks
    `User.IsActive` only (`AuthService.cs:76, 117, 131`); nothing deactivates the user.

**Employee code**

11. `POST E1030` then `POST e1030` sequentially → 409 (works today).
12. `POST E1030` and `POST e1030` **concurrently** → today both insert, because the index is
    case-sensitive. Must be 409 for one of them, enforced by the constraint.
13. `FindByCodeAsync("e1030")` for employee `E1030` → must resolve. Today it returns null and the
    punch is rejected as *"Unknown employee code"* (`PunchService.cs:34`).
14. Codes differing only by leading zeros (`42` / `0042`) → **distinct** in WM (deliberate
    divergence from legacy, open question 1). A migration importing legacy data can never hit this,
    because legacy forbade it.

**Department**

15. `DepartmentId` naming a department that does not exist → rejected.
16. `DepartmentId` naming a department of a **different site** than `SiteId` → rejected. Today
    accepted, and it makes one person visible to two disjoint scopes.
17. `DepartmentId = null` → allowed, and never satisfies a department scope constraint
    (`EmployeeScopeExtensions.cs:20-21` already fails closed — assert it, do not change it).
18. Moving an employee's site while keeping a department belonging to the old site → rejected.

---

## Target design in WM

**Employment** (ARCHITECTURE §8.1 People). Replace the undated enum:

```
Employee
  EmployedFrom  : DateOnly            -- was HireDate, renamed for symmetry
  EmployedUntil : DateOnly?           -- legacy DischargeDate; last day INCLUSIVE; null = open
  IsSuspended   : bool                -- legacy IsActive, inverted so false is the good default
```

One predicate, one place:

```
IsEmployedOn(date) => !IsSuspended && date >= EmployedFrom && (EmployedUntil is null || date <= EmployedUntil)
```

`EmployeeStatus` is **derived for display only** (`Active | Suspended | Leaver | NotYetStarted`)
and computed from the dates. It is never stored, so the two can never disagree — the defect
`ActiveEmployeesView` demonstrates.

**Contract** (`Contracts/EmployeeDirectory.cs`, invariant 1): `EmployeeSummary` gains
`EmployedFrom`/`EmployedUntil`/`IsSuspended` and an `IsEmployedOn(DateOnly)` member, so
TimeAttendance decides without reaching into People's schema. `ListActiveAsync` becomes
`ListEmployedOnAsync(DateOnly)` with a `Today` convenience overload.

**Endpoints:** `PUT /api/employees/{id}` accepts the three fields instead of `status`;
`GET /api/employees` gains `?employedOn=` (default today) and projects the derived status.
Authorization unchanged (`employees.view` / `employees.manage`).

**Uniqueness:** a unique index on `lower(code)` (or `citext`) so the constraint and the stated rule
are the same thing, and `FindByCodeAsync` normalises identically.

**Integrity:** a real FK on `Employee.DepartmentId`, plus a validation that the department's
`SiteId` equals the employee's.

**Screens:** the employee modal gets *Employed from* / *Employed until* / *Suspended* in place of
the status dropdown; the list shows the derived badge.

---

## Out of scope for this plan

- **Bank details, salary, entitlements and deductions.** They need field-group write permissions
  first — legacy guards them behind `PersonnelTab.BankDetails`/`Salary` and 24 per-tab flags
  (`UpdateEmployeePermissions.cs:5-28`), while WM has one `employees.manage` and a full-replace
  `PUT`. Shipping them onto today's model would make `employees.manage` a grant of *"read and
  rewrite everyone's bank account"*. **Blocked on plan 004.**
- **The 8 HR document categories, e-signature, virus scanning.** Documents module, §10.
- **Employee contracts and `…Effective` resolution.** Rules; the unwritten tariffs plan. This plan
  only records that legacy has two mechanisms that disagree (`TLW-PEOPLE-MODEL.md` §5.6).
- **`Department.ParentId`.** Belongs with 001 P4/P5, which are already open on the scope dimensions.
  Raised there, not here.
- **Field-level change history** (`Employees_Update_Trigger` → `AuditTrailLogs`). Admin +
  `wm.audit`; needs the audit store that does not exist yet.
- **Manager relations, population groups, positions, qualifications, custom fields, photo.**
  Backlog; §13 now carries a row for each so they cannot vanish again.
- **`Employees.RoleId`.** Purpose unverified. Do not model it.

---

## Portions

### [x] P1 — Employment is a date, not an enum  ·  review passed 2026-08-17 (round 3), open as [#61](https://github.com/00008550/WM/pull/61)
**Touches:** `src/Modules/People/WM.Modules.People/Domain/Employee.cs`,
`Data/PeopleDbContext.cs`, a new migration + snapshot, `PeopleModule.cs` (list projection, upsert,
`EmployeeDirectory`), `Contracts/EmployeeDirectory.cs`, `Data/PeopleSeeder.cs`;
**new** `src/Modules/People/WM.Modules.People.Tests/` wired into `WM.sln` (People has no test
project — CLAUDE.md).
**Done when:** `Employee` carries `EmployedFrom`/`EmployedUntil`/`IsSuspended`; `EmployeeStatus` is
computed, not stored; the migration maps `Active→(not suspended, open)`, `OnLeave→(suspended,
open)` and `Terminated→(not suspended, EmployedUntil = UpdatedAt ?? CreatedAt date)` and says in a
comment that the terminated date is a **best-effort backfill**, because the information was never
captured; `IEmployeeDirectory` exposes employment so no consumer reads People's tables.

**Also, per the 2026-08-06 correction — the leaver record:** a new **`LeavingReason`** entity
(`Id, Name, IsActive`) with its own table, seeded empty (reasons are customer vocabulary, not
WM's); `Employee` gains a nullable **`LeavingReasonId`** FK and a nullable `LeaverComments`
(`nvarchar(500)`, matching legacy's width). Setting `EmployedUntil` may carry a reason; clearing it
must clear the reason and comments, so a re-hired employee does not keep a stale leaving reason.
Deactivating a `LeavingReason` must **not** orphan employees already referencing it — that is the
whole point of `IsActive` over a delete.

> **Name decided by the user, 2026-08-06 (was open question 6).** WM's entity is **`LeavingReason`**,
> *not* `LeaveReason`. In HR English "leave" means *absence* — annual leave, sick leave — so
> `LeaveReason` beside a future `AbsenceType` would re-create the exact conflation this plan exists
> to untangle. `LeavingReason` says "why employment ended" and cannot be misread.
>
> **Legacy's table stays `dbo.LeaveReasons` in every citation.** That is a fact about TLW, not a
> name WM chooses; do not rewrite it in the survey documents. The mapping is
> `dbo.LeaveReasons` → WM `LeavingReason`, and `Employees.LeaveReasonId` → WM
> `Employee.LeavingReasonId`. Name the divergence in the migration comment so the next reader
> knows it is deliberate rather than a typo.

**Tests:** edge cases 1–6 against `IsEmployedOn`, each as a named test; the `Down()` migration; a
round-trip asserting every pre-migration status maps to an employment window and back; a leaver
keeps its reason and comments through a round-trip; **clearing `EmployedUntil` clears both**;
deactivating a reason leaves existing references readable and stops it being offered for new ones.
**Risk:** medium — a contract change plus a lossy-by-necessity backfill.

**Notes added by the 2026-08-06 Absence dependency check** (no change to *Done when*):
- The "clearing `EmployedUntil` clears both" test now has legacy backing rather than first
  principles: `PersonnelService.SetEmployeesActive:151-173` un-leaves with
  `set IsActive = 1, DischargeDate = null, LeaveReasonId = null`.
- **The entity name is settled: `LeavingReason`** (user, 2026-08-06 — question 6 is closed, see the
  block above). Raising it before P1 was the point: renaming after the migration ships costs a
  second migration and a contract change.
- Do **not** fold `IsSuspended` into the employment window used for entitlement pro-rating. Legacy's
  accrual calculation reads dates only and never `IsActive`
  (`EmployeeAccrualCalculationsService.cs:728-741`).
- `FinalEmploymentDate` — a second, **higher-precedence** leaving date (`:941`) — stays out of P1 as
  planned. It is already among `TLW-PEOPLE-MODEL.md` §3.1's 33 unowned columns; §4.1a now records
  why Phase 3 will meet it.

> ### As built, 2026-08-14 — three corrections to this portion's own text, and what is untested
>
> **1. The *Touches* line was stale.** It said to create `WM.Modules.People.Tests` and wire it into
> `WM.sln` "because People has no test project". **003 P2b created it** (merged `1462c0f`, #57). This
> portion extended it — three new files beside P2b's three — and touched none of P2b's tests except a
> mechanical `HireDate` → `EmployedFrom` rename in two seed helpers. The write-scope checks in
> `PeopleModule.cs` and their ordering are untouched; the test that pins the `POST` scope check ahead
> of the code probe still passes unmodified.
>
> **2. The migration *was* executed, against real Postgres — but there is still no harness.** The
> *Tests* line asked for a `Down()` test and a status round-trip, and this repository has no Postgres
> test harness (that gap is what swallowed 001 P2's identical promise). Both halves were done, and
> they are not the same kind of evidence:
> - **In the suite** (`EmploymentMigrationTests`, **7** tests, xUnit, runs in CI): the round-trip
>   `status → window → status` over the migration's own `StatusMap`/`StatusFrom`; that the backfill
>   SQL says what the map says; that the backfill runs **before** `Status` is dropped; that `Down`
>   restores the status before dropping what it is computed from; that `Down` drops every column
>   `Up` adds, computed rather than listed; that both lookups' foreign keys are `RESTRICT`; and that
>   the four schema-only items are all present, nullable and defaultless. These read the migration's
>   operations. **They execute no SQL.**
> - **By hand against real Postgres**, re-run in full on **2026-08-17** and transcribed below. The
>   first run (2026-08-14) is **void**: three columns and a second lookup table were added to this
>   migration after it, so it no longer described the migration that exists.
>
> #### The transcript, 2026-08-17 — `Up` and `Down` against `postgres:17-alpine`
>
> Read what this is before reading it: **the EF migrator executing this migration's own SQL against a
> real server**, not a test. It is reproducible from the commands shown, and nothing re-runs it.
>
> Four properties were required of it and each is shown rather than asserted: **one transaction each
> direction**; the **backfill strictly before `DROP COLUMN "Status"`**; `Down` restoring the original
> schema **including the absence of a `DEFAULT` on `Status`**; and **unrecognised status values
> failing closed to suspended**.
>
> Two choices make the run harsher than the last one. The hostile timezone is set on the
> **database**, not the session, so EF's own connection inherits UTC+14 and the `AT TIME ZONE 'UTC'`
> guard is tested where it actually runs — a session-level `SET` never reached the migrator at all.
> And `log_statement = 'all'` puts the server's own account of what executed into the record, which
> is what makes the transaction and ordering claims checkable by a reader instead of taken on trust.
>
> **Environment.** Docker engine 27.1.1; `wm-dev-postgres-1` = `postgres:17-alpine`, PostgreSQL
> 17.10; `dotnet ef` 10.0.10. Throwaway database `wm_p1_verify`; the dev `wm` database was never
> touched and both throwaways were dropped at the end.
>
> ```
> $ psql -d postgres -c 'CREATE DATABASE wm_p1_verify;'
> CREATE DATABASE
> $ psql -d postgres -c "ALTER DATABASE wm_p1_verify SET TimeZone = 'Pacific/Kiritimati';"
> ALTER DATABASE
> $ psql -d postgres -c "ALTER DATABASE wm_p1_verify SET log_statement = 'all';"
> ALTER DATABASE
> $ psql -d wm_p1_verify -tAc 'show TimeZone;'
> Pacific/Kiritimati
> ```
>
> **1. The pre-migration schema**, applied by the migrator itself so it is the real thing:
>
> ```
> $ dotnet ef database update 20260720080022_Initial --context PeopleDbContext \
>     --connection 'Host=localhost;Port=5432;Database=wm_p1_verify;...'
> Done.
>
> $ psql -d wm_p1_verify -c '\d people."Employees"'
>     Column    |           Type           | Nullable | Default
> --------------+--------------------------+----------+---------
>  ...
>  HireDate     | date                     | not null |
>  Status       | integer                  | not null |          <-- NO DEFAULT. The baseline for Down.
>  CreatedAt    | timestamp with time zone | not null |
>  UpdatedAt    | timestamp with time zone |          |
> ```
>
> **2. Six rows seeded at that schema — every value the enum could hold, and two it could not.**
> Five rows was not enough: `-1` was added because "unrecognised" has two sides, and a row was later
> given a state the enum cannot express at all.
>
> ```
>     Code     | Status |  HireDate  |       CreatedAt        |       UpdatedAt
> -------------+--------+------------+------------------------+------------------------
>  E-ACTIVE    |      0 | 2024-01-15 | 2024-01-15 23:00:00+14 |
>  E-ONLEAVE   |      1 | 2023-05-01 | 2023-05-01 23:00:00+14 | 2026-02-03 00:00:00+14
>  E-TERM-UPD  |      2 | 2020-03-02 | 2020-03-02 23:00:00+14 | 2025-03-11 12:30:00+14
>  E-TERM-NULL |      2 | 2019-07-01 | 2022-12-01 13:45:00+14 |
>  E-BOGUS-7   |      7 | 2021-09-09 | 2021-09-09 23:00:00+14 | 2024-04-05 02:00:00+14
>  E-BOGUS-NEG |     -1 | 2022-02-02 | 2022-02-02 23:00:00+14 |
> ```
>
> The two `Terminated` rows are the timezone trap, and rendering it first is what makes the result
> below mean anything — the stored instants are 2025-03-10 **22:30Z** and 2022-11-30 **23:45Z**, so
> under UTC+14 the naive cast lands on the *following* day:
>
> ```
>     Code     |          src           |  utc_date  | naive_local_date
> -------------+------------------------+------------+------------------
>  E-TERM-NULL | 2022-12-01 13:45:00+14 | 2022-11-30 | 2022-12-01
>  E-TERM-UPD  | 2025-03-11 12:30:00+14 | 2025-03-10 | 2025-03-11
> ```
>
> **3. `Up`.** `dotnet ef database update` → `Done.`, and the backfill landed on `utc_date` both
> times, including through `COALESCE` to `CreatedAt`:
>
> ```
>     Code     | EmployedFrom | EmployedUntil | IsSuspended | FinalEmploymentDate | ResignationDate | LeaveNoticePeriodId
> -------------+--------------+---------------+-------------+---------------------+-----------------+---------------------
>  E-ACTIVE    | 2024-01-15   |               | f           |                     |                 |
>  E-ONLEAVE   | 2023-05-01   |               | t           |                     |                 |
>  E-TERM-UPD  | 2020-03-02   | 2025-03-10    | f           |                     |                 |
>  E-TERM-NULL | 2019-07-01   | 2022-11-30    | f           |                     |                 |
>  E-BOGUS-7   | 2021-09-09   |               | t           |                     |                 |
>  E-BOGUS-NEG | 2022-02-02   |               | t           |                     |                 |
> ```
>
> `0 → (not suspended, open)`. `1 → (SUSPENDED, open)`. `2 → (not suspended, the UTC date)`.
> **`7` and `-1` both → suspended, window open: failed closed.** `HireDate` became `EmployedFrom`
> with every value intact, and the three deferred columns are NULL for every row — nothing backfilled
> them, which is the point of shipping them inert. Both lookups exist and both are **empty**
> (`leaving_reasons 0 | leave_notice_periods 0`), and `Status` is gone
> (`status_columns_remaining 0`).
>
> **4. One transaction, and the ordering — the server's own log.** The first `BEGIN`/`COMMIT` pair is
> EF's history-table bootstrap, not the migration; the second is the entire migration plus its history
> row:
>
> ```
> statement: BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED
> execute: CREATE TABLE IF NOT EXISTS people.__ef_migrations ( ...
> statement: COMMIT
> statement: BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED     <-- the migration starts
> execute: LOCK TABLE people.__ef_migrations IN ACCESS EXCLUSIVE MODE
> execute: ALTER TABLE people."Employees" RENAME COLUMN "HireDate" TO "EmployedFrom"
> execute: ALTER TABLE people."Employees" ADD "EmployedUntil" date
> execute: ALTER TABLE people."Employees" ADD "IsSuspended" boolean NOT NULL DEFAULT FALSE
> execute: ALTER TABLE people."Employees" ADD "LeaverComments" character varying(500)
> execute: ALTER TABLE people."Employees" ADD "LeavingReasonId" uuid
> execute: ALTER TABLE people."Employees" ADD "FinalEmploymentDate" date
> execute: ALTER TABLE people."Employees" ADD "ResignationDate" date
> execute: ALTER TABLE people."Employees" ADD "LeaveNoticePeriodId" uuid
> execute: CREATE TABLE people."LeavingReasons" ( ...
> execute: CREATE TABLE people."LeaveNoticePeriods" ( ...
> execute: CREATE INDEX "IX_Employees_LeavingReasonId" ...
> execute: CREATE INDEX "IX_Employees_LeaveNoticePeriodId" ...
> execute: CREATE UNIQUE INDEX "IX_LeavingReasons_Name" ...
> execute: CREATE UNIQUE INDEX "IX_LeaveNoticePeriods_Name" ...
> execute: ALTER TABLE ... ADD CONSTRAINT "FK_Employees_LeavingReasons_LeavingReasonId" ... ON DELETE RESTRICT
> execute: ALTER TABLE ... ADD CONSTRAINT "FK_Employees_LeaveNoticePeriods_LeaveNoticePeriodId" ... ON DELETE RESTRICT
> execute: UPDATE people."Employees"                              <-- the backfill
> execute: ALTER TABLE people."Employees" DROP COLUMN "Status"     <-- strictly AFTER it
> execute: INSERT INTO people.__ef_migrations ("MigrationId", "ProductVersion")
> statement: COMMIT                                                <-- one transaction, whole migration
> ```
>
> That `UPDATE` precedes that `DROP COLUMN` **on a real server** is the property the in-suite ordering
> test infers from the operation list. This is the same claim, executed. (The DDL appears as `execute`
> rather than `statement` because Npgsql uses the extended query protocol — a reason to read the log
> for `execute` too, not evidence of a second transaction.)
>
> **5. State planted before `Down`, so its documented losses are shown and not merely claimed.** Both
> lookups were filled — by SQL, since P1 ships no maintenance surface — and `RESTRICT` refused to let
> a reason in use be deleted, which is the whole argument for `IsActive` over a delete:
>
> ```
> $ DELETE FROM people."LeavingReasons" WHERE "Id" = 'aaaa...0001';
> ERROR:  update or delete on table "LeavingReasons" violates foreign key constraint
>         "FK_Employees_LeavingReasons_LeavingReasonId" on table "Employees"
> DETAIL:  Key (Id)=(aaaaaaaa-0000-0000-0000-000000000001) is still referenced from table "Employees".
> ```
>
> ```
>     Code     | EmployedFrom | EmployedUntil | IsSuspended |   reason   |          LeaverComments
> -------------+--------------+---------------+-------------+------------+----------------------------------
>  E-ACTIVE    | 2027-01-04   |               | f           |            |                     <-- future start
>  E-ONLEAVE   | 2023-05-01   | 2026-09-30    | t           |            |                     <-- suspended AND leaving
>  E-TERM-UPD  | 2020-03-02   | 2025-03-10    | f           | Redundancy | Role withdrawn in the March...
>  ...
> ```
>
> **6. `Down`.** The reverse map, executed:
>
> ```
>     Code     | Status |  HireDate
> -------------+--------+------------
>  E-ACTIVE    |      0 | 2027-01-04     <-- a pre-boarded starter becomes Active; 0 cannot say "not yet"
>  E-ONLEAVE   |      2 | 2023-05-01     <-- suspended AND leaving -> 2: leaving is the stronger fact
>  E-TERM-UPD  |      2 | 2020-03-02
>  E-TERM-NULL |      2 | 2019-07-01
>  E-BOGUS-7   |      1 | 2021-09-09     <-- 7 came back as 1, not 7: fail-closed is one-way, by design
>  E-BOGUS-NEG |      1 | 2022-02-02
> ```
>
> Every one of those five outcomes is what `StatusFrom`'s remarks say will happen. `E-BOGUS-7` is
> worth stating plainly: **an unrecognised value does not survive a round trip**, because `Up`
> deliberately discards it in favour of "suspended". That is the fail-closed choice being paid for,
> not a defect, and it is why the in-suite round-trip test pins the *three real* statuses only.
>
> **7. `Down` restored the original schema — measured, not eyeballed.** A second database was created
> and taken to `Initial` and no further, then both `people` schemas dumped and diffed:
>
> ```
> $ pg_dump --schema-only -n people -d wm_p1_reference   > reference.sql   # fresh at Initial
> $ pg_dump --schema-only -n people -d wm_p1_verify      > after_down.sql  # Up, then Down
> $ diff -u reference.sql after_down.sql
> @@
>       "HireDate" date NOT NULL,
> -     "Status" integer NOT NULL,
>       "CreatedAt" timestamp with time zone NOT NULL,
>       "UpdatedAt" timestamp with time zone,
>       "CreatedBy" uuid,
> -     "UpdatedBy" uuid
> +     "UpdatedBy" uuid,
> +     "Status" integer NOT NULL
>   );
> ```
>
> **That hunk is the entire difference in the schema** — every table, column, type, nullability,
> index and constraint is otherwise identical, and the only thing that moved is `Status`'s ordinal,
> exactly the cosmetic residue `Down`'s remarks predict. Note what the restored line does **not**
> say: `DEFAULT 0`. `AddColumn` had to invent one to populate existing rows, and `Down`'s
> `DROP DEFAULT` removes it. The counterfactual, so the claim is falsifiable rather than decorative:
>
> ```
> $ ALTER TABLE people."Employees" ALTER COLUMN "Status" SET DEFAULT 0;   -- i.e. had Down omitted it
> $ pg_dump ... | grep '"Status"'
>     "Status" integer DEFAULT 0 NOT NULL          <-- what a reader would see if it were missing
> $ ALTER TABLE people."Employees" ALTER COLUMN "Status" DROP DEFAULT;
> $ pg_dump ... | grep '"Status"'
>     "Status" integer NOT NULL                    <-- what the migration actually leaves
> ```
>
> Both lookup tables are gone, all seven added columns are gone, `EmployedFrom` is `HireDate` again,
> and `__ef_migrations` holds `Initial` alone.
>
> **8. `Down` is also one transaction, and also reads before it drops:**
>
> ```
> statement: BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED
> execute: LOCK TABLE people.__ef_migrations IN ACCESS EXCLUSIVE MODE
> execute: ALTER TABLE people."Employees" ADD "Status" integer NOT NULL DEFAULT 0
> execute: UPDATE people."Employees"                                   <-- restore, while the window still exists
> execute: ALTER TABLE people."Employees" ALTER COLUMN "Status" DROP DEFAULT
> execute: ALTER TABLE ... DROP CONSTRAINT "FK_Employees_LeavingReasons_LeavingReasonId"
> execute: ALTER TABLE ... DROP CONSTRAINT "FK_Employees_LeaveNoticePeriods_LeaveNoticePeriodId"
> execute: DROP TABLE people."LeavingReasons"
> execute: DROP TABLE people."LeaveNoticePeriods"
> execute: DROP INDEX people."IX_Employees_LeavingReasonId"
> execute: DROP INDEX people."IX_Employees_LeaveNoticePeriodId"
> execute: ALTER TABLE people."Employees" DROP COLUMN "EmployedUntil"   <-- only now
> execute: ALTER TABLE people."Employees" DROP COLUMN "IsSuspended"
> execute: ALTER TABLE people."Employees" DROP COLUMN "LeaverComments"
> execute: ALTER TABLE people."Employees" DROP COLUMN "LeavingReasonId"
> execute: ALTER TABLE people."Employees" DROP COLUMN "FinalEmploymentDate"
> execute: ALTER TABLE people."Employees" DROP COLUMN "ResignationDate"
> execute: ALTER TABLE people."Employees" DROP COLUMN "LeaveNoticePeriodId"
> execute: ALTER TABLE people."Employees" RENAME COLUMN "EmployedFrom" TO "HireDate"
> execute: DELETE FROM people.__ef_migrations
> statement: COMMIT
> ```
>
> **⚠️ None of section 3–8 is a regression test, and it must not be counted as one.** It is one
> afternoon's evidence about one afternoon. Nothing re-runs it, no CI job would fail if the migration
> were changed to break any property above, and the next migration inherits the same gap — so the
> honest coverage claim for this repository is still the one at the top of
> `EmploymentMigrationTests`: the operations are read, and **no SQL is executed by the suite.**
> Reading a migration's operations is not executing it, and a transcript is not a harness.
>
> **Standing up a real Postgres test harness deserves a portion of its own.** Docker is available on
> the dev machine — demonstrably, since the above ran on it — and on GitHub's runners, so
> Testcontainers is viable. It was not done here because a flaky container in CI blocks every later
> PR, and that trade is not this portion's to make.
>
> **3. Two things this portion did that its *Touches* line does not mention**, both forced by the
> contract change rather than chosen:
> - **The portal.** `PUT` stopped accepting `status`, so the employee modal's status dropdown had to
>   go; it is now *Employed from* / *Employed until* / *Suspended*, exactly as the plan's *Target
>   design → Screens* describes, and the list shows the derived badge. Leaving the SPA alone would
>   have shipped a broken employee editor.
> - **`EndpointAuthorizationInventoryTests`.** `GET /api/leaving-reasons` is a new transport, and that
>   test fails by name until a permission is chosen for it. It reads with `employees.view`.
>
> **Deliberately not built, and why:** there is **no maintenance surface for `LeavingReason`** — no
> create, rename or retire endpoint, and no screen. P1's *Done when* asks for the entity, the table
> and the seeded-empty lookup, and the read endpoint is what makes "offered" mean something. Creating
> and retiring reasons is a customer-administration screen with its own permission question
> (`employees.manage`, or an administration permission of its own), and it should be planned, not
> improvised here. **Until it exists the table can only be filled by SQL**, so the leaver *reason* is
> reachable through the API but not through any UI; `LeaverComments` likewise. Worth a portion — and
> the portion that gates the leaver record on a reason is **the same one**, never an earlier one
> (decision 5's ordering note: gating an unfillable empty lookup makes the leaver record
> unreachable).
> ### ✅ Provenance audit of P1 as built (2026-08-14) — **both findings resolved before merge**
>
> P1 was built at `e482f83` and commits a migration that drops `Status`. This audit re-measured its
> five load-bearing claims against `E:\Tlw` only. **Three held as written. Two did not** — both were
> fixed on this branch before it merged, and each is annotated below with what happened. The measured
> ground truth in the "Holds" section stands as the citation of record. Evidence in
> `TLW-PEOPLE-MODEL.md` §4.1b and §4.1c.
>
> **Holds — measured, correct, no change needed:**
> - `dbo.IsActiveEmployment(@dischargeDate DATE, @referenceDate DATE) RETURNS BIT`,
>   `76.V5.22.0.0.sql:25-38`, defined exactly once and never redefined. Last day **inclusive**
>   (`@dischargeDate < @referenceDate THEN 0`), NULL ⇒ employed. 37 call sites across 21 C# files
>   plus the SQL estate. "20+" was conservative.
> - Employment-as-a-date, and the derived three-value label. `PersonnelModels.cs:1741-1744` computes
>   `Status` from `IsActive` × `IsLeaver` and stores nothing. Precedence there is
>   **suspended wins over leaver**, which is what `Employment.StatusOn` does.
> - `OnLeave` has no legacy counterpart. Re-tested independently of the 2026-08-06 narrow check: the
>   status filter takes exactly three flags (`FilterEmployeesByStatus:1848-1852`), the grid badge
>   renders exactly three (`Views/Personnel/Index.cshtml:343`), and absence is per-day and
>   morning/afternoon (`Documentation/Absences Configuration.md`). **The deletion was correct.**
>
> **Did not hold — 1. `IsSuspended` was folded into `IsEmployedOn`. ✅ FIXED at `d132c65`** — the
> parameter was dropped from `IsEmployedOn` / `EmployedOn`, suspension stays in `StatusOn`, and every
> call site now composes the two halves. Review round 3 mutated the fold back in and confirmed two
> tests catch it. The finding as originally written follows.
> `Employment.IsEmployedOn` returns `!isSuspended && employedFrom <= on && …`. Legacy never fuses
> them: `ActiveNotFired()` *is* `Active().NotFired()` (`EmployeeExtensions.cs:22-27`), `NotFired()`
> ships alone in five production paths, and `dbo.IsActiveEmployment` cannot see `IsActive` at all.
> The note at `:348-350` of this plan said *"do not fold `IsSuspended` into the employment window"*
> and the built `Employee.cs` doc-comment repeats the reason — then folds it. Consequence:
> `IEmployeeDirectory.ListEmployedOnAsync(date)` silently drops suspended people from every
> "who was employed on D" question, including the ones P2 and plan 002 will ask.
> **Fix is small and pre-merge:** drop the `isSuspended` parameter from `IsEmployedOn` /
> `EmployedOn`, keep it in `StatusOn`, and let callers compose (`IsEmployedOn(d) && !IsSuspended`)
> exactly as legacy does. See §4.1c.
>
> **Did not hold — 2. The leaver record is six fields and two lookups, not three and one.**
> **◐ SCHEMA CLOSED at `d132c65`, RULES DEFERRED by user decision 2026-08-15** — all six fields and
> both lookups now exist (`ResignationDate`, `FinalEmploymentDate`, `LeaveNoticePeriodId` and the
> `LeaveNoticePeriods` table are stored but not yet read). The three legacy *rules* below — the
> mandatory triple, the reason gating, and the date ordering — are adopted in principle and deferred
> to a later portion, which **must also build the lookup maintenance surface**: gating on an
> unfillable empty lookup would make the leaver record unreachable. See decision 5. The finding as
> originally written follows.
> `_Leaver.cshtml` renders `LeaveReasonId`, **`LeaveNoticePeriodId`**, `DischargeDate`,
> **`ResignationDate`**, **`FinalEmploymentDate`**, `AdditionalLeaverComments`. Three of the six and
> the whole `dbo.LeaveNoticePeriods` lookup (own service, own controller, own three screens) were
> not in the 2026-08-06 correction and so not in P1. Worse, legacy enforces rules P1 does not know
> about:
> - leaving is an **all-or-nothing triple** — reason + discharge + final date, server-side
>   (`PersonnelModels.cs:1354-1368`). P1 makes the reason optional and omits the final date.
> - the **reason gates the record** — every leaver date is disabled and cleared while it is empty
>   (`addEditEmployee.js:1104-1143`). P1 seeds `LeavingReason` **empty**, which under legacy's rule
>   would make it impossible to record any leaver.
> - `EnterDate ≤ DischargeDate ≤ FinalEmploymentDate`, enforced in the pickers; P1 has no ordering
>   constraint between `EmployedFrom` and `EmployedUntil` at all.
>
> **None of this invalidates the migration's shape.** `EmployedFrom` / `EmployedUntil` /
> `IsSuspended` / `LeavingReasonId` / `LeaverComments` are all right, all measured, and the
> `Active→open` / `OnLeave→suspended` / `Terminated→dated` backfill is sound. What is missing is
> additive. The one thing that should not ship as-is is the `isSuspended` fold, because it changes
> the meaning of a contract other portions are about to build on.
>
> **Newly measured, no owner in WM, and not among §3.1's 33 as *behaviour*:** the
> `Employees_Update_Trigger` accrual invalidation (`87.V5.33.0.0.sql:768-806`) — editing
> `EnterDate`, `DischargeDate`, `ContinuousServiceDate` or `FinalEmploymentDate` **deletes the
> employee's stored accrual history and requeues recalculation from their start date**. An
> `EmployedFrom`/`EmployedUntil` edit in WM is therefore a recalculation event, and P1 raises no
> event at all. Candidate portion. See §4.1b point 6. **Now also recorded as its own `ARCHITECTURE.md`
> §13 row** ("Employment-date edit ⇒ accrual invalidation"), so it survives outside this plan.

### [ ] P2 — The punch boundary fails closed
**Touches:** `src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Services/PunchService.cs`,
`TimeAttendanceModule.cs` if the error shape changes; `WM.Modules.People.Tests` or a new
`WM.Modules.TimeAttendance.Tests`; **`ARCHITECTURE.md:385`** — the "Swipe capture" §13 row. Its
*substance* is still true after P1 (the boundary does accept terminated employees — `PunchService.cs:106-108`
says so in a comment, and P1 left it deliberately to P2), but its evidence has rotted: it cites
`PeopleModule.cs:197-207` and `ListActiveAsync`, which P1 renamed to `ListEmployedOnAsync`, and it
attributes the fix to **007 P1**. P2 is what closes the row, so P2 is what rewrites it — listed here
rather than corrected in P1, because a three-way conflict on that table while
[#59](https://github.com/00008550/WM/pull/59) and [#60](https://github.com/00008550/WM/pull/60) are
open costs more than the stale citation does (reviewer's ruling, 2026-08-17).
**Done when:** `RecordAsync` and `RecordForEmployeeAsync` reject a punch whose **timestamp** falls
outside the employee's employment window, with the same problem shape as an unknown code;
`GetRecentAsync`'s visibility set and the accept decision are derived from the same predicate, so
WM can no longer accept a punch it will not display.
**Tests:** edge cases 7–10; explicitly, a back-dated punch into an employed period **succeeds** and
the same punch dated after the leave date **fails**; a test asserting the rejection message does not
differ between "unknown employee" and "not employed".
**Risk:** low.
**Note:** closes defect **D1**. If the user wants it closed before P1 lands, a status-only check is
a two-line interim — but it cannot answer edge case 8, so it is a stopgap, not the fix.

> ### ⚠️ D4 — punch direction is never validated. Added 2026-08-06, found by the user in the UI.
> `RecordAsync` (`PunchService.cs:30-38`) validates exactly two things: the employee resolves, and
> the timestamp is not more than five minutes in the future. **It never compares `request.Direction`
> against the employee's last punch**, so `IN, IN, IN` is accepted.
>
> Observed on the running dev stack, employee `E1000` (`0` = IN, `1` = OUT):
> ```
> 2026-08-04 12:43:27  IN
> 2026-08-04 12:39:49  IN
> 2026-08-04 12:08:43  IN     ← three consecutive INs, no OUT
> 2026-07-21 15:07:16  OUT
> 2026-07-21 08:12:00  IN     ← seeded history is correctly paired
> ```
> The seeder produces valid pairs; the unpaired run came from punches made through the UI. The
> consequence is visible on `/me`: that employee reads *"Clocked in since 17:43"* two days later and
> **0h this week**, because there is no closing punch to compute against. The page is not broken —
> it is faithfully reporting corrupt data. **A T&A product that accepts consecutive INs cannot
> compute worked hours**, which is the whole point of the product.
>
> **This is not recorded anywhere else** — grepped across `docs/`; no plan, audit or §13 row mentions
> direction validation. Three surveys missed it because it is invisible in the schema and only shows
> up when someone punches twice.
>
> **Do not assume "reject the second IN" is the fix — decide it first.** Legacy pairs swipes into
> `BadgeTime1..12` slots on the clocking (`TLW-CLOCKING-MODEL.md`), so an unpaired IN is a
> *recognised, correctable state* there, not a refused punch. A real T&A product generally accepts
> the swipe and flags the day for correction, because refusing it loses the fact that someone was
> at the door. The options are:
> 1. **Reject** the out-of-sequence punch — simplest, and loses data.
> 2. **Accept and flag** the day as needing correction — matches legacy and matches what supervisors
>    actually do.
> 3. **Accept and auto-close** the previous IN at a configured time — convenient, and silently
>    invents a time nobody recorded.
>
> Option 2 is the recommendation; it needs the daily-aggregate concept plan **002** owns, so P2 may
> only be able to land the *detection* and leave the correction workflow to 002. Say which in the
> PR rather than picking one silently.
>
> ---
>
> #### Measured 2026-08-06 — the user pointed at legacy's **exceptions**, and option 2 is confirmed
>
> This is not an inference any more. TLW has a first-class **exception** concept, configured **per
> daily model**, listed at `E:\Tlw\Source\Core\Enumeration\Enums.cs:1414-1434`:
>
> | Setting | |
> |---|---|
> | `EarlyEntryExceptionEnabled` (16), `LateEntryExceptionEnabled` (17) | arrival |
> | `EarlyExitExceptionEnabled` (18), `LateExitExceptionEnabled` (19) | departure |
> | `EarlyBreakStart/End`, `LateBreakStart/End` (20–23) | breaks |
> | **`OddNumberOfSwipesMinusTheoretic` (24)** | **exactly this case — an unpaired swipe run** |
> | `OneSwipeEnough` (14), `AllowNoSwipes` (25), `SwipesExpected` (30) | how many swipes a day requires |
> | **`ShouldGenerateBlockingExceptionsOnSwipe` (29)** | exceptions raised **at swipe time**, and *blocking* |
> | `ShouldHideExceptions` (9) | on the clocking itself |
>
> Two report views exist — `UnifiedExceptionsReportViewRecord` and
> `Unified**Authorized**ExceptionsReportViewRecord` (`HorioDB.designer.cs:5921`, `:5929`) — so an
> exception carries an **authorised / unauthorised** state that a manager resolves. **Legacy never
> refuses the swipe.** Option 1 is therefore wrong, and option 2 is what the product this replaces
> actually does.
>
> #### The correction to this finding: these are *two* mechanisms, not one
>
> The same symptom has two causes, and conflating them would build the wrong thing:
>
> | Observed | Interval | What it is | Where it belongs |
> |---|---|---|---|
> | `E1037` 21:32:14, 21:32:25, 21:32:28 IN | **seconds** | a double-click / double-swipe — **noise** | **deduplicate**, 007 P2 |
> | `E1000` 12:08, 12:39, 12:43 IN | **minutes** | a genuine unpaired run — **an exception** | raise on the day, plan **002** |
>
> A rapid repeat must be *ignored*, not recorded and not raised — raising an exception for a
> double-click trains supervisors to dismiss exceptions, which destroys the value of the whole
> mechanism. An unpaired run minutes or hours apart is real and must reach a human.
>
> **The dedupe window is a WM decision, not a legacy port.** No minimum-interval setting was found:
> the only anti-passback hits in `E:\Tlw` are Salto and Suprema **device** configuration
> (`Communication.SaltoSpace\ExportModels\Door.cs:88-99`), which is dropped hardware (§14 decision
> 3). Legacy relied on the terminal to swallow double swipes; WM's terminal is a web page, so WM
> must do it in `RecordAsync`.
>
> **Split for the builder:** P2 lands the **dedupe guard** — a same-direction punch within a
> configured window is accepted idempotently and returns the existing punch rather than creating a
> second one. It does **not** land the exception model; that is day-level state and plan 002 owns
> it. P2's job is to stop generating the noise, so 002 inherits clean data.
>
> **Related, not a defect:** `E1000` and `E1037` are both named *"Luca Dubois"*. `PeopleSeeder` draws
> from small name pools, so collisions across 40 employees are certain, and they make the demo
> genuinely confusing to read — a punch in the live feed looks like it should appear on your own
> timesheet. Noted against **006 P1**, which rewrites the seeders.

### [ ] P3 — One employee-code rule, held by the database
**Touches:** `Data/PeopleDbContext.cs`, a new migration + snapshot, `PeopleModule.cs:83-85, 107-111,
128, 199` (including the comment at `:109`, which is currently false), `WM.Modules.People.Tests`.
**Done when:** uniqueness is case-insensitive **at the constraint** (unique index on `lower(code)`
or a `citext` column); the pre-check and the 23505 handler agree with it; `FindByCodeAsync`
normalises the same way; the migration fails loudly on pre-existing case-colliding rows rather than
silently dropping one.
**Tests:** edge cases 11–14. 12 is the one that matters and needs two concurrent inserts, not a
sequential pair — a test that only does the sequential case would pass today.
**Risk:** low — but the migration can fail on real data by design, which is correct and must be
documented in the portion's PR.

### [ ] P4 — `DepartmentId` is a real reference
**Touches:** `Data/PeopleDbContext.cs`, a new migration + snapshot, `PeopleModule.cs:77-113,
115-154`, **`PeopleModule.cs:314`** — `MissingReference()`, which answers *"The selected leaving
reason does not exist."* to **any** `23503`. Correct for every input reachable today (two foreign
keys, and nothing can set `LeaveNoticePeriodId`), and wrong half the time the moment P4 adds a third.
The remedy is named in a code comment at `:310-313` — read `PostgresException.ConstraintName` and say
which reference is missing — but a comment nobody greps is not a plan, so the line is listed here.
Also `WM.Modules.People.Tests`.
**Done when:** `Employee.DepartmentId` has a foreign key and an index; create and update reject a
department that does not exist or belongs to a different site than `SiteId`; the migration nulls
orphaned references and **reports how many** rather than failing.
**Tests:** edge cases 15–18, plus a regression test that a null department still fails closed
against a department scope (`EmployeeScopeExtensions.cs:20-21`).
**Risk:** low.
**⚠️ Ordering:** must land **with or before 003 P3**. 003 P3 fixes `PHASE-AUDIT.md` B5, which
hard-codes `departmentId: null` in the SPA; fixing it makes this field live and turns a latent
integrity hole into a reachable one.

### [ ] P5 — The person gets a name, an identity and an address
**Touches:** `Domain/Employee.cs`, `Data/PeopleDbContext.cs`, a new migration + snapshot,
`PeopleModule.cs` (upsert + projections), `Contracts/EmployeeDirectory.cs` (display name only),
`frontend/portal/src/app/pages/employees/employees.component.ts`, `WM.Modules.People.Tests`.
**Done when:** the record carries the 15 measured, unowned, non-sensitive People columns —
`MiddleName`, `KnownAs`, `Title`, `DateOfBirth`, `Gender`, `NationalityId`, `SocialSecurityNumber`,
`Address`, `PersonalEmail`, `PersonalPhone`, `Mobile`, `EmploymentTypeId`, `ContinuousServiceDate`,
`ExternalId`, and the photo reference — with `Nationalities`/`EmploymentTypes` as lookup tables
(`dbo.Nationalities:182546`, `dbo.EmploymentTypes:182684`); `FullName` honours `KnownAs` so the
punch event and every screen show the name the person uses.
**Tests:** display-name precedence (`KnownAs` beats `FirstName`, blank falls back); `ExternalId`
uniqueness where non-null; `SocialSecurityNumber` is searchable, as it is in legacy
(`PersonnelFilterType.SocialSecurityNumber`, `PersonnelEnums.cs:18`) **and is excluded from the
list projection** — it is the one field here a manager should not read in bulk.
**Risk:** medium — the biggest surface, and the first time WM stores personal data with a privacy
dimension. ARCHITECTURE §12 (OWASP/GDPR) applies; if it needs a design change, stop and propose.

---

## Decisions (user; 1–4 on 2026-08-06, 5–6 on 2026-08-15)

1. **Employee code uniqueness: case-insensitive only, not padding.** Padding-equivalence is a
   badge-format workaround from fixed-width readers, and WM has no physical devices by decision
   (§14 decision 3) — nothing generates short codes needing padding, and carrying it forward would
   permanently forbid `42` and `0042` as distinct codes. Safe either way for imported data, because
   legacy already forbade the collision.
2. **`OnLeave` is dropped, and the *leaver* record is adopted instead.** The user corrected this
   plan's original claim that leaving had no model in legacy — see the ⚠️ correction at the top.
   Leaving is `DischargeDate` + `LeaveReasonId` + `AdditionalLeaverComments`, backed by the
   customer-maintained `dbo.LeaveReason` lookup, and P1 now adopts all three. `OnLeave` still goes,
   but because a *temporary* absence belongs to the Absence module — not because leaving was
   unmodelled.
3. **P4 stays separate from 003 P3** (orchestrator's call, 2026-08-06). Same file and same area,
   but 003 is approved and this plan was not at the time; folding an unapproved portion into an
   approved plan changes the shape of something the user already signed off. The ordering
   constraint is cheap; rewriting an approved plan is not.

4. **P1's leaving-reason entity is `LeavingReason`.** *(Question 6, raised by the 2026-08-06 Absence
   dependency check and answered the same day.)* Legacy has **two** reason vocabularies and they are
   not the same thing —
   - `dbo.LeaveReasons` (3 cols, `HorioDB.designer.cs:53137`) — *why employment ended*. A label.
   - `dbo.Absence` (**35 cols**, `:8743`) — *why someone is not here today*. Carries `Unit`,
     `Category` (`Holiday | Sick | MaternityPaternity | OtherEvent`), `AllowOnDayOff`,
     `AllowOnHoliday`, `CounterId`, `ExportCode`, `BlockAbsenceRequestOnNegativeBalance`,
     `IsApprovalRequiredForBookingAbsence`, `IncludeToBradfordCalculation` — pay, accrual and
     export rules, not a label.

   They stay two tables, and WM names them so they cannot be read as synonyms. In HR English
   "leave" means *absence*, so `LeaveReason` beside a future `AbsenceType` would invite exactly the
   conflation this plan spent a correction untangling. **Legacy's `dbo.LeaveReasons` keeps its name
   in every citation** — that is a fact about TLW, not a name WM chooses.

5. **Schema now, behaviour later — the four remaining leaver items ride P1's migration as columns
   only.** *(User, 2026-08-15. Recorded here 2026-08-17: the code has cited this decision since it
   was made — the migration's summary and `EmploymentMigrationTests` both name the date — but the
   plan never carried it, so the one place a reader looks for decisions did not have it.)*
   `FinalEmploymentDate`, `ResignationDate`, `LeaveNoticePeriodId` and the `LeaveNoticePeriods`
   lookup are added by P1's migration, nullable and defaultless. **Nothing writes them, nothing
   validates them, and no screen shows them.** They ride this migration because the alternative is a
   second migration against a table P1 has already rewritten.

   Three legacy rules over them are **deliberately not implemented**: the mandatory
   reason + discharge + final **triple**; the leaving **reason gating** the record; and the
   `EnterDate ≤ DischargeDate ≤ FinalEmploymentDate` **date ordering**. Each is a behaviour with its
   own edge cases, and guessing one now would cost the portion that builds it an undo first. A
   default on any of these columns would itself be a decision about what the value means, made by the
   portion that is explicitly declining to make it. **This needs a portion**, alongside the
   `LeavingReason`/`LeaveNoticePeriods` maintenance surface the As-built note also defers — the two
   are the same gap seen from the write side and the admin side.

   **⛔ Ordering, and it is not negotiable: the reason-gating rule must never land before the
   `LeavingReason` maintenance surface.** Both lookups ship **empty** and can be filled only by SQL
   today. A rule that refuses a leaving date without a reason, applied to an empty and unfillable
   vocabulary, makes the leaver record **unreachable** — every attempt to record that someone left is
   a 400 with no way for the user to clear it. The gate and the surface are **one portion**, in that
   order (reviewer, 2026-08-17).

6. **`LeaverComments` is excluded from `GET /api/me/employee`.** *(User, 2026-08-15.)* The leaver
   comments are HR's note **about** this person — "poor timekeeping", "would not re-hire" — so
   returning the whole employee row put somebody's assessment of them into the JSON their own browser
   receives. Everything else on the record stays: the employment window and the leaving reason are
   facts about their own employment, not an opinion of them, and hiding those would make the
   self-service page lie about their own status.

   Recorded with its limitation, because it is one a later plan must close: this is a **field-level
   exclusion hard-coded at a single endpoint, and that is not a permission model.** The list and
   detail endpoints still return `LeaverComments` to any holder of `employees.view`. Plan 004's
   field-group rights are where the general rule belongs (§*Out of scope* already blocks bank details
   and salary on the same plan for the same reason).

## Open questions for the user

4. **`ExternalId` in P5, or with the Connectors phase?** It is one nullable unique column and every
   two-way HR sync needs it, but nothing today reads it. Including it now costs almost nothing;
   deferring it means a second migration on a bigger table later.
5. **Two things this survey found that belong to *other* plans, and need your ordering call:**
   - **002 (Clocking replay) is blocked by P1.** A replay cannot know who was employed on the day
     it is replaying. 002 is `draft`; either it takes P1 as a prerequisite, or it accepts that
     replays include leavers.
   - **005 should carry a caveat.** Legacy did *not* keep all access in one object: six live
     permissions sit on `dbo.[User]`, including a period-lock bypass and a second absence-approval
     scope (`TLW-PEOPLE-MODEL.md` §6.2). 005's conclusion is unchanged and still right; its stated
     evidence is weaker than written.
