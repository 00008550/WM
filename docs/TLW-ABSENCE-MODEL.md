# TLW — the absence & accrual model, measured

*Surveyed 2026-08-30 against `E:\Tlw` only. Every claim below carries a `file:line`. Companion to
[`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md) (absence lives **on** the clocking) and
[`TLW-PEOPLE-MODEL.md`](./TLW-PEOPLE-MODEL.md) (the employment dates entitlement pro-rates over).
Plan: [`plans/015-absence-and-entitlement.md`](./plans/015-absence-and-entitlement.md).*

> This is the **survey** [`COVERAGE-AUDIT.md`](./COVERAGE-AUDIT.md) §2a said had not happened.
> The 2026-08-06 four-question dependency check is superseded by it.

---

## 0. Headline findings, in the order they matter

1. **The `OnLeave` deletion (plan 007 P1) was correct.** Independently re-tested. There is no
   temporary non-availability state anywhere on `dbo.Employees`. See §1.
2. **Legacy has *two* independent entitlement-balance engines, live at the same time**, chosen
   per (employee, absence) at request time by `IsAccrualBalanceEnabled`
   (`Logic/Planning/AbsenceRequests.cs:466`). This is the answer to `ARCHITECTURE.md` §14 open
   question 9 — *"some customers reportedly use an older absence variant"*. See §4.
3. **Booking fails open, by design and with a comment saying so.** If neither engine returns a
   balance row, the request is **Valid** (`AbsenceRequests.cs:433-436`). Two more fail-opens in
   §7.
4. **Absence lives on the daily clocking aggregate**, as two nullable FK columns plus three
   durations. Plan 002 cannot model a Clocking without them. See §5.
5. **Six of `dbo.Absence`'s 35 columns are write-only** — editable in the UI and the public API,
   read by nothing. See §2.
6. **WM has zero absence code and is missing an input the engine needs**:
   `ContinuousServiceDate` appears nowhere in `src/` (`grep -rn ContinuousServiceDate src/` → no
   matches) yet it is the *primary* length-of-service input
   (`EmployeeAccrualCalculationsService.cs:488`).

---

## 1. `OnLeave`: re-tested independently, and the deletion stands

All 153 `dbo.Employees` columns were re-extracted mechanically from
`Logic/Entities/HorioDB.designer.cs:28594+` (script in §9) and read as a flat list. The
absence-adjacent columns are:

| Column | What it is | Temporary state? |
|---|---|---|
| `IsActive` | employed / not | no — permanent |
| `DischargeDate`, `FinalEmploymentDate`, `ResignationDate` | leaving dates | no — permanent |
| `LeaveReasonId` → `dbo.LeaveReasons` (3 cols) | *leaver* reason label | no — a leaver, not a person on leave |
| `LeaveNoticePeriodId` → `dbo.LeaveNoticePeriods` (2 cols) | notice period label | no |
| `HolidaysAmount`, `HolidaysPeriod`, `HolidayGroupId` | entitlement defaults | configuration, not state |
| `AbsenceGroupId` | which absence types this person may book | configuration |
| `IsAbsenceManager`, `IsDeputyAbsenceManager` | approver flags | role, not state |

**There is no `IsOnLeave`, no `AbsenceUntil`, no status enum with a leave member.** Legacy answers
"is this person absent today?" by reading the *day*: `Clockings.MorningAbsenceID` /
`AfternoonAbsenceID` for that date (used exactly that way at
`Logic/Planning/AbsenceRequests.cs:3056-3058`, to decide whether a deputy approver is active).

**Verdict: plan 007 P1 dropped nothing legacy has.** "On leave" is a derived, dated fact of a
specific day, not a lifecycle state of a person. WM's model is the better one and the shipped
migration is safe.

---

## 2. `dbo.Absence` — all 35 columns classified

`HorioDB.designer.cs:8954`. Enum definitions: `Logic/Settings/AbsenceEnums.cs`.

| # | Column | Meaning / evidence | Class |
|---:|---|---|---|
| 1 | `Id` | PK | Keep |
| 2 | `Code` | customer-facing code; `GetAbsenceAvailableCode` proc (`1_horio_structure.sql:750`) auto-suggests the next free one | Keep |
| 3 | `Name` | display name | Keep |
| 4 | `ShortName` | grid/board abbreviation (`PlanningEnums.cs:24` `BlocksDisplayMode.ShortName`) | Keep |
| 5 | `Color` | board colour | Keep |
| 6 | `IsActive` | soft retire; filtered in `AbsenceService.cs:245` | Keep |
| 7 | `Category` | `Uncategorized \| Holiday \| Sick \| MaternityPaternity \| OtherEvent` (`AbsenceEnums.cs:29-36`) | Keep |
| 8 | `Unit` | `Day \| Hour` (`AbsenceEnums.cs:23-27`) | Keep |
| 9 | `CompensationMethod` | how the absence contributes to net hours — see §3 | Keep |
| 10 | `Package` | the fixed value for "Pay Fixed Hours" (`AbsenceDurationCalculator.cs:132,188`) | Keep |
| 11 | `TheoreticIsPackage` | expected-hours subtraction uses `Package/2` per half day (`DifferenceCalculator.cs:27-37`) | Improve — fold into `CompensationMethod` |
| 12 | `TheoreticIsNull` | zeroes the day's expected duration (`DurationTheoreticCalculator.cs:24,32`; `DifferenceCalculator.cs:43,50`) | Keep |
| 13 | `WorkDone` | absence counts as time worked (`AbsenceDurationCalculator.cs:18,29,39`) | Keep |
| 14 | `CounterId` | which counter the absence's hours land in | Keep |
| 15 | `AllowOnDayOff` | may be booked on a day-off template — see §6 | Keep |
| 16 | `AllowOnHoliday` | may be booked on a public holiday | Keep |
| 17 | `IncludeToBradfordCalculation` | feeds the Bradford Factor report (`ClockingReportsService.cs:177-178`) | Keep |
| 18 | `BlockAbsenceRequestOnNegativeBalance` | the *only* thing that makes a balance binding (`AbsenceRequests.cs:440-444`) | **Invert** — see §7 |
| 19 | `BlockAbsenceRequestBeforeDate` | cutoff date for requests (`Extensions/AbsenceExtensions.cs:14-15`) | Keep |
| 20 | `IsApprovalRequiredForBookingAbsence` | per-type approval toggle | Keep |
| 21 | `IsApprovalRequiredForCancellingAbsenceRequest` | per-type cancel-approval toggle | Keep |
| 22 | `IncludeInWeeklyDigestEmail` | `Notifications/WeeklyAbsenceDigestService.cs:91` | Keep (Notifications) |
| 23 | `IsTopList` | pin to the top of the kiosk picker (`KioskExternalAccessService.cs:966`) | Improve — a display `SortOrder` |
| 24 | `ExportCode` | payroll export code | Keep |
| 25 | `ExportCode2` | second export code, per-plugin | Improve — plugin-scoped mapping, not two columns |
| 26 | `IsExportable` | Arno HR export flag (`Models/ArnoHRExportModel.cs:14-15`) | Improve — plugin-scoped |
| 27 | `IsExcludedFromPayrollExport` | `Settings/SageExportService.cs`, `CascadePayrollController.cs` | Improve — plugin-scoped |
| 28 | `IsExcludedFromAutomaticExport` | `HorioExportService/cascade/cascadeExport.cs:97` | Improve — plugin-scoped |
| 29 | `ContractId` | absence defined *for one contract*; written at `AbsenceController.cs:342,401`, read only to display a name at `:519` | **Drop** — never filters anything |
| 30 | `Deviation` | round-tripped `AbsenceController.cs:347,406,457`; read by no calculator | **Drop** — write-only |
| 31 | `IncludeHours` (`IncludePacket`) | mapped, localised, never read | **Drop** — write-only |
| 32 | `IncludeEcart` | `AbsenceController.cs:349,408,459` only | **Drop** — write-only |
| 33 | `WorkProduct` | `AbsenceController.cs:351,410,461` only | **Drop** — write-only |
| 34 | `Repeatability` | `AbsenceController.cs:356,416,467` only | **Drop** — write-only |
| 35 | `LunchTicketActive` | one occurrence in the whole solution: the LINQ-to-SQL mapping | **Drop** — dead |

**Six write-only columns (30–35) plus `ContractId`.** All seven are settable through
`WebSite/API/Models/DTOs/ManipulateAbsenceDTO.cs` — the *public API* — and validated by
`AbsenceFluentValidator.cs`. Customers can configure them and nothing happens. WM must not
reproduce them.

---

## 3. `CompensationMethod` — the rule that decides what an absent day is worth

`AbsenceEnums.cs:3-13` defines **eight** values. Only **four** reach any calculator
(`AbsenceDurationCalculator.cs`, `DifferenceCalculator.cs`, `DurationTheoreticCalculator.cs`):

| Enum value | UI name (`Documentation/Absences Configuration.md:13`) | Live? |
|---|---|---|
| `None` | None | yes |
| `BaseTheoreticPlanning` | Daily Expected Hours | yes |
| `BasePackage` / `ComplementPackage` | Pay Fixed Hours (+ `Package`) | yes |
| `CalculateHHmm` | Calculate by HH:MM | yes |
| `ComplementTheoreticPlanning`, `BaseWeekly`, `ComplementWeekly` | — | **no UI, no reader — Drop** |

`Documentation/Absences Configuration.md` supplies **eleven worked examples**; they are lifted
verbatim into plan 015's *Edge cases* as the acceptance tests for this rule.

---

## 4. Two balance engines, live simultaneously

This is the biggest structural finding, and it resolves `ARCHITECTURE.md` §14 open question 9.

```
AbsenceRequests.GetAbsenceRequestBalance (AbsenceRequests.cs:458-491)
  └─ IsAccrualBalanceEnabled(absenceId, employeeId)      AccrualAbsenceBalanceService.cs:55-71
       │   (does a row exist in AccrualEmployeeAllocationView for this employee
       │    joined to an Accrual whose AbsenceId matches?)
       ├─ true  → ACCRUAL engine (C#)   AccrualAbsenceBalanceService.cs:152-249
       └─ false → RECAP engine (T-SQL)  dbo.GetEmployeeAbsenceBalanceWithPeriod
                                        (83.V5.29.0.0.sql:380 — latest redefinition)
```

### 4a. The RECAP engine — the "legacy absences" variant

Tables: `dbo.RecapPeriods` (9), `dbo.RecapPeriodEmployees` (3), `dbo.RecapAbsences` (12),
`dbo.PeriodAbsences` (3). Screens: `WebSite/Controllers/RecapsController/AbsencesController.cs`,
`WebSite/Views/Absences/{Periods,AddEditPeriod,PrintPeriod}.cshtml`.

Balance is **hand-entered, not computed**:
`Quota + PreviousPeriodBalance + Adjustment` (`83.V5.29.0.0.sql:456`), each with its own free-text
note column (`QuotaNote`, `PreviousPeriodBalanceNote`, `AdjustmentNote`). `RecapPeriods.Locked`
freezes a period. `PeriodAbsences` carries a `UserId`, so *which absence types a period shows* is
per-user.

There is **no carry-over automation** — `PreviousPeriodBalance` is a number someone types.

### 4b. The ACCRUAL engine — the modern one

Rule table `dbo.Accruals` (21 cols, `:186223`) + `dbo.AccrualLengthOfServiceBonuses` (8) +
five-way allocation + `dbo.EmployeeAccrualCalculations` (15, the stored result) +
`dbo.EmployeeAccrualCalculationChanges` (6, the ledger) + `dbo.AccrualAdjustments` (7) +
`dbo.AccrualsCalculationQueue` (5).

**`dbo.Accruals`, column by column** — enums in `Core/Enumeration/Enums.cs:3017-3097`:

| Column | Meaning |
|---|---|
| `AbsenceId` | the absence type this entitlement funds — **one accrual per absence type** |
| `ContractType` | `AccrualContractType { FixedHours = 1 }` — a one-member enum (`:3017`) |
| `Unit` | `Days \| Hours` (`:3022`) |
| `Amount` | the amount granted **per recurrence period** |
| `AnnualEntitlement` | display-only headline figure; the engine never reads it |
| `RecurrencePatternType` | `Weekly \| Monthly \| Quarterly \| Yearly` (`:3052`) |
| `RecurrencePatternThreshold1` | overloaded: day-of-week (weekly), `MonthStart\|MonthEnd` (`:3067`), `QuarterStart\|QuarterEnd` (`:3074`) |
| `RecurrenceRangeType` | `EndBy \| EndAfter \| NoEndDate` (`:3060`) |
| `RecurrenceRangeStartDate` / `EndDate` / `Threshold1` | the range; `EndBy` with a null end date **aborts the whole calculation** (`EmployeeAccrualCalculationsService.cs:168-174`) |
| `MaximumCarryover` | applied **annually**, after rounding (`:781-785`) |
| `BalanceRoundingRule`, `CarryoverRoundingRule` | `AccrualRoundingRule { None, UpHalfDay, DownHalfDay, UpWholeDay }` (`:3028`) |
| `AccrualLengthOfServiceBonusType` | `Default \| DeferredToNextCycle` (`:3080`) |
| `IsLengthOfServiceBonusCarriedover` | if off, the bonus is stripped from carry-over and clamped at 0 (`:768-777`) |
| `IsFutureBalanceAllowed` | only honoured for `Yearly` (`Entities/BusinessRules/Accrual.cs:16`) |
| `Id`, `Name`, `IsActive`, `UpdatedBy` | plumbing |

**How entitlement is actually computed** (`EmployeeAccrualCalculationsService.cs:392-473`) — an
**event ledger**, not a stored number. Every fact becomes an `EmployeeAccrualCalculationChange`
with an `AccrualChangeAction` (`Enums.cs:3086`):

| Action | Source | `BalanceChange` |
|---|---|---|
| `Accrued` | `Amount × ratio`, per period (`:935-1001`) | + |
| `LengthOfServiceBonus` | `AccrualLengthOfServiceBonus.ShouldApplyBonus` | + |
| `Carryover` | previous period's `RawBalance`, capped (`:758-794`) | + |
| `Adjustment` | a row in `AccrualAdjustments` | ± |
| `Taken` | a clocking with this absence, `Date <= today` (`:416-421`) | − |
| `Scheduled` | same, `Date > today` (`:423-428`) | − |
| `Requested` | a pending `AbsenceRequest` (`:430-434`) | − |
| `EffectiveDateOfTerminationCorrection` | claws back over-granted entitlement when the leave date falls mid-period (`:969-984`) | − |

`RawBalance = Σ BalanceChange`; `RoundedBalance` applies `BalanceRoundingRule` (`:679-683`). The
seven `Carryover/Accrued/Adjustments/Requested/Taken/Scheduled/CumulativeLengthOfServiceBonus`
columns on `EmployeeAccrualCalculations` are **stored aggregates of the ledger** (`:644-689`) — the
same `calc_*` denormalisation the Clocking has, and the same reason recalculation is destructive.

**Pro-rating.** `GetActualToFullPeriodRatio` (`:722-742`) intersects the recurrence period with
`DateTimeInterval(EnterDate, FinalEmploymentDate ?? DischargeDate ?? MaxValue)` and takes the
day-count ratio. Note the **two-date precedence**: `FinalEmploymentDate` wins over `DischargeDate`.
When the accrual grants **at period start** (`ShouldBeAccruedAtTheEndOfPeriod == false`), the grant
uses the *entry-only* ratio (`GetEntryOnlyRatio`, `:1004-1020` — ignores the leave date) and a
separate correction change is emitted on the leave date. When it grants at period **end**, the full
intersection ratio is used directly.

**Length-of-service bonus** (`Entities/BusinessRules/AccrualLengthOfServiceBonus.cs`) — measured
from `ContinuousServiceDate ?? EnterDate` (`EmployeeAccrualCalculationsService.cs:488`), evaluated
on **two** anniversary dates (the accrual's `RecurrenceRangeStartDate` *and* the employee's service
date, `:495`), banded `GreaterThan | LessThan | Between` (`Enums.cs:3044`). On the employee's own
anniversary the bonus lands on **anniversary + 1 day** (`:509`) under `Default`, and on the
anniversary itself under `DeferredToNextCycle`. Legacy ships unit tests for this:
`Source/Logic.Tests/Entities/BusinessRules/AccrualLengthOfServiceBonusTests.cs`.

**What invalidates it.** `Employees_Update_Trigger` (`Database/Versioning/87.V5.33.0.0.sql:768-806`
— **verified as the latest of seven redefinitions**; 88–95 do not touch it) `DELETE`s the
employee's entire `EmployeeAccrualCalculations` history and inserts a JSON message into
`AccrualsCalculationQueue` whenever any of **seven** columns changes: `DepartmentId`,
`EmployeeLocationId`, `EmploymentTypeId`, `DischargeDate`, `EnterDate`, `ContinuousServiceDate`,
`FinalEmploymentDate`. The requeue is always `fromDate = EnterDate`, i.e. **a full replay from
hire**.

**WM raises no such event.** 007 P1 writes `EmployedFrom`/`EmployedUntil` with no notification of
any kind (`ARCHITECTURE.md:422` records this as *not started*).

### 4c. Five-way allocation, and its ambiguity bug

`dbo.AccrualEmployeeAllocationView` (`74.V5.20.0.0.sql:49-65`) is a **`UNION ALL`** over four base
tables: `AccrualsEmployees` (0), `AccrualsDepartments` (1), `AccrualsLocations` (2),
`AccrualsEmploymentTypes` (3). The `EmployeeAccrualAllocation` discriminator is selected but
**never used to prioritise anything**.

Consequences, both real:

- `GetAccrualIdForEmployeeAbsence` (`AccrualsCalculationRepository.cs:87-106`) is
  `SELECT … FirstOrDefault()` with **no `ORDER BY`**. An employee allocated to two accruals for the
  same absence — trivially: by employee *and* by department — gets an **arbitrary** one.
- The view joins `Employees` with **no `IsActive` / `DischargeDate` filter**, so leavers stay
  allocated and keep being recalculated.

---

## 5. The clocking seam — what `MorningAbsenceID` / `AfternoonAbsenceID` mean

Absence is recorded **on the daily aggregate**, not as its own row. On `dbo.Clockings`:
`MorningAbsenceID`, `AfternoonAbsenceID`, `AbsenceDuration`, `MorningAbsenceDuration`,
`AfternoonAbsenceDuration`.

- A **half day** = one column set. A **whole day** = both set to the same id
  (`AbsenceRequests.cs:1160`). Two *different* half-day absences on one day are representable.
- `CountFullDayAbsenceAsHalfDay` lives on the **daily template**, not the absence, and makes a
  both-columns-set day count 0.5 instead of 1.0 (`EmployeeAccrualCalculationsService.cs:840-844`).
- Durations are **denormalised three ways**: `AbsenceDuration` (whole-day total) plus the two
  halves. `GetAbsenceDurationInHours` (`:866-912`) has an explicit *"compatability with old logic —
  new column added but not re-calculated yet"* branch that reconstructs a half from
  `AbsenceDuration − GetDurationAfternoon()`. Three columns that must agree, and legacy knows they
  sometimes do not.
- `dbo.HistoricalClockingAbsences` (16 cols, `:190592`) archives the absence face of a clocking —
  `ClockingId, Date, DailyModelId, DurationTheoretic/Morning/Afternoon, Morning/AfternoonAbsenceId,
  AbsenceDuration + halves, CountFullDayAbsenceAsHalfDay, IsHoliday, IsDayOff` — i.e. everything the
  accrual engine needs, frozen. It exists because recalculation is destructive.

**Six writers of these columns**, all direct UPDATEs:

| Writer | Path |
|---|---|
| Absence-request approval | `Planning/AbsenceRequests.cs:1129-1194` (`SetAbsence`) |
| Auto-planning | `Planning/Auto.cs:109,133` |
| Planning board (manual) | `Planning/PlanningService.cs:243-252,323-326` |
| Weekly shift assignment | `Planning/SharedPlanningServiceMethods.cs:288-289` — `isnull(MorningAbsenceID, wsd.FirstAbsenceId)`: the schedule can *seed* an absence, and never overwrites an existing one |
| Bulk import | `ImportService/AbsencesImportService.cs:195-248` |
| Recalculation | `HoursCalculation/ProxyClocking.cs:710-712` |
| External API | `ExternalAccess/TLW.cs:1321-1328` |

Approval also triggers same-day recalculation inline, per clocking, only for the past:
`if (clocking.Date.Date <= DateTime.Now.Date) service.CalculateEmployee(...)`
(`AbsenceRequests.cs:1190-1193`).

---

## 6. The approval flow

**Request** — `dbo.AbsenceRequests` (14 cols, `:46630`). Status enum
(`SharedLogic/Enums/AbsenceRequestStatus.cs`): `Pending, ApprovedByManager, RejectedByManager,
CancelledByEmployee, CancellationRequested, CancellationRejected, CancellationApproved,
ApprovedByAll`. Period enum: `Morning=1, Afternoon=2, WholeDay=3`
(`SharedLogic/Enums/AbsenceRequestPeriod.cs`) — the Android kiosk adds a fourth, `PartialDay=4`
(`TabletKiosk/.../Enums.cs:114`), which the server enum does not have.

**Validation order** (`AbsenceRequests.cs:397-447`), and each gate:

1. `ValidateBlockedAbsencesByDepartment` → `DepartmentLimitsReached` or `BlockedDepartmentDates`.
   `BlockedAbsencesByDepartment (DepartmentId, AbsenceId, EmployeeNo, AbsencePeriod)` caps how many
   people in a department may be off at once, `Daily` or `Weekly`
   (`BlockAbsencePeriod`, `Enums.cs:1761`); the week is aligned to the *global*
   `Calculation.WeekStartsOn` (`AccrualAbsenceBalanceService.cs:89-92`).
   `BlockedAbsenceDatesByDepartment (DepartmentId, StartDate, EndDate)` is a blackout window.
2. `CheckAbsenceDateClockingsPresent` (`:449-456`) → `NoClockingsOnAbsenceDates` unless a
   `Clockings` row exists for **every** day in the range. **Absence booking depends on clocking
   pre-generation** — a hard dependency on plan 002.
3. Balance (§4), then `BlockAbsenceRequestOnNegativeBalance`.

Blocked also by daily template: `BlockedAbsencesByDailyModel (AbsenceId, DailyModelId)`, applied at
`AbsenceRequests.cs:1110-1120` and again inside the accrual engine at `:810-814`.

**Approver routing** — `AbsenceRequestDetails` (10 cols) is one row per approval step.
`CreateAbsenceRequestDetails` (`:3005-3032`) resolves the approver:

```
EmployeeAbsenceManagers (EmployeeId, AbsenceManagerId, DeputyAbsenceManagerId, Order)   ← first
  ↓ if none
DepartmentAbsenceManagers (DepartmentId, AbsenceManagerId, DeputyAbsenceManagerId, Order)
  ↓ if none
no AbsenceRequestDetail row is created at all
```

`Order` is the escalation chain; `AbsenceRequests.ManagerPosition` tracks the current step and
`LastChangedManager` the last actor. `AbsenceRequestsLogs` (26 cols) is the audit copy and betrays
the history: it still has **fixed `Manager1*`/`Manager2*` slot columns** (eight of them) alongside
the modern unlimited-chain design — a numbered-slot ceiling that was outgrown but never removed
("*According to task 0003881 there could be unlimited number of managers*",
`AbsenceRequests.cs:1222`).

**Deputy activation is derived from the primary approver's own clocking**
(`AbsenceRequests.cs:3054-3060`): a deputy sees requests only when the primary manager has an
absence recorded on **today's** clocking, or when `AbsenceManagerId IS NULL`. Elegant, and
completely undocumented.

**Department-hierarchy expansion** (`AbsenceRequests.cs:3039-3043`) — the *only* place in the whole
legacy product that walks the department tree, via `dbo.DepartmentHeirarchyView` *(legacy's
spelling)*. It maps a manager's own `DepartmentId` to every subdepartment. Relevant to plans 001 P4
and 005; already flagged at `SCREEN-TREE.md:334-335`.

**Administrator is `employeeId < 0`** (`AbsenceRequests.cs:1216-1218`) — a magic negative id, not a
role check.

**Which absence types a person may book** — `Employees.AbsenceGroupId` →
`AbsenceGroups` → `AbsenceGroupContent`, with a *second, parallel* list for manual timesheets,
`AbsenceGroupContentManualTimesheets` (`Settings/AbsenceService.cs:204-236`).

**Documents.** `AbsenceRequestEmployeeDocuments (AbsenceRequestId, EmployeeDocumentId)`; on
approval they are copied onto every affected clocking as `ClockingEmployeeDocuments`
(`AbsenceRequests.cs:1174-1187`).

---

## 7. Fail-opens, ceilings and defects

Every one of these is in currently-shipping legacy code.

| # | Where | Behaviour | Class |
|---|---|---|---|
| F1 | `AbsenceRequests.cs:433-436` | *"if no period set, then no balance = valid"* — **no balance data ⇒ approve**. Applies to both engines: no recap period, or no accrual calculation covering the dates. | **Invert** |
| F2 | `Settings/AbsenceService.cs:204-220` | employee has **no** `AbsenceGroupId` ⇒ **every** active absence type is bookable. Identical shape to plan 001's three scope fail-opens. | **Invert** |
| F3 | `83.V5.29.0.0.sql:445-457, 530-533` | recap engine: `@periodsCount = 0 ⇒ RETURN` an empty table ⇒ F1 fires. | **Invert** |
| F4 | `AccrualEmployeeAllocationView` (`74.V5.20.0.0.sql:49-65`) | joins `Employees` with no `IsActive`/`DischargeDate` filter — leavers keep accruing. | **Invert** |
| F5 | `AccrualsCalculationRepository.cs:87-106` | two accruals for one (employee, absence) ⇒ **arbitrary** winner, no `ORDER BY`. | **Invert** |
| F6 | `Planning/AbsenceRequests.cs` (whole file) | **no employment check anywhere** — no `DischargeDate`, `IsActive` or `EnterDate`. `PersonnelService.SetEmployeesLeaver` does not touch absence requests. A leaver keeps approved absence past their leave date. *(Carried forward from `COVERAGE-AUDIT.md` §2a and re-confirmed.)* | **Invert** |
| C1 | `AbsenceRequestsLogs` `Manager1*`/`Manager2*` | fixed two-approver slots surviving beside the unlimited chain. | **Improve** |
| C2 | `Clockings` morning/afternoon | **exactly two** absence slots per day; a third distinct absence in one day is unrepresentable. | Keep (the halves are the domain), **Improve** the duration triplication |
| C3 | `EmployeeAccrualCalculations` seven aggregate columns | stored sums of the ledger; only correct after a destructive full recalculation. | **Improve** — project from the ledger |
| C4 | `DailyModel.Code == '98'` (`Constants.SYSTEM_SANS_DAILY_MODEL_CODE`, `AbsenceRequests.cs:1075,1084`; `83.V5.29.0.0.sql:519`) | "is a day off" is a **magic string code** compared in both C# and T-SQL. | **Improve** — a flag |

---

## 8. All 43 tables, classified. 0 unclassified, 0 duplicated.

**Boundary correction.** `COVERAGE-AUDIT.md:57` records *"44 tables / 526 columns"* and §2a itself
warns the figure was unverified. Measured: the absence-and-entitlement family has **43 base tables
carrying 331 column mappings** (324 distinct columns — `dbo.PredefinedAbsences` is the schema's
one double-mapped table, 7 columns counted twice, `HorioDB.designer.cs:194689`). The gap to 526 is
**report views**, which belong to the Reporting bucket: `UnifiedEmployeeAbsencesReportView` (74),
`UnifiedSummaryAbsenceTrackingReportView` (54), `EmployeeAccrualCalculationsView` (27),
`UnifiedAccrualCalculationReportView` (19), `AbsenceRequestsForManagerView` (18),
`ClockingsForAccrualCalculationView` (16), `AccrualAdjustmentsView` (16),
`EmployeeAccrualAbsenceBalanceView` (14), `EmployeeAbsenceApiView` (9),
`AbsenceRequestAccrualBalanceView` (6), `AccrualEmployeeAllocationView` (3),
`EmployeeHolidayDatesView` (3), `AbsenceRequestsLogsView` (4).

| Table | Cols | Role | Class |
|---|---:|---|---|
| `dbo.Absence` | 35 | the rule-carrying type — §2 | Keep (35 → **~22**) |
| `dbo.AbsenceRequestsLogs` | 26 | audit copy with `Manager1/2` slots | **Improve** → generic audit event |
| `dbo.Accruals` | 21 | the entitlement rule — §4b | Keep |
| `dbo.StudentRegistrationAbsenceAlertSettings` | 18 | schools vertical (AM/PM + lesson alerts) | **Drop** — schools out of scope |
| `dbo.HistoricalClockingAbsences` | 16 | frozen absence face of a clocking | **Improve** → the event log makes it redundant |
| `dbo.EmployeeAccrualCalculations` | 15 | stored per-cycle balance + 7 aggregates | **Improve** → projection over the ledger |
| `dbo.Holidays` | 14 | public holidays, per `HolidayGroupId`, with `CounterId` + `ShouldAllocateGrossPresenceToCounter` | Keep |
| `dbo.AbsenceRequests` | 14 | the request — §6 | Keep |
| `dbo.PredefinedAbsences` | 14 (7×2) | schools: staff pre-marks a student absent (`StudentRegistration/PredefinedAbsences/`) | **Drop** — schools |
| `dbo.RecapAbsences` | 12 | hand-entered quota/balance/adjustment + 3 note columns | **Drop** — superseded by accruals; migrate data |
| `dbo.FlexiBalances` | 11 | flexi balance definition incl. a **`SqlStatement` escape hatch** | Keep — but owned by **Rules**, not Absence |
| `dbo.AbsenceRequestDetails` | 10 | one approval step | Keep |
| `dbo.RecapPeriods` | 9 | recap period, `Locked`, `Format`, `ShowInAbsence` | **Drop** with 4a |
| `dbo.SalaryEntitlements` | 8 | salary components on `EmployeeSalaryId` — **not absence** | **Drop from this bucket** → HR/payroll |
| `dbo.AccrualLengthOfServiceBonuses` | 8 | banded LoS bonus — §4b | Keep |
| `dbo.AccrualAdjustments` | 7 | manual ± with a note | Keep |
| `dbo.EmployeeAccrualCalculationChanges` | 6 | **the ledger** — the good idea in the whole subsystem | **Keep, and promote** to the source of truth |
| `dbo.AccrualsCalculationQueue` | 5 | JSON-message recalculation queue | **Improve** → RabbitMQ job |
| `dbo.BlockedAbsencesByDepartment` | 5 | headcount cap, daily/weekly | Keep |
| `dbo.FlexiBalanceResetValues` | 5 | per-employee flexi reset | Keep → **Rules** |
| `dbo.EmployeeAbsenceManagers` | 5 | approver + deputy + `Order`, per employee | Keep |
| `dbo.DepartmentAbsenceManagers` | 5 | approver + deputy + `Order`, per department | Keep |
| `dbo.BlockedAbsenceDatesByDepartment` | 4 | blackout window | Keep |
| `dbo.ac_holidays` | 4 | access-control calendar holidays | **Drop** — devices/AC |
| `dbo.EmployeeAdditionalFlexiBalanceCalculations` | 4 | per-clocking flexi result | Keep → **Rules** |
| `dbo.AbsenceGroupContent` | 3 | which types a group may book | **Improve** → one join with a purpose column |
| `dbo.AbsenceGroupContentManualTimesheets` | 3 | the same list, for manual timesheets | **Improve** — merge into the above |
| `dbo.AbsenceManagers` | 3 | flat manager list, `IsActive` | **Drop** — duplicates `Employees.IsAbsenceManager` |
| `dbo.AccrualsEmploymentTypes` | 3 | allocation dimension 3 | **Improve** → one polymorphic assignment |
| `dbo.AccrualsEmployees` | 3 | allocation dimension 0 | **Improve** — as above |
| `dbo.AccrualsDepartments` | 3 | allocation dimension 1 | **Improve** — as above |
| `dbo.AccrualsLocations` | 3 | allocation dimension 2 | **Improve** — as above |
| `dbo.EmployeeAdditionalFlexiBalances` | 3 | extra flexi balances per employee | Keep → **Rules** |
| `dbo.RecapPeriodEmployees` | 3 | who is in a recap period | **Drop** with 4a |
| `dbo.SalaryEntitlementTypes` | 3 | lookup for `SalaryEntitlements` | **Drop from this bucket** → HR/payroll |
| `dbo.BlockedAbsencesByDailyModel` | 3 | type blocked on a template | Keep |
| `dbo.PeriodAbsences` | 3 | per-**user** visibility of types in a recap period | **Drop** with 4a |
| `dbo.LeaveReasons` | 3 | *leaver* reason label | **Drop from this bucket** — People owns it (007 P1 `LeavingReasons`) |
| `dbo.AbsenceRequestEmployeeDocuments` | 3 | request ↔ document | Keep |
| `dbo.HolidayGroups` | 2 | holiday calendar name | Keep |
| `dbo.LeaveNoticePeriods` | 2 | notice-period label | **Drop from this bucket** — People owns it (007 P1) |
| `dbo.AbsenceGroups` | 2 | group name | Keep |
| `dbo.AutoBalanceResetEmployees` | 2 | opt-in to automatic flexi reset | Keep → **Rules** |

**Totals: 43 tables, 331 mappings.** Keep 18 · Improve 12 · Drop 13 (`ac_holidays`,
`StudentRegistrationAbsenceAlertSettings`, `PredefinedAbsences`, `AbsenceManagers`, the four recap
tables, `SalaryEntitlements(+Types)`, `LeaveReasons`, `LeaveNoticePeriods` — the last four
*reassigned*, not deleted).

**Six tables here belong to other modules** and should leave the Absence bucket:
the five flexi tables + `AutoBalanceResetEmployees` → **Rules** (37 columns);
`SalaryEntitlements(+Types)` → HR/payroll (11); `LeaveReasons`, `LeaveNoticePeriods` → People (5).
The absence-and-entitlement core is therefore **34 tables / 278 columns**.

---

## 9. Reproducing the measurement

```powershell
$f='E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs'; $lines=Get-Content $f -Encoding utf8
$cur='(none)'; $out=@(); $i=0
foreach ($l in $lines) {
  $i++
  if     ($l -match 'TableAttribute\(Name="([^"]+)"') { $cur=$matches[1] }
  elseif ($l -match 'ColumnAttribute\(') {
    $n=$null
    if ($l -match 'Name="([^"]+)"') { $n=$matches[1] }
    elseif ($l -match 'Storage="_([A-Za-z0-9_]+)"') { $n=$matches[1] }
    $out += "$cur`t$n`t$i"
  }
}
$out | Out-File -Encoding utf8 schema.tsv     # 8173 rows / 579 tables — matches COVERAGE-AUDIT §1
```

Completeness was asserted by set difference, not by eye: the 43 names in §8 were fed back as a
filter over `schema.tsv`; the script reported `TABLES: 43`, `COLUMNS: 331`, `MISSING: (none)`, and
every §8 row is a distinct name (no table appears twice).

---

## 10. What this survey did **not** measure

Numbered so the next reader can close them.

1. **The report views** (13 views / ~263 columns listed in §8). Bucketed out, not read. The two
   `Unified*ReportView`s at 74 and 54 columns are large enough to hide product.
2. **`dbo.Holidays`' calculation behaviour.** The 14 columns are listed; `IsForTeoreticAccounts`,
   `AddTheoreticalIfThereAreSwipes`, `ShouldAllocateGrossPresenceToCounter` /
   `AllocateGrossPresenceToCounterId` and `DontCalculateInactive` were **not** traced into the
   calculators. `HolidayService.HasHoliday` was seen called (`AbsenceRequests.cs:1094`), not read.
3. **School holidays** (`Views/SchoolHolidays`) — assumed schools-vertical, not confirmed.
4. **The flexi-balance subsystem** (5 tables / 26 cols) including its `SqlStatement` escape hatch.
   It is Rules' territory and overlaps the concurrent calculation-engine survey (plan 013).
5. **`AbsencesImportService`** — bulk import writes the clocking columns directly
   (`:195-248`); the null-out branch at `:239-248` was not analysed.
6. **The notification path** — `WeeklyAbsenceDigestService`, `AbsenceRequestDetails.NotificationStatus`,
   `SendAbsenceRequestCreatedNotification`. Named only.
7. **Absence screens' rights** — the `workrules.absence` / `absencerequests` right nodes
   (`SCREEN-TREE.md:218,249,252`) were not cross-checked against controller attributes.
8. **The kiosk / Android `PartialDay=4` divergence** — noted in §6, not chased. Something accepts a
   period value the server enum lacks.
9. **`Absence.Repeatability`'s intent.** `Logic/Settings/CalculationEnums.cs:16` defines a
   `RepeatabilityTreat` enum; whether it was ever wired to this column was not established.
10. **Data volumes.** No customer database was queried, so "how many customers actually use the
    recap engine" is unanswered — and it decides whether §4a needs a migration or just a shim.
