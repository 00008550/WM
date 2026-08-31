# TLW Scheduling — measured model

*Survey 2026-08-31. Every finding carries a `file:line` against `E:\Tlw`. WM's own documents are
claims under audit; where this document corrects one, the correction is proposed here and applied
by the user in a consolidated pass (per the survey brief — I do not edit `COVERAGE-AUDIT.md`,
`ARCHITECTURE.md`, `SCREEN-TREE.md`, `STATE.md`, or the other locked docs).*

Schema measured against `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (578 tables, 8,173
columns — reconciles with the audit). Logic read under `E:\Tlw\Source\Logic`; DB scripts checked
under `E:\Tlw\Database`.

---

## 0. Headline

1. **"Auto-planning" is not an optimiser.** `Horio.Logic.Planning.PlanningService`
   (`Planning/Auto.cs`) is a **bulk-apply** tool: pick employees + weekdays + a date range, then
   stamp a weekly-shift rota, a daily model, an absence, a correction, enter/exit times, multi-shift
   models, or copy one employee's plan onto others — straight onto `dbo.Clockings` rows. There is no
   demand solver, no coverage optimiser, no assignment algorithm anywhere in `Logic\`. The word
   "auto" names the **Planning → Auto** screen (`Constants.MODULE_PLANNING_AUTO`, `Auto.cs:859,917`),
   not automation.

2. **There is no working-time-directive validation in the product.** `Employees.WTDOptOut` and
   `Employees.HoursWTD` are **data-only** columns: read by the employee import/export mapping
   (`87.V5.33.0.0.sql:621-622,905-906`) and nowhere else. `grep` for `WTD` across all of
   `Source\Logic\*.cs` matches **only** `HorioDB.designer.cs`. `ARCHITECTURE.md` §8 lists
   "working-time-directive validation" under Scheduling as a *capability to build* — correct, but it
   is a **WM invention**, not a legacy behaviour to mirror. `WeeklyShift.WeeklyMin/WeeklyMax/
   WeeklyAverage` (`:17338-17342`) are advisory display fields on the rota template
   (`WeeklyModelController.cs`), enforced by nothing.

3. **"Open shift → eligible employees" has no legacy eligibility engine.** Roster notifications
   (`RosterNotificationService.GetDataForNotification:67-175`) fire for employees a manager has
   **already** rostered onto a plan (a `Vacancy` row), de-duplicated by `RosterNotificationLogs`.
   The notification *reports* the position's required qualifications and the employee's
   qualifications side by side (`:106-110, :141-145`) but **filters on neither** — eligibility is a
   manager's manual judgement at assignment time. WM's promised "eligible employees" audience (§13,
   `ARCHITECTURE.md:343`) is therefore **new logic WM must design**, not lift.

4. **Two parallel planning mechanisms feed the same Clocking row, with a precedence rule.**
   A **weekly rota** (`WeeklyShifts`) is the repeating baseline; a **dated roster plan**
   (`Plans`/`Vacancies`) overrides it for specific days. Assigning a vacancy calls
   `SetClockingDataFromPlanByEmployeeId`; removing it reverts to
   `SetClockingDataFromPeriodicTemplateByEmployeeId` (`RosterCalendarService.cs:121-127, :63, :242`).
   The dated plan wins; the weekly rota is the fallback.

5. **The scheduling→calculation seam is the Clocking row itself.** Scheduling does not hand a
   separate "planned shift" object to the engine. It **writes the planned template onto the clocking**
   — `DailyModelID`, `Enter1Start/End`, `Enter2Start/End`, `Exit1Start/End`, `Exit2Start/End`,
   `DurationTheoretic`, `BreakDurationMin` (`Auto.cs:485-524, :606-614`) — and the engine's
   shift-matching stage (plan 013, `HoursCalculationService.cs:420-497` +
   `DailyModelShiftMatchingRules`) matches **actual swipes** against those planned windows.
   `GlobalScheduleThresholds` supplies the in/out grace around them (see §6).

---

## 1. Ground-truth measurement, and a correction to the bucket count

`COVERAGE-AUDIT.md:62` records Scheduling as **16 tables / 251 columns, "named in §14, not
surveyed."** The audit's bucketing script "was never committed" (`COVERAGE-AUDIT.md:137`), so the
exact 16 are **not reproducible**. Measured independently, the scheduling *area* is both **wider and
narrower** than that line implies:

- **Narrower in owned entities.** The base scheduling entities total **~83 columns across 13
  tables** (§2 below). The rest of the "251" is almost entirely two denormalised **report views** —
  `dbo.PlanningControlView` (**82 columns**, `:138953`) and `dbo.PlanningControlActivitiesView`
  (**20**, `:143048`), plus `UnifiedScheduledActivitiesReportView` (**59**, an Activities/reporting
  view). 82 + 20 + the roster/weekly cluster ≈ the audit's 251. **A view is not product to build.**
- **Wider in dependencies.** The eligibility substrate — `Positions`, `EmployeePositions`,
  `PositionsQualifications`, `PositionsDepartments`, `Qualifications`, `EmployeeQualifications` — is
  **already bucketed to People/HR** (`TLW-PEOPLE-MODEL.md:50`, "D. Positions + qualifications, 8
  tables / 50 cols"). Scheduling *reads* it and cannot function without it. This is a
  **context-boundary decision for the user** (§7, open question 1), not a fact the count settles.

**Proposed correction to `COVERAGE-AUDIT.md:62`** (for the consolidated pass): replace the bare
"16 / 251" with "≈13 base tables / ~83 columns of owned scheduling entities; the 251 figure is
inflated by ~161 columns of denormalised planning **views** (`PlanningControlView` 82,
`PlanningControlActivitiesView` 20, `UnifiedScheduledActivitiesReportView` 59) and by the
Positions/Qualifications cluster that People/HR bucket D already owns."

### 1a. Full classification — every scheduling-area table, 0 unclassified

| Table | Cols | Line | Role | Class |
|---|---:|---:|---|---|
| `WeeklyShifts` | 12 | 17328 | Rota template (name, weekly min/avg/max, colour, dept) | **Improve** |
| `WeeklyShiftDetails` | 7 | 17710 | One row per `DayOfWeek` → a `DailyModel` (+2 absence slots) | **Keep** |
| `Link_Employee_WeeklyShift` | 4 | 18164 | Employee ↔ rota assignment (+`SortOrderId`) | **Keep** |
| `WeeklyShiftsGridView` | 6 | 18380 | View over the above | **Drop** (view) |
| `Rosters` | 3 | 109075 | Named roster template, per department | **Keep** |
| `RosterPlans` | 10 | 109213 | Template demand line: `DayNumber`, Position, DailyModel, `TimeFrom/To`, `TotalQuantity`, CostCentre | **Improve** |
| `RosterVacancies` | 3 | 109532 | Template assignment (Employee ↔ RosterPlan) | **Keep** |
| `Plans` | 9 | 108645 | **Dated** demand line (same shape, `Date` not `DayNumber`) | **Improve** |
| `Vacancies` | 3 | 108453 | **Dated** assignment (Employee ↔ Plan) | **Keep** |
| `RosterNotificationTemplates` | 7 | 110216 | Message templates for roster notifications | **Keep** |
| `RosterNotificationLogs` | 7 | 110422 | Sent-notification de-dup ledger | **Keep** |
| `GlobalScheduleThresholds` | 7 | 106022 | In/out grace windows for swipe→schedule matching | **Keep** |
| `EmployeeScheduleRequests` | 3 | 79158 | Employee self-service availability marks (EmpId, Date) | **Improve** |
| `Schedulers` | 2 | 182318 | Names a **set of pay `Periods`** — a period grouping, not shift scheduling | **Invert/Drop** (belongs to Rules; see §5) |
| `TimeTable` | 16 | — | Timetable feature (SCREEN-TREE lists "Timetables / Timetable creator") | **survey-later** |
| `TimetableCreatorSessions` | 6 | — | Timetable-builder scratch sessions | **Drop** (transient UI state) |
| `PlanningControlView` | 82 | 138953 | Denormalised planning-board read model | **Drop** (view) |
| `PlanningControlActivitiesView` | 20 | 143048 | View, activities overlay on planning | **Drop** (Activities/view) |

**Adjacent, bucketed elsewhere — named so nobody re-counts them here:**

| Table | Cols | Owner bucket | Note |
|---|---:|---|---|
| `Positions` | 7 | People/HR (D) | Job position w/ `DailyModelId`, dates, `IsActive` (`:109667`) |
| `EmployeePositions` | 4 | People/HR (D) | Employee's positions (preferences/capability) |
| `PositionsQualifications` | 2 | People/HR (D) | Position → required qualification |
| `PositionsDepartments` | 3 | People/HR (D) | Position → department |
| `Qualifications` | 7 | People/HR (D) | Skill/qualification catalogue |
| `EmployeeQualifications` | 17 | People/HR (D) | Employee's held qualifications |
| `DailyModelShiftMatchingRules` | 8 | Rules/calc (plan 013) | Swipe→shift matching rules — the **engine** side of the seam |
| `DailyModelSplitShifts` | 7 | Rules/calc | Split-shift sub-templates of a daily model |
| `DailyModelMultiShifts` | 4 | Rules/calc | Multi-shift sub-templates |
| `SplitShiftCounters` / `SplitShiftCounterValues` | 2 / 4 | Rules/calc | Counter accumulation per split shift |
| `ClockingShiftCorrections` | 6 | T&A/Clocking (plan 002) | Per-clocking shift corrections |
| `Rotageek*`, `HotSchedules*` | 13/4/4/2 | Connectors | External scheduling integrations (`IConnectorPlugin`, `ARCHITECTURE.md:225`) |

*(`DailyModel`/`WeeklyModel` themselves are the daily/weekly **templates** and are owned by Work
Rules / plan 012–013, not re-surveyed here.)*

---

## 2. The scheduling model (what a schedule, shift, rota, plan actually are)

Two independent structures, both resolving down to fields on a `dbo.Clockings` row:

### A. The weekly rota — the repeating baseline
- **`WeeklyShift`** = a named rotation pattern scoped to a department, carrying advisory weekly
  hour bounds (`WeeklyMin/Average/Max`), a colour/short-name/code, `IsSystem`/`IsActive`
  (`:17328-17617`).
- **`WeeklyShiftDetail`** = exactly the seven days: `(WeeklyShiftId, DayOfWeek, DailyModelId,
  FirstAbsenceId?, SecondAbsenceId?)` (`:17710-17762`). This is where "on a Monday this rota means
  *that* daily model" is stored. `Clocking` rows FK back to `WeeklyShiftDetailId` (`:17730`).
- **`Link_Employee_WeeklyShift`** = which employees are on the rota (`:18164`). Assigning a rota
  (`PlanningService.ApplyWeeklyShiftsToEmployees:271-338`) creates missing clockings for the range
  and stamps each day's `DailyModel` onto the clocking; `RescheduleEmployees:340-405` re-applies an
  employee's own rota from a start date forward.

### B. The dated roster — demand and coverage
- **`Roster`** (`:109075`) = a named template for a department; **`RosterPlan`** (`:109213`) = its
  demand lines by **`DayNumber`** (1..n within the pattern): *for this Position, in this Department,
  on day N, from `TimeFrom` to `TimeTo`, running `DailyModel`, we need `TotalQuantity` people*
  (optionally against a `CostCentre`). This is genuine **demand-based staffing** data — headcount
  targets per position per slot.
- The template is expanded into **dated** rows by the `dbo.SaveRoster` / `dbo.RestoreRoster` stored
  procedures (`RosterCalendarService.cs:174, :252`; procs live in `Database\Versioning\35–36`):
  - **`Plan`** (`:108645`) = a `RosterPlan` fixed to a real `Date` (identical columns, `Date`
    replaces `DayNumber`).
  - **`Vacancy`** (`:108453`) = an employee assigned to fill one slot of a `Plan`.
- **Open shift** = a `Plan` where `Vacancies.Count < TotalQuantity`. Computed inline as
  `NoVacancies = totalRequired == totalRostered` (`EmployeeSchedulingService.cs:58-62`). There is no
  "open shift" table or status — it is a live count comparison.

### C. Planned vs actual
- **Planned** lives as template fields written onto the clocking (daily model, enter/exit windows,
  theoretical duration).
- **Actual** = the swipes captured against that clocking (plan 002/012).
- The clocking is the single row that carries both, which is why `dbo.Clockings` is 249 columns and
  why scheduling has no separate "planned shift" entity: **the plan is materialised into the
  clocking at assignment time**, not matched at calculation time.

---

## 3. Auto-planning / bulk apply — what `PlanningService` computes

Every operation is *"take a selection, write a template onto its clockings."* None solves anything.

| Operation | `Auto.cs` | What it writes |
|---|---|---|
| `ApplyWeeklyShiftsToEmployees` | 271 | rota → per-day DailyModel onto clockings, creating missing ones |
| `RescheduleEmployees` | 340 | re-apply employee's own rota from a date |
| `ApplyDailyModelToEmployees` | 407 | one DailyModel + its enter/exit windows onto matching weekdays (raw SQL `UPDATE clockings`, `:485-504`) |
| `ApplyEnterExitTimeToEmployees` | 526 | find-or-create a DailyModel by times, stamp it |
| `ApplyMultiModelsToEmployees` | 626 | up to **6** sub-model slots `DailyModelId1..6` onto clockings (raw SQL, `:649-668`) |
| `ApplyAbsenceToEmployees` | 19 | morning/afternoon absence + theoretical durations |
| `ApplyCorrectionToEmployees` | 206 | correction id + duration |
| `CopyPlanningOfEmployee` | 716 | day-by-day copy of one employee's clocking template + scheduled activities onto others (raw `WHILE` loop SQL, `:764-804`) |
| `ApplyScheduledActivity` | 923 | append a `ClockingScheduledActivity` (Activities bucket) |

**Smells found here:**
- **Fixed numbered slots (ceiling).** `DailyModelId1..DailyModelId6` on the clocking — six hard
  multi-shift slots (`Auto.cs:652-657`, `DailyModelMultiShifts` cols). A seventh sub-shift is
  unrepresentable. Same family as the `BadgeTime1..12` / `CPTN01..20` ceilings already on record.
- **Stored calculated values written wholesale.** Each apply persists `DurationTheoretic`,
  `BreakDurationMin`, and the eight enter/exit boundary columns directly onto every clocking
  (`Auto.cs:606-614`), rather than deriving them from the template at read time. This is exactly the
  materialise-everywhere pattern WM's replay design replaces.
- **`GetLastCalculationDate()` returns a hard-coded `1900-01-01`** (`Auto.cs:905-908`), so the
  "don't plan before the last calculation" guard (`IsParametersValidOnDailyModelApply:891-899`) is
  **dead** — it never blocks. A fail-open guard.
- **Silent absence-on-day-off widening.** When an absence is `!AllowOnDayOff`, rows on the
  system "SANS" model are filtered out (`Auto.cs:52-55`) — a `default`-style carve-out worth
  re-checking against WM's intended semantics.

---

## 4. Open shifts, eligibility, and notifications

- **Eligibility is not enforced in code.** `RosterNotificationService.GetDataForNotification`
  (`:67-175`) notifies **already-assigned** employees (`Vacancies`), joining in the position's
  required qualifications and the employee's qualifications for **display only**. The manager
  chooses who fills a plan in the UI; the position/qualification data is decoration.
- **De-dup ledger.** A notification is suppressed if a `RosterNotificationLogs` row already exists
  for `(PlanId, PositionId, DailyModelId, EmployeeId)` (`:162-166`). So re-saving a roster does not
  re-spam; changing the daily model or position **does** re-notify (different key).
- **Employee availability is a separate, minimal signal.** `EmployeeScheduleRequest` = `(EmpId,
  Date)` (`:79158`), toggled by the employee via `EmployeeSchedulingService.SaveRequests:85-126`
  (past dates ignored, `:109`). It surfaces on the self-service calendar as `IsRequested`
  (`:54`) but **feeds no assignment logic** — a manager sees it, nothing consumes it.
- **The "eligible employees" audience WM promises is undefined by legacy.** Building it (skills ∩
  site ∩ contract hours ∩ availability) is a genuine WM improvement, but it is **design work with no
  legacy spec** — see §7 open question 2.

---

## 5. Schedule compliance / WTD

- **No WTD engine exists.** `WTDOptOut` / `HoursWTD` are import/export columns only
  (`87.V5.33.0.0.sql:621-622, :905-906`); no `Logic\` service reads them.
- **No max-hours, rest-break, or consecutive-day validation** anywhere in `Logic\` (searched
  `WTD|WorkingTime|maxhours|consecutivedays|restbreak`). The only weekly bounds are the advisory
  `WeeklyShift.WeeklyMin/Max/Average`, displayed and never checked.
- **`GlobalScheduleThresholds` is not compliance** — it is swipe-matching tolerance (§6).
- **Conclusion:** "schedule compliance / WTD validation" in `ARCHITECTURE.md` §8 is a **build-new**
  item. Classify **Invert-from-absence**: legacy fails open (no validation at all); WM should fail
  closed (validate and warn). The employee attribute `WTDOptOut` (People, unowned per
  `ARCHITECTURE.md:423`) is the input this validation would read.

---

## 6. The shift-matching seam (what scheduling supplies the engine)

- Scheduling's **output** is the planned template on the clocking: `DailyModelID`, the four
  enter/exit window pairs, `DurationTheoretic`, `BreakDurationMin` (`Auto.cs:494-504`,
  `ApplyDailyModelSettingToDb`).
- `GlobalScheduleThreshold` carries **two** in/out grace pairs — `ScheduleInThreshold1/2`,
  `ScheduleOutThreshold1/2` (`:106022-106208`) — attached to `DailyModel`s
  (`GlobalScheduleThreshold_DailyModel`, `:106210`). These bound how far an actual swipe may sit
  from a scheduled boundary and still be treated as on-schedule. Two pairs = two shift segments.
- The **engine** side (plan 013, out of scope here) reads that planned window + threshold and does
  the matching via `HoursCalculationService.cs:420-497` and `DailyModelShiftMatchingRules` (8 cols).
- **Boundary for WM:** Scheduling *owns* producing the planned window + which daily model applies +
  the threshold configuration. The engine *owns* comparing actual swipes to it. In WM's replay
  design this means Scheduling emits a **planned-shift fact** (employee, date, daily model,
  windows, thresholds) that the calculation replay consumes — rather than legacy's approach of
  pre-writing the fields onto the row. That inversion (event, not stamped column) is the main
  Keep-vs-Improve decision (§7, open question 3).

---

## 7. Open questions for the user (product calls, not surveyable)

1. **Where do `Positions` / `Qualifications` live?** People/HR bucket D owns them
   (`TLW-PEOPLE-MODEL.md:50`), but Scheduling is their only heavy consumer (demand lines reference
   `PositionId`; eligibility would read qualifications). Options: (a) People owns them, Scheduling
   reads via contract; (b) move the position↔demand↔qualification-matching slice to Scheduling. DDD
   says the *catalogue* (a qualification exists) is HR; the *demand for it* (this slot needs it) is
   Scheduling. Recommend (a) with a `Positions`/`Qualifications` read contract.

2. **Define "eligible employee" for open-shift offers.** Legacy has no rule — it shows required vs
   held qualifications and lets a human decide. WM's §13 promise implies an automatic audience. What
   are the criteria: qualification match, department/site scope, contract-hours headroom,
   `EmployeeScheduleRequest` availability, some/all? This is the single largest piece of *new* logic
   in the area.

3. **Planned shift: stamped column or emitted event?** Legacy materialises the plan onto the
   clocking (§6). WM's replay design argues for a planned-shift **event** the calculation consumes.
   Confirm the inversion so `wm-builder` does not reproduce the column-stamping.

4. **`ARCHITECTURE.md` §8 wording (propose-only).** §8 lists "auto-planning" and "working-time-
   directive validation" as if mirroring TLW. Neither exists in legacy. Suggested edit: mark both
   explicitly as **WM additions** ("auto-planning" today = bulk template application; a real
   optimiser and WTD validation are net-new), so the roadmap does not imply a legacy port. Not
   applied here — §8 is a design section.

---

## 8. What I did NOT measure (numbered, so the next reader knows the edge)

1. **`TimeTable` (16) / `TimetableCreatorSessions` (6)** — SCREEN-TREE lists "Timetables /
   Timetable creator" under Scheduling. I confirmed the tables exist and their sizes but did **not**
   read their columns or the timetable services. If Scheduling is planned, survey these first.
2. **`PlanningControlView` (82) columns individually** — confirmed it is a denormalised read view,
   not base data; did not enumerate its 82 columns.
3. **The `dbo.SaveRoster` / `dbo.RestoreRoster` / template-expansion stored procedures** — located
   (`Database\Versioning\35.V3.5.0.0.sql`, `36.V3.6.0.0.sql`) and understood via their C# callers,
   but did not read the T-SQL bodies line by line. The swipe-in-T-SQL trap (plan 012) means the
   expansion logic could hold edge cases; read before building roster expansion.
4. **`RosterNotificationTemplates` (7) token/placeholder grammar** — the notification *sends*; I did
   not enumerate the template's substitution tokens.
5. **`Schedulers` (2) / `Period` relationship** — confirmed `Scheduler` groups pay `Periods`
   (`:182386`); did not trace whether it belongs to Rules' period model. Flagged as likely
   mis-bucketed into Scheduling.
6. **WebSite screen surface** — read `RosterCalendarService`, `EmployeeSchedulingService`,
   `RosterNotificationService`, `PlanningService`; did **not** open the Angular/MVC planning-board
   or roster-calendar screens. The drag-and-drop board behaviour is UI-only and unsurveyed.
</content>
