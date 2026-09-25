# TLW Clocking model — the entity WM does not have

*Measured 2026-08-04 from `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (the LINQ-to-SQL
schema, 6.1 MB / 240,262 lines), `Logic\HoursCalculation\ProxyClocking.cs`,
`Logic\CountersAllocation\BaseCountersHolder.cs`, and the Obsidian vault at `E:\Tlw\Documentation`.*

> **Why this document exists.** `ARCHITECTURE.md` §13 records
> *"Clockings / swipes / swipe processing → TimeAttendance → ✅ built"*. That is wrong, and it is
> the most consequential error in the plan. WM has built **Punches** — individual swipe events.
> TLW's **Clocking** is something else entirely: a per-employee-per-day aggregate of 249 columns
> that is the actual product. Nearly every screen, report, rule and payroll export reads it.

---

## 1. Measured schema facts (corrects TLW-INVENTORY.md)

| Fact | Value |
|---|---|
| Tables in the LINQ-to-SQL model | **579** |
| Mapped columns | **8,173** |
| Associations | **1,147** |
| `TLW-INVENTORY.md`'s earlier claim | "247 entities" — roughly **half** the real table count |

**Widest tables** (the ones that define the product's real shape):

| Table | Columns | Note |
|---|---|---|
| `dbo.Devices` | 268 | dropped from WM scope |
| **`dbo.Clockings`** | **249** | **the widest table WM actually needs** |
| `dbo.SoftwareMainOptions` | 227 | per-install configuration; **47 columns are calculation and swipe rules**, 18 are dead — measured in [`TLW-GLOBAL-OPTIONS.md`](./TLW-GLOBAL-OPTIONS.md) (2026-08-30) |
| `dbo.FireMarshalMusterPoints` | 177 | Safety, phase 6b |
| `dbo.UnifiedTimesheetReportView` | 167 | reporting view |
| **`dbo.Employees`** | **153** | WM's `Employee` has **12** |
| `dbo.DailyModels` (daily templates) | 124 | the ~150-field editor |
| `dbo.DailyBrowserView` | 126 | the main T&A screen |
| `dbo.ClockingsLog` | 97 | change audit of clockings |

---

## 2. What a Clocking actually is

From TLW's own glossary (`Documentation\Swipe to clocking allocation.md`):

> *"Clocking – a record of all employee swipes and calculations for the day."*

Three properties that WM's `Punch` model does not have:

1. **One row per employee per day**, holding *all* of that day's swipes plus every calculated
   result — not one row per swipe.
2. **Rows are pre-generated, not created by swiping.** `HorioService` generates the "calendar"
   of clocking records **overnight** for every employee
   (`Troubleshooting\No Calendar (Clockings) for employee.md`: *"Ones restarted, calendar
   (clocking) records will be generated overnight"*; see also `Logic\GenerateClockings\`).
   A day with no attendance still has a row — carrying expected duration, absence, exceptions
   and counters. **The Daily Browser renders a calendar, not a punch list.**
3. **A swipe's owning day is a calculation, not a fact.** Which clocking a swipe lands on depends
   on the *previous* and *next* day's daily template (night-shift end time, offset-to-next-day,
   allocate-to-previous/next-day). Change a template and swipes move between days.
   **Import divergence (plan 010 P1, edge case A3):** legacy applies an employee's master template
   (`dbo.EmployeeMasterDailyModels`) only when `StartDate <= yesterday AND EndDate >= tomorrow`
   (`37.V3.6.1.0.sql:768-769`); WM uses the inclusive window `StartDate <= date <= EndDate`. Migrated
   assignments therefore take effect one day earlier and end one day later in WM, and one-day
   assignments — dead in legacy — become live. An importer must decide per customer whether to
   carry the dates as-is or narrow them by a day at each end.

### 2.3a Where that calculation actually lives *(measured 2026-08-14)*

> **Corrected 2026-09-25 (plan 002 refresh).** The claim below that `30.V3.0.0` holds the *only*
> definition is wrong. The function is re-created at `32.V3.1.5.0.sql:370` and `33.V3.3.0.0.sql:1082`
> and last **altered** at `37.V3.6.1.0.sql:748-868` — the live definition, which plan 010 P2 ported
> (`DayAllocationService.cs`). Its line numbers (`:775-867`) are the ones to cite; the table below
> keeps the `30.V3.0.0` numbering only as history. The allocation is **built** in WM (010 P2); plan
> 002 P3 puts the real Clocking store behind it.

It is a **T-SQL scalar function**, not C#: `dbo.ProcessQueryGetClockingForSwipe(@employeeid int,
@swipeTime datetime)`, defined at `Database\Versioning\30.V3.0.0.ProcessQuery module.sql:429-528`.
That is the **only** definition in the whole `Database` tree; later scripts only call it
(`67.V5.13.0.0.sql:3321`, `:3898`; `73.V5.19.0.0.sql:232`). Nothing in `Source\Logic` implements it —
a `Grep` for `InterswipeIntervalToMoveToYesterday` across `Logic` hits only the generated model
(`HorioDB.designer.cs`, `HorioDB.dbml`), and `NightShiftStartTime` / `ShiftToSunday` appear in
`Logic` in no hand-written file at all.

It opens by discarding the instant — `convert(date, @swipeTime)` / `convert(time, @swipeTime)`
(`:440-441`) — and then runs **five** branches, first match wins:

| # | Rule | Column | Line |
|---|---|---|---|
| 1 | → yesterday if `@time <` yesterday's `NightShiftEndTime` | `DailyModels.NightShiftEndTime` | `:449-454` |
| 2 | → tomorrow if tomorrow's template has `ShiftToSunday = 1` and `@time > NightShiftStartTime` | `ShiftToSunday`, `NightShiftStartTime` | `:457-463` |
| 3 | → yesterday if no swipes today and `yesterday + firstSwipe + interval > @swipeTime` | `InterswipeIntervalToMoveToYesterday` | `:465-494` |
| 4 | → yesterday if yesterday's template is `ModelType = 7` (shift matching) and a matched model has `NightShiftEndTime > @time` | `DailyModelShiftMatchingRules` | `:496-524` |
| 5 | → today | — | `:527` |

Three things this adds to §2.3 above:

- **Branch 4 is a full fifth rule.** `Documentation\Swipe to clocking allocation.md` gives it one
  sentence at `:55` and no worked example.
- **No time zone enters at any point.** Every comparison is `time` against `time`. See
  [`TLW-TIME-MODEL.md`](./TLW-TIME-MODEL.md) §3.
- **A swipe whose resolved day has no pre-generated clocking row is rejected** — the function
  returns `NULL` and the caller answers `'Clock Record not found'`, filing the swipe as unsuccessful
  (`73.V5.19.0.0.sql:233-256`). This is the mechanism behind
  `Documentation\Troubleshooting\No Calendar (Clockings) for employee.md`, and it means §2.2's
  pre-generated calendar is **load-bearing, not a convenience**.

The vault documents four UI labels; the columns behind them
(`WebSite\Views\DailyModel\_AddEditDailyModelControls.cshtml:1527-1539`) are:
*Night Shift End Time* → `NightShiftEndTime`; *Offset Transaction to Next Day* → **`ShiftToSunday`**
(a `Bit`; the name is historical); *If the swipe is after* → `NightShiftStartTime`;
*Allocate Transactions to Previous/Next Day* → `InterswipeIntervalToMoveToYesterday`.

Finally, the time of day **survives the move**: after allocation the caller re-composes
`@dateTime = CONVERT(datetime, @time) + CONVERT(datetime, @date)` (`73.V5.19.0.0.sql:259-260`), so a
02:00 swipe allocated to the previous day is stored as **02:00 on that previous day** — deliberately
not the instant it happened.

---

## 3. Anatomy of the 249 columns

### 3a. Twelve badge slots — six IN/OUT pairs
`BadgeTime1..12`, documented as *1st IN, 1st OUT, 2nd IN … 6th OUT*. Each slot carries a
**family of seven columns**:

| Per-slot column | Meaning |
|---|---|
| `BadgeTimeN` | the effective time |
| `BadgeTimeNGeneratedBy` | provenance (`BadgeTimeGeneratedBy` enum) — swiped vs auto-generated |
| `BadgeTimeNAdjusted` | after rounding/cut-off rules |
| `DeviceBadgeTimeN` | the raw time the device reported, before adjustment |
| `BadgeTimeNLocation` | geolocation, `NVarChar(250)` |
| `SwipeSourceIdN` + `SwipeSourceN` | where it came from (id + name) |
| `SwipeReaderTypeN` | reader type (device-era; WM drops the hardware, not the concept of a source) |

Keeping the raw device time *and* the adjusted time side by side is deliberate: rounding is
auditable and reversible. WM's `Punch` keeps one timestamp.

> **Every one of those slots is SQL `Time` — a naked time of day** (`HorioDB.designer.cs:21446`
> onward), hung off a single `Clockings.Date DateTime` (`:21426`). There is no offset and **no
> column that says which calendar day the N-th slot falls on**, so `Date + BadgeTimeN` is not an
> instant. Two shipped legacy exports get this wrong in the obvious way: People First emits an
> overnight `End` 22 hours *before* its `Start` (`TimeHelper.cs:198-200`) and Sage HR throws
> `Time in > Time out` and drops the whole clocking (`TimeSheetService.cs:148`).
> **Any WM projection that reproduces the flat 12-slot shape must carry the day-carry explicitly**
> or it inherits both bugs. Measured 2026-08-14 — [`TLW-TIME-MODEL.md`](./TLW-TIME-MODEL.md) §4a.

### 3b. Twenty pay categories — fixed slots
`CPTN01 … CPTN20`, `decimal(12,5)`. TLW's own SQL guide maps them explicitly:

> `CPTN01 → Pay Category 1` … `CPTN20 → Pay Category 20`

- Their **names are per-installation config**: `PayCategoryNamesModel` is literally
  `Counter1Name … Counter20Name`.
- Allocation is a **hard-coded switch on counter id → column**
  (`BaseCountersHolder.AddToCounter`: `case 1: CPTN01 …` through `case 20:`).
- **There is a hard ceiling of 20.** A 21st pay category is impossible without both a schema
  migration and a code change. This is a real constraint customers live with today.
- `CPTT01` exists as a separate single total slot.

### 3c. Six shifts — every measure repeated per shift
`DailyModelId1..6`, `CostCentreId1..6`, and each calculated measure duplicated six times:
`calc_grossAttendanceShift1..6`, `calc_netAttendanceShift1..6`, `calc_differenceShift1..6`,
`calc_correctionShift1..6`, `calc_presenceCorrectedShift1..6`, `calc_absencesBreaksShift1..6`,
`calc_actualWorkShift1..6`, `calc_dayHoursShift1..6`, `calc_nightHoursShift1..6`,
`calc_breaksDurationShift1..6`, plus `BadgeTime1AdjustedShift1..6` and `BadgeTime2AdjustedShift1..6`.

This is split-shift and multi-shift support, denormalized. **Cost centre is per shift**, which is
how a day's hours get split across cost centres.

### 3d. Calculated results (`calc_` prefix)
`grossAttendance`, `netAttendance`, `difference`, `correction`, `presenceCorrected`, `actualWork`,
`dayHours`, `nightHours`, `panDay`, `panNight`, `absencesBreaks`, `balance`, `breaksDuration`,
`latenessTimes`, `latenessMinutes` — all `decimal(18,8)`.

These are **stored, not derived on read**. That is exactly the design that forced legacy's
whole-estate nightly recalculation and its separate `Reprocessor` project, and it is the pain
`ARCHITECTURE.md` §7A's Kafka-replay design is meant to solve.

> **Superseded 2026-09-03 by `ARCHITECTURE.md §0a` decision 2 (user):** WM **stores** calculated
> state on the Clocking too, because the aggregate is human-editable; provenance is per value
> (decision 3). Plan 002 P7 stores each value's calculated and manual halves side by side. The
> whole-estate nightly recalculation is still not inherited: a recalculation is explicit and
> scoped (002 P8). §6 point 4 below is superseded the same way.

### 3e. Absence, correction and exception state on the row
`MorningAbsenceID`, `AfternoonAbsenceID` (half-day absences live **on the clocking**),
`CorrectionId`, `CorrectionDuration`, `CorrectionCostCentreId`, `IsDTManualChanged`,
`ScoresAbnormalitiesSetupId`, `ShouldHideExceptions`, `Explanation` (`NVarChar(1000)` free text),
`DelayQuantity`, `DelayDuration`, `PauseDuration`, `HourlyRateId` (tariff link),
`WeeklyModelDetailsId`, `CheckSumValue` (tamper/change detection), `UpdatedBy`.

### 3f. Shift-matching windows
`Enter1Start/End`, `Exit1Start/End`, `Enter2Start/End`, `Exit2Start/End`, `DurationTheoretic`,
`BreakDurationMin` — the resolved template windows, snapshotted onto the row.

---

## 4. Two behaviours the rules engine must reproduce

### Contract overrides employee defaults
Every threshold resolves through an `…Effective(contracts)` method
(`Logic\Entities\BusinessRules\EmployeeCounters.cs`): if the employee has a `ContractId` and that
contract exists, the **contract's** value wins; otherwise the employee's own value applies.
Applies to `WeeklyThreshold1..4`, `RCThreshold`, `CounterHS`, `CounterYearHS` and more.
**WM models neither employee contracts nor thresholds.**

### Per-template custom SQL — and how much power it has
`Documentation\Custom SQL\Daily Template SQL.md` shows the escape hatch WM intends to replace with
a "safe rules expression language". The required expressive power is higher than §14 implies:

- placeholders `{0}` = EmployeeId, `{1}` = ClockingId;
- `select`/`update` only, `update` must be keyed by `ClockingId` or `EmployeeId + Date`;
- runs **during calculation** of each record, with an option to *reprocess after execution*;
- real examples in the doc include **cross-row aggregation over a date window for the same
  employee** — e.g. "set Pay Category 15 on Sunday to the sum of Pay Category 15 for the week",
  and "set Pay Category 10 on Saturday if there are swipes Mon–Sat".

So the replacement language needs: conditional assignment to a counter, weekday predicates,
absence-id predicates, comparisons against calculated values, and **windowed aggregation across
other days for the same employee**. A per-row expression evaluator is not sufficient.

---

## 5. What WM has today

| TLW | WM today |
|---|---|
| `Clockings` — 249 cols, one per employee per day, pre-generated | **nothing** |
| `BadgeTime1..12` with provenance/adjusted/raw/location/source | `Punch` — one row per event, one timestamp |
| `CPTN01..20` pay categories | **nothing** |
| 6 shifts with per-shift cost centres and measures | **nothing** |
| Stored `calc_*` results | timesheet computed on read |
| `DailyModels` — 124 cols | `DayTemplate` — the five allocation columns + master override (010 P1, 2026-09-24) |
| `Employees` — 153 cols | `Employee` — 12 |
| `SoftwareMainOptions` — 227 cols | **nothing** — and 47 of those columns govern how hours and punches are calculated ([`TLW-GLOBAL-OPTIONS.md`](./TLW-GLOBAL-OPTIONS.md)) |

WM's punch pipeline is a sound *input* to this model — an append-only event stream is a better
foundation than a mutable row. What is missing is the **aggregate the whole product reads**.

---

## 6. Consequences

1. **§13's `✅ built` for Clockings is wrong.** WM built punch capture, which is the input to a
   Clocking, not a Clocking.
2. **Phase 2 is under-scoped.** §14 lists "daily templates, weekly models, counters, flexi
   balances, pay categories, cost-centre allocation" at 10–16 weeks without naming the
   aggregate they all read and write. The Clocking aggregate and its generation job are
   prerequisites for all of it.
3. **A design decision is now unavoidable and belongs to the user:** WM should almost certainly
   *normalise* what legacy denormalised — punches as rows rather than 12 slots, counter values as
   rows rather than 20 columns, shifts as rows rather than a ×6 suffix. That removes the hard
   20-counter ceiling and the 12-swipe ceiling. But every payroll export, report and customer
   mental model is shaped by the flat form, so the flat shape must remain reproducible as a
   *projection*. See plan 002.
4. **The replay design is vindicated but must be explicit.** Storing `calc_*` on the row is what
   made legacy need nightly whole-estate recalculation. WM's Kafka projection approach is the
   right answer, and the Clocking aggregate is precisely the projection to rebuild.
5. **`ClockingsLog` (97 columns)** shows clocking changes are separately audited. WM's audit
   design must cover this.
