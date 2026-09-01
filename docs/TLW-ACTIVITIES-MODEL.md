# TLW Activities / Job-Costing — schema survey

Surveyed 2026-08-31 for plan 016. Ground truth is `E:\Tlw` only; every claim carries a `file:line`.
WM's own records are treated as claims under audit. **This document proposes corrections to
`COVERAGE-AUDIT.md`, `TLW-SCHEMA-SWEEP.md` and `TLW-CLOCKING-MODEL.md`; it does not edit them —
see §8.**

Legacy sources opened:
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (LINQ-to-SQL model, 578 tables — the schema measurement).
- `E:\Tlw\Source\Logic\HoursCalculation\HoursCalculationService.cs:4203-4663` (cost-centre counter re-run).
- `E:\Tlw\Database\Support\set device cost centres to clockings.sql` (back-fill mapping — reveals slot semantics + a bug).
- `src\SharedKernel\WM.SharedKernel\Security\ScopeModel.cs:22-40` (WM scope reservation).
- `docs\TLW-INVENTORY.md:79,107` (HorioService cost-centre allocation; Ascendia activity-swipe export).

---

## 1. Measurement — the footprint is 30 base tables, not "20"

`COVERAGE-AUDIT.md:59` records the bucket as **20 tables / 345 columns, "named, not surveyed."**
Measured against the designer, the activity/cost-centre footprint is **30 base tables / 261 columns**
plus **11 report/grid views** (the views carry the 345-column weight — `UnifiedActivitiesReportView`
alone is 148 cols, `UnifiedJobCostingReportView` 137). The audit's 20/345 count is an estimate whose
membership overlaps other buckets: `CostCentreCounters` is counted under **Rules** (`SCHEMA-SWEEP.md:185`),
and the six `*ManagedByRole` / `*Group*` link tables belong conceptually to **Identity/scope**. The
honest statement is: *this is two subsystems (cost-centre allocation, and work-activity/job-costing)
sharing the Clocking aggregate, ~30 base tables wide, feeding ~11 report views.*

### 1a. The 30 base tables, classified (0 unclassified, 0 duplicated)

Set-difference assertion: the list below is exactly the designer tables matching
`/ctivit|CostCent/` and not matching `/View|Epos|Visitor|DeviceSync|Synergy|Pending/`. Count = 30,
column sum = 261 (verified by the measurement script). Each row is Keep / Improve / Invert / Drop.

| Table | Cols | What it is | Class |
|---|---:|---|---|
| `dbo.WorkActivities` | 53 | the **job / work-activity master** — code, name, client/terminal code, cost & charge rates, rounding, exception rules, geo | **Improve** |
| `dbo.ClockingActivities` | 53 | **time booked to an activity within a clocking** — begin/end, ActivityId, CostCentreId, machine, client, gross/net hours, its own `CPTN01..20` | **Improve** |
| `dbo.CostCentreCounters` | 23 | `ClockingId + CostCentreId + CPTN01..20` — the **per-cost-centre counter split** | **Improve** |
| `dbo.CostCentres` | 15 | **cost-centre master** — code, description, cost/charge rate, `ParentId` (hierarchy), dates | **Keep** |
| `dbo.WorkActivityExceptionRules` | 15 | reusable early/late/long/short exception rule set for activities | **Keep** |
| `dbo.ClockingScheduledActivities` | 12 | **planned** activity for a clocking (from Scheduling) — time-from/to, client, job ref | **Keep** (owned by Scheduling survey) |
| `dbo.GlobalActivityRoundingRules` | 9 | reusable rounding rule (in/out round-to, cut-off, precise-presence) | **Keep** |
| `dbo.ClockingActivitySignatures` | 8 | captured client signature blob per activity (field service) | **Keep** |
| `dbo.EmployeeDefaultActivities` | 6 | employee's default activity, date-ranged | **Keep** |
| `dbo.ClockingActivityDocuments` | 6 | document blob attached to an activity | **Keep** |
| `dbo.ClockingActivityInventory` | 4 | inventory item + amount consumed on an activity | **Keep** |
| `dbo.ClockingActivityCorrections` | 4 | manual correction applied to an activity's time | **Keep** |
| `dbo.ClockingActivityNotes` | 3 | free-text note per activity | **Keep** |
| `dbo.ClockingActivitySatisfaction` | 3 | client satisfaction rating per activity | **Keep** |
| `dbo.ClockingActivitySwipeOutsideOfficeNotifications` | 3 | geofence-breach flag per activity swipe | **Keep** |
| `dbo.CostCentreGroups` | 3 | cost-centre grouping | **Keep** |
| `dbo.CostCentreGroupContent` | 3 | group→cost-centre link | **Keep** |
| `dbo.CostCentresManagedByRole` | 3 | **scope link: role → cost-centre** | **Keep** (feeds `ScopeDimension.CostCentre`) |
| `dbo.WorkActivitiesManagedByRole` | 3 | **scope link: role → activity** | **Keep** (feeds `ScopeDimension.WorkActivity`) |
| `dbo.WorkActivityGroups` | 3 | activity grouping | **Keep** |
| `dbo.WorkActivityGroupContent` | 3 | group→activity link | **Keep** |
| `dbo.WorkActivity_Parents` | 3 | activity → department attachment | **Keep** |
| `dbo.WorkActivityClients` | 3 | activity → client link (field service) | **Keep** |
| `dbo.WorkActivityDevices` | 3 | activity → device link | **Drop** (device scope, decision 2026-07-20) |
| `dbo.WorkActivityGroupDevices` | 3 | activity-group → device link | **Drop** (device scope) |
| `dbo.WorkActivityGroupsClients` | 3 | activity-group → client link | **Keep** |
| `dbo.ActivityMachine` | 3 | machine master (job costing on a machine) | **Improve** (fold into a generic resource, or Drop if machine-tracking unused) |
| `dbo.ActivityMachineGroups` | 2 | machine grouping | **Improve/Drop** with `ActivityMachine` |
| `dbo.ActivityMachineGroupContent` | 3 | group→machine link | **Improve/Drop** |
| `dbo.ClockingScheduledAndActualActivityMapping` | 3 | maps a **scheduled** activity to the **actual** ClockingActivity | **Keep** (Scheduling seam) |

The 11 views (`UnifiedActivitiesReportView`, `UnifiedJobCostingReportView`,
`DailyActivityRecordsView`, `DailyClockingActivityRecordsView`, `UnifiedScheduledActivitiesReportView`,
`ClockingActivitiesView`, `ShortClockingActivitiesView`, `PlanningControlActivitiesView`,
`UnifiedTATransactionsReportActivitiesView`, `VisitorActivityGridView`, and the Epos activity views)
are **reporting projections** — **Drop as tables**; their *content* is the spec for WM report/export
endpoints (§6).

---

## 2. What an "activity" actually is

There are **two distinct dimensions**, both denormalised onto the Clocking aggregate, and the audit
conflated them under one bucket:

1. **Cost centre** (`CostCentres`) — an accounting bucket. A clocking's hours are *split* across up to
   six cost centres (one per shift slot) and re-counted into `CostCentreCounters`. Answers *"whose
   budget paid for these hours?"* Rate fields (`CostCentreCost`, `ChargeRate`) make it a costing unit.

2. **Work activity** (`WorkActivities`) — a **job / task / work type** the employee books time
   *against*, captured as `ClockingActivities` child rows (many per clocking). It is field-service /
   job-costing: it carries client (`ClientId/ClientContactId/ClientSiteId`), machine, inventory,
   signature, satisfaction, documents, geofence, drive-time (`DriveStartTime/DriveEndTime`), and its
   own rate fields (`ChargeRate`, `HourlyRate`, `ActivityCost`, `EstimatedCost`, `PlannedNoOfHrsOrDays`).
   `WorkActivities.ShouldScanJobCode` means an activity can require a scanned **job code** at the
   terminal — this is the "activity swipe."

**Relationship to a punch:** a punch (`BadgeTimeN` on `Clockings`) opens/closes a *shift slot*; the
cost centre of that slot is derived from the punch's device (§3). A **work activity** is a *finer*
booking inside the clocking — `ClockingActivities.BeginTime/EndTime` with `SwipeSource1/2` — so one
shift can contain many activities. `ClockingActivities` is effectively "the clocking model, one level
down, per job." It even repeats the `CPTN01..20` counter columns and the swipe-source/reader/location
slots — the same denormalisation smell as `Clockings`.

---

## 3. The cost-centre seam with `dbo.Clockings` — what plan 002 cannot ignore

`Clockings` (249 cols) carries **six** per-shift cost-centre slots: `CostCentreId1..6` +
`CorrectionCostCentreId`. **Correction to the task framing and `SCHEMA-SWEEP.md:66`:** there is **no
`Out1..6` column** on `Clockings`. The six cost-centre slots pair with the six **`DailyModelId1..6`**
shift slots and the `calc_*Shift1..6` measure blocks (verified in the `Clockings` column dump). The
"6" is the shift count, and each shift slot has its own cost centre. Say "`CostCentreId1..6` paired
with `DailyModelId1..6`", not "`Out1..6`".

### 3a. How a slot's cost centre is resolved (`HoursCalculationService.cs:4205-4258`)
Fallback chain, first non-null wins:
```
device (first swipe of the slot) → daily model → employee → department
```
`GetDefaultCostCenterId` (`:4215`) starts at `clocking.CostCentreId1` (already device-derived, unless
manually changed) then `dailyModel.CostCentreId ?? employee.CostCentreId ?? department.CostCentreId`.
The device source is confirmed by the back-fill script
(`set device cost centres to clockings.sql:2`): slot N's cost centre = the device of that slot's IN
swipe (`SwipeSourceId1/2` → slot 1, `3/4` → slot 2, …).

- **Fail-open smell:** the chain never terminates in "unallocated"; it always resolves *something*
  (department default). Hours are never left un-costed — arguably right for accounting, but a
  mis-configured department silently mis-allocates. **Classify: Improve** — WM should make
  "unallocated" an explicit, reportable state rather than silently absorbing into department default.
- **Bug found (do not port):** `set device cost centres to clockings.sql:43` fills `CostCentreId6`
  from `isnull(SwipeSourceId11, SwipeSourceId10)` — a copy-paste; slot 6's IN swipe is
  `SwipeSourceId11`/`12`, so this reads the wrong device for the 6th shift. WM must not port the
  off-by-one.
- **Reset rules (`ResetCostCentersForClockings:4221`):** future-dated clockings (from the scheduler)
  have all six cleared; an empty swipe pair clears its slot. Re-entrancy: recalculation **re-derives**
  every slot, so a device re-assignment retroactively changes history — the exact behaviour WM's
  replay design must own deliberately.

### 3b. The counter re-run (`:4292-4663`)
`CalculateCostCentreCounters` is a **second full pass of the counter engine**, partitioned by cost
centre. `ClearCostCentreCounters` (`:4261`) deletes `CostCentreCounters` for the clocking, then five
sources re-accumulate into `CPTN01..20` **per cost centre**:
- `...FromPauses` (`:4317`) — each pause's hours to `pause.GetCostCentreId(clocking)`.
- `...FromAbsences` (`:4338`) — morning/afternoon absence hours to the **employee default** cost centre.
- `...FromCorrections` (`:4368`) — correction hours to `CorrectionCostCentreId`.
- `...FromHolidays` (`:4386`) — holiday theoretic hours to employee default cost centre.
- `...FromClockings` (`:4430`) — worked hours, with special holiday allocation.

So **every counter in the estate has a cost-centre-split twin**, keyed `(ClockingId, CostCentreId,
CPTN01..20)`. This is why `SCHEMA-SWEEP.md` files `CostCentreCounters` under Rules: it is the counter
model with an extra key column. **Plan 002 (Clocking aggregate) must reserve the cost-centre-per-shift
slot on the aggregate, and plan 003's counter model must carry an optional cost-centre key** — or
job-costing cannot be added later without re-shaping the aggregate.

---

## 4. What legacy scopes on (for `ScopeModel.cs`)

`ScopeModel.cs:22-40` reserves `CostCentre = 4` and `WorkActivity = 5` as "not yet resolvable
(phase 7)." Legacy confirms **both are real scope dimensions**, each backed by a role-link table:
- `dbo.CostCentresManagedByRole` (`RoleId, CostCentreId`) — a role sees only its cost centres.
- `dbo.WorkActivitiesManagedByRole` (`RoleId, WorkActivityId`) — a role sees only its activities.

Both follow the same `*ManagedByRole` shape as the six legacy scope accessors named in
`ScopeModel.cs:22`. **The reservation is correct;** what phase 7 must resolve is exactly these two
link tables, plus their group indirections (`CostCentreGroupContent`, `WorkActivityGroupContent`) so a
role can be granted a whole group. **Watch the standing fail-open trap:** confirm at build time whether
an empty `*ManagedByRole` set means "no activities" (fail-closed, correct) or "all activities"
(fail-open); the six legacy accessors were fail-open (`001`/`005` findings), so assume this pair is too
until proven otherwise, and invert it.

---

## 5. Configuration vs code

Mixed, and better-configured than the calc engine:
- **Configurable (data):** rounding is a reusable rule table (`GlobalActivityRoundingRules`) *and*
  duplicated inline on `WorkActivities` (`RoundingPrecisePresence`, `In/OutRoundTo`, `In/OutCutOff`) —
  same "global rule vs inline copy, nothing records which wins" ambiguity `TLW-INVENTORY.md:150` flags
  for `RoundingRules`/`DailyModels`. **Improve:** one rule reference, no inline twin. Exception
  handling is likewise a rule table (`WorkActivityExceptionRules`) with inline copies on
  `WorkActivities`.
- **Code (hard-coded):** the cost-centre fallback chain (device→model→employee→department) is C#, not
  data (`:4208`). No per-record custom-SQL escape hatch was found *specific to activities* (unlike the
  85-script daily-model hatch 013 found); the nearest are one-off support/custom scripts
  (`Database\Support\set device cost centres to clockings.sql`, `Custom Scripts\Contracts\Aktrion -
  Contract - CC.sql`, `Custom Scripts\Other\clear cost centres from daily browser.sql`) — operational,
  not a product feature. **Counter formula language** does reach here: `SCHEMA-SWEEP.md:84` records
  `CostCenterFormula` / `WorkActivityFormula` columns on the weekly/monthly/annual counter tables — the
  same TLW `FormulaEvaluator` DSL, so cost-centre and activity totals can be *formula-derived* counters.
  That DSL is plan 003's problem; note only that job-costing consumes it.

---

## 6. Reporting & exports — why job costing exists

The 11 views are the consumption layer. `UnifiedJobCostingReportView` (137 cols) and
`UnifiedActivitiesReportView` (148 cols, `Counter1..20`) are the flattened job-costing/activity
reports. `TLW-INVENTORY.md:107` confirms the export reach: **HorioExportService FTP-exports "Ascendia
activity swipes"** — i.e. `ClockingActivities` rows are pushed to an external payroll/costing system.
`HorioService` overnight **"allocate cost centres per schedule"** (`TLW-INVENTORY.md:79`) is the batch
that runs §3's allocation across the estate. So the seam reaches **plan 014 (payroll plugins)**: an
activity/cost-centre export is a plugin output, and the `CostCentreCounters` split is what a
cost-allocated payroll run consumes. WM's Activities module must expose activity + cost-centre counter
data through the PluginSdk, not only through UI.

---

## 7. What I did **not** measure (numbered gaps)

1. **The 11 report/grid views' column-level definitions** — I measured their widths, not their SQL. The
   job-costing report spec lives in their `SELECT`s (not in the designer); a build of §6 must read them.
2. **`ClockingActivities` full behavioural model** — begin/end derivation, `IsAutoClosed`,
   `StartTimeGeneratedBy`, drive-time, geofence (`SwipeWorkLocation1/2`). I established the schema, not
   the allocation algorithm that splits a clocking into activities. That is a plan-016 build task.
3. **Scheduling seam** — `ClockingScheduledActivities` and `ClockingScheduledAndActualActivityMapping`
   are owned by the concurrent **Scheduling** survey (`TLW-SCHEDULING-MODEL.md`, plan 017). I named them
   and stopped.
4. **`ActivityMachine*`** — whether machine/resource tracking is used by any live customer (governs
   Keep-vs-Drop). Not determinable from schema alone.
5. **Inventory (`ClockingActivityInventory.InventoryId`)** — the `Inventory` master is outside this
   bucket; not traced.
6. **Client / field-service master** (`Clients`, `ClientSites`, `ClientContacts`) referenced by
   `ClockingActivities` — a separate bucket, not surveyed here; job-costing for field service depends
   on it.
7. **Whether the `*ManagedByRole` empty-set is fail-open or fail-closed** — needs a source read of the
   scope-filter builder, deferred to the build (§4).

---

## 8. Proposed corrections to protected docs (apply in the consolidated pass)

I did **not** edit these; proposing here per task boundaries.

- **`COVERAGE-AUDIT.md:59`** — change "20 tables / 345 columns … named, not surveyed" to
  "**30 base tables / 261 columns + 11 report views**; surveyed 2026-08-31 →
  `TLW-ACTIVITIES-MODEL.md`. The 345-column figure is report-view weight, not base tables. Note overlap:
  `CostCentreCounters` also counted under Rules; `*ManagedByRole`/`*Group*` are scope tables."
- **`TLW-SCHEMA-SWEEP.md:66`** — the row reading "`CostCentreId1..6`, `Out1..6`" is wrong: there is **no
  `Out1..6`**. Replace with "`CostCentreId1..6` paired with `DailyModelId1..6` (per-shift), +
  `CorrectionCostCentreId`."
- **`TLW-CLOCKING-MODEL.md`** — record that the Clocking aggregate carries a **per-shift cost-centre
  slot** whose value is device-derived and **recomputed on every recalculation** (retroactive history
  change), and that a cost-centre-split counter twin (`CostCentreCounters`) exists — both must be
  designed into plan 002/003, not bolted on at phase 7.
- **`ScopeModel.cs:39`** — comment is accurate; add that resolution = `WorkActivitiesManagedByRole` +
  `WorkActivityGroupContent` (and the CostCentre equivalents), and must be **fail-closed**.
