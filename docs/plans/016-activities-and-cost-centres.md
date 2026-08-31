# 016 — Activities & cost centres (job costing)

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 phase 7 (Expenses & Field service — activities/job costing); §13 module 12 (Activities)
Legacy sources surveyed: see `docs/TLW-ACTIVITIES-MODEL.md`. Schema measured from
`E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (30 base tables / 261 cols + 11 report views);
allocation from `HoursCalculationService.cs:4203-4663`; slot mapping from
`E:\Tlw\Database\Support\set device cost centres to clockings.sql`.

## Ground truth
Full measurement in `docs/TLW-ACTIVITIES-MODEL.md`. Key facts:
- **Two dimensions, not one:** `CostCentres` (accounting split, on the Clocking aggregate) and
  `WorkActivities` (job/task booking, in `ClockingActivities` child rows). The audit's single
  "Activities / job costing" bucket conflates them.
- The footprint is **30 base tables / 261 columns** (audit said 20/345; the 345 is report-view weight).
- `Clockings` carries **`CostCentreId1..6` paired with `DailyModelId1..6`** (per shift) +
  `CorrectionCostCentreId`. There is **no `Out1..6`** — a correction to `TLW-SCHEMA-SWEEP.md:66`.
- Cost-centre allocation is a **second full counter run** (`CostCentreCounters` =
  `ClockingId + CostCentreId + CPTN01..20`).
- `CostCentresManagedByRole` / `WorkActivitiesManagedByRole` are the scope tables behind
  `ScopeModel.cs` `CostCentre=4` / `WorkActivity=5`.

## Legacy behaviour (what we are replacing)
- **Cost-centre resolution** (`HoursCalculationService.cs:4215`): `device → daily model → employee →
  department`, first non-null wins, per shift slot, re-derived on every recalculation.
- **Counter split** (`:4292-4663`): pauses, absences, corrections, holidays and worked hours are each
  re-accumulated per cost centre into `CPTN01..20`.
- **Work-activity booking:** `ClockingActivities` rows (begin/end, `ActivityId`, `CostCentreId`,
  client, machine, inventory, signature, geo, drive-time), optionally gated by
  `WorkActivities.ShouldScanJobCode` (the "activity swipe").
- **Reporting/export:** `UnifiedJobCostingReportView` (137), `UnifiedActivitiesReportView` (148);
  `HorioExportService` FTP-exports Ascendia activity swipes (`TLW-INVENTORY.md:107`); overnight
  "allocate cost centres per schedule" (`:79`).

## Keep / Improve / Invert / Drop
Full table in `TLW-ACTIVITIES-MODEL.md §1a`. Headlines:
- **Keep:** `CostCentres` (+ hierarchy `ParentId`), rule tables, group/scope link tables, the
  field-service child tables (documents, signatures, inventory, satisfaction, notes).
- **Improve:** `WorkActivities`/`ClockingActivities` (drop inline rounding/exception copies in favour
  of a single rule reference; counters as rows not `CPTN01..20`); `CostCentreCounters` (cost-centre
  key on the counter model, not a twin table); `ActivityMachine*` → generic resource or Drop.
- **Invert:** cost-centre scope and the `*ManagedByRole` empty-set must be **fail-closed**; make
  "unallocated" an explicit cost-centre state instead of the silent department-default absorb.
- **Drop:** `WorkActivityDevices`, `WorkActivityGroupDevices` (device scope); all 11 report views as
  tables (rebuild as query endpoints); do **not** port the slot-6 device off-by-one bug
  (`set device cost centres to clockings.sql:43`).

## Edge cases
- **Slot→cost-centre mapping:** slot N = badge pair (2N−1, 2N); cost centre = device of that slot's IN
  swipe. Legacy back-fill has a bug on slot 6 — WM must use `SwipeSourceId11/12`.
- **Empty swipe pair** clears that slot's cost centre (`:4236-4241`).
- **Future-dated clocking** (scheduler pre-created) resets all six slots (`:4225`).
- **Absence/holiday hours** go to the **employee default** cost centre, not the shift device
  (`:4351`, `:4396`).
- **Recalculation re-derives** every slot → retroactive history change; WM's replay must decide whether
  a device/department re-assignment rewrites past allocations.
- **`*ManagedByRole` empty set** — determine fail-open vs fail-closed before building the scope filter.
- **Many activities per shift** — `ClockingActivities` is 1..N per clocking; overlapping/auto-closed
  (`IsAutoClosed`) bookings need a defined ordering.

## Target design in WM
New module `src/Modules/Activities` (schema-isolated). Reaches TimeAttendance/Rules only via
contracts/events (invariant 1).
- **Contracts/events:** consume the clocking-calculated event; publish a `CostCentreAllocated` /
  `ActivityBooked` event carrying the per-cost-centre counter split for downstream payroll plugins
  (§13 module 6/14) and reporting (Insight, §16).
- **Endpoints (API-first, invariant 2):** CRUD for `CostCentres` (+ hierarchy, groups), `WorkActivities`
  (+ groups, rules), employee default activity; activity booking on a clocking; job-costing report
  query; activity-swipe capture (phone only — invariant 3).
- **Scope:** resolve `ScopeDimension.CostCentre` / `WorkActivity` (fail-closed) from the
  `*ManagedByRole` + `*GroupContent` tables — coordinate with plan 001/005.
- **PluginSdk:** activity/cost-centre export surface for payroll plugins (plan 014).
- Depends on plan 002/003 having reserved the per-shift cost-centre slot and the cost-centre counter
  key — see the proposed corrections in `TLW-ACTIVITIES-MODEL.md §8`. **This plan should not be
  `approved` before those reservations land**, or the aggregate must be re-shaped later.

## Out of scope for this plan
Scheduling of activities (`ClockingScheduledActivities`, mapping table) → plan 017. Clients /
field-service master (`Clients`, `ClientSites`) → its own bucket. Inventory master. Device links.
EPOS activity views. The counter formula DSL (`WorkActivityFormula`) → plan 003.

## Portions

### [ ] P1 — Cost-centre master + hierarchy + groups
**Touches:** `src/Modules/Activities` (new), migration, module service, endpoint, contract, screen, `Activities.Tests` (new).
**Done when:** CRUD for `CostCentres` (code, description, rates, `ParentId`, dates) + groups; hierarchy resolves.
**Tests:** hierarchy walk, group membership, date-range validity.
**Risk:** low

### [ ] P2 — Work-activity master + groups + exception/rounding rules
**Touches:** Activities module, migration, endpoint, screen, tests.
**Done when:** CRUD for `WorkActivities`, groups, `WorkActivityExceptionRules`, `GlobalActivityRoundingRules`; single rule reference (no inline copies).
**Tests:** rule reference resolution; `ShouldScanJobCode` flag; early/late/long/short thresholds.
**Risk:** medium

### [ ] P3 — Cost-centre scope resolution (fail-closed)
**Touches:** Activities module, `ScopeModel.cs` consumers, Identity contract, tests.
**Done when:** `ScopeDimension.CostCentre` + `WorkActivity` resolve from `*ManagedByRole` + `*GroupContent`; empty set = deny.
**Tests:** empty-set denies (fail-closed); group grant widens; role with no grant sees nothing.
**Risk:** high

### [ ] P4 — Per-shift cost-centre allocation on the clocking
**Touches:** Activities module, TimeAttendance contract/event, allocation service, tests.
**Done when:** on a clocking-calculated event, each shift slot resolves a cost centre (device→model→employee→department, "unallocated" explicit) and the correct slot mapping (no slot-6 bug).
**Tests:** slot→badge-pair mapping incl. slot 6; empty pair clears; future-dated resets; absence/holiday → employee default; unallocated is explicit.
**Risk:** high

### [ ] P5 — Cost-centre counter split
**Touches:** Activities module, Rules counter contract, migration (counter cost-centre key), tests.
**Done when:** per-cost-centre `CPTN`-equivalent counters produced from pauses/absences/corrections/holidays/worked hours; a `CostCentreAllocated` event published.
**Tests:** the five accumulation sources; totals reconcile with the un-split counters; re-run idempotence (clear-then-rebuild).
**Risk:** high

### [ ] P6 — Activity booking on a clocking + field-service children
**Touches:** Activities module, endpoint, screen (activity swipe, phone), tests.
**Done when:** `ClockingActivities` bookings (begin/end, activity, cost centre, client, notes, satisfaction, documents/signature, geofence, drive-time) create/read; job-code scan honoured.
**Tests:** many activities per shift; auto-close ordering; geofence-breach flag; job-code gate.
**Risk:** medium

### [ ] P7 — Job-costing report + export surface
**Touches:** Activities module, report query endpoint, PluginSdk export contract, tests.
**Done when:** flattened job-costing/activity report endpoint reproduces `UnifiedJobCostingReportView` content; activity/cost-centre data exposed to payroll plugins.
**Tests:** report totals match the counter split; export contract shape stable.
**Risk:** medium

## Open questions for the user
1. **`ActivityMachine*` — Keep, Improve (generic resource), or Drop?** No live-usage signal from schema
   alone (`TLW-ACTIVITIES-MODEL.md §7.4`). Product call.
2. **"Unallocated" cost centre (Improve) vs legacy silent department-default absorb (mirror)?** I
   recommend Improve (explicit, reportable). Confirm.
3. **Retroactive re-allocation:** should a device/department re-assignment rewrite past cost-centre
   allocations on recalculation (legacy does), or should history be frozen? Interacts with plan 002
   replay design.
4. **Proposed protected-doc corrections in `TLW-ACTIVITIES-MODEL.md §8`** (COVERAGE-AUDIT 20→30/261,
   SCHEMA-SWEEP `Out1..6` removal, CLOCKING-MODEL cost-centre slot note, ScopeModel comment) — apply in
   the consolidated pass?
5. **Phase-7 go/no-go** (ARCHITECTURE.md §14 open question 10 flags "revisit before Phase 7"). This plan
   stays `draft` until then.
