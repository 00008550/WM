# TLW People/HR model — what legacy holds about a person, and what WM does not

**Surveyed 2026-08-06.** The People/HR bucket was the largest never-measured area of the legacy
product ([`COVERAGE-AUDIT.md`](./COVERAGE-AUDIT.md) §2: *"◐ People partly; **HR barely**"*) and the
one WM has already shipped a module on top of. This document is the measured answer to the
question the user asked — *"did we miss something from old TLW?"*

**Short answer: yes, and three of the misses are defects in running code rather than backlog.**
The rest is a large but defensible subset, and this document says which is which.

Every claim carries a `file:line` on both sides. Where a claim could not be settled it says
*unverified*.

Companions: [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md) (who may see whom),
[`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md) (the daily aggregate),
[`TLW-SCHEMA-SWEEP.md`](./TLW-SCHEMA-SWEEP.md) (rules, tariffs, counters).

---

## 1. The measurement

### `dbo.Employees` is the sixth-largest table in the product

Re-measured from `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (578 tables / 8,173 columns).
Ranked by column count, the top of the schema is:

| Rank | Table | Cols | Status in WM |
|---:|---|---:|---|
| 1 | `dbo.Devices` | 268 | ⏹ dropped (invariant 3) |
| 2 | `dbo.Clockings` | 249 | ▢ [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md), plan 002 |
| 3 | `dbo.SoftwareMainOptions` | 227 | ▢ named, unwritten |
| 4 | `dbo.FireMarshalMusterPoints` | 177 | ▢ Safety |
| 5 | `dbo.UnifiedTimesheetReportView` | 167 | ▢ Reporting (a view) |
| **6** | **`dbo.Employees`** | **153** | **◐ 11 of 153 columns modelled** |

`dbo.Employees` is declared at `HorioDB.designer.cs:28594`; its columns run `:29583`–`:32703`.

### The People/HR bucket, subdivided

`COVERAGE-AUDIT.md` §2 records **95 tables / 1,074 columns**. That figure is not reproducible from
this repository — the bucketing script was not committed — so this survey states its own boundary
instead of asserting the old number is wrong. Measured here, the **person master, its org
dimensions and its HR records** are:

| Group | Tables | Cols |
|---|---:|---:|
| A. Person master + person-shaped detail (`Employees`, grid/info views, images, imports, emergency contacts, custom fields ×4) | 12 | 247 |
| B. Org dimensions (`Departments`, `Locations`, `Buildings`, `CostCentres` + groups, `EmployeeGroups`) | 9 | 54 |
| C. HR lookups (`JobRoles`, `Nationalities`, `EmploymentTypes`, `LeaveReasons`, `LeaveNoticePeriods`, `Countries`, `Perks`, `Assessments`, `Vacancies`) | 10 | 28 |
| D. Positions + qualifications | 8 | 50 |
| E. HR document categories (8) + their tag tables | 16 | 140 |
| F. Pay on the person (`EmployeeSalaries`, entitlements, deductions, hourly rates, tariffs) | 9 | 48 |
| G. Managerial relations (absence / notification / expense / fire-marshal managers, per employee **and** per department) | 9 | 34 |
| H. Contracts (`EmployeeContract`, `EmployeeAssignedContracts`, `ContractHoursLimits`) | 3 | 30 |
| **Total surveyed** | **76** | **631** |

`dbo.Employee*`-prefixed tables alone are **80 tables / 844 columns**; 22 of those are report views
totalling 558 columns and belong to the Reporting bucket. Both figures are consistent with
`COVERAGE-AUDIT.md`'s 95/1,074 under a slightly wider boundary; **the correction this survey makes
to §2 is the coverage mark, not the count** — see §10.

### WM's side, measured

| | Legacy | WM |
|---|---|---|
| Person entity | `dbo.Employees`, **153 columns** | `Employee`, **10 domain properties + 4 audit** (`Domain/Employee.cs:27-42`, `SharedKernel/Domain/Entity.cs:8-14`) |
| Person screens | **24 tabs** (`PersonnelTab`, `Security/AccessControl/Enums.cs:15-42`) + **9** document tabs (`:58-70`) | one list + one modal (`frontend/portal/src/app/pages/employees/employees.component.ts`) |
| Person endpoints | — | **5** (`PeopleModule.cs:35, 69, 77, 115, 167`) + `/api/me/employee` (`:173`) |
| Org dimensions on the person | `DepartmentId` **NOT NULL**, `EmployeeLocationId` **NULL**, `CostCentreId` NULL, `CostCentreGroupId` NULL | `SiteId` **required**, `DepartmentId` **nullable**, no cost centre |
| Cross-module contract | — | `IEmployeeDirectory`, 4 methods, `EmployeeSummary` of 6 fields (`Contracts/EmployeeDirectory.cs:6-31`) |

### Legacy files actually opened

```
Logic/Entities/HorioDB.designer.cs                       (schema measurement, scripted)
Logic/Personnel/PersonnelService.cs                      (3,639 lines — §§ read in full below)
Logic/Personnel/PersonnelEnums.cs                        (91)
Logic/Personnel/PersonnelValidator.cs                    (14)
Logic/Personnel/UpdateEmployeePermissions.cs             (30)
Logic/Entities/BusinessRules/Employee.cs                 (73)
Logic/Entities/BusinessRules/EmployeeCounters.cs         (read to :126)
Logic/Extensions/EmployeeExtensions.cs                   (286 — read in full)
Logic/EmployeeContractsCalculation/CalculatedEmployeesContractsForPeriod.cs (115)
Logic/Security/AccessControl/Enums.cs                    (152)
Logic/Security/AccessControl/AuthorizationService.cs     (:392-435, :788-800)
Logic/Security/HorioIdentity.cs                          (60)
Logic/DataLocking/DataLockChecker.cs                     (65)
Logic/UserProviders/UserFromEmployeeIdProvider.cs        (21)
Logic/Planning/AbsenceRequests.cs                        (:3034-3046)
SharedLogic/Enums/EmployeeDocumentCategory.cs            (14)
WebSite/Misc/HorioSiteStructure.cs                       (Personnel branch)
WebSite/Misc/Localization.Personnel.cs                   (:74, :223)
Database/Versioning/76.V5.22.0.0.sql:25-46               (IsActiveEmployment, ActiveEmployeesView)
Database/Versioning/77.V5.23.0.0.sql:1273-1544           (Employees_Update_Trigger)
Database/Versioning/78.V5.24.0.0.sql:74-80               (ActiveEmployeesView, latest revision)
Database/Versioning/81.V5.27.0.0.sql:566-601             (GetDepartmentWithChildren, DepartmentHeirarchyView)
```

---

## 2. The answer in one paragraph

A TLW person is one 153-column row carrying **six different products' worth of attributes** — HR
identity, T&A calculation state, device enrolment, EPOS, a schools vertical and visitor
management — distinguished only by `EmployeeType` (`Regular | Visitor | Guard`,
`PersonnelEnums.cs:48-53`). Editing it is governed by **24 per-tab write permissions**
(`UpdateEmployeePermissions.cs:5-28`), one per `PersonnelTab`. Employment is **not a status
column**: it is `IsActive` (administrative) crossed with `DischargeDate` evaluated **against a
reference date** by `dbo.IsActiveEmployment` — so *"was this person employed on 3 March?"* is a
first-class, answerable question, and every device, report and notification path asks it. WM models
**11 of the 153 columns** with the same meaning, four more with a divergence, and holds a single
undated `Status` enum where legacy holds a date. The subset is defensible; the `Status`-for-date
substitution is not, and three shipped behaviours are wrong rather than merely absent.

---

## 3. `dbo.Employees`, column by column, against WM

All 153 classified. Counts are exact and reproducible; the classification lists are in this
document, not in a script that was thrown away.

| Class | Cols | Meaning |
|---|---:|---|
| ✅ **Modelled in WM, same meaning** | **11** | `Id, Code, Firstname, Lastname, DepartmentId, EnterDate, EmployeeLocationId, PhoneNumber, ContactEmail, UpdatedAt, UpdatedBy` |
| 🔶 **Modelled but diverges** | **4** | `IsActive`, `DischargeDate`, `JobRoleId`, `UserId` — §4 |
| ⏹ Dropped — devices / biometrics (invariant 3) | 12 | `Badge, IsNoBadge, PinNumber, BioStarApiId, IsHaveVirtualTerminal, FaceUnitAdministrator, FaceUnitAuthMode, FaceUnitAdminAuthMode, SupremaAdministratorLevel, IsSupremaAdministrator, CarPlateNumber, VisitDeviceId` |
| ⏹ Dropped vertical — EPOS / schools | 19 | `LunchTicketActive, IsTillOperator, EposTillOperatorLevel, ParentPayAccountId, ParentPayRollNumber, MISUserId, AdditionalMISUserId, Year, Quarter, Class, Period, RegisterId, RegisterGroup, TutorGroup, Subgroup, Subgroup1, Subgroup2, IsStudentRegistrationAbsenceAlertEnabled, MentorId` |
| ♻️ Stored calculated state — replaced by replay | 13 | `CumulationDate, WeeksCumulation, MonthsCumulation, CurrentCumulation, RealTeoretic{Weeks,Months,Current}CummulationDifference, DateEcartZero, DateInitEcart, OffsetEcart, EcartCurrentAbsolute, HoursWTD, Mod_Time` |
| ▢ Owned by the Rules / contract phase | 26 | `ContractId, WeeklyThreshold1..4, RCThreshold, MaxWeekly, WeeklyAbsolute, WeeklyRelative, Periodic, WarningThresholdMin/Max, CounterHS, CounterYearHS, DelayTolerance, CalculationPerPiece, IsCompensated, HourlyRate, HourlyRate2, HourlyRate3, ContractedHoursAmount/Period, FlexiBalanceId, CostCentreId, CostCentreGroupId, AnalyticalCode` |
| ▢ Owned by another named module | 35 | Absence (6), Expenses (4), Visitors (14), Activities (1), Safety (1), Notifications (1), T&A (4), Identity (4) |
| ❌ **Silently missing — no WM owner anywhere** | **33** | **§3.1** |

11 + 4 + 12 + 19 + 13 + 26 + 35 + 33 = **153**. ✓

### 3.1 The 33 with no owner

Not in `Employee.cs`, not in any migration, not named in any plan, not a row in §13, not in
`SCREEN-TREE.md`'s Personnel branch. These are the finding.

| Group | Columns | Why it matters |
|---|---|---|
| **Names a human actually has** (6) | `MiddleName`, `OtherName`, **`KnownAs`**, `Title`, `Initials`, `ShortName` | `KnownAs` is the *preferred* name. WM renders `$"{FirstName} {LastName}"` (`Employee.cs:41`) everywhere including the punch event (`PunchService.cs:58`), so a person called Robert-known-as-Bob is Robert on every screen with no way to change it. |
| **Core HR identity** (4) | `DateOfBirth`, `Gender`, `NationalityId`, `SocialSecurityNumber` | Legally required for payroll and right-to-work. `SocialSecurityNumber` is also a **first-class search axis** (`PersonnelFilterType.SocialSecurityNumber`, `PersonnelEnums.cs:18`; `EmployeeExtensions.cs:147-148`). |
| **Contact detail** (5) | `Address`, `Mobile`, `PersonalPhone`, `PersonalEmail`, `ContactNumber` | Legacy separates *work* from *personal* contact on both phone and email. WM has one `Email` and one `Phone` and no address at all. Emergency contact is a whole separate table (`dbo.EmployeeEmergencyContacts`, 8 cols) with nothing in WM. |
| **Bank details** (6) | `BankName`, `BankCode`, `AccountNumber`, `AccountName`, `BankAddress`, `OtherAccountInformation` | A whole `PersonnelTab.BankDetails` (`Enums.cs:38`). **No payroll export can be built without these**, and payroll plugins are the legacy business model (CLAUDE.md invariant 6). |
| **Employment lifecycle dates** (9) | `EmploymentTypeId`, `ContinuousServiceDate`, `FixedTermEndDate`, `ProbationDueDate`, `LeaveReasonId`, `LeaveNoticePeriodId`, `ResignationDate`, `FinalEmploymentDate`, `AdditionalLeaverComments` | `PersonnelTab.Leaver` (`Enums.cs:41`) is an entire tab. `ContinuousServiceDate` ≠ `EnterDate` and drives every length-of-service entitlement (`dbo.AccrualLengthOfServiceBonuses`). Probation and fixed-term end dates are what an HR product *notifies on*. |
| **Working-time law** (1) | `WTDOptOut` | The UK 48-hour opt-out. ARCHITECTURE §8.3 names "working-time-directive validation" under Scheduling; **the employee attribute the validation reads has no owner.** |
| **Integration + media** (2) | `ExternalId`, `EmployeeImageId` | `ExternalId` is the stable foreign key every connector plugin needs (§8's `IConnectorPlugin`). Without it a two-way HR sync has nothing to match on but `Code`. Photo has its own table (`dbo.EmployeeImages`). |

**The single sentence:** *legacy holds a person's identity, contact, banking, lifecycle and legal
status; WM holds a badge number, a name, a work email, a work phone, a job-title string, a site,
an optional department, a hire date and a status enum.*

---

## 4. The four modelled-but-divergent columns

### 4.1 `IsActive` × `DischargeDate` → `EmployeeStatus` — the important one

Legacy employment is **two orthogonal facts**:

```sql
-- Database/Versioning/76.V5.22.0.0.sql:25-38
CREATE OR ALTER FUNCTION dbo.IsActiveEmployment (@dischargeDate DATE, @referenceDate DATE)
RETURNS BIT AS BEGIN
    RETURN CASE WHEN @dischargeDate < @referenceDate THEN 0 ELSE 1 END;
END;
```

- `IsActive` (bit) — *administratively suspended*. Set and cleared in bulk with no date
  (`PersonnelService.cs:93-114`, `:151-173`).
- `DischargeDate` (date) — *last day of employment, inclusive*, evaluated **against a reference
  date**. `DischargeDate = today` is still employed; `< today` is a leaver
  (`EmployeeExtensions.cs:251-273`).

The three states are named and filtered independently — active / leaver / inactive
(`PersonnelService.FilterEmployeesByStatus:1848-1871`, `FilterEmployeeIdsByStatus:1873-1922`,
`FilterEmployeesNonActiveFiredExclusive:1818-1846`). Setting a leaver is a *separate operation*
from deactivating (`SetEmployeesLeaver:122-145` writes `DischargeDate` + `LeaveReasonId` and does
**not** touch `IsActive`).

WM has `EmployeeStatus { Active, OnLeave, Terminated }` (`Employee.cs:20-25`) — one undated enum.
Three consequences, all real:

1. **A future-dated leaver cannot be recorded.** "Leaves on 30 September" is the single most common
   HR data entry there is. In WM it is either wrong today or forgotten in September.
2. **Historical employment is unanswerable.** Legacy asks `IsActiveEmployment(e.DischargeDate,
   @date)` at 20+ sites in `76.V5.22.0.0.sql` alone. WM's `Terminated` carries no date, so a
   timesheet, a payroll export or a plan-002 clocking replay for March cannot know who was employed
   in March. **This lands on plan 002, which has not been written to expect it.**
3. **`OnLeave` has no legacy counterpart and should not.** Legacy's "on leave" is an *absence* — a
   dated subsystem. Putting it on the employment-status enum conflates a time-boxed fact with a
   lifecycle state; the moment the Absence module exists there will be two contradicting answers.
   **Measured 2026-08-06 — see §4.1a.** When this was written it was an assumption; it has since
   been checked against the Absence schema and source and it holds.

> ### ⚠️ Correction, 2026-08-06 — this survey under-reported the leaver record
> The user pushed back on point 3 with *"there is a counterpart in Legacy called LeaveDate so we
> can know if person is a leaver and we also have Leave where Leave Reason can be selected"*, and
> they were right about the substance. This section mentions `LeaveReasonId` only in passing at
> `:179`, as an argument that setting a leaver differs from deactivating. It never carried the
> **reason** into the model, the column classification in §3, or the plan. Two of the 153 columns
> and one whole table were missed:
>
> | Legacy | Where |
> |---|---|
> | `Employees.DischargeDate` — `Date`, nullable | `HorioDB.designer.cs:29831` |
> | `Employees.LeaveReasonId` — nullable FK, association `LeaveReason_Employee` | `:30707`, `:34148` |
> | `Employees.AdditionalLeaverComments` — `nvarchar(500)` | `:32539` |
> | `dbo.LeaveReason` — `Id`, `Name`, `IsActive` | `:53138-53148` |
>
> So **leaving is a date, a reason chosen from a customer-maintained list, and free-text comments.**
> `IsActive` on the lookup means a reason is retired rather than deleted, which keeps historical
> leavers readable — the right pattern, and one WM should copy.
>
> Point 3's *conclusion* survives: there is still no **temporary** "on leave" state on the person,
> and absence remains a separate subsystem. What was wrong was the implication that leaving itself
> is only a date. Plan 007 P1 now adopts all three fields and the lookup.
>
> **Method note for future surveys.** The failure mode here was reading a write path
> (`SetEmployeesLeaver`) for what it proved about *one* argument, and not following the columns it
> touched back into the classification. A column seen in passing is not a column classified.

### 4.1a Point 3, measured — a narrow dependency check into Absence (2026-08-06)

Point 3 was an *assumption* when written: this survey never opened Absence. Plan 007 P1 deletes
`EmployeeStatus.OnLeave` on it, so it was checked before the portion was built. **Scope: four
questions only. This is not a survey of Absence** — see `COVERAGE-AUDIT.md` §2's note.

**There is no temporary-non-availability state on `dbo.Employees`.** All 153 columns were re-scanned
for `absen|leave|holiday|sick|suspend|status|avail`. Every absence-shaped hit is *configuration or a
role*, never a state:

| Column | Line | What it is |
|---|---|---|
| `AbsenceGroupId` | `:30555` | which absence-type group the person may book from (`dbo.AbsenceGroups`, `:45988`) |
| `HolidayGroupId` / `HolidaysAmount` / `HolidaysPeriod` | `:30731`, `:30667`, `:30687` | entitlement configuration |
| `IsAbsenceManager` / `IsDeputyAbsenceManager` | `:31275`, `:31295` | approver flags — who approves *others* |

The absence *instance* is dated, in two places, neither of them the person:

- **`dbo.AbsenceRequests`** — 14 cols, `:46542`: `EmployeeId, StartDate, EndDate, AbsenceId,
  AbsenceDuration, RequestDate, …`.
- **materialised onto the day**: `dbo.Clockings.MorningAbsenceID` (`:21310`) /
  `AfternoonAbsenceID` (`:21334`), with `AbsenceDuration` / `MorningAbsenceDuration` /
  `AfternoonAbsenceDuration` (`:26194`–`:26234`); archived in `dbo.HistoricalClockingAbsences`
  (16 cols, `:190508`). Already recorded at [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md)`:114`.

And legacy's own person-status vocabulary is **exactly three values, computed and never stored**:

```sql
-- PersonnelService.FilterEmployeeIdsByStatus:1890-1895
select Id, IsActive,
  case when dbo.IsActiveEmployment(DischargeDate, getdate()) = 1 then 0 else 1 end as IsLeaver
from Employees
```
→ active / leaver / inactive (`:1902-1904`). There is no fourth member and nothing to mirror.
**Point 3 stands, now on evidence rather than inference. 007 P1's drop of `OnLeave` is correct.**

**Two vocabularies, and they do not collide.** `dbo.LeaveReasons` (3 cols, `:53137` — `Id`,
`Name(200)`, `IsActive`) is a *label*, maintained under `Menu_Personnel_LeaveReasons`
(`WebSite/Misc/localization1.generated.cs:6108-6122`). The absence vocabulary is
**`dbo.Absence`** — **35 columns**, `:8743` — and it is a *rule-carrying type*, not a label:
`Code`, `Name`, `ShortName(4)`, `Color(6)`, `IsActive` (`:9478`), `Unit`
(`AbsenceUnit {Day, Hour}`, `:9235`), `Category`
(`AbsenceCategory {Uncategorized, Holiday, Sick, MaternityPaternity, OtherEvent}`, `:9559`,
`Logic/Settings/AbsenceEnums.cs:24-37`), plus ~25 columns of behaviour — `AllowOnDayOff`,
`AllowOnHoliday`, `IncludeToBradfordCalculation`, `BlockAbsenceRequestOnNegativeBalance`,
`BlockAbsenceRequestBeforeDate`, `IsApprovalRequiredForBookingAbsence`,
`IsExcludedFromPayrollExport`, `CounterId`, `ExportCode`/`ExportCode2`, `Package`, `Deviation`.
*Why is this person not here today, and how does it pay, accrue and export* is a different question
from *why did this person stop being employed*. **Two tables, correctly two.** The only risk is
naming — see 007 open question 6.

**Absence reads employment; it never writes it.** Three measured consumers:

1. **Accruals pro-rate on the employment *interval*.** `AccrualsCalculationRepository.GetEmployeesMap:53-57`
   selects exactly `Id, EnterDate, ContinuousServiceDate, DischargeDate, FinalEmploymentDate`, and
   `EmployeeAccrualCalculationsService.GetActualToFullPeriodRatio:728-741` builds
   `new DateTimeInterval(employee.EnterDate, employmentEndDate)` and intersects it with the accrual
   period. `CalculateAccruedChanges:941-954` skips periods outside the window and flags
   `terminationInThisPeriod`. **This is the strongest independent confirmation of 007 P1's design
   that exists: the consumer wants `[EmployedFrom, EmployedUntil]`, not a status enum.** It also
   uses *dates only* — `IsActive` is never read — which confirms §8's Keep of suspension and
   leaving as two facts, and means `IsSuspended` must not be folded into the accrual window.
2. **Legacy's window end is `FinalEmploymentDate ?? DischargeDate`** (`:728-730`, `:941`), not
   `DischargeDate` alone. `FinalEmploymentDate` is one of §3.1's 33 unowned columns and 007 P1 does
   not adopt it. Fine for P1 — but Phase 3 will meet a *second* leaving date with **higher
   precedence** and no home. Recorded here so it is not rediscovered.
3. **The planning board re-derives employment a sixth time, and fails open.**
   `PlanningService.GetAllEmployees:32-43` and `:45-57`:
   `(e.IsActive || includeNonActiveEmployees) && (!e.DischargeDate.HasValue || e.DischargeDate >= DateTime.Now.Date || includeFiredEmployees)`.
   Evaluated against **`DateTime.Now.Date`, not the period being planned** — so planning next
   quarter still lists someone leaving next month. Nothing in `Logic\Planning` or `Logic\Accruals`
   calls `ActiveEmployeesView` or `IsActiveEmployment`. Another argument for one `IsEmployedOn`.

**A P1 test can now cite legacy instead of first principles.** `PersonnelService.SetEmployeesActive:151-173`
un-leaves with `set IsActive = 1, DischargeDate = null, LeaveReasonId = null` — legacy clears the
reason together with the date, exactly as 007 P1 requires.

**One fail-open found in passing, for the full survey — not chased here.** `Logic\Planning\AbsenceRequests.cs`
contains **no** reference to `DischargeDate`, `IsActive` or `EnterDate`, and
`SetEmployeesLeaver:122-145` does not touch absence requests. A leaver keeps approved absence past
their leave date. *Invert* candidate when Absence is planned.

> **Legacy defect worth recording, because it shows the trap.** `dbo.ActiveEmployeesView`
> (latest revision `Database/Versioning/78.V5.24.0.0.sql:74-80`, unchanged since
> `76.V5.22.0.0.sql:40-46`) reads:
> ```sql
> WHERE IsActive = 1 AND DischargeDate IS NULL OR DischargeDate >= CAST(GETDATE() AS date);
> ```
> `AND` binds tighter than `OR`, so this is `(IsActive = 1 AND DischargeDate IS NULL) OR
> (DischargeDate >= today)`. **An inactive employee with a future discharge date is returned as
> active** — and that view gates device enrolment (`KioskExternalAccessService.cs:152, 201`,
> `IrTemplatesService.cs:76`, `FaceService.cs:176`, `DevicesService.cs:1230`) and HR notifications
> (`HRService.cs:1450, 1487, 1524, 1561, 1598`). A suspended person can badge in.
> Two orthogonal facts written as one boolean expression, wrongly — which is exactly the argument
> for WM computing employment from dates in one place rather than re-deriving it per query.

### 4.2 `JobRoleId` → `JobTitle`

Legacy `Employees.JobRoleId` is an FK to `dbo.JobRoles(Id, Name)` (2 cols,
`HorioDB.designer.cs:182432`). WM's `JobTitle` is a free `string?` capped at 128
(`PeopleDbContext.cs:36`). A reference list becomes free text: no rename propagation, no grouping,
no "all Technicians" query. `dbo.Positions` (7 cols) + `dbo.EmployeePositions` (4, with `Priority`)
are a *third*, separate concept — **preferred positions** for scheduling — and are unrelated to
`JobRoles`. WM has neither.

### 4.3 `UserId` — the link points the other way

`dbo.Employees.UserId` is the link; `dbo.[User]` has **no `EmployeeId` column** (30 columns, all
listed in §6). The resolver is:

```sql
-- AuthorizationService.GetUserByEmployeeId:788-800
SELECT TOP 1 UserId FROM dbo.Employees WHERE Id = @employeeId
```

WM stores it as `User.EmployeeId` (`Identity/Domain/User.cs:14`). **The WM direction is better** —
Identity owns the link, People stays free of an Identity reference, and invariant 1 is satisfied by
construction. But `ARCHITECTURE.md:171` attributes WM's direction to TLW: *"(TLW: `User.EmployeeId`,
…)"*. That is the same class of error as the `WebPages` and one-role-per-user findings: a design
choice credited to legacy that legacy does not make. Correction proposed in §10.

### 4.4 `Employees.RoleId` — a second, undocumented role pointer

`dbo.Employees.RoleId` (`:29811`) exists alongside `dbo.UsersInRoles`. `TLW-AUTHORIZATION-MODEL.md`
§3 establishes that user→role goes through `UsersInRoles`. What `Employees.RoleId` is for was
**not settled by this survey — unverified.** It is in the `Employees_Update_Trigger` audit map as
column key 12 (`77.V5.23.0.0.sql:1300`), so it is edited. Flagged for whoever writes plan 004.

---

## 5. Structural findings beyond the column list

### 5.1 WM put the hierarchy on the dimension legacy leaves flat

Measured, all four org dimensions:

| Legacy table | Cols | Hierarchical? | WM counterpart | Hierarchical in WM? |
|---|---:|---|---|---|
| `dbo.Locations` (`:182940`) | 4 | **No** — `Id, Code, DisplayName, IsActive` | `Site` (`Employee.cs:5-12`) | **Yes** — `ParentId`, `Children`, `SiteHierarchy.ExpandWithDescendantsAsync` |
| `dbo.Departments` (`:55085`) | 10 | **Yes** — `ParentId` at `:55267` | `Department` (`Employee.cs:14-18`) | **No** — `Name`, `SiteId`, nothing else |
| `dbo.Buildings` (`:113785`) | 5 | Yes — `ParentId` | — | dropped (no `Employees.BuildingId`; `PHASE-AUDIT.md` B4) |
| `dbo.CostCentres` (`:91377`) | 15 | Yes — `ParentId` | — | not modelled |

`SCREEN-TREE.md:294-296` records that *"locations and sites are the same axis, not two"*. If so,
**WM's site tree, `ScopeConstraint.IncludeChildSites` and `SiteHierarchy` have no legacy
counterpart on that axis** — legacy `Locations` is a flat list — while the axis legacy *does* nest
is flat in WM.

This is not merely cosmetic, because department is the axis legacy actually expands:

```csharp
// Logic/Planning/AbsenceRequests.cs:3039-3043 — the ONLY consumer of the department tree view
var departmentIds = db.Employees.Where(e => e.Id == managerId).Select(e => e.DepartmentId)
    .Join(db.DepartmentHeirarchyView, depId => depId, hv => hv.DepartmentId, (dep, hv) => hv.SubdepartmentId)
    .ToArray();
```

`dbo.DepartmentHeirarchyView` = `CROSS APPLY dbo.GetDepartmentWithChildren(dp.Id)`
(`Database/Versioning/81.V5.27.0.0.sql:566, 594-601`) — added in the **newest** versioning script in
the tree. `AuthorizationService.ManagedDepartmentsForTree:392-435` walks the same `ParentId` chain
in C# to render the department picker.

This **does not contradict** `TLW-AUTHORIZATION-MODEL.md` C9 — the role-scope TVF still matches
`e.DepartmentId = dr.DepartmentId` exactly, with no expansion. It adds a fact: legacy expands the
department tree in exactly one place, for a *different* scope mechanism (§6.2). So:

- Keeping WM's site tree is fine — it is a WM improvement, not a port, and should be labelled that.
- **`Department.ParentId` is the genuine legacy shape and WM does not have it.** Any department
  scope WM ships is flat by construction, and a customer migrating from TLW with a nested
  department structure will find the tree gone.

**This changes plan 001 P4/P5**, which add and expose scope dimensions without a department parent —
see §9.

### 5.2 Line management is not a relation on the person — it is a security group

`PersonnelTab.LineManager` (`Enums.cs:29`) is a tab on the employee screen. There is **no
`ManagerId` column on `dbo.Employees`**. The tab's localization resolves to the managed-by-roles
resource:

```csharp
// WebSite/Misc/Localization.Personnel.cs:223
public static string Menu_Personnel_Personnel_LineManager =>
    LocalizationService.GetResourceString("AddEmployee_TabTitleManagedByRoles");
```

and the bulk import writes line management into `dbo.EmployeesManagedByRole`
(`EmployeesImportService.cs:1396-1401`). **Legacy's org chart is its access model.** That is worth
knowing before anyone adds a `ManagerId` to WM's `Employee`: it would be a new concept, not a port,
and it would compete with the security group for the same meaning. (`Employees.MentorId` is the
schools vertical only — `StudentRegistrationAbsenceNotificationsService.cs:203, 271, 473`.)

Legacy does have four **explicit** manager relations, each a table, each with per-employee *and*
per-department rows and an `Order` column for escalation: absence (+ deputy), notification,
expense, fire marshal (`dbo.EmployeeAbsenceManagers:115067`, `dbo.DepartmentAbsenceManagers:115266`,
`dbo.EmployeeNotificationManagers:115465`, and 6 more — group G in §1). WM has none of these; they
belong to Absence / Notifications / Expenses and are correctly deferred, but they are the reason
"who approves this person's leave" is a *four-table* question in legacy and should not be
back-filled as a single `ManagerId`.

### 5.3 Eight HR document categories, eight near-identical tables

```csharp
// SharedLogic/Enums/EmployeeDocumentCategory.cs:3-13
public enum EmployeeDocumentCategory
{ Onboarding, Remunerations, Disciplinary, Appraisals, Objectives, Certificates, Documents, Qualifications }
```

Each has its own table with the same 13–19 columns — a file blob, `FileName`, `FileContentType`,
create/update stamps, `Notification`, `IssueDate`/`DueDate`/`NotifyOnDate`, `SignDocumentRequestId`,
`VirusScanStatus` — plus its own `…Tags` table:

`EmployeeAppraisal` (17, `:76783`) · `EmployeeCertificates` (17, `:78075`) ·
`EmployeeDisciplinary` (17, `:76161`) · `EmployeeObjectives` (19, `:77405`) ·
`EmployeeQualifications` (17, `:107028`) · `EmployeeRemuneration` (18, `:75515`) ·
`EmployeeOnboardingDocuments` (13, `:183762`) · `EmployeeDocuments` (10, `:78697`).

**16 tables / 140 columns expressing one idea eight times.** Adding a ninth category is a schema
migration, a table, a tag table, an enum member, a `DocumentsTab` member and a
`RoleHrDocumentSecurity` row. WM's Documents module (§10) should be **one** document table with a
category dimension — and the category is already the unit `RoleHrDocumentSecurity` grants on
(`TLW-AUTHORIZATION-MODEL.md` §8), so the normalisation and the permission model agree.

Two things this settles for §10, which currently designs object storage with no scanning step:

- **`VirusScanStatus` is a column on all eight tables**, not just the `FileVirusScanQueue` (5 cols)
  that `COVERAGE-AUDIT.md` §3 found. Scanning is a per-document state machine, not a queue.
- **File bytes are in the database** (`AppraisalFile`, `CertificatesFile`, …). WM moving to object
  storage is an improvement; the migration path for existing blobs is real work nobody has costed.

### 5.4 Per-tab write permissions — 24 of them

```csharp
// Logic/Personnel/UpdateEmployeePermissions.cs:5-28
public bool CanUpdateGeneral { get; set; }
public bool CanUpdateCalculations { get; set; }
… 24 flags, one per PersonnelTab …
public bool CanUpdateSalary { get; set; }
public bool CanUpdateLeaver { get; set; }
```

This is where screen rights and record scope finally meet: the tab right becomes a **field-group
write permission** on the employee update. `Salary`, `BankDetails` and `Disciplinary` are separate
grants from `General`. WM has one permission — `employees.manage` (`WmPermissions.cs:13`) — and
`PUT /api/employees/{id}` is a full replace of every field (`PeopleModule.cs:133-142`).

Once WM holds bank details and salary, **`employees.manage` becomes a grant of "read and rewrite
everyone's bank account"**. Plan 004 (screen rights) must carry field-group granularity or the
People expansion cannot ship safely. Recorded here so 004 discovers it before it is built, not
after.

### 5.5 A column-level audit trail, in a trigger

`Employees_Update_Trigger` (`Database/Versioning/77.V5.23.0.0.sql:1273-1544`, enabled at `:1544`)
writes one `dbo.AuditTrailLogs` row per **changed column** — old value, new value, username, row id
(`:1489-1491`) — driven by a hard-coded `(ColumnName → ColumnKey)` map in the trigger body
(`:1288-1352`, ~80 entries). It also sets `Employees.UpdatedAt` (`:1494-1496`) and enqueues device
sync/delete tasks (`:1511-1541`).

`dbo.EmployeeCustomFieldValueLogs` (7 cols, `:103826`) is a **second, independent** change log for
the same entity, covering custom fields only.

WM has `AuditableEntity` — `CreatedAt/UpdatedAt/CreatedBy/UpdatedBy` (`Entity.cs:8-14`) — and **no
field-level history at all**. For HR data that is a compliance gap, not a feature gap. The
requirement is Keep; the trigger is Improve (§8).

### 5.6 Two contract-resolution mechanisms that disagree

```csharp
// Logic/Entities/BusinessRules/EmployeeCounters.cs:8-13  (×17 properties)
public float WeeklyThreshold1Effective(Dictionary<int, EmployeeContract> contracts)
{
    if (ContractId.HasValue && contracts.ContainsKey(ContractId.Value))
        return contracts[ContractId.Value].WeeklyThreshold1;
    return WeeklyThreshold1;                       // employee's own default
}
```

**Contract overrides employee default** — the `…Effective(contracts)` pattern. But it resolves
through `Employees.ContractId`, the *current* pointer, with **no date**. The dated history lives in
`dbo.EmployeeAssignedContracts(EmployeeId, ContractId, StartDate, EndDate)` and is resolved
separately:

```csharp
// Logic/EmployeeContractsCalculation/CalculatedEmployeesContractsForPeriod.cs:37-43
_employeeAssignedContracts
  .Where(c => c.EmployeeId == employeeId
           && (!c.EndDate.HasValue   || c.EndDate.Value   >= date)
           && (!c.StartDate.HasValue || c.StartDate.Value <= date))
  .Select(c => c.ContractId).FirstOrDefault();
```

Two answers to one question. `…Effective` is date-blind, so **a retroactive contract change is
invisible to it** while the day-resolver sees it. And the day-resolver's `FirstOrDefault()` has
**no `OrderBy`** — overlapping assignments resolve non-deterministically, and a null `StartDate`
*or* `EndDate` widens the match rather than narrowing it. Both are inputs plan 002's replay and the
unwritten tariffs plan must be built against. `GetDefaultCostCenter` is the same two-level shape
done cleanly — employee, then department, then null (`Entities/BusinessRules/Employee.cs:39-48`).

### 5.7 Fixed numbered slots on the person

Per the standing smell list, and consistent with `Clockings`' `BadgeTime1..12` / `CPTN01..20`:
`WeeklyThreshold1..4`, `HourlyRate`/`HourlyRate2`/`HourlyRate3`, `Subgroup`/`Subgroup1`/`Subgroup2`.
Three hourly rates is a ceiling customers hit; `dbo.EmployeeHourlyRates(RateId, EmployeeId, Rate,
EffectiveFrom)` (`:57784`) exists as the dated, unbounded version **alongside** them — legacy
already built the fix and never removed the columns.

---

## 6. `dbo.[User]` — 30 columns, settled

Owed before Phase 3 per `TLW-AUTHORIZATION-MODEL.md` §15. All 30 columns, `HorioDB.designer.cs:6987`
(`:7208`–`:7788`):

| Group | Columns | WM today | Verdict |
|---|---|---|---|
| Identity (6) | `Id, FirstName, LastName, Email, Login, Password` | `UserName, Email, DisplayName, PasswordHash` (`User.cs:7-10`) | ✅ covered |
| Session (2) | `CreateDate, LastLoginDate` | `CreatedAt`; **no `LastLoginDate`** | ◐ trivial, add it |
| Lockout (3) | `IsLockedOut, InvalidLoginAttemptsCount, LastLockedOut` | `FailedLoginAttempts, LockedOutUntil` (`:12-13`), thresholds configurable since 006 P3 | ✅ covered, better |
| Password lifecycle (8) | `ResetPasswordCode(+Time), ShouldChangePasswordNextTime, PasswordNeverExpires, ChangePasswordPeriod, LastPasswordChangedDate, OneTimePassword(+Time)` | **none** | ▢ **Phase 3**, real |
| SSO / federation (3) | `WindowsUser, SsoLoginCode, SsoLoginCodeGeneratedTime` | none | ▢ Phase 3, expected |
| Visibility (1) | `IsHidden` | none | ◐ minor (service accounts) |
| **Cross-cutting permissions (7)** | `IsUserCanManageAllRequests, IsUserCanManageRequestsByDepartment, IsUserCanManageAllExpenseRequests, IsUserCanPayAllExpenseRequests, IsVisitorManager, IsAllowedToModifyLockedData, IsAllowedToSetSecurityGroupForAnprCarPlate` | **none, and no plan holds them** | ❌ **§6.2 — the finding** |

**2FA, password history and email preferences are *not* columns of `[User]`.** They are separate
tables: `dbo.User2FAHistory(Id, UserId, CreatedAt)` — 3 columns, a *trusted-device / recent
verification* log with **no secret store**; `dbo.UserPasswordHistory(Id, UserId, Password)` — 3
columns, reuse prevention; `dbo.UserEmailPreferences(Id, UserId, EmailCategory)` — 3 columns, an
opt-out list. The 2FA *configuration* is global, on `SoftwareMainOptions`
(`IsTwoFactorAuthenticationEnabled`, `TwoFactorAuthenticationType`), and SSO is
`dbo.SingleSignOnSAMLSettings(Id, Type, Endpoint, Certificate)` — 4 columns, **one row for the whole
install**, not per user. `TLW-AUTHORIZATION-MODEL.md` §1 and §15 describe those three as columns of
`[User]`; corrected in §10.

**Verdict on the deferral: correct.** Password lifecycle and SSO are genuinely Phase 3 and nothing
shipped depends on them. `LastLoginDate` and `IsHidden` are cheap and can ride along with any user
work. The one thing that is *not* safely deferrable is §6.2.

### 6.2 Seven per-user permissions outside the role model — a fourth authorization axis

`TLW-AUTHORIZATION-MODEL.md` §1 measured *"18 tables, 59 columns — that is the entire persisted
authorization surface"*. It is not: seven boolean permissions live on `dbo.[User]` and are read at
runtime, three of them promoted straight onto the identity principal alongside `RoleId`:

```csharp
// Logic/Security/HorioIdentity.cs:26-35
RoleId                              = userWrapper.RoleId;
IsAllowedToModifyLockedData         = userWrapper.IsAllowedToModifyLockedData;
IsUserCanManageRequestsByDepartment = userWrapper.IsUserCanManageRequestsByDepartment;
IsUserCanManageAllRequests          = userWrapper.IsUserCanManageAllRequests;
```

Measured consumers:

| Column | Read at | What it does |
|---|---|---|
| `IsAllowedToModifyLockedData` | `DataLocking/DataLockChecker.cs:22-35`, `AuthorizationService.cs:1384-1403` | **Bypasses the period lock.** `IsLocked()` returns false unconditionally for these users, so they can edit closed payroll periods. |
| `IsUserCanManageAllRequests` / `…ByDepartment` | `HorioIdentity.cs:31-32, 52-53`; consumed by `Planning/AbsenceRequests.cs:3034-3046` | A **second, independent data scope** for absence approvals — and the one place legacy expands the department tree (§5.1). |
| `IsUserCanManageAllExpenseRequests`, `IsUserCanPayAllExpenseRequests` | `Interfaces/IAuthorizationService.cs:92` (`out bool canManage, out bool canPay`) | Expense approval vs. payment, separated. |
| `IsVisitorManager` | `ExternalAccess/SYQR.cs:124, 245, 683` | Gates the entire SYQR external login: `if (!BCrypt.CheckPassword(...) || !user.IsVisitorManager) …` |
| `IsAllowedToSetSecurityGroupForAnprCarPlate` | localization only (`localization1.generated.cs:9274`) — **no read found in `Logic` or `WebSite`** | ANPR, a dropped vertical. Treat as dead in scope. |

Six of the seven are live and in-scope. **They matter now, not in Phase 3**, because plan 005 is
about to declare "one group is the single unit of access" and these are counter-evidence that TLW
never achieved that — it leaked four cross-cutting capabilities onto the user row. WM should model
them as **ordinary permissions on the security group** (`WmPermissions`), not as user columns —
that is strictly better and it is what option A already implies. Two of them, though, are not
permissions at all but a **scope**: `IsUserCanManageAllRequests` vs `…ByDepartment` is a
scope *kind*, and it will need a home in the Absence phase. Raised as a question in §11.

---

## 7. Divergences that are already load-bearing — defects, not backlog

The brief drew the line correctly: **wrong is a defect, missing is a backlog item.** Three things
are wrong. None of them is on `PHASE-AUDIT.md`'s list (checked A1–A4, B1–B7, D1–D17).

### D1 · A terminated employee can still punch — **blocking**

`IEmployeeDirectory.FindByIdAsync` / `FindByCodeAsync` apply the caller's data scope and **no
status filter** (`PeopleModule.cs:197-207`; only `ListActiveAsync:209-213` filters
`Status == Active`). Both punch paths go through them and neither checks status afterwards:

- `PunchService.RecordAsync:32` → `FindByCodeAsync` → punch accepted.
- `PunchService.RecordForEmployeeAsync:69` → `FindByIdAsync` → delegates to `RecordAsync`.

`AuthService` checks `User.IsActive` only (`AuthService.cs:76, 117, 131`); nothing deactivates a
user when their employee is terminated, and nothing links the two. So a leaver whose login survives
keeps clocking in, and the punch is written, published to `wm.punches` and broadcast to the live
feed.

The inconsistency is visible inside one class: `GetRecentAsync:100` builds its visible set from
`ListActiveAsync`, so **the punch is accepted and then never shown**. WM accepts data it has already
decided not to display.

Legacy fails closed here, emphatically and in every channel: `ActiveEmployeesView` or
`.ActiveNotFired()` gates device enrolment (`KioskExternalAccessService.cs:152, 201`,
`IrTemplatesService.cs:76` — commented `--active and not fired`, `FaceService.cs:176`,
`DevicesService.cs:1230`), and `Employees_Update_Trigger:1523-1528` pushes a **delete** task to
every device the moment `IsActive` goes 1→0.

**Severity.** WM's own punch surface is the product's core write path. Accepting attendance for
someone who has left is a payroll-correctness and audit problem, and it is reachable today by any
employee-linked user whose record was marked `Terminated`.

### D2 · The employee-code uniqueness rule is not enforced by the database — **material**

```csharp
// PeopleModule.cs:83-85
// Case-insensitive: 'E1030' and 'e1030' are the same badge number.
if (await db.Employees.AnyAsync(e => e.Code.ToLower() == code.ToLower(), ct))
    return Results.Problem(..., statusCode: 409);
```
```csharp
// PeopleModule.cs:107-111
catch (DbUpdateException ex) when (... SqlState: "23505" })
    // The unique index is the real guarantee; the check above races.
```

The index is `IX_Employees_Code` on `character varying(32)`, plain and unique
(`20260720080022_Initial.cs:85-90`; `PeopleDbContext.cs:30`) — **case-sensitive** under any standard
Postgres collation. So:

- The comment at `:109` is **false**. The index does not guarantee the rule the check enforces.
- Under concurrency, simultaneous `POST E1030` and `POST e1030` both pass the check and **both
  insert**. The installation then holds two employees whose codes differ only in case, which the
  product says is impossible — and every subsequent `PUT` on either 409s against the other,
  making them uneditable.
- `EmployeeDirectory.FindByCodeAsync:199` does `e.Code == code` — **case-sensitive**. A punch for
  `e1030` against employee `E1030` returns *"Unknown employee code"* (`PunchService.cs:34`). The
  write path and the read path disagree about what a code is.

Legacy is stricter and differently strict, which is worth knowing for migration:
`IsEmployeeCodeFieldUnique` compares `right('0000000000' + @code, 10)`
(`PersonnelService.cs:2697-2709`), i.e. **left-zero-padded to 10** — `42`, `0042` and `000042` are
the same code — on top of SQL Server's case-insensitive default collation
(`SQL_Latin1_General_CP1_CI_AS`, visible at `77.V5.23.0.0.sql:1425`). `IsEmployeeBadgeUnique:2726-2741`
does the same for `Badge`.

**Fix shape:** one decision (case-insensitive? padding-insensitive?) enforced in **one** place — a
Postgres `citext` column or a unique index on `lower(code)` — and `FindByCodeAsync` normalised the
same way. Not a redesign.

### D3 · `DepartmentId` is validated nowhere, and department is a scope axis — **material**

`POST /api/employees` validates that `SiteId` names an existing site (`PeopleModule.cs:86-87`) and
then assigns `DepartmentId = request.DepartmentId` (`:98`) with **no check at all**. `PUT` is the
same (`:130-131`, `:140`). The migration creates `DepartmentId` as a bare nullable `uuid` with **no
foreign key and no index** (`20260720080022_Initial.cs:44`); the only department constraint in the
schema is `IX_Departments_SiteId_Name` (`:78-83`).

Two consequences:

1. An employee can carry a `DepartmentId` that does not exist, or one belonging to a **different
   site**. `Department.SiteId` exists (`Employee.cs:17`) and nothing enforces agreement with
   `Employee.SiteId`.
2. Department is a **data-scope dimension** (`EmployeeScopeExtensions.cs:20-21`,
   `EmployeeSummary.DepartmentId` exists precisely for this — `Contracts/EmployeeDirectory.cs:26-31`).
   A site/department mismatch therefore means two managers scoped to non-overlapping parts of the
   estate can both see the same person, and neither can tell why.

Legacy cannot have this bug: `Employees.DepartmentId` is `NOT NULL` and `EmployeeLocationId` is an
*independent* nullable column — there is no site↔department relation on the employee to be
inconsistent with. **WM introduced the relation and then did not enforce it.**

The frontend hard-codes `departmentId: null` (`employees.component.ts:300`, which is
`PHASE-AUDIT.md` B5), so this is not reachable through the SPA today — but invariant 2 makes the
API the product, and 003 P3 is about to *fix* B5 and make the field live. **Ordering matters: D3
should land with or before 003 P3**, or fixing B5 turns a latent hole into a reachable one.

---

## 8. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A person is one aggregate with an org placement and a lifecycle | **Keep** | Genuine domain truth; every module keys on it. |
| Employment as **(administrative state × dated discharge)**, evaluated at a reference date | **Keep — and adopt properly** | `IsActiveEmployment(dischargeDate, referenceDate)` (`76.V5.22.0.0.sql:25-38`) is right. WM's undated enum is the regression (§4.1). |
| `IsActive` and `DischargeDate` as *two* facts | **Keep** | Suspension ≠ leaving. `SetEmployeesLeaver:122-145` deliberately does not touch `IsActive`. |
| `EmployeeStatus.OnLeave` | **Drop** | Absence is a dated subsystem, not a lifecycle state (§4.1). WM invented this status. *(Corrected 2026-08-06: dropping it is still right, but not because leaving is unmodelled.)* **Measured into Absence 2026-08-06 (§4.1a): no non-availability state exists on `dbo.Employees`; the instance is `AbsenceRequests(StartDate, EndDate)` and `Clockings.MorningAbsenceID`/`AfternoonAbsenceID`; legacy's person-status vocabulary is three computed values. Confirmed.** |
| **Leaving = `DischargeDate` + `LeaveReasonId` + `AdditionalLeaverComments`** | **Keep — adopt all three** | *(Added 2026-08-06; §4.1's correction block.)* "Why did they leave?" is a question every HR customer asks and legacy answers. WM's `Terminated` discards it. |
| `dbo.LeaveReason` as a customer-maintained lookup with `IsActive` | **Keep** | Leaving reasons are per-customer vocabulary, not a WM enum, and `IsActive` retires one without orphaning the leavers who used it. |
| `ActiveEmployeesView`'s `AND … OR …` precedence | **Invert** | A real legacy defect (§4.1). Employment must be computed once, not re-derived per query. |
| One person table holding six products' columns, keyed on `EmployeeType` | **Improve** | `SCREEN-TREE.md` decision 9 (`PersonType`) already chose this. It also means **visitors are 14 of the 153 columns** — dropping Visitors drops them, not the person. |
| 24 per-tab **write** permissions (`UpdateEmployeePermissions.cs:5-28`) | **Keep the granularity, improve the mechanism** | Bank/salary/disciplinary must not ride on one `employees.manage`. Field groups on the group, not 24 booleans in a DTO (§5.4). |
| 8 HR document tables + 8 tag tables | **Improve** | One document table + a category dimension. The category is already the permission unit (`RoleHrDocumentSecurity`). |
| Document bytes in the database | **Improve** | Object storage (§10). Migration of existing blobs is uncosted work. |
| `VirusScanStatus` on every document table | **Keep** | Scanning is per-document state, not a queue. Wider than `COVERAGE-AUDIT.md` §3 recorded. |
| Column-level change history (`Employees_Update_Trigger`) | **Keep the requirement, Invert the mechanism** | A trigger with a hard-coded column→key map (`77.V5.23.0.0.sql:1288-1352`) silently stops auditing any column added after it. WM should emit field-level change events to `wm.audit` from the application. |
| Two audit mechanisms for one entity (trigger + `EmployeeCustomFieldValueLogs`) | **Improve** | One. |
| `JobRoleId` → free-text `JobTitle` | **Invert back** | WM lost a reference list for a string (§4.2). Cheap to restore, expensive later. |
| `Locations` flat, `Departments` nested | **Improve — but fix the axis** | WM nested Site (no legacy precedent, and fine) and flattened Department (legacy precedent, and lost). Both dimensions should nest (§5.1). |
| `Employees.DepartmentId` NOT NULL | **Improve, deliberately** | WM's nullable department is defensible *if* the scope filter fails closed on null — it does (`EmployeeScopeExtensions.cs:20-21`). Keep, but enforce the FK (D3). |
| Line management expressed as `EmployeesManagedByRole` | **Keep for now, decide explicitly** | Do not add `Employee.ManagerId` casually; it competes with the security group for one meaning (§5.2). |
| 4 manager relations × (per-employee, per-department) with `Order` | **Keep** | Escalation chains are a real requirement; they belong to Absence/Notifications/Expenses. |
| `HourlyRate` / `HourlyRate2` / `HourlyRate3` | **Improve** | Fixed slots. `dbo.EmployeeHourlyRates(EffectiveFrom)` is the dated fix legacy already built and never adopted. |
| `WeeklyThreshold1..4` | **Improve** | Same shape; belongs to Rules. |
| 13 columns of stored cumulation/écart state | **Drop** | The stored-calculated-value smell in its purest form; replaced by replay. |
| `…Effective(contracts)` resolving through the undated `Employees.ContractId` | **Invert** | Date-blind override next to a dated history that disagrees (§5.6). WM must resolve config **as at a date**, one way. |
| `GetEmployeeAssignedContractForDay`'s unordered `FirstOrDefault` | **Invert** | Non-deterministic on overlap; null bounds widen the match. |
| `GetDefaultCostCenter` (employee → department → null) | **Keep** | The two-level resolution done cleanly (`Entities/BusinessRules/Employee.cs:39-48`). |
| `CanViewEmployee` / `CanEditEmployee` "no departments — allow all" | **Invert** | Fail-opens **#13 and #14**, in `PersonnelService.cs:3151-3152` and `:3177-3178`. A *fifth* copy of the employee filter beyond the four in `TLW-AUTHORIZATION-MODEL.md` §5. |
| Seven per-user permission booleans on `[User]` | **Improve** | Model as group permissions, not user columns — except the two that are a *scope* (§6.2, §11 Q3). |
| `Employees.RoleId` | **Unverified** | Exists and is audited; purpose not established. Do not model until settled. |
| `MentorId`, `Register*`, `Subgroup*`, `Class`, `Year`, `Quarter`, `Period` | **Drop** | Schools vertical. |
| `ParentPay*`, `IsTillOperator`, `EposTillOperatorLevel`, `LunchTicketActive` | **Drop** | EPOS. |
| `Badge`, `IsNoBadge`, `PinNumber`, all Face/Suprema/BioStar columns | **Drop** | Invariant 3. Note `Badge` is a *second identifier distinct from `Code`* — confirm no customer keys on it before migration. |

---

## 9. What this changes in plans already written

Ranked by how much it changes them.

| # | Plan | What this survey found | Effect |
|---|---|---|---|
| **1** | **002 — Clocking aggregate** (`draft`) | Employment is date-effective in legacy and undated in WM (§4.1). A replay over historical clockings **cannot determine who was employed on the day being replayed**. | **Amendment required before 002 is approved.** 002's replay premise assumes it can reconstruct a day; it cannot without a dated employment record. Add an explicit dependency, or accept that replays silently include leavers. |
| **2** | **001 P4/P5 — scope dimensions** (`in-progress`, paused) | `Departments` is hierarchical in legacy (`ParentId`, `:55267`) and flat in WM; `Locations` is flat in legacy and nested in WM (§5.1). Legacy's one tree expansion is on **department**, for absence approvals (`AbsenceRequests.cs:3039-3043`). | **P4/P5 add and expose dimensions without a department parent.** Either add `Department.ParentId` in P4, or record in P4 that WM's department scope is deliberately flat and legacy's is not — the current text implies neither. Does **not** contradict `TLW-AUTHORIZATION-MODEL.md` C9. |
| **3** | **003 P3** (`approved`, in the queue) | D3 — `DepartmentId` has no FK and no site-agreement check. 003 P3 fixes B5, which makes the department field **live in the SPA**. | **Ordering constraint.** D3 must land with or before 003 P3, or B5's fix converts a latent integrity hole into a reachable one. Cheapest as an addition to 003 P3's *Touches*. |
| **4** | **005 — one membership** (`draft`) | Six live per-user permissions sit outside the role model on `dbo.[User]` (§6.2), including the period-lock bypass and a second absence-approval scope. | **Not a contradiction, but a caveat 005 should carry.** 005's premise ("legacy proves one object works") is weaker than stated: legacy leaked four capabilities onto the user row. The right conclusion is unchanged — WM should carry them as group permissions — but 005 should say so rather than leave them undiscovered until Phase 3. |
| **5** | **004 — screen rights** (`draft`) | 24 per-tab **write** permissions (§5.4); `Employees.RoleId` unexplained (§4.4). | 004 currently plans page-level tri-state rights. Once People holds salary and bank details, page-level is not enough. Add field-group granularity to 004's scope, or note explicitly that it is deferred and what that costs. |
| 6 | **006** | Nothing. 006 surveyed no legacy and correctly says so. | None. |

---

## 10. Corrections applied to WM's records

Made directly by this survey. Each was a claim carried forward rather than measured.

| # | Claim | Where | Measured truth |
|---|---|---|---|
| P1 | People/HR marked *"◐ People partly; HR barely"* with no measurement behind it | `COVERAGE-AUDIT.md:53` | **Measured: 11 of 153 `Employees` columns modelled; 33 with no owner anywhere.** "Partly" overstated it. Row corrected and pointed here. |
| P2 | `dbo.[User]`'s 30 columns are *"(identity, password, 2FA, lockout)"*, and §15's *"2FA, lockout, password history, email preferences"* | `TLW-AUTHORIZATION-MODEL.md:25`, `:832-833` | **2FA, password history and email preferences are separate tables** (`User2FAHistory`, `UserPasswordHistory`, `UserEmailPreferences` — 3 columns each). `[User]` holds identity, password lifecycle, lockout, SSO codes and **7 permission booleans**. |
| P3 | *"18 tables, 59 columns — that is the entire persisted authorization surface"* | `TLW-AUTHORIZATION-MODEL.md:41` | **Not entire.** Seven permission columns on `dbo.[User]`, six of them live (§6.2). Surface is 19 tables / 66 columns. Note added in place. |
| P4 | Twelve fail-opens in the in-scope surface | `TLW-AUTHORIZATION-MODEL.md` §10 | **Fourteen.** `PersonnelService.CanViewEmployee:3151-3152` and `CanEditEmployee:3177-3178` are a fifth copy of the filter, both `//no departments — allow all`. Added to the table. |
| P5 | Org structure lists *"Sites · Departments · Locations · Buildings · Cost centres"* as five things, while the same file states sites and locations are one axis | `SCREEN-TREE.md:57` vs `:294-296` | Self-contradictory. Corrected to name the axis once and record which dimensions nest in legacy (`Departments`, `Buildings`, `CostCentres`) and which do not (`Locations`). |
| P6 | §13 has no row for the person record itself — `Personnel / contracts / custom fields / emergency contacts` is one row marked *"◐ partial"* | `ARCHITECTURE.md:411` | Split and re-marked against the measurement; rows added for HR records, bank/salary, employment lifecycle and the audit trail, so a `✅`/`◐` can be checked rather than assumed. |
| P7 | Realtime/punch rows do not mention employment status | `ARCHITECTURE.md` §13 | Row added for D1 — punch accepts terminated employees. |

### Proposed, not applied (design sections)

`ARCHITECTURE.md` §4A is design and off-limits to this survey. One factual correction is owed:

```diff
-- A **User** (login, credentials, roles) optionally carries an **`EmployeeId`** linking it to one **Employee** (people record). WM's `Identity.User` already has this field. (TLW: `User.EmployeeId`, `UserFromEmployeeIdProvider`, `GetUserByEmployeeId`, `UpdateUserNameFromEmployee`.)
+- A **User** (login, credentials, roles) optionally carries an **`EmployeeId`** linking it to one **Employee** (people record). WM's `Identity.User` already has this field. **TLW stores this link the other way round** — `dbo.Employees.UserId`; `dbo.[User]` has no `EmployeeId` column (`AuthorizationService.GetUserByEmployeeId:788-800` reads `SELECT TOP 1 UserId FROM dbo.Employees WHERE Id = @employeeId`). WM's direction is deliberate and better: Identity owns the link, so People needs no reference to Identity (invariant 1). (TLW: `UserFromEmployeeIdProvider.cs:16-19`, `GetUserByEmployeeId`, `UpdateUserNameFromEmployee`.)
```

Also proposed for §8's People bullet (`ARCHITECTURE.md:302`), which lists the planned expansion but
omits the four largest missing groups: **bank details, employment lifecycle dates, HR identity
(DOB/gender/nationality/NI), and personal vs work contact separation.**

---

## 11. Open questions for the user

1. **Is a dated employment record in scope before plan 002?** Replacing `EmployeeStatus` with
   `EnterDate` + `LeaveDate` + an administrative `IsActive` is a small migration now and a
   data-correction exercise later. 002's replay needs it. *(My recommendation: yes, and it is P1 of
   the plan this survey produced.)*
2. **Employee code: which uniqueness rule?** Legacy is case-insensitive **and**
   leading-zero-insensitive (`right('0000000000' + code, 10)`). WM's comment claims
   case-insensitive and its index enforces neither. Case-insensitive alone is the sane choice, but
   if any customer's codes rely on zero-padding equivalence, migration will collide. *Needs a
   product answer, not a technical one.*
3. **`IsUserCanManageAllRequests` / `…ByDepartment` — permission or scope?** They read as
   permissions but behave as a *second data scope* for absence approvals, and they are the one place
   legacy expands the department tree. Under option A a group already carries a scope; do absence
   approvals reuse it, or is approval scope genuinely independent of visibility scope? This has to
   be settled before the Absence phase, and it interacts with 005.
4. **Should `Department` nest?** Legacy nests departments and not locations; WM does the reverse.
   Adding `Department.ParentId` is cheap now and touches 001 P4. Leaving it flat is defensible if
   said out loud.
5. **Does any customer key on `Badge` separately from `Code`?** Both are unique, both are searchable
   (`PersonnelFilterType.Badge`, `PersonnelEnums.cs:17`), and dropping devices drops the reason
   `Badge` existed — but not necessarily the data. *Unverified from the code; only a customer can
   answer.*

---

## 12. One-line notes for future surveys (outside this scope)

- `dbo.EmployeeGroups(8 cols)` + `dbo.EmployeesInGroups` is the **population-group** mechanism
  ARCHITECTURE §8 names; `RegistrationPopulationType` and `IsDinerDefault` on it are schools/EPOS
  leakage into a general-purpose table.
- `dbo.ImportedEmployees` (8 cols, `:15800`) is a staging table with French-legacy column names
  (`Matricule`, `CodeAnalyze`) — evidence the import path predates the current schema.
- `dbo.Clients` (29) / `dbo.ClientSites` (13) / `dbo.ClientContacts` (10) are a **customer** entity
  nobody has bucketed; they hang off `WorkActivityClients` (job costing), not off People.
