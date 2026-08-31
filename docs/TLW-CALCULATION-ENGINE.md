# TLW hours calculation engine — measured

Surveyed 2026-08-30. Sources are `E:\Tlw` only. Every claim below has a `file:line`.
This document supersedes any prior WM statement about the engine; nothing here was taken
from `TLW-INVENTORY.md`, `ARCHITECTURE.md` or a previous plan.

---

## 1. The measurement

### 1.1 Size

`E:\Tlw\Source\Logic\HoursCalculation\` — 866 KB, 38 files at the root plus three subfolders
(`Adjusters` 10 files, `Calculators` 36 files, `DAL` 1 file).

| File | Lines |
|---|---|
| `HoursCalculationService.cs` | 5,665 |
| `HoursCalculationServiceUtils.cs` | 1,794 |
| `ProxyClocking.cs` | 1,248 |
| `DAL/HoursCalculationRepo.cs` | 979 |
| `HoursCalculationServiceMultiModel.cs` | ~700 |
| `HoursCalculationServiceSplitShifts.cs` | ~400 |
| 36 `Calculators/*` + 10 `Adjusters/*` | ~4,000 combined |

**The 276 KB file is not one algorithm.** By `#region` (`HoursCalculationService.cs:32-4664`) it is
six things, and the largest is not calculation:

| Region | Lines | What it is |
|---|---|---|
| Entry points + orchestration | 132–990 | the actual pipeline |
| Absences and breaks | 993–1530 | break matching, auto-pause closing |
| Util / adjusted swipes | 1531–2572 | swipe fixing, presence-corrected, custom SQL |
| **Exceptions** | **2573–3829** | **1,256 lines — 22 named exception rules, zero hours arithmetic** |
| Counters | 3944–4202 | CPTN01–20 allocation |
| Cost-centre counters | 4203–4663 | per-cost-centre re-run of the same allocation |

Roughly **22% of the biggest file is exception detection**, not calculation. That is the single
most important sizing fact in this document: the arithmetic is small, the *judgement about whether
a day is trustworthy* is large.

### 1.2 Schema

`HorioDB.designer.cs`: **579 tables, 8,173 columns**.

| Table | Columns | Role in this engine |
|---|---:|---|
| `dbo.Clockings` | 249 | the record being calculated and written back |
| `dbo.DailyModels` | 124 | **the configuration that drives calculation** |
| `dbo.SoftwareMainOptions` | 227 | global switches — only 13 reach the engine |
| `dbo.Calculations` | 45 | global calculation settings — only 17 reach the engine |
| `dbo.ClockingsLog` | 97 | audit copy written by `clocking.Log()` |

---

## 2. Inputs → stages → outputs

### 2.1 Entry points

Public surface is `IHoursCalculationService` (`IHoursCalculationService.cs:1-45`) — 28 members, of
which **3 calculate** and **25 read balances/attendance**. The calculating three:

```
CalculateEmployee(employeeId, date, shouldLog)
CalculateEmployee(employeeId, fromDate, toDate, shouldLog)
CalculateEmployees(employeeIds[], fromDate, toDate, shouldLog, ignoreDaysOff, progress, key, dailyModelIds[])
```

All three funnel into the static `CalculateEmployees` (`HoursCalculationService.cs:163`).
`CalculateClockingMain` (`:290`) is `public static` and is also called directly from outside the
service — it is the per-day unit of work.

### 2.2 The run

`HoursCalculationService.cs:163-285`:

1. `DataCache.LoadData` — **loads the whole configuration estate into memory in one pass**
   (`DataCache.cs:75-115`): all daily models, all weekly model details, all breaks, all split/multi
   shift rows, all absences, all pauses, all departments, all corrections, all periods, all flexi
   balances, all shift-matching rules, all holidays, all contracts, all counter allocation rules,
   all work activities, all devices — plus `context.Calculations.Single()` and
   `context.SoftwareMainOptions.Single()` (`DataCache.cs:81-82`).
2. `ExecuteCustomSql(..., isExecutedForTheFirstCalculation: true)` (`:181`) — **customer SQL runs
   before any calculation**.
3. `CalculateClockings` (`:190`) — `Parallel.ForEach` over employees, `MaxDegreeOfParallelism = 4`
   (`:218`), `Thread.CurrentPrincipal` manually propagated into each worker (`:220`, `:224`).
4. `ExecuteCustomSql(..., false)` (`:185`) then a second `CalculateClockings` pass for models
   flagged `RecalculateAfterCustomSql`.

Per employee: `PrepareCalculationData` loads that employee's clockings + pauses + corrections +
activities, then `CalculateClockingMain` per clocking.

### 2.3 `CalculateClockingMain` — dispatch (`:290-517`)

Order matters and is unusual:

1. `ShouldCalculate(clocking, employee)` — start/leave dates. **A leaver's already-calculated days
   after the leave date are wiped, not skipped** (`:296-306`, comment cites bug #60634).
2. `ShouldCalculate(clocking, SoftwareOptions.BlockCalculationBeforeDate)` (`:316`) — a global
   "history is frozen before this date" line.
3. `ShouldMoveOpenBreakToLastSwipe` (`:321-352`) — **the engine mutates raw swipe data**: an
   unclosed break whose start is after the last swipe is promoted into `BadgeTime2` or
   `BadgeTime4` and the pause row is *deleted from the database* (`:344-347`).
4. Resolve daily model; if `!TreatAsMasterDailyModel`, substitute the employee's master model
   (`:359-364`).
5. Dispatch on `dailyModel.ModelType` — `SplitShifts`, `MultiModel`,
   `FixedSheduleWith6PairsOfSwipes`, `ShiftMatching`, or normal (`:373-509`).
6. `ShiftMatching` (`:420-497`): match the clocking's first IN and/or last OUT against
   `DailyModelShiftMatchingRules`; on a match, **rewrite the clocking's model ID and copy 12
   template columns onto the clocking** including `IsDTManualChanged = false` (`:459-474`).
7. `clocking.Log()` if `shouldLog` (`:513`).

### 2.4 `CalculateClocking` — the pipeline (`:525-966`)

This is the whole thing, in execution order. Everything writes to a `calc_*` column on `Clockings`.

| # | Stage | Code | Output |
|---:|---|---|---|
| 0 | override clocking from template if `ShouldOverrideClockingValues` | `:540-557` | 12 clocking columns |
| 1 | `ResetClocking` — null every `calc_*`, zero all 20 `CPTN`, zero all 12 `*Adjusted` | `:560`, `Utils.cs:1552-1609` | — |
| 2 | reset cost centres / clear CC counters | `:563-578` | — |
| 3 | drop corrections whose value is null or 0 | `:581` | — |
| 4 | bail if future day and `SkipNetHoursCalculationForFutureDays` | `:586-590` | — |
| 5 | **`SwipeFixer.GetFixedSwipes`** — no-badge fill, absence fill, night-shift wrap | `:593-598`, `Adjusters/BaseSwipesFixer.cs:12-40` | `FixedSwipes` |
| 6 | `ClosePauses` | `:609` | pause end times |
| 7 | `RecalculateAbsenceDuration` | `:615-621` | `MorningAbsenceDuration`, `AfternoonAbsenceDuration`, `AbsenceDuration` |
| 8 | **`CheckForBlockingExceptions`** | `:623-631` | if any → skip 9–19 |
| 9 | one-swipe-enough substitution | `:638-651` | temporary swipes |
| 10 | `DurationTheoretic` | `:653-658` | `DurationTheoretic` |
| 11 | `GrossAttendance` | `:660-665` | `calc_grossAttendance` |
| 12 | corrections total | `:667-669` | `calc_correction` |
| 13 | **`CalculatePresenceCorrected`** — grace + rounding + break deduction | `:671-678` | `calc_presenceCorrected` |
| 14 | `ProcessDailyModelBreaks` | `:681-691` | pause `BeginTimeAdjusted`/`DurationAdjusted` |
| 15 | breaks + absence duration | `:693-706` | `calc_absencesBreaks`, `calc_breaksDuration` |
| 16 | **`NetAttendance`** | `:713-731` | `calc_netAttendance` |
| 17 | cap to `dailyModel.DurationMax` | `:741-745` | `calc_netAttendance` |
| 18 | `Difference` | `:747-751` | `calc_difference` |
| 19 | `AddDebitCredit` | `:753-755` | adjusts 13, 16, 18 |
| 20 | `ActualWork` | `:768-771` | `calc_actualWork` |
| 21 | **night band lookup + `GetNightDuration`** | `:777-784` | — |
| 22 | `PerformHoursRepartion` | `:790` | `calc_dayHours`, `calc_nightHours`, `calc_panDay`, `calc_panNight` |
| 23 | lateness | `:809-814` | `calc_latenessMinutes`, `calc_latenessTimes` |
| 24 | **balance**, from the *previous day's* balance | `:817-824` | `calc_balance` |
| 25 | geolocation / no-location / info / activity exceptions | `:827-869` | `ScoresAbnormalities` rows |
| 26 | generate default activities | `:872-899` | `ClockingActivities` |
| 27 | **`CalculateCounters`** → `CPTN01..20` | `:902-909` | 20 counter columns |
| 28 | `CalculateClockingActivities` | `:912-922` | activity counters |
| 29 | `CalculateCostCentreCounters` | `:925-935` | `ClockingCostCenterCounters` |
| 30 | `clocking.Save()` | `:937-951` | 249-column UPDATE |

**The blocking-exception branch (`:792-807`) is the important one.** If any blocking exception
fires, stages 9–22 do not run at all, `calc_balance` is nulled, and — if
`Calculations.CalculationException == NetPresence0EcartTheoretic` — `calc_netAttendance` is set to
*minus the theoretical duration* (`:800-806`). A day with a blocking exception is not "uncalculated";
it is calculated as a full-day deficit.

### 2.5 Balance is a chain, not a value

Stage 24 reads the *previous day's* balance (`:817-820`), which itself calls
`CalculateAndGetCurrentBalance`. Balance is therefore a running total walked forward day by day
(`Utils.cs:195-215` walks `date = date.AddDays(1)` in a loop). Caps are applied at week end
(`Utils.cs:1245-1247`) and at period start *or* end depending on
`Calculations.FinishTheCurrentGap` (`Utils.cs:1251-1262`).

Consequence: **editing one day silently invalidates every later day's balance.** There is a
dedicated `RecalcuateBalanceForEmployees` API for exactly this, separate from calculation.

---

## 3. `dbo.Calculations` — all 45 columns, classified

Column list extracted mechanically from `HorioDB.designer.cs` (`TableAttribute(Name="dbo.Calculations")`).
Classification asserted against that list: **45 classified, 45 in schema, exact set match, 0
duplicated, 0 unclassified.**

- **LIVE-ENGINE (17)** — read inside `Logic\HoursCalculation`.
- **LIVE-ELSEWHERE (6)** — read by services/portal but never by the engine.
- **DEAD (19)** — appear in DDL, in the settings screen `Views\Calculation\Index.cshtml`, and in
  localization keys, and **nowhere else in `E:\Tlw`**. No read site exists.
- **HOUSEKEEPING (3)** — identity/presentation.

| # | Column | Class | Evidence |
|---:|---|---|---|
| 1 | `Id` | HOUSEKEEPING | identity |
| 2 | `NumberOfDaysInAdvancedCalendar` | LIVE-ELSEWHERE | `Logic\Planning\Boards.cs:40`, `Logic\Personnel\PersonnelService.cs:443` |
| 3 | `WeekStartsOn` | LIVE-ENGINE | `Utils.cs:701`, `:923`, `:1460`; `DAL\HoursCalculationRepo.cs:893` |
| 4 | `CalculationEndDateInclusive` | DEAD | only `Views\Calculation\Index.cshtml:74,76` |
| 5 | `NumberOfDaysBeforeArchiving` | LIVE-ELSEWHERE | `Logic\Settings\CalculationService.cs:86-96` |
| 6–19 | `MonTueStart/End` … `SunMonStart/End` (14) | LIVE-ENGINE | `DataCache.cs:216-253` |
| 20 | `RoundMinutes` | DEAD | DDL `1_horio_structure.sql:569` + localization key 1189 only |
| 21 | `RoundInMonthlyRecord` | DEAD | DDL `:570` + key 1188 |
| 22 | `IsGenerateAnomalyAndStopCalculation` | DEAD | DDL `:571` + key 1177 |
| 23 | `NoModelHoursCounter` | DEAD | DDL `:572` + key 1183 |
| 24 | `NoModelEcartCounter` | DEAD | DDL + localization only |
| 25 | `HolidayHoursCounter` | DEAD | DDL + localization only |
| 26 | `HolidayEcartCounter` | DEAD | DDL + localization only |
| 27 | `IsToCopyEcart` | DEAD | only `Views\Calculation\Index.cshtml:185-186` |
| 28 | `FinishTheCurrentGap` | LIVE-ENGINE | `Utils.cs:1251` |
| 29 | `CalculateOnActivity` | DEAD | DDL `:578` + key 1165 |
| 30 | `PersistentActivity` | DEAD | DDL `:579` + key 1186 |
| 31 | `RepeatabilityTreat` | DEAD | `Views\Calculation\Index.cshtml:209-211` + `CalculationEnums.cs:16` (enum defined, never switched on) |
| 32 | `IsManageSQL` | DEAD | only `Views\Calculation\Index.cshtml:219-221` |
| 33 | `CalculationException` | LIVE-ENGINE | `HoursCalculationService.cs:802`, `:2684` |
| 34 | `IsCalculateNightHours` | DEAD | only `Views\Calculation\Index.cshtml:241-243` |
| 35 | `IsCalculateNightHoursOnlyIfModelByNight` | DEAD | only `Views\Calculation\Index.cshtml:247-249` |
| 36 | `BreakDelay` | DEAD | DDL `:585` + key 1164 |
| 37 | `BreakBack` | DEAD | only `Views\Calculation\Index.cshtml:258-260` |
| 38 | `IsBreaks` | DEAD | only `Views\Calculation\Index.cshtml:267-269` |
| 39 | `GeneralInfo` | HOUSEKEEPING | free-text note |
| 40 | `IsNumberOfDaysBeforeArchivingEnabled` | DEAD | DDL `:589` + constraint `:2205` only |
| 41 | `Color` | HOUSEKEEPING | UI |
| 42–45 | `AutoBalanceReset`, `…Month`, `…Day`, `…Value` | LIVE-ELSEWHERE | `HorioService\ServiceTasks.cs:394-417` |

### 3.1 The finding that matters

**19 of 45 columns — 42% of this settings table — are settings the product renders, saves, and
never reads.** `IsManageSQL` is the most consequential: it is the switch users believe gates the
custom-SQL escape hatch, and it gates nothing. `ExecuteCustomSql` never looks at it (§5).

`RoundMinutes` is the second: a *global* rounding setting that does not round anything. All real
rounding is per-daily-template (`NetAttendanceCalculator.cs:78-88` reads
`dailyModel.RoundingPrecisePresence` and `dailyModel.NetPresenceRoundingCutOff`).

---

## 4. Night bands — the precedence question, settled

The concept appears in three places in the schema. **Only one is live.**

| Location | Columns | Status |
|---|---|---|
| `dbo.Calculations` | `MonTueStart/End` … `SunMonStart/End` (14) | **LIVE — the only night band the engine uses** |
| `dbo.DailyModels` | `NightShiftStartTime`, `NightShiftEndTime`, `IsCalculateNightHours`, `IsCalculateNightHoursOnlyIfModelByNight` | **not a night band** — see below |
| `dbo.GlobalScheduleThresholds` | `ScheduleInThreshold1/2`, `ScheduleOutThreshold1/2` | **not a night band at all** — schedule in/out thresholds, 7 columns total, no time-of-night semantics |

Measured, in `E:\Tlw\Source\Logic`, excluding generated files:

- `DailyModels.NightShiftStartTime` — **zero read sites anywhere in the source.** Dead column.
- `DailyModels.NightShiftEndTime` — 5 read sites, all in the *exception* region
  (`HoursCalculationService.cs:3633`, `:3669`, `:3701-3702`, `:3720`), used solely to decide
  whether a shift crosses midnight so early/late arrival and departure comparisons can add a day.
  It never enters night-hours arithmetic.
- `DailyModels.IsCalculateNightHours` / `IsCalculateNightHoursOnlyIfModelByNight` — **zero read
  sites.** Dead, same as their `Calculations` namesakes.

**Therefore there is no precedence to establish: there is one night band, global, keyed on the
clocking's own `DayOfWeek`** (`DataCache.cs:207-269`), and two dead duplicates. This is the first
time it has been recorded. There is **no per-employee, per-site, per-template or per-country night
band in TLW at all.** For a product sold across France, UK, and West Africa, that is a real gap,
not merely a modelling wart.

### 4.1 The night calculation has a swipe ceiling bug

`NightDurationCalculator.GetNightDuration` (`Calculators/NightDurationCalculator.cs:9-47`) sums
night overlap for **`BadgeTime1/2` and `BadgeTime3/4` only**. `Clockings` holds 12 badge times.
For a normal (non-6-pairs) template, **any work recorded in pairs 3–6 contributes zero night
hours**, while `calc_netAttendance` counts it in full — so
`totalDayDuration = calc_netAttendance − totalNightDuration` (`:786`) silently books it all as day.

`NightDuration6PairsOfSwipesCalculator.cs:9-48` sums all six pairs correctly. The bug is therefore
scoped to templates that are *not* `FixedSheduleWith6PairsOfSwipes` but nonetheless accumulate more
than four swipes — which is exactly what `SwipesFixer6PairsOfSwipes` and the shift-matching path can
produce.

---

## 5. The escape hatch: per-template custom SQL

**Not gated by `Calculations.IsManageSQL`.** Driven entirely by three `DailyModels` columns:
`CustomSql`, `CustomSqlForNoRecalculating`, `RecalculateAfterCustomSql`.

`dbo.ExecuteCustomSql` (`Database\Versioning\45.V4.0.0.0.sql:2438-2537` — the highest-numbered
definition; earlier ones at `29.V2.2.1.sql:4715`, `33.V3.3.0.0.sql:3228`, `38.V3.6.2.0.sql:2366`,
`39.V3.6.3.0.sql:4285` are superseded):

- Opens a cursor over every clocking in range whose daily model — **or whose employee's master
  daily model**, resolved through `EmployeeMasterDailyModels` with date bounds — has non-empty
  custom SQL.
- Substitutes `{0}` → `@employeeId`, `{1}` → `@clockingId`, then `sp_executesql`.
- `CustomSqlForNoRecalculating` runs only on the first pass; `CustomSql` runs on the second pass
  only if `RecalculateAfterCustomSql = 1`.
- Output rows are collected into a message table and surfaced as `CustomSqlExecutionResult.Messages`
  (`HoursCalculationService.cs:2549-2556`).

**No sandbox.** The documented rules are conventions in `E:\Tlw\Documentation\Custom SQL\Daily
Template SQL.md:32-39` — "only `select` or `update`", "NO `delete` `drop` `truncate`", "`update`
always must have `ClockingId = {1}`" — none of which the procedure enforces. The only real
restriction is a name collision list in `Database\Custom Scripts\Daily Models\_readme.txt`.

### 5.1 What the scripts actually do

`E:\Tlw\Database\Custom Scripts\Daily Models\` holds **85 customer scripts**. Sampling them, the
expressive power WM's replacement must match is:

1. **Lookup-table rounding** — `Pommier_Rounding_Script.sql:1-60` declares four `TABLE` variables
   (`@EntryRounding`, `@BreakStartRounding`, `@BreakEndRounding`, `@ExitRounding`) each holding
   `(FromTime, ToTime, RoundTo)` triples, then rewrites `BadgeTime1` / `COALESCE(BadgeTime4,
   BadgeTime2)` and the lunch pause through them. This is a rounding *policy per time-of-day band*
   — strictly more expressive than the template's single before/after cut-off pair.
2. **Cross-day and cross-week aggregation** — `Daily Template SQL.md:149-196`: "set pay category 10
   to 1 on Saturday if there are swipes Monday to Saturday" (a 6-row `count(*)` subquery over the
   employee's week); "set pay category 15 on Sunday to the week's total of pay category 15".
3. **Conditional pay-category assignment** — thresholds on `calc_netAttendance`
   (`≥ 8 → CPTN05 += 2`), on `MorningAbsenceID`, on absence pairs.
4. **Naming by business meaning** — `SOS_CIB_-_Scripts_for_lunch_vouchers.sql`,
   `SAE_-_Script_for-Transport_Vouchers.sql`, `System consulting - balance calculation.sql`,
   `FCE SBA - Need scripts for balance - hours above 43 hours.sql`. These are entitlement rules
   (meal vouchers, transport allowances, weekly OT thresholds) that the declarative product could
   not express.

That is the bar for WM's rules language: **time-banded lookup tables, windowed aggregation across
an employee's week, and threshold-conditional counter assignment.** Not "arbitrary SQL".

---

## 6. Configuration versus code

| Source of behaviour | Distinct members read by the engine | Of a table with |
|---|---:|---:|
| `dailyModel.*` | **101** | 124 columns |
| `SoftwareOptions.*` | **13** | 227 columns |
| `CalculationOptions.*` (`Calculations`) | **17** | 45 columns |

The 13 global switches, in full (`grep -o 'SoftwareOptions\.[A-Za-z]+' Logic\HoursCalculation`):
`SystemTimeZone`, `BlockCalculationBeforeDate`, `ShouldOverrideClockingValues`,
`ShouldOverwriteCostCentres`, `SkipNetHoursCalculationForFutureDays`, `DisableAutoSwipesForNoBadge`,
`QrGenerateExceptionForSwipesWithoutGeo`, `ShouldGenerateExceptionForOverlappingActivities`,
`IsDailyModelLimitsAppliesToActivity`, `EnableCounterHoursCapping`,
`IsBalanceCalculationFromContractsDisabled`, `PreProcessSwipesWithBreaksInSwipeAndGo`.

**Verdict: the engine is overwhelmingly data-driven, and the data is the daily template.** 101 of
124 template columns are live. This is much better news than 700 KB suggests — the size is not
101 special cases, it is one algorithm reading 101 knobs, plus 1,256 lines of exception rules, plus
three near-duplicate variants of the pipeline (`…MultiModel.cs`, `…SplitShifts.cs`,
`NightDuration6PairsOfSwipesCalculator` / `SwipesFixer6PairsOfSwipes`) that exist because the
schema has fixed slots rather than rows.

The hard-coded parts are:
- the 22 exception rules (`:2573-3829`), each a bespoke `#region`;
- the shift-type dispatch (`:373-509`), a five-way `if/else` on `ModelType`;
- the four strategy variants of swipe-fixing / presence-corrected / night-duration, selected by
  `ClockingCalculationStrategy` (`ClockingCalculationStrategy.cs:1-30`) whose own comment reads
  *"didn't have enough time to redo presence corrected calculation properly … too much refactoring
  required … so for now simple solution"*.

---

## 7. The minimum viable calculation

**Question: what is the smallest set of stages that turns a day's punches plus a daily template
into hours a timesheet could show?**

Answer, measured from §2.4: **eight stages**, and they are all in small, separable calculator
classes, not in the 276 KB file.

| # | Stage | Legacy implementation | Lines |
|---:|---|---|---:|
| 1 | Fix swipes (night-shift wrap only; skip no-badge and absence fill) | `Adjusters/BaseSwipesFixer.cs` | ~330 |
| 2 | Theoretical duration | `Calculators/DurationTheoreticCalculator.cs` | 39 |
| 3 | Gross attendance = Σ(OUT−IN) over pairs | `Calculators/GrossAttendanceCalculator.cs` | 78 |
| 4 | Grace (entry/exit tolerance) | `Adjusters/GraceAdjuster.cs` | 128 |
| 5 | Break deduction | `Calculators/BreaksDurationCalculator.cs` + `ProcessDailyModelBreaks` | ~40 + 320 |
| 6 | Net attendance + rounding + cap | `Calculators/NetAttendanceCalculator.cs` | 92 |
| 7 | Difference vs theoretical | `Calculators/DifferenceCalculator.cs` | 57 |
| 8 | Day/night split | `Calculators/NightDurationCalculator.cs` + `BaseNightDurationCalculator.cs` | 89 + 73 |

The core arithmetic, stages 2–8 excluding break matching, is **~560 lines of readable C# with no
database access and no `Clockings` dependency beyond a handful of fields.** Stage 5's
`ProcessDailyModelBreaks` is the one genuinely hard piece — it matches actual pause records against
template break definitions, applies minimum duration, maximum deduction, theoretical-duration
snapping, and rounding-above-expected (`HoursCalculationService.cs:1023-1343`).

**This is the number that matters for planning.** WM's first calculation portion costs
approximately 600 lines of ported arithmetic plus its tests — not 700 KB. Everything else in the
engine is exceptions, counters, cost centres, activities, split/multi-shift, balance chaining and
custom SQL, each of which is separately deferrable.

---

## 8. The editable path — what happens when a human overrides

The user has ruled the Daily Browser editable. Measured answer: **legacy records the override, then
destroys most of it on the next recalculation.** Precisely:

### 8.1 Swipe times — recorded, and survive

Each `BadgeTimeN` has a paired `BadgeTimeNGeneratedBy` (`ClockingEnums.cs:12-29`) with six values:
`Auto` (blank), `Preset` (`P`), `Manually` (`*`), `NoBadge` (`A`), `ManualTimesheet` (`M`),
`Default` (`D`). A manual edit sets the time and stamps `Manually`. The engine does not overwrite a
manually-set `BadgeTimeN` — it only *derives* `BadgeTimeNAdjusted` from it. **This is the right
design and WM should keep it.**

Two caveats measured in the fixer:

- Provenance is shifted along with the times when swipes are re-indexed
  (`BaseSwipesFixer.cs:107-118` shifts `BadgeTime1..12GeneratedBy` down by two and stamps the
  vacated top two `Auto`; `:192-195` shifts up by two). A manual `*` can therefore move to a
  different slot.
- `ResetGeneratedByForNoBadgeForToday` only clears stale `NoBadge` provenance **for today**
  (`BaseSwipesFixer.cs:48-54`, comment: *"this should only be applied to today, since earlier days
  can be recalculated"*). Historic days keep provenance that no longer matches the employee's
  configuration.

### 8.2 Calculated values — destroyed, unconditionally

`ResetClocking` (`Utils.cs:1552-1609`) runs at stage 1 of **every** calculation and nulls
`calc_grossAttendance`, `calc_netAttendance`, `calc_correction`, `calc_difference`,
`calc_presenceCorrected`, `calc_actualWork`, `calc_absencesBreaks`, `calc_nightHours`,
`calc_dayHours`, `calc_latenessMinutes`, `calc_latenessTimes`, `calc_panDay`, `calc_panNight`, all
60 per-shift `calc_*Shift1..6` variants, **all 20 `CPTN01..20`**, and all 12 `*Adjusted` badge
times. There is no guard, no provenance check, no "manual" flag consulted. **Answer to the survey
question: the engine recalculates and silently discards.**

The intended way to express a human adjustment is therefore **not** editing `calc_*` but adding a
`ClockingCorrection` row, which the engine reads at stage 12 and folds in (`:667`, `:713-717`,
`:762-766`). Corrections are additive, survive recalculation, and are excluded from
`calc_actualWork` selectively. **This is the mechanism WM should build the editable Daily Browser
on.**

### 8.3 `IsDTManualChanged` — a manual flag the engine writes but never reads

Measured across all of `E:\Tlw\Source`:

- **Written** by the planning module (`Logic\Planning\Auto.cs:492`, `:604`, `:775-792`) and by the
  UI (`Views\ScoresDetails\EditDailyDetails.cshtml:348`).
- **Respected** by bulk template application, which explicitly skips rows with it set
  (`Logic\Settings\DailyModelService.cs:319-345` — `AND IsDTManualChanged = 0`).
- **Set to `false`** by the hours engine at `HoursCalculationService.cs:474` (shift matching) and
  `:549` (`ShouldOverrideClockingValues`), and **read by the engine nowhere.**
  `DurationTheoreticCalculator.CalculateForClocking` recomputes `DurationTheoretic` from the
  template with no reference to it (`Calculators/DurationTheoreticCalculator.cs:13-36`).

So a user who manually overrides a day's expected duration is protected from a *template re-apply*
and unprotected from a *recalculation* — and the recalculation additionally clears the flag, so the
protection is lost for the future too. This is a defect, not a design.

### 8.4 The engine mutates source data

Three places where calculation writes to what should be immutable input:

1. `ShouldMoveOpenBreakToLastSwipe` (`:321-352`) — promotes a pause start into a badge time and
   `DELETE`s the pause row.
2. Shift matching (`:459-474`) — rewrites `clocking.DailyModelID` and 11 schedule columns.
3. `ExecuteCustomSql` — arbitrary customer `UPDATE` against `Clockings` before *and* after
   calculation.

Any WM replay design must treat these as the reason legacy cannot replay: **the input is not
preserved.**

---

## 9. Is the 10–16 week Phase 2 estimate defensible?

**No. It is low by roughly a factor of three.** `ARCHITECTURE.md:558` scopes "2 — Rules engine" as
*daily templates (shifts, breaks, core hours, rounding policy, exceptions, shift matching,
split/multi-shift), weekly models, counters, flexi balances, pay categories, cost-centre
allocation, recalculation & replay, safe rules expression language* at **10–16 wks**. That estimate
was made without opening the code — this is the first survey to do so. Measured against what is
actually there:

| Scope item in the §14 estimate | Measured legacy | Honest estimate |
|---|---|---|
| Daily templates as an entity | 124 columns, 101 live in the engine | 3–4 wks |
| Core arithmetic (§7, 8 stages) | ~560 lines + break matching 320 | **2–3 wks** |
| Exceptions | 22 rules, 1,256 lines, each with its own template thresholds | 5–7 wks |
| Shift matching | `:420-497` + rules table | 1–2 wks |
| Split / multi-shift | 2 near-duplicate pipelines, ~1,100 lines, 60 `calc_*Shift*` columns | 4–6 wks |
| Counters / pay categories | `CPTN01..20` + hour/band counter allocation rules, two-level (daily/weekly model) resolution | 4–5 wks |
| Cost-centre allocation | `:4203-4663`, a second full re-run of allocation | 3–4 wks |
| Flexi balances | balance chain + week/period capping + `AutoBalanceReset` job | 3–4 wks |
| Weekly models | `WeeklyShiftDetails`, `WeeklyModelHourCounters`, `WeeklyModelBandCounters` | 2–3 wks |
| Recalculation & replay | legacy has none — it mutates input (§8.4); WM must design this fresh | 4–6 wks |
| Safe rules expression language | replacing 85 customer scripts at the power level in §5.1 | 6–10 wks |
| **Total** | | **37–54 wks** |

That is **9–13 months, not 2.5–4 months**, at one developer. It is consistent with
`TLW-INVENTORY.md`'s own whole-product figure of 2.5–4 years, which the 10–16 week Phase 2 line is
not.

The estimate is not wrong because the engine is unportable. It is wrong because §14 priced *the
arithmetic* (§7 — genuinely 2–3 weeks) and labelled it *the rules engine*, which is eleven things.

**Recommended restatement**, to propose to the user (I have not edited `ARCHITECTURE.md`):

> | **2 — Rules engine** ⭐ | … | **10–16 wks** | ▢ |

becomes

> | **2a — Day calculation core** ⭐ | daily templates, swipe fixing, grace, breaks, net/gross/difference, day-night split, deterministic replay from punches | **8–11 wks** | ▢ |
> | **2b — Exceptions & counters** | 22 exception rules, `CPTN` pay categories, hour/band counter allocation, cost centres | **12–16 wks** | ▢ |
> | **2c — Shifts, balances, weekly models** | split/multi-shift, flexi balances, week/period capping, weekly models | **9–13 wks** | ▢ |
> | **2d — Rules expression language** | replaces 85 per-customer SQL scripts; must express time-banded rounding tables, weekly windowed aggregation, threshold-conditional counters | **6–10 wks** | ▢ |

---

## 10. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A day + its template → hours, as an aggregate | **Keep** | genuine domain truth; the pipeline order in §2.4 is sound |
| Small, single-purpose calculator classes with interfaces | **Keep** | `Calculators/` is the best-factored part of the codebase; port shape-for-shape |
| `BadgeTimeNGeneratedBy` provenance per swipe | **Keep** | the right answer to "who set this time"; WM should have it from day one |
| `ClockingCorrection` as the additive human-adjustment mechanism | **Keep** | survives recalculation; the correct basis for an editable Daily Browser |
| Grace/tolerance (`GraceAdjuster`) with Absolute/Relative/None/Anticipation | **Keep** | clean, documented, worked examples exist (§11) |
| `BadgeTime1..12` + `…Adjusted` + `…GeneratedBy` (36 fixed columns) | **Improve** | punch rows with an ordinal, not 12 slots; removes the §4.1 ceiling bug by construction |
| `CPTN01..20` | **Improve** | counter rows keyed by counter ID; 20 is a hard ceiling customers already work around in custom SQL |
| 60 `calc_*Shift1..6` columns | **Improve** | a day has *n* shift segments; make them rows |
| Global weekday night band | **Improve** | keep the weekday-varying idea (genuinely used); make it a resolvable rule with template/site/employee scope, since TLW has none (§4) |
| Rounding policy | **Improve** | template's single before/after cut-off pair is too weak — 85 customers wrote SQL to get time-banded lookup tables (§5.1). Ship the lookup table as first-class config |
| Per-template custom SQL | **Improve** | keep the *capability*, replace the mechanism with a sandboxed expression language at the §5.1 power level |
| `ResetClocking` wiping all `calc_*` unconditionally | **Invert** | WM: calculation output is a *projection* rebuilt from immutable punches (`ARCHITECTURE.md:274`); there is nothing to wipe, and a human override is a first-class input, never collateral |
| `IsDTManualChanged` written-but-never-read | **Invert** | if a value is flagged manual, the engine must honour it or refuse — never silently recompute (§8.3) |
| Engine mutating swipes and deleting pause rows (§8.4) | **Invert** | inputs immutable; corrections are new records |
| Blocking exception → `calc_netAttendance = −DurationTheoretic` | **Invert** | an unprocessable day must be *flagged as unknown*, not booked as a full-day deficit that flows into a running balance and then into payroll |
| 19 dead settings columns in `Calculations` | **Drop** | do not migrate settings nothing reads; `IsManageSQL` in particular is a security-relevant lie |
| `DailyModels.NightShiftStartTime`, `IsCalculateNightHours*` | **Drop** | zero read sites |
| `dbo.GlobalScheduleThresholds` as a night-band source | **Drop** | it is not one; delete the assumption |
| `Parallel.ForEach` with manual `Thread.CurrentPrincipal` propagation | **Drop** | replace with a job queue over Kafka/RabbitMQ per `CLAUDE.md` invariant 4 |
| `HoursCalculationServiceMultiModel` / `…SplitShifts` as separate pipelines | **Drop** | duplication caused by fixed slots; one pipeline over segment rows |

---

## 11. Edge cases and worked examples

Lifted verbatim from `E:\Tlw\Documentation\Daily Templates\Daily Templates - Rounding rules.md`.
These are the closest thing to a spec that exists and should become WM tests directly.

**Ordering (`:32`):** *"Both grace rules and rounding policy can be used together: first the grace
rules will be applied, then the rounding policy. Rounding policy will be applied only if swipe was
outside of grace rules."*

**Scope (`:50`, `:88`, `:124`):**
- *"The calculation will be applied for first IN and second IN (if present) for regular daily
  templates. The calculation will be applied for first IN only for daily template with 6 pairs of
  swipes."*
- *"…for first OUT and second OUT (if present) for regular daily templates. …for last OUT only for
  daily template with 6 pairs of swipes."*
- *"The rounding policy will be applied for each of 2 pairs of swipes for regular daily templates…
  However, only the last OUT will be rounded according to the rules for daily template with 6 pairs
  of swipes."*

**Entry grace, Absolute (`:58`, `:62`):** Given tolerance 10 min and expected IN 08:00 —
- When employee arrives 08:00–08:09, then net hours are calculated as if on time.
- When employee arrives 08:10 or later, then net hours use the exact arrival time.

**Entry grace, Relative (`:72`, `:78`):** Given tolerance 10 min and expected IN 08:00 —
- When employee arrives 08:00–08:10, then net hours are calculated as if on time.
- When employee arrives 08:11 or later, then net hours use *exact arrival − 10 min*.
  (Confirmed in code: `GraceAdjuster.cs:52-62`, with the inclusive `<=` comment
  *"for relative - check is inclusive"*.)

**Exit grace, Absolute (`:96`, `:100`):** expected OUT 17:00, tolerance 10 min —
- Leaves 16:51–17:00 → as if on time. Leaves 16:50 or earlier → exact time.

**Exit grace, Relative (`:110`, `:116`):**
- Leaves 16:50–17:00 → as if on time. Leaves 16:49 or earlier → *exact + 10 min*.

**Rounding, Before-IN, 10-minute interval, cut-off 5 (`:146-152`):**
`07:50–07:55 → 07:50` · `07:56–08:00 → 08:00` · `07:40–07:45 → 07:40` · `07:46–07:50 → 07:50` ·
`07:30–07:35 → 07:30` · `07:36–07:40 → 07:40`

**Rounding, After-IN, 15-minute interval, cut-off 10 (`:166-172`):**
`08:01–08:10 → 08:00` · `08:11–08:15 → 08:15` · `08:16–08:25 → 08:15` · `08:26–08:30 → 08:30` ·
`08:31–08:40 → 08:30` · `08:41–08:45 → 08:45`

**Rounding, Before-OUT (`:186-192`):**
`16:50–16:55 → 16:50` · `16:56–17:00 → 17:00` · `16:40–16:45 → 16:40` · `16:46–16:50 → 16:50`

**Rounding, After-OUT (`:206-212`):**
`17:01–17:10 → 17:00` · `17:11–17:15 → 17:15` · `17:16–17:25 → 17:15` · `17:26–17:30 → 17:30`

### 11.1 Edge cases found in code, not documented anywhere

| Case | Legacy behaviour | Cite |
|---|---|---|
| Employee left the company mid-history | Days after `DischargeDate` are **wiped**, not skipped | `:296-306` |
| No daily model resolvable for the day | `return` with the *incoming* result unchanged — neither success nor failure | `:356-358` |
| Blocking exception fires | Stages 9–22 skipped, balance nulled, net attendance set to −theoretical if configured | `:792-807` |
| Only one swipe, template has `OneSwipeEnough` | Missing swipes filled from the template, calculated, then **reset back to null** afterwards | `:638-651`, `:733-738` |
| Only one swipe and it is an *out* (morning absence) | Separate fill path `SetTimeFromDailyModelForOneSwipeCaseWithMorningAbsence` | `:648` |
| Shift crossing midnight | `badgeEnd < badgeStart` → add 24h; night window `22:00–06:00` handled by a *third* branch adding a further day to badge times | `BaseNightDurationCalculator.cs:36-39`, `:53-67` |
| Net attendance above `DurationMax` | Capped — but positive corrections are added *on top of* the cap | `NetAttendanceCalculator.cs:53-71` |
| Capping suppresses an exception | Info exception "Maximum Duration Exceeded" (code 15) would never fire, so the pre-cap value is carried in a local variable purely to raise it | `:604-606`, `:739` |
| Employee is `IsNoBadge` with no absence | Gross **and** net attendance are set to the theoretical duration; no swipes consulted | `GrossAttendanceCalculator.cs:29-32`, `NetAttendanceCalculator.cs:16-19` |
| Half-day absence with no half-day theoretical set on the template | Falls back to `DurationTheoretic / 2` — comment: *"it's possible that they forget to put the value"* | `DifferenceCalculator.cs:15-21` |
| Debit/credit bonus | Added to net, presence-corrected **and** difference — triple-counted by design; suppressed on bank holidays and on full-day absence | `:2443-2461` |
| Unswiped break deducted, template says deduct from day hours | Night duration is *increased* to shrink day duration | `NightDurationCalculator.cs:26-45` |
| Future-dated days | Skipped only if `SkipNetHoursCalculationForFutureDays`; otherwise fully calculated | `:586-590` |
| Recalculation after a template edit | History changes. There is no versioning of `DailyModels`; recalculating an old date applies today's template | `DataCache.cs:83` loads current templates only |
| Work in badge pairs 3–6 on a non-6-pairs template | Contributes zero night hours; booked entirely as day (§4.1) | `NightDurationCalculator.cs:12-24` |

---

## 12. What I did not measure

Explicitly, so nobody assumes otherwise:

1. **`ProcessDailyModelBreaks` in full** (`:1023-1343`, 320 lines). I read its inputs, outputs and
   the adjustment rules at `:1062-1189` but did not trace every branch of pause-to-template
   matching. It is the hardest single piece in the minimum viable calculation.
2. **`CalculatePresenceCorrected`** (`:1599-1971`) and its 6-pairs twin (`:1972-2326`) — 730 lines
   combined. I established what they consume and produce and that grace and rounding live inside
   them; I did not trace the arithmetic.
3. **`HoursCalculationServiceMultiModel.cs` (28 KB) and `HoursCalculationServiceSplitShifts.cs`
   (15 KB)** — not opened beyond their call sites. My split/multi-shift estimates in §9 are from
   file size and the 60 `calc_*Shift*` columns, not from reading.
4. **The 22 exception rules individually.** I counted and named them from `#region` markers
   (`:2595-3350`); I read only the geolocation and shift-matching ones. Their thresholds, template
   dependencies and interactions are unmeasured.
5. **Counter allocation** — `ClockingCountersCalculator.cs`, `ClockingActivityCountersCalculator.cs`,
   `GetEffectiveCounterAllocationRules` (`:4736-4792`). I confirmed there is a two-level
   daily-model/weekly-model resolution and did not trace it.
6. **Cost-centre counters** (`:4203-4663`) — including the `#region " Working on holiday - special
   rules "` at `:4441`, which is unread.
7. **`AbsenceDurationCalculator.cs` (18 KB) and `ActivityPresenceCorrectedCalculator.cs` (23 KB)** —
   not opened. Absence is a concurrent survey (plan 015); activities are unclaimed.
8. **`DAL/HoursCalculationRepo.cs` (979 lines)** — I read only `ResetExceptions`,
   `DeleteClockingPause`, `SaveDefaultActivity` call sites. Its own SQL is unaudited.
9. **Whether swipe→clocking allocation stored procedures interact with this engine.** Plan 012
   established allocation lives in T-SQL. I confirmed `ExecuteCustomSql` is the only stored
   procedure this engine calls by name; I did not audit `HoursCalculationRepo` for others.
10. **The other 84 custom scripts.** I read `Pommier_Rounding_Script.sql` in full and the
    documented examples; the rest were classified by filename only.
11. **`ClockingsLog` (97 columns) and the audit path** — `clocking.Log()` was not opened.
12. **Any runtime behaviour.** Nothing was executed. All findings are static.

---

## 13. Proposed corrections to fenced documents

Not applied — `ARCHITECTURE.md` is fenced for this survey. Concrete diffs:

**§14 roadmap, line 558** — replace the single "2 — Rules engine, 10–16 wks" row with the four-row
2a/2b/2c/2d breakdown in §9. The current figure understates by ~3×.

**§13 coverage matrix, line 397** — the `Calculations` row is factually right about the 7-window
night band. Append: *"17 of 45 columns reach the engine; **19 are dead** — rendered by
`Views\Calculation\Index.cshtml`, saved, and never read, including `IsManageSQL`, which users
believe gates custom SQL and gates nothing. See `TLW-CALCULATION-ENGINE.md` §3."*

**§13, new row** — there is no row for the calculation engine itself:

> `| **Hours calculation engine (`Logic\HoursCalculation`, 866 KB, 5,665-line service)** | Rules | ▢ **not started** — pipeline mapped in `TLW-CALCULATION-ENGINE.md` §2.4 (30 stages). Minimum viable calculation is 8 stages / ~600 lines (§7). 22% of the main file is exception detection, not arithmetic. |`

**§13, line 408** — `Hours calculation / rounding / tariffs | Rules | ▢ planned` is too coarse to
be useful and should be replaced by the row above.
