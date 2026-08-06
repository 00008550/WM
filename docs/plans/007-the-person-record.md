# 007 — The person record: employment as a date, and three defects under it

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 phase 1c ("Core depth"), and §13's newly-split People rows
Legacy sources surveyed: [`../TLW-PEOPLE-MODEL.md`](../TLW-PEOPLE-MODEL.md) — 76 tables / 631
columns measured, `dbo.Employees`' 153 columns classified one by one. Full file list in its §1.

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

> **And legacy still got it wrong once, in the obvious way.** `dbo.ActiveEmployeesView` (latest
> revision `Database/Versioning/78.V5.24.0.0.sql:74-80`) reads
> `WHERE IsActive = 1 AND DischargeDate IS NULL OR DischargeDate >= CAST(GETDATE() AS date)`.
> `AND` binds tighter than `OR`, so an **inactive** employee with a future discharge date is
> returned as active — and can badge in. That is the argument for computing employment **once**,
> in one place, rather than re-deriving it in every query.

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
| `EmployeeStatus.OnLeave` | **Drop** | WM invented it. Legacy's "on leave" is an *absence* — dated, a 44-table subsystem. Two systems would answer differently the day Absence ships. |
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

### [ ] P1 — Employment is a date, not an enum
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
**Tests:** edge cases 1–6 against `IsEmployedOn`, each as a named test; the `Down()` migration; a
round-trip asserting every pre-migration status maps to an employment window and back.
**Risk:** medium — a contract change plus a lossy-by-necessity backfill.

### [ ] P2 — The punch boundary fails closed
**Touches:** `src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Services/PunchService.cs`,
`TimeAttendanceModule.cs` if the error shape changes; `WM.Modules.People.Tests` or a new
`WM.Modules.TimeAttendance.Tests`.
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
115-154`, `WM.Modules.People.Tests`.
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

## Open questions for the user

1. **Employee code uniqueness — case only, or padding too?** Legacy is *both* case-insensitive and
   leading-zero-insensitive (`right('0000000000' + code, 10)`, `PersonnelService.cs:2697-2709`).
   This plan proposes **case-insensitive only**, because padding-equivalence is a badge-format
   workaround and permanently forbids `42` and `0042` as distinct codes. That is a product call,
   and it is safe either way for imported data — legacy already forbade the collision.
2. **Does `EmployeeStatus.OnLeave` have a customer behind it?** It has no legacy counterpart and
   P1's migration maps it to *suspended*. If any WM demo, doc or screen means something else by it,
   say so before P1 — the migration is where that meaning is decided.
3. **Should P4 be folded into 003 P3 instead of standing alone?** Same file, same area, and the
   ordering constraint disappears if they are one portion. The argument against is that 003 is
   approved and this is not, so folding it in changes the shape of an approved plan.
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
