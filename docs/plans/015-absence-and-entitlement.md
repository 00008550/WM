# 015 — Absence & entitlement

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 (Absence & accruals); §13 module 8 (Absence / entitlement);
resolves §14 open question 9 ("some customers reportedly use an older absence variant")
Legacy sources surveyed: see `docs/TLW-ABSENCE-MODEL.md` (survey 2026-08-30 — **measurement already
done, not re-measured here**). 43 base tables / 331 mappings extracted mechanically from
`E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (script + set-difference completeness check in
that doc §9); balance engines from `Logic/Planning/AbsenceRequests.cs:458-491`,
`AccrualAbsenceBalanceService.cs`, `EmployeeAccrualCalculationsService.cs`, and
`dbo.GetEmployeeAbsenceBalanceWithPeriod` (`83.V5.29.0.0.sql:380`); the eleven `CompensationMethod`
worked examples from `E:\Tlw\Documentation\Absences Configuration.md`.

## Ground truth
Full measurement in `docs/TLW-ABSENCE-MODEL.md`. Load-bearing facts:
- **The `OnLeave` deletion (plan 007 P1) was correct** — independently re-tested (§1). There is no
  temporary non-availability state on `dbo.Employees`; "on leave" is a derived, dated fact of a
  *day* (`Clockings.MorningAbsenceID`/`AfternoonAbsenceID`), not a lifecycle state. **Do not
  re-open it.** Cited, not reversed.
- **Two independent entitlement-balance engines run live at once**, chosen per (employee, absence)
  at request time by `IsAccrualBalanceEnabled` (`AbsenceRequests.cs:466`): the **accrual** engine
  (C#, `AccrualAbsenceBalanceService.cs:152-249`) vs the **recap** engine (T-SQL,
  `dbo.GetEmployeeAbsenceBalanceWithPeriod`). This is the answer to §14 open question 9 and shapes
  the entitlement portion.
- **`dbo.Absence` is a 35-column rule-carrying type** (§2). The distinctive rules columns: `Unit`
  (Day/Hour), `Category`, `CompensationMethod` (decides **what an absent day is worth**),
  `AllowOnDayOff`/`AllowOnHoliday`, `CounterId`, `BlockAbsenceRequestOnNegativeBalance` (the *only*
  thing that makes a balance binding), `IsExcludedFromPayrollExport`. **Six columns (30-35) plus
  `ContractId` are write-only** — settable via the public API, read by nothing. WM must not
  reproduce them: target ~22 columns.
- **Booking fails open** (§7 F1, `AbsenceRequests.cs:433-436`): if neither engine returns a balance
  row, the request is **Valid**. An Invert. Plus five more fail-opens (F2-F6).
- **Absence sits on the daily Clocking aggregate** (§5): `MorningAbsenceID`/`AfternoonAbsenceID` +
  `AbsenceDuration`/`MorningAbsenceDuration`/`AfternoonAbsenceDuration`. **Plan 002 cannot model a
  Clocking without these** — a hard prerequisite/coupling named here, not owned here.
- **WM is missing `ContinuousServiceDate`** — the *primary* length-of-service accrual input
  (`EmployeeAccrualCalculationsService.cs:488`), nowhere in `src/` (`grep` → no matches). 007 P1
  wrote `EmployedFrom`/`EmployedUntil` and raises no recalculation event; legacy's
  `Employees_Update_Trigger` requeues a full replay when any of seven columns change.

## Legacy behaviour (what we are replacing)
- **`CompensationMethod`** (§3, `AbsenceEnums.cs:3-13`): eight values, only four reach a calculator
  (`None`, `BaseTheoreticPlanning`, `BasePackage`/`ComplementPackage` + `Package`, `CalculateHHmm`).
  The other three (`ComplementTheoreticPlanning`, `BaseWeekly`, `ComplementWeekly`) have no UI and
  no reader → Drop.
- **Recap engine** (§4a): hand-entered balance `Quota + PreviousPeriodBalance + Adjustment`
  (`83.V5.29.0.0.sql:456`), each with a free-text note; `RecapPeriods.Locked` freezes a period;
  **no carry-over automation** — `PreviousPeriodBalance` is typed. Tables `RecapPeriods`(9),
  `RecapPeriodEmployees`(3), `RecapAbsences`(12), `PeriodAbsences`(3, per-*user* visibility).
- **Accrual engine** (§4b): `dbo.Accruals`(21) rule + `AccrualLengthOfServiceBonuses`(8) + five-way
  allocation + `EmployeeAccrualCalculations`(15, stored result with 7 aggregate columns) +
  `EmployeeAccrualCalculationChanges`(6, **the ledger** — the good idea) + `AccrualAdjustments`(7) +
  `AccrualsCalculationQueue`(5). Entitlement is an **event ledger**, not a stored number: `Accrued`,
  `LengthOfServiceBonus`, `Carryover`, `Adjustment`, `Taken`, `Scheduled`, `Requested`,
  `EffectiveDateOfTerminationCorrection`. The seven aggregate columns are `calc_*`-style
  denormalisation, correct only after a **destructive** full recalculation.
- **Pro-rating** (`GetActualToFullPeriodRatio:722-742`): intersects the recurrence period with
  `(EnterDate, FinalEmploymentDate ?? DischargeDate ?? MaxValue)` — `FinalEmploymentDate` wins.
  Grant-at-start vs grant-at-end use different ratios + a leave-date correction change.
- **Length-of-service bonus** from `ContinuousServiceDate ?? EnterDate`, banded, evaluated on two
  anniversaries; lands on anniversary+1 under `Default`, on the anniversary under
  `DeferredToNextCycle`. Legacy ships tests (`AccrualLengthOfServiceBonusTests.cs`).
- **Five-way allocation** (§4c): `AccrualEmployeeAllocationView` is a `UNION ALL` over
  employee/department/location/employment-type; the discriminator is selected but **never
  prioritises** — `GetAccrualIdForEmployeeAbsence` is `FirstOrDefault()` with **no `ORDER BY`**, so
  two accruals for one (employee, absence) gives an **arbitrary** winner. The view has no
  `IsActive`/`DischargeDate` filter, so leavers keep accruing.
- **The clocking seam** (§5): half day = one column set; whole day = both set to the same id;
  durations denormalised three ways with a known "old logic" reconstruction branch. Six writers,
  all direct UPDATEs. Approval triggers same-day recalc inline, past-only.
- **Approval flow** (§6): `AbsenceRequests`(14) + `AbsenceRequestDetails`(10, one row per step).
  Validation order: department caps/blackout → `CheckAbsenceDateClockingsPresent` (**hard dependency
  on clocking pre-generation, plan 002**) → balance → `BlockAbsenceRequestOnNegativeBalance`.
  Approver routing: `EmployeeAbsenceManagers` → `DepartmentAbsenceManagers` → no detail row.
  `AbsenceRequestsLogs`(26) still carries fixed `Manager1*`/`Manager2*` slots beside the
  unlimited chain. Deputy activation derived from the primary's own clocking. Department-hierarchy
  expansion (`AbsenceRequests.cs:3039-3043`, `DepartmentHeirarchyView`) is the **only** place in the
  product that walks the department tree. Administrator = `employeeId < 0` (magic negative id).

## Keep / Improve / Invert / Drop
Full tables in `TLW-ABSENCE-MODEL.md §2, §7, §8`. Headlines:
- **Keep:** `dbo.Absence` as the rule-carrying type (35 → ~22); `dbo.Accruals` rule;
  `AccrualLengthOfServiceBonuses`; `AccrualAdjustments`; `AbsenceRequests`/`AbsenceRequestDetails`;
  the department/employee absence-manager tables; `BlockedAbsencesByDepartment`/`ByDailyModel`,
  blackout windows; `Holidays`/`HolidayGroups`; the morning/afternoon halves (the domain is real).
- **Keep + promote:** `EmployeeAccrualCalculationChanges` — **the ledger** — becomes the source of
  truth; balances **project** from it (aligns with plan 002's replay design).
- **Improve:** `EmployeeAccrualCalculations` 7 aggregate columns → projection over the ledger;
  `HistoricalClockingAbsences` → redundant once the ledger exists; `AccrualsCalculationQueue` →
  RabbitMQ job; the four allocation dimension tables → one polymorphic assignment; the two
  absence-group content lists → one join with a purpose column; `DailyModel.Code=='98'` magic
  string → a flag; `AbsenceRequestsLogs` `Manager1/2` slots → generic audit event;
  `TheoreticIsPackage` → fold into `CompensationMethod`; the payroll-export columns
  (`ExportCode2`, `IsExportable`, `IsExcludedFrom*`) → plugin-scoped mapping (plan 014).
- **Invert:** F1-F6 fail-opens (no balance ⇒ valid; no `AbsenceGroupId` ⇒ every type bookable;
  recap empty period; allocation view keeps leavers; arbitrary accrual winner; **no employment check
  anywhere** — a leaver keeps approved absence past their leave date). All → fail closed;
  `BlockAbsenceRequestOnNegativeBalance` semantics reconsidered so a balance is meaningfully binding.
- **Drop:** the six write-only `Absence` columns + `ContractId`; the three dead `CompensationMethod`
  values; the recap tables (superseded by accruals — migrate data, see open question);
  schools-vertical tables (`StudentRegistrationAbsenceAlertSettings`, `PredefinedAbsences`);
  `AbsenceManagers` (duplicates `Employees.IsAbsenceManager`); `ac_holidays` (devices/AC).
- **Reassign, not delete:** the five flexi tables + `AutoBalanceResetEmployees` → **Rules** (plan
  013); `SalaryEntitlements(+Types)` → HR/payroll; `LeaveReasons`/`LeaveNoticePeriods` → People
  (007 P1). The absence-and-entitlement **core is 34 tables / 278 columns**.

## Edge cases
Lift the **eleven `CompensationMethod` worked examples** verbatim from
`E:\Tlw\Documentation\Absences Configuration.md` as the acceptance tests for what an absent day is
worth (Daily Expected Hours / Pay Fixed Hours+`Package` / Calculate HH:MM / None). Plus:
- **Half vs whole day:** one column set = half; both set to the same id = whole; two *different*
  half-day absences on one day are representable. `CountFullDayAbsenceAsHalfDay` (on the daily
  template, not the absence) makes a both-set day count 0.5.
- **Duration triplication:** `AbsenceDuration`, morning, afternoon must agree; legacy has an "old
  logic — not re-calculated yet" reconstruction branch. WM projects from the ledger instead.
- **No clocking for a booked day** → `NoClockingsOnAbsenceDates`: booking requires a `Clockings` row
  for **every** day in the range (hard dependency on plan 002 pre-generation).
- **No balance data** (no recap period, or no accrual calc covering the dates) → legacy approves
  (F1). WM must decide deny/hold, not silently approve.
- **No `AbsenceGroupId`** → legacy makes every active type bookable (F2). WM: nothing bookable
  without an explicit grant.
- **Two accruals for one (employee, absence)** → arbitrary winner (F5). WM: deterministic precedence
  (employee > department > location > employment-type is the natural order to confirm).
- **Leaver:** mid-period leave date → `EffectiveDateOfTerminationCorrection` claws back;
  `FinalEmploymentDate` wins over `DischargeDate`; and a leaver must **not** keep approved absence
  past their leave date (F6).
- **Length-of-service anniversary:** bonus on anniversary+1 (`Default`) vs anniversary
  (`DeferredToNextCycle`); measured from `ContinuousServiceDate ?? EnterDate`.
- **Recurrence boundary:** `EndBy` with a null end date aborts the whole calculation
  (`EmployeeAccrualCalculationsService.cs:168-174`); `MaximumCarryover` applied annually after
  rounding; carry-over strips the LoS bonus unless `IsLengthOfServiceBonusCarriedover`.
- **Recalculation is destructive/replayed** — the seven columns that requeue a full replay from hire
  (`DepartmentId`, `EmployeeLocationId`, `EmploymentTypeId`, `DischargeDate`, `EnterDate`,
  `ContinuousServiceDate`, `FinalEmploymentDate`).

## Target design in WM
New module `src/Modules/Absence` (schema-isolated). Reaches TimeAttendance / People / Identity /
Rules only via contracts/events (invariant 1).
- **Absence-type master:** `dbo.Absence` → ~22-column entity carrying the live rules
  (`Unit`, `Category`, `CompensationMethod`, `AllowOnDayOff`/`AllowOnHoliday`, `CounterId`,
  `BlockAbsenceRequestOnNegativeBalance`); write-only and dead columns dropped.
- **Entitlement as a ledger:** `EmployeeAccrualCalculationChanges` promoted to the source of truth;
  balances are a **projection** over the ledger (no stored aggregates), recalculation via a RabbitMQ
  replay job triggered by a People-published employment-change event (the seven-column trigger,
  inverted into an event — closes the 007 P1 gap). `ContinuousServiceDate` added to People and
  carried on the event.
- **Two balance engines** behind one `IEntitlementBalance` contract, selected by an
  accrual-enabled check per (employee, absence) — accrual (projected ledger) and recap (period
  balance); recap may ship as a data migration + read-only shim (open question).
- **Booking + approval:** request → validation gates (department caps/blackout → clocking-present
  via TimeAttendance contract → balance → negative-balance block) → approver chain
  (`EmployeeAbsenceManagers` → `DepartmentAbsenceManagers`, `Order` escalation, deputy activation).
  Department-hierarchy expansion coordinates with plans 001 P4 / 005.
- **Clocking seam:** Absence publishes an `AbsenceBooked`/`AbsenceApproved` event carrying the
  morning/afternoon absence ids + durations; **plan 002's Clocking aggregate must reserve these
  fields** — named here as a prerequisite, owned by 002.
- **Endpoints (API-first, invariant 2):** absence-type CRUD; accrual-rule CRUD; entitlement balance
  query; absence request create/approve/cancel; approver-chain config. Every endpoint an
  authorization policy (invariant 5).

## Out of scope for this plan
The flexi-balance subsystem (5 tables + `AutoBalanceResetEmployees`) → **Rules** / plan 013.
`SalaryEntitlements(+Types)` → HR/payroll. `LeaveReasons`/`LeaveNoticePeriods` → People (007 P1).
Schools vertical (`PredefinedAbsences`, `StudentRegistrationAbsenceAlertSettings`, school holidays).
The 13 report views (§8 — Reporting bucket / Insight). `dbo.Holidays` calculation behaviour beyond
CRUD (the theoretic/gross-presence columns were not traced — `TLW-ABSENCE-MODEL.md §10.2`). Payroll
export mapping itself → plan 014. The Clocking aggregate shape → plan 002 (named as prerequisite).

## Portions

### [ ] P1 — Absence-type master (~22 cols) + CRUD
**Touches:** `src/Modules/Absence` (new), migration, module service, endpoint, screen, `Absence.Tests` (new).
**Done when:** CRUD for the absence type with the live rule columns only; write-only/dead columns and the three dead `CompensationMethod` values are absent.
**Tests:** rule columns persist; `CompensationMethod` accepts only the four live values; no write-only column exists on the API surface.
**Risk:** low

### [ ] P2 — `CompensationMethod` valuation (what an absent day is worth)
**Touches:** Absence module, day-valuation service, contract to TimeAttendance expected-hours, tests.
**Done when:** given a day + absence type, the module computes the day's worked/expected contribution for all four live methods.
**Tests:** the **eleven** `Absences Configuration.md` worked examples pass verbatim; half-day and `Package/2` cases covered.
**Risk:** medium

### [ ] P3 — Accrual rule + the entitlement ledger
**Touches:** Absence module, migration (`Accruals`, `AccrualAdjustments`, ledger table), engine service, endpoint, tests.
**Done when:** an accrual rule produces ledger change rows (`Accrued`/`Carryover`/`Adjustment`/`LengthOfServiceBonus`) and the balance is a projection over them (no stored aggregate).
**Tests:** pro-rating ratio; `MaximumCarryover` annual cap; `EndBy` null-end abort; LoS bonus anniversary+1 vs deferred; balance = Σ ledger.
**Risk:** high

### [ ] P4 — Employment-change replay event + `ContinuousServiceDate`
**Touches:** People module (add `ContinuousServiceDate`, publish event — coordinate with 007), Absence replay consumer, RabbitMQ job, tests.
**Done when:** a change to any of the seven employment fields publishes an event that replays the employee's ledger from hire; `ContinuousServiceDate` exists in People and feeds the LoS bonus.
**Tests:** each of the seven fields triggers replay; replay from hire is deterministic/idempotent; missing `ContinuousServiceDate` falls back to `EnterDate`.
**Risk:** high

### [ ] P5 — Deterministic allocation + fail-closed balance selection
**Touches:** Absence module, allocation resolution, `IEntitlementBalance` selector, tests.
**Done when:** an employee resolves to exactly one accrual per absence by defined precedence; leavers are excluded; the accrual-vs-recap engine is chosen per (employee, absence).
**Tests:** two-accrual conflict resolves deterministically (invert F5); leaver excluded (invert F4); engine selection matches `IsAccrualBalanceEnabled` semantics.
**Risk:** high

### [ ] P6 — Absence request + validation gates (fail closed)
**Touches:** Absence module, request service, TimeAttendance clocking-present contract, endpoint, screen, tests.
**Done when:** create/cancel a request through department caps/blackout → clocking-present → balance → negative-balance block; no balance data does **not** auto-approve; no `AbsenceGroupId` grants nothing.
**Tests:** each gate; invert F1 (no balance ⇒ not auto-valid); invert F2 (no group ⇒ nothing bookable); invert F6 (leaver cannot hold future approved absence); missing clocking row rejects.
**Risk:** high

### [ ] P7 — Approver chain + deputy + department-hierarchy expansion
**Touches:** Absence module, approver-routing service, `AbsenceRequestDetails`, department-hierarchy contract (plans 001/005), endpoint, tests.
**Done when:** approvals route `EmployeeAbsenceManagers` → `DepartmentAbsenceManagers` with `Order` escalation; deputy activation derived from the primary's clocking; hierarchy expansion via the shared department-tree contract; administrator is a role, not `employeeId < 0`.
**Tests:** routing precedence; escalation order; deputy activates on primary's recorded absence; hierarchy expansion maps subdepartments; no magic negative id.
**Risk:** medium

### [ ] P8 — Clocking-seam event (absence on the daily aggregate)
**Touches:** Absence module (publish), TimeAttendance/plan-002 consumer contract, tests.
**Done when:** approval publishes an absence event carrying morning/afternoon ids + the three durations; the Clocking aggregate records them (half/whole-day, `CountFullDayAbsenceAsHalfDay`).
**Tests:** half day = one set; whole day = both same id; two different half-days; `CountFullDayAbsenceAsHalfDay` → 0.5; durations reconcile (no triplication drift). **Blocked on plan 002 reserving the fields.**
**Risk:** high

## Open questions for the user
1. **Shared vs separate approval engine.** This survey found the absence approval shape
   (`AbsenceRequests`/`AbsenceRequestDetails`, chain via `Order`, deputy-from-clocking); **plan 020
   (expenses) found a *different* approval shape** and raised the same question as its open question 1.
   Is there one shared approval engine across absence/expenses (and future workflows), or separate
   engines per domain? A product/architecture call — flagged consistently with plan 020.
2. **Recap engine: migrate or shim?** No customer database was queried, so how many customers
   actually use the recap variant is unknown (`TLW-ABSENCE-MODEL.md §10.10`). Do we ship recap as a
   one-time data migration into the accrual ledger, or a read-only compatibility shim kept live?
3. **`ContinuousServiceDate` on People (007 coupling).** Adding it and the replay event touches the
   People module and 007 P1's `EmployedFrom`/`EmployedUntil`. Confirm People owns the column + event
   and Absence only consumes it.
4. **Allocation precedence order.** Legacy never defined one (the bug). Proposed:
   employee > department > location > employment-type. Confirm this is the intended precedence.
5. **`BlockAbsenceRequestOnNegativeBalance` semantics.** Today it is the *only* thing making a
   balance binding, and everything else fails open. Should a computed balance be binding by default
   (Invert), with this flag becoming an *allow-negative* opt-out?
