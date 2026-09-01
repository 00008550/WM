# 017 — Scheduling foundation: the rota, the roster, and the planned-shift seam

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 item 7 (Scheduling) / §8.3 / Phase 5
Legacy sources surveyed: `HorioDB.designer.cs` (schema measurement, 578 tables / 8,173 cols),
`Planning/Auto.cs`, `Interfaces/IPlanningService.cs`, `Settings/Rostering/RosterCalendarService.cs`,
`Settings/Rostering/EmployeeSchedulingService.cs`, `Settings/Notifications/RosterNotificationService.cs`,
`Database/Versioning/87.V5.33.0.0.sql`. Full write-up: `docs/TLW-SCHEDULING-MODEL.md`.

> **This is a foundation plan, not the whole module.** Scheduling carries three unresolved product
> decisions (see *Open questions* — positions ownership, eligibility definition, planned-shift
> inversion). This plan builds only the slice that is decidable now and that other modules already
> lean on: the **planned-shift fact** the calculation engine needs. Auto-planning-as-optimiser, the
> open-shift eligibility engine, and WTD validation are **explicitly deferred** to follow-on plans
> once the user answers. Do not let this plan grow into them.

## Ground truth

Measured (`TLW-SCHEDULING-MODEL.md` §1–§2). Owned scheduling entities: **~83 columns / 13 base
tables**, not the "16 / 251" of `COVERAGE-AUDIT.md:62` — that figure double-counts two report
**views** (`PlanningControlView` 82, `PlanningControlActivitiesView` 20,
`UnifiedScheduledActivitiesReportView` 59) and the Positions/Qualifications cluster People/HR
already owns. Correction proposed in `TLW-SCHEDULING-MODEL.md` §1 (locked doc; user applies).

Two planning structures, both materialising onto the `dbo.Clockings` row:
- **Weekly rota** — `WeeklyShift` (12) + `WeeklyShiftDetail` (7, one per `DayOfWeek`) +
  `Link_Employee_WeeklyShift` (4). The repeating baseline.
- **Dated roster** — `Roster`/`RosterPlan` (template, by `DayNumber`) expanded to `Plan`/`Vacancy`
  (dated) by `dbo.SaveRoster`. Demand = `RosterPlan.TotalQuantity`; open shift = `Vacancies < Total`.
  Dated plan overrides weekly rota (`RosterCalendarService.cs:121-127`).

WM today: **no Scheduling module** (`ARCHITECTURE.md:124` ▢ planned). Nothing in `src/Modules`
owns rotas, plans, or planned shifts. The calculation-engine work (plan 013) consumes a planned
shift it currently has no producer for.

## Legacy behaviour (what we are replacing)

- **"Auto-planning" = bulk stamp**, not optimisation (`Auto.cs`, `TLW-SCHEDULING-MODEL.md` §3).
- **Planned shift is a stamped column set on the clocking**, not an object: `DailyModelID`,
  `Enter1Start/End`, `Enter2Start/End`, `Exit1Start/End`, `Exit2Start/End`, `DurationTheoretic`,
  `BreakDurationMin` (`Auto.cs:485-524`).
- **`GlobalScheduleThreshold`** (7) = two in/out grace pairs bounding swipe→schedule matching,
  attached to daily models (`:106022-106210`).
- **No WTD validation, no eligibility engine** anywhere in `Logic\` (§4–§5).

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Weekly rota (WeeklyShift + 7 day details) | **Keep** | A repeating weekly pattern of daily models is genuine domain truth |
| Roster demand line (`RosterPlan`/`Plan`: position × slot × quantity) | **Improve** | Right idea; `DayNumber` template + `Date` instance duplicated into two table pairs — WM models one demand entity with a template/instance distinction |
| `DailyModelId1..6` six multi-shift slots on the clocking | **Improve** | Fixed numbered slots = hard ceiling; WM uses ordered sub-shift rows |
| Planned shift stamped onto the clocking | **Invert** | WM emits a planned-shift **event/fact** the calc replay consumes; legacy pre-writes derived columns everywhere (the pattern replay exists to kill) |
| `GetLastCalculationDate()` → `1900-01-01` dead guard | **Invert** | Fail-open; WM's guard must be real |
| WTD "validation" | **Invert-from-absence** | Legacy validates nothing; WM fails closed (deferred plan) |
| Open-shift eligibility | **Improve/new** | Legacy leaves it to the manager; WM's promised audience is net-new (deferred) |
| `PlanningControlView`, `WeeklyShiftsGridView`, `TimetableCreatorSessions` | **Drop** | Views / transient UI state |
| `Schedulers` (period grouping) | **Drop from Scheduling** | Belongs to Rules' period model |

## Edge cases

- **Rota vs dated plan precedence.** A dated `Plan`/`Vacancy` overrides the weekly rota for that
  day; deleting the vacancy reverts to the rota template (`RosterCalendarService.cs:63,121,242`).
  Test both directions.
- **Missing clocking.** Applying a rota **creates** absent clockings for the range
  (`Auto.cs:302, CreateMissingClockings`). WM's producer must define what a planned shift means on a
  day with no clocking yet.
- **Past-date availability.** `EmployeeScheduleRequest` silently ignores past dates
  (`EmployeeSchedulingService.cs:109`).
- **Open-shift boundary.** `NoVacancies = totalRequired == totalRostered` — over-rostering
  (`> Total`) also reads as "full"; decide WM semantics.
- **Night shift / midnight.** Enter/exit windows can cross midnight; the day a planned shift belongs
  to follows the same swipe→day rule plan 008/010 measured — reuse it, do not reinvent.

## Target design in WM

New `WM.Modules.Scheduling` (schema-isolated, licensable — `ARCHITECTURE.md:205,215`). It reads
People (positions/qualifications) and Work Rules (daily/weekly models) **via contracts only**.
Its first job is to **produce the planned-shift fact** the calculation engine (plan 013) consumes —
an event on Kafka (`ARCHITECTURE.md` invariant 4), not a stamped column. Endpoints are REST-first
(invariant 2); every endpoint carries an authorization policy (invariant 5).

## Out of scope for this plan

Auto-planning optimiser; open-shift eligibility engine and its notification audience; WTD/schedule
compliance validation; the drag-and-drop planning board UI; timetables; external scheduling
connectors (Rotageek/HotSchedules). Each is its own later plan and most are gated on an open
question below.

## Portions

### [ ] P1 — The planned-shift contract and event
**Touches:** `src/Modules/Scheduling/*` (new module skeleton), `src/SharedKernel` (event contract),
calc-engine consumer seam.
**Done when:** a `PlannedShift` fact (employee, date, daily-model ref, enter/exit windows,
thresholds) is defined as a contract + Kafka event, and plan 013's shift-matching stage can consume
it in place of reading stamped clocking columns.
**Tests:** contract round-trips; a planned shift with two segments (two enter/exit pairs) matches
the legacy field set; a night-shift crossing midnight resolves to the correct day.
**Risk:** medium (defines the seam other work depends on)

### [ ] P2 — Weekly rota (WeeklyShift + day details + employee assignment)
**Touches:** Scheduling module (entity, migration, service), one endpoint, one screen, tests.
**Done when:** a rota of seven daily-model slots can be created, an employee assigned, and the rota
produces planned-shift facts (P1) for a date range.
**Tests:** 7-day rota → 7 facts/week; reassignment; the advisory `WeeklyMin/Max` are stored, not
enforced (record as deliberate).
**Risk:** medium

### [ ] P3 — Roster demand (template + dated instance) read model
**Touches:** Scheduling module (Roster/RosterPlan template, Plan/Vacancy instance), endpoint, tests.
**Done when:** a roster template expands to dated demand lines with a headcount target, and
coverage (assigned vs required) is queryable; a dated plan overrides the rota for its day.
**Tests:** template→instance expansion; open-shift count (`assigned < required`); rota-override
precedence both directions (assign then unassign reverts to rota).
**Risk:** medium

### [ ] P4 — Schedule thresholds and the grace-window config
**Touches:** Scheduling module (threshold entity), contract to the engine, tests.
**Done when:** in/out grace windows are configurable per daily model and travel on the planned-shift
fact, replacing legacy `GlobalScheduleThresholds`.
**Tests:** a swipe inside/outside the grace resolves on/off-schedule at the engine seam.
**Risk:** low

## Open questions for the user

1. **Positions/Qualifications ownership** (`TLW-SCHEDULING-MODEL.md` §7.1). People/HR bucket D owns
   them; Scheduling is their heavy consumer. Recommend People owns, Scheduling reads via contract —
   confirm before P3, which references `PositionId`.
2. **Define "eligible employee"** for open-shift offers (§7.2). Legacy has no rule. This gates the
   deferred open-shift plan entirely; no portion here depends on it, but the answer shapes the next
   plan.
3. **Planned shift: event or stamped column?** (§7.3, and P1's premise). This plan assumes **event**
   per WM's replay design. If the user wants to mirror legacy's stamped columns instead, P1 changes
   shape — confirm before building P1.
4. **`ARCHITECTURE.md` §8 wording** (propose-only, §7.4): "auto-planning" and "WTD validation" are
   listed as if ported from TLW; neither exists in legacy. Suggest marking both as WM additions.
</content>
