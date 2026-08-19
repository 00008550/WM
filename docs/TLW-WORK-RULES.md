# TLW Work Rules — measured anatomy

*Survey 2026-08-18. Companion to [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md), which measures
the row Work Rules writes into. This document measures the rules that write it, and sizes the
**Daily Browser** — TLW's main Time & Attendance screen, which WM has no plan for.*

Everything here was measured against `E:\Tlw`. Where a WM document said otherwise, the WM document
is corrected and the correction is listed in §9.

---

## 1. The measurement

Schema counted from `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` by walking
`TableAttribute` / `ColumnAttribute` pairs: **578 distinct table names, 8,173 columns** — matching
`TLW-INVENTORY.md:26-27` exactly, including its 578-vs-579 reconciliation
(`dbo.PredefinedAbsences` is mapped by two classes).

The two tables this area turns on, both confirmed at the counts `TLW-CLOCKING-MODEL.md:34-35`
claims:

| Table | Columns | Declared at |
|---|---|---|
| `dbo.DailyModels` — the daily template | **124** | `HorioDB.designer.cs:11056` |
| `dbo.DailyBrowserView` — the screen's read model | **126** | `HorioDB.designer.cs:118931` |

Both are top-10 tables by width in the whole schema (`DailyBrowserView` 9th, `DailyModels` 10th,
behind `Devices` 268, `Clockings` 249, `SoftwareMainOptions` 227).

### 1.1 The satellite tables — the whole Work Rules base, with counts

| Table | Cols | Line | What it holds |
|---|---|---|---|
| `dbo.DailyModels` | 124 | `:11056` | the daily template |
| `dbo.DailyModelBreaks` | 34 | `:56848` | multi-break rules (types 5 and 6 only) |
| `dbo.RoundingRules` | 21 | `:105480` | named rounding policy — **19 fields duplicated from `DailyModels`** |
| `dbo.ClockingPauses` | 18 | `:37101` | a break as recorded on a day |
| `dbo.IntermediatePauses` | 17 | `:36377` | break *types* |
| `dbo.Corrections` | 13 | `:10553` | correction/time-adjustment types |
| `dbo.ScoresAbnormalities` | 13 | `:19596` | an exception raised on a day |
| `dbo.DailyModelBandCounters` | 13 | `:83110` | counter allocation by time band, per weekday |
| `dbo.DailyModelHourCounters` | 12 | `:83567` | counter allocation by hours worked, per weekday |
| `dbo.DailyModelShiftMatchingRules` | 8 | `:58855` | shift matching (type 7) |
| `dbo.ScoresAbnormalitiesAuthorized` | 8 | `:86218` | manager sign-off on an exception |
| `dbo.DailyModelSplitShifts` | 7 | `:84142` | split shifts (type 8) |
| `dbo.GlobalScheduleThresholds` | 7 | `:106022` | named swipe-blocking window — **4 fields duplicated from `DailyModels`** |
| `dbo.ScoresAbnormalitiesSetup` | 7 | `:19334` | exception *types* |
| `dbo.Counters` | 7 | `:53275` | **the pay categories** |
| `dbo.Periods` | 6 | `:182095` | **the pay periods** |
| `dbo.ClockingShiftCorrections` | 6 | `:102796` | a correction against one shift of a day |
| `dbo.ClockingCorrections` | 5 | `:55796` | a correction against a day |
| `dbo.EmployeeMasterDailyModels` | 5 | `:111428` | master template assignment, dated |
| `dbo.DailyModelMultiShifts` | 4 | `:102580` | multi-shift composition (type 9) |
| `dbo.SplitShiftCounterValues` | 4 | `:84471` | split-shift counter results |
| `dbo.SplitShiftCounters` | 2 | `:84000` | split-shift counter definitions |

**Total Work Rules base: 22 tables, 341 columns.** 124 of them (36%) are one table.

### 1.2 What is *not* here

Searched for and absent from the schema:

- **No `PayCategories` table.** A pay category is a row in `dbo.Counters` (7 columns). Its *value*
  for a day is `Clockings.CPTN01..20` — twenty fixed columns, so twenty pay categories, maximum.
  Confirmed by the vault's own mapping table: *"CPTN01 → Pay Category 1 … CPTN20 → Pay Category 20"*
  (`Documentation\Custom SQL\Daily Template SQL.md:74-93`).

  **The ceiling is not merely a schema limit — the product has never had a way to exceed it.**
  `dbo.Counters` is created and seeded with **exactly 20 rows** in one statement block, named
  "Counter 1".."Counter 20" via localization keys
  (`Database\Versioning\27.V2.1.12.sql:390-501` — 20 `insert into [Counters]`, counted).
  `WebSite\Controllers\CountersController.cs` has **only `Index` and `EditCounter`** — no Create,
  no Delete. A customer can *rename* pay category 17; nobody has ever been able to add an 18th
  beyond the seeded set, or remove one. This answers plan 002's open question 2 as far as the code
  can: no customer hits the ceiling by adding, because adding is impossible. The migration is
  therefore trivial — 20 named rows — and the *reason* to normalise is future headroom, not
  rescuing existing overflow.
- **No `DayTypes` table and no calendar-day-model table.** See §9.1 — those screens are Access
  Control, not Work Rules.
- **No `PeriodicTemplates` table by that name.** The weekly/periodic branch is `dbo.WeeklyShifts`
  (12), `dbo.WeeklyShiftDetails` (7), `dbo.WeeklyModelBandCounters` (6), `dbo.WeeklyModelHourCounters`
  (5). Out of scope for this survey; recorded so the next one does not search for the wrong name.

### 1.3 The editor

| Artefact | Size |
|---|---|
| `WebSite\Views\DailyModel\_AddEditDailyModelControls.cshtml` | **133 KB, one Razor file** |
| `WebSite\Controllers\DailyModelControllerBase.cs` | **91 KB** |
| `WebSite\Models\DailyModelModels\DailyModelModels.cs` | **163 public properties** |
| `Logic\Settings\DailyModelService.cs` | 39 KB |

`TLW-INVENTORY.md:32` says "~150" configuration fields. Measured: **163 view-model properties**
over 124 persisted columns plus five child collections. The estimate was low but honest.

### 1.4 The calculation engine behind it

`Logic\HoursCalculation\` — **79 files, ~700 KB of C#**, of which
`HoursCalculationService.cs` alone is **276 KB** and `ProxyClocking.cs` **68 KB**. It branches on
`ModelType` throughout. This is the thing "Phase 2 — Rules engine, 10–16 wks" (`ARCHITECTURE.md:549`)
is estimating, and it is the single largest unbuilt component in the product.

---

## 2. `ModelType` — eleven templates in one table

`SharedLogic\Enums\ShiftType.cs:4-17`, carrying the comment *"don't change order, 'cause it's
indexes are in use in javascript"*:

| # | ShiftType | Notes |
|---|---|---|
| 1 | `FixedShedule1Range` | one IN/OUT pair, fixed times |
| 2 | `FixedShedule2Ranges` | two pairs (morning + afternoon) |
| 3 | `VariableShedule2Ranges` | one pair, window-bounded |
| 4 | `VariableShedule4Ranges` | two pairs, window-bounded |
| 5 | `FixedShedule1RangeWithBreaks` | uses `DailyModelBreaks` |
| 6 | `VariableShedule2RangesWithBreaks` | uses `DailyModelBreaks` |
| 7 | `ShiftMatching` | template chosen *after the fact* from the swipes, via `DailyModelShiftMatchingRules` |
| 8 | `SplitShifts` | uses `DailyModelSplitShifts` |
| 9 | `MultiModel` | composes up to 6 child templates via `DailyModelMultiShifts` |
| 10 | `FlexiModel` | delegates to `FlexiDailyModelId` |
| 11 | `FixedSheduleWith6PairsOfSwipes` | the 12-swipe model |

`ModelType` is `Int NOT NULL` with **no zero member**. `DailyModel.Duration`
(`Logic\Entities\DailyModel.cs:12-13`) special-cases `ModelType == 0` to return `TimeSpan.Zero`
rather than hitting its own `default: throw new NotSupportedException()` at `:69` — because a
zero-valued `DailyModel` is constructible and *is* constructed on the read path (§4.1).

Types 7, 8, 9 and 10 all return `TimeSpan.Zero` from `Duration` (`:19-23`): for those, the day's
expected hours come from elsewhere (`TheoreticalDurationForShiftMatching`, the child models, or the
flexi target). A naive port that reads `DurationTheoretic` for all eleven types is wrong for four.

There is also a **twelfth, nameless template**: the system "day off" model, identified by code
rather than type — `IsSystemDayOffModel => Code == Constants.SYSTEM_SANS_DAILY_MODEL_CODE`
(`Logic\Entities\DailyModel.cs:79`), fetched by `GetSansDailyModel()`
(`Logic\Settings\DailyModelService.cs:670-673`). *Sans* = French for "without". Every employee's
non-working day points at it.

---

## 3. All 124 columns of `dbo.DailyModels`, classified

Classified programmatically against the schema dump; **124 assigned, 0 unclassified, 0 duplicated**.

| # | Group | Cols |
|---|---|---|
| A | Identity & admin — `Id, Code, ShortName, Color, Name, IsSystem, IsActive, UpdatedBy, CostCentreId, DepartmentId, ModelType` | 11 |
| B | **Shift windows — two fixed IN/OUT pairs** `Enter1Start/End, Exit1Start/End, Enter2Start/End, Exit2Start/End` | 8 |
| C | Window limits & calc bounds `LimitEnter1..LimitExit2, Enter1DontCalculateBefore, Exit1DontCalculateAfter, Enter2DontCalculateBefore, Exit2DontCalculateAfter` | 8 |
| D | Tolerance / grace `DelayTolerance(+Type), ClearingOutput(+Type)` | 4 |
| E | Expected durations `DurationTheoretic, DurationTheorMorning, DurationTheorAfternoon, DurationMin, DurationMax, DurationMinToCart, MinTimeForHalfDay, GenerateAnomalyIfMore, Package, Compensation, Convention` | 11 |
| F | Half-day rules `FirtHalfRequired` *(sic)*`, SecondHalfRequired, CountFullDayAbsenceAsHalfDay` | 3 |
| G | Debit/credit `DebitCredit1, DebitCredit2` | 2 |
| H | **Rounding policy — 18 of these are duplicated verbatim in `dbo.RoundingRules`** + `RoundingRuleId` | 19 |
| I | Breaks `BreakDurationMin, AutomaticPauses, DeltaPause, MorningPauseId, AfternoonPauseId, AutoBreakShouldStartAfter, DeductUnswipedBreakFromDayHours` | 7 |
| J | **Exception thresholds inline** — Early/Late × Entry/Exit/BreakStart/BreakEnd × Enabled/Minutes | 16 |
| K | Exception behaviour `EntryExceptionAffectsIn2, ExitExceptionAffectsOut1, ShouldGenerateBlockingExceptionsOnSwipe, GenerateNoSwipesExceptionOnHoliday, OddNumberOfSwipesMinusTheoretic, AllowNoSwipes, IsRemarkRequired` | 7 |
| L | Swipe expectations `OneSwipeEnough, ApplyEntryRulesToOneSwipeEnough, SwipesExpected, IsSwipeRequiredForPanDayNight, UseFirstInLastOutSwipeForCalculation` | 5 |
| M | **Day allocation — the only four plan 002 needs** `NightShiftEndTime, NightShiftStartTime, ShiftToSunday, InterswipeIntervalToMoveToYesterday` | 4 |
| N | Hours classification `HoursRepartion` *(sic)*`, CountHoursIn, CountGapsIn, Features` | 4 |
| O | Master / flexi `IsMasterDailyModel, TreatAsMasterDailyModel, FlexiDailyModelId` | 3 |
| P | **Schedule thresholds — 4 duplicated verbatim in `dbo.GlobalScheduleThresholds`** + `GlobalScheduleThresholdId` | 5 |
| Q | Shift matching `TheoreticalDurationForShiftMatching` | 1 |
| R | **Custom SQL escape hatch** `CustomSql, CustomSqlForNoRecalculating, ShouldCustomSqlReturnMessage, RecalculateAfterCustomSql` | 4 |
| S | Reporting / misc `LostPremiaPercentage, ShowCodeForWeeklySchedulesReport` | 2 |
| | **Total** | **124** |

### 3.1 What this classification says

- **Groups B, C, D, G, H, P are all built on "a day has a morning half and an afternoon half."**
  Two IN/OUT pairs, two limits each, two debit/credit values, two schedule-threshold pairs,
  `DurationTheorMorning` / `DurationTheorAfternoon`, `MorningPauseId` / `AfternoonPauseId`,
  `FirtHalfRequired` / `SecondHalfRequired`, `MinTimeForHalfDay`. That is **31 of 124 columns**
  encoding a two-halves assumption that `ModelType` 9 and 11 then have to work around with six
  child models and twelve swipe slots.
- **Groups H and P are pure duplication.** `dbo.RoundingRules` and `dbo.GlobalScheduleThresholds`
  contain *exactly* the same fields as the inline columns, verified by set comparison — every
  `RoundingRules` column except `Id/Code/Name` exists on `DailyModels`, and there are none the
  other way. 23 of 124 columns exist twice, and nothing in the schema says which wins.
- **Group J is exception *configuration* stored on the template**, not in
  `ScoresAbnormalitiesSetup` (7 columns) — which holds only the type's name, colour and ordering.
  Add `DailyModelBreaks`' own ten exception columns and the exception rules are spread across three
  tables.

---

## 4. The Daily Browser — what it actually needs

`WebSite\Controllers\DailyBrowserController.cs`, **113 KB**, 26 constructor dependencies
(`:64-90`), 12 public actions.

### 4.1 `SCREEN-TREE.md:78-83` is half right, and the wrong half matters

The claim: *"Daily Browser cannot render without these. Its controller pulls in HoursCalculation,
ClockingPauses and Scores…"*. Tested against the controller by counting call sites:

| Injected service | Uses | Where |
|---|---|---|
| `IScoreService` | 15 | **read path — essential.** It *is* the repository for the grid rows: `GetDailyBrowserClockingsByFiltersAndDateRange` (`:1059`) |
| `IDailyModelService` | 4 | read path, but **degrades** — see below |
| `ICostCentreService` | 3 | read path (`:984` sets `HasCostCentres`) |
| `IPausesService` | 2 | read path — break *types*, for naming (`:990`) |
| **`IHoursCalculationService`** | **2** | **write path only** — `CalculateClockings` (`:1257`, `:1270`), reached solely from `Save` |
| **`IClockingPausesService`** | **1** | **write path only** — `:1725`, inside the save loop |
| `CorrectionService` | 1 | detail dropdown (`:814`) |
| `IAbsenceService` | 1 | detail dropdown (`:748`) |
| `IPlanningService` | 1 | save-time validation (`:415`) |
| **`ClockingService`** | **0** | **injected and never used** |

So: **the Daily Browser renders without an hours-calculation engine.** It reads stored `calc_*`
columns. HoursCalculation is required to *edit* a day, not to *see* one. That distinction is what
makes a first deliverable possible, and `SCREEN-TREE.md` currently forbids it.

The daily-template dependency is real but soft. `DailyBrowserDetailsViewModel.GetSwipes` opens with:

```csharp
var dailyModel = dailyModels.SafeGetValue(Clocking.DailyModelID, new DailyModel());//just in case
```
`WebSite\Models\DailyBrowser\DailyBrowserDetailsViewModel.cs:61`

A missing template yields a blank `DailyModel` — `ModelType == 0`, all times null. The grid renders:
no theoretical times, no second shift, `Duration` zero. It does not throw. The dictionary is built
by `GetAllDailyModelsWithPackages()`, a raw Dapper `select d.* from DailyModels d` with **no filter**
(`Logic\Settings\DailyModelService.cs:631-638`), so the fallback fires only when
`Clockings.DailyModelID` points at a deleted row — but it is a designed, reachable path, not an
assertion.

*(That query also pulls all 124 columns of every template on every grid render, and encodes a magic
value: for `ModelType in (5,6)` it returns `BreakDuration` **negated** to distinguish multi-break
models from two-shift models — `:633-637`.)*

### 4.2 The 126 columns are 74 columns of ceiling

Classified programmatically; **126 assigned, 0 unclassified**.

| Group | Cols |
|---|---|
| Key + employee identity | 15 |
| Daily model, denormalised onto the row | 13 |
| **CEILING — `BadgeTime1..12` + `BadgeTime1..12GeneratedBy`** | **24** |
| **CEILING — 6 shift slots (`CostCentreId1..6`, `DailyModelId1..6`)** | **12** |
| **CEILING — per-shift `calc_gross/net/breaks` × 6** | **18** |
| **CEILING — `CPTN01..20`** | **20** |
| Day totals (`calc_*`) | 11 |
| Absence (morning/afternoon + durations) | 5 |
| Correction | 3 |
| Day state (`Explanation, ShouldHideExceptions, HasAttachedDocuments, HolidayId, HolidayName`) | 5 |
| **Total** | **126** |

**74 of 126 columns — 59% — exist only because four things are fixed-width arrays.** WM's plan 002
already proposes replacing all four with rows. If it does, this view is a ~50-column projection over
normalised data, and most of it is derived.

### 4.3 The screen renders 48 columns, and ships with 14

`Logic\Scores\DailyBrowserColumn.cs` — the user-selectable column set is an enum of **48 members**,
of which 20 are `CPTN01..20`. Not 126.

Legacy's own out-of-the-box default (`Core\Constants.cs:48`):

```csharp
public const string DailyBrowser_SelectedColumnsValues = "2,3,4,5,6,9,10,11,12,20,22,24,28,29,30";
```

Resolved against the enum: **EmployeeName, Date, DailyModel, Swipes, Exceptions, NetAttendance,
Difference, Corrections, Absences, DurationTheoretic, Expenses, CurentBalance, CalcDifference,
Notes.** Fourteen columns.

Note `29`: the enum runs `CalcDifference = 28` then `Notes = 30`. **There is no member 29.** The
shipped default column list references a column id that does not exist. Harmless (it resolves to
nothing) but it is a defect, and it means the real default is 14 columns, not 15.

What legacy does **not** show by default: any pay-category counter, any per-shift breakdown
(members 14–19), gross attendance, unpaid break. **A useful Daily Browser is fourteen columns.**

---

## 5. Swipe → day allocation: exactly what plan 002 needs

This is the highest-value finding of the survey, so the provenance is spelled out.

### 5.1 Finding the authoritative definition

`E:\Tlw\Database\Versioning\*.sql` is an **append-only `CREATE OR ALTER` log**. The four allocation
fields appear in **38 separate files**. Following the call chain rather than the newest mention:

| Step | Object | Where the **latest** definition lives |
|---|---|---|
| 1 | C# calls stored proc `ProcessInOutSwipe` | `Horio.SyServerDll\Constants.cs:15`, invoked `SqlClient.cs:325` |
| 2 | `ProcessInOutSwipe` delegates validation + date resolution | `Versioning\67.V5.13.0.0.sql:3939` (only definition) |
| 3 | `ProcessSwipeValidateData` resolves the clocking | `Versioning\73.V5.19.0.0.sql:75` — **not** the copy at `67.V5.13.0.0.sql:3741`, which is six releases stale |
| 4 | `ProcessQueryGetClockingForSwipe` **is the allocation** | `Versioning\37.V3.6.1.0.sql:748` (latest in `Versioning\`) |

The swipe path is not C#. `SwipesProcessing.Application\ProcessInOutSwipe.cs:35` calls a
reflection-loaded DLL (`Horio.SwipeProcessing.Core\SwipeProcessingDLLWrapper.cs:101-124`) whose only
in-source implementation, `Horio.SyServerDll\SwipeProcessor.cs:18`, immediately calls the stored
procedure. **The rule lives in T-SQL.**

### 5.2 The algorithm, in order

`ProcessQueryGetClockingForSwipe(@employeeId, @swipeTime)` — `37.V3.6.1.0.sql:748-868`. Five
branches, first match wins, each returning a `ClockingId`:

| # | Branch | Reads | Lines |
|---|---|---|---|
| 0 | resolve the employee's **master template**, if any | `EmployeeMasterDailyModels.MasterDailyModelId/StartDate/EndDate` | `:765-769` |
| 1 | → **yesterday** if `@time < NightShiftEndTime` of *yesterday's* template | `NightShiftEndTime` | `:775-785` |
| 2 | → **tomorrow** if `@time > NightShiftStartTime` of *tomorrow's* template **and** `ShiftToSunday = 1` | `NightShiftStartTime`, `ShiftToSunday` | `:788-799` |
| 3 | → **yesterday** if no swipes today **and** `swipeTime < yesterdayFirstSwipe + InterswipeIntervalToMoveToYesterday` | `InterswipeIntervalToMoveToYesterday` | `:803-834` |
| 4 | → **yesterday** if yesterday is `ModelType = 7` and a shift-matching rule matches with the target's `NightShiftEndTime > @time` | `DailyModelShiftMatchingRules.*`, target `NightShiftEndTime` | `:839-864` |
| 5 | → **today** (fallback) | — | `:867` |

**The complete input set is five columns of `dbo.DailyModels` plus two small tables:**

```
DailyModels.NightShiftEndTime
DailyModels.NightShiftStartTime
DailyModels.ShiftToSunday                      -- misnamed; see 5.3
DailyModels.InterswipeIntervalToMoveToYesterday
DailyModels.TreatAsMasterDailyModel
DailyModels.ModelType                          -- only to test "= 7"
DailyModelShiftMatchingRules  (8 cols)
EmployeeMasterDailyModels     (5 cols)
```

**Five columns of 124.** Plan 002 P3 does not need the daily template module. It needs five
columns, two lookup tables, and a migration.

### 5.3 `ShiftToSunday` does not mean shift-to-Sunday

The column is the UI's **"Offset Transaction to Next Day"** toggle, paired with
`NightShiftStartTime` = **"If the swipe is after"** (`Documentation\Daily Templates\Daily
Templates.md:370-371`). Every one of its five non-generated usages in the codebase reads it together
with `NightShiftStartTime` and nothing else — `37.V3.6.1.0.sql:795`,
`DailyBrowserSwipeViewModel.cs:46`, `ManualTimesheetSwipeViewModel.cs:37`,
`Helpers\Mappers\FireReport.cs:810`, `DailyModelControllerBase.cs:556`. Nothing anywhere tests a
weekday. WM must not carry the name.

### 5.4 Six behaviours the vault does not document

`Documentation\Swipe to clocking allocation.md` describes branches 1–3 and 5 and is accurate as far
as it goes. The function does more.

1. **Master daily model is a two-level override, and it is invisible in the docs.** All three of
   branches 1–3 resolve their field as
   `case when d.TreatAsMasterDailyModel = 0 and m.Id is not null then m.<field> else d.<field> end`
   (`:775-778`, `:788-791`, `:812-815`). An employee with a master assignment has the *day's own
   template overridden* for allocation purposes unless that template opts out via
   `TreatAsMasterDailyModel = 1`. This is the two-level config-resolution smell, and no WM document
   currently records it.

2. **The master-model date window is asymmetric and looks like a bug.** `:768-769`:
   ```sql
   and (StartDate is null or StartDate <= @yesterday)
   and (EndDate   is null or EndDate   >= @tomorrow)
   ```
   The window is tested against **yesterday and tomorrow**, never the swipe date. A master model
   assigned for exactly one day never applies on that day; it must span three days to affect the
   middle one. WM should compare against the swipe's own date and say so.

3. **Branch 4 — shift matching — is a fifth rule nobody wrote down.** For `ModelType = 7`, the
   swipe can be pulled back to yesterday if it would complete a shift-matching rule
   (`MatchType = 1` by end time, `MatchType = 2` by both start and end) *and* the template that rule
   would assign has `NightShiftEndTime > @time`. The vault mentions this in one sentence
   (`Swipe to clocking allocation.md:55`) without the mechanism.

4. **"First swipe of yesterday" is the first non-null slot, not the earliest time.** `:823-825`
   and `:845-847` both compute it as a twelve-deep
   `isnull(badgetime1, isnull(badgetime2, … badgetime12))`. If the slots are not in ascending time
   order — which manual correction, `hasOnly2SwipesFor4SwipesModel` (§6.2) and multi-shift
   assignment can all cause — branches 3 and 4 use the wrong anchor.

5. **Boundaries are strict inequalities.** `@time < @nightShiftEndTime` (`:784`) and
   `@time > @nightShiftStartTime` (`:798`). A swipe landing *exactly* on the boundary stays on its
   own day. This matches the vault's 04:00/04:30 examples and must be preserved.

6. **A swipe with no pre-generated clocking row is rejected and lost.**
   `73.V5.19.0.0.sql:232-256`: if `ProcessQueryGetClockingForSwipe` returns null, `@isValid = 0`,
   message `'Clock Record not found'`, and the swipe goes to the unsuccessful-swipe log. TLW
   **pre-generates one `Clockings` row per employee per day** in a nightly service job; the swipe
   attaches to an existing row and never creates one. The customer-facing symptom is documented at
   `Documentation\Troubleshooting\No Calendar (Clockings) for employee.md:19` — *"Ones restarted,
   calendar (clocking) records will be generated overnight"*. This is fail-closed in a way that
   silently discards real attendance, and it is why plan 002 P2 (the calendar job) must exist.

### 5.5 What plan 008 already got right, and the one thing it missed

`008-a-day-has-a-place.md:253-256` correctly names the seam and the four fields, and correctly
assigns the eleven vault Given/When/Thens to plan 002. Its count of "five allocation branches"
matches this measurement exactly.

It does not mention the **master daily model override** (§5.4.1–2), which is a sixth input and
changes the answer for any employee who has one. Recorded here; 010 P1 owns it.

---

## 6. Behaviour found only by reading the code

### 6.1 The clocking is a *snapshot* of its template

Ten `DailyModels` columns are copied onto every `Clockings` row and read from there:
`Enter1Start, Enter1End, Exit1Start, Exit1End, Enter2Start, Enter2End, Exit2Start, Exit2End,
DurationTheoretic, BreakDurationMin` (set comparison of the two tables' column names).

The read path prefers the snapshot and falls back to the template:

```csharp
Time = Clocking.BadgeTime1 ?? (Clocking.Enter1Start ?? dailyModel.Enter1Start),
```
`DailyBrowserDetailsViewModel.cs:92`

This is **good design accidentally**: editing a template does not silently rewrite history, and a
manager can override one day's expected window without touching the template. It is denormalisation
that buys temporal correctness. **Keep it** — as an explicit effective-dated snapshot rather than
an accident.

### 6.2 The read path mutates the entity

`DailyBrowserDetailsViewModel.cs:70-79`:

```csharp
var hasOnly2SwipesFor4SwipesModel =
    !isOneShiftModel &&   //two shifts
    HasOnly2Swipes &&     //only two swipes
    HasNoAbsences;        //and not on a leave
if (hasOnly2SwipesFor4SwipesModel)
{
    Clocking.BadgeTime4 = Clocking.BadgeTime2;
    Clocking.BadgeTime2 = null;
}
```

A two-shift template that received only two swipes has them **re-slotted on read** — the OUT moves
from slot 2 to slot 4, so the pair renders as "shift 1 IN, shift 2 OUT" (a shift spanning the
unswiped middle) rather than "shift 1 IN/OUT". Guarded by "no absence", because a half-day absence
means the two swipes really are one shift.

The reinterpretation is right; doing it in a getter on the shared entity is not. WM must make it an
explicit, tested projection rule — and note §5.4.4, since this mutation is one of the things that
can leave slots out of time order.

### 6.3 The custom-SQL escape hatch, and the bar it sets

Four columns (group R) hold arbitrary SQL run during calculation of every affected day.
`IDailyModelService.CopySqlToDailyModels(List<int> dailyModelIds, string sql, int? sourceDailyModelId)`
(`Logic\Settings\Interfaces\IDailyModelService.cs:12`) exists to **bulk-copy one script across many
templates**, which is what a heavily-used feature looks like. Failures surface to the user:
`Save` collects `calculationResult.CustomSqlExecutionResult.Messages` (`DailyBrowserController.cs:1258-1261`).

`Documentation\Custom SQL\Daily Template SQL.md` documents the contract — `{0}` = EmployeeId,
`{1}` = ClockingId, `select`/`update` only, no `delete`/`drop`/`truncate` (`:32-39`) — and gives
five worked examples. **These set the expressive bar for WM's replacement rules language:**

| Example | Capability required | Line |
|---|---|---|
| `set CPTN01 = 2 where ClockingId = {1} and HasSwipes = 1` | assign a counter on a day predicate | `:133-137` |
| `set CPTN05 = isnull(CPTN05,0) + 2 … and calc_netAttendance >= 8` | **read-modify-write** a counter against a calculated measure | `:140-146` |
| `set CPTN10 = 1 … datepart(weekday,…) and (select count(*) … date >= dateadd(day,-6,…) and HasSwipes = 1) = 6` | **weekday predicate + cross-day count over a relative window** | `:149-163` |
| `set CPTN02 = 0.5 … and MorningAbsenceID = 1` | half-day absence predicate | `:165-171` |
| `set CPTN15 = (select sum(isnull(CPTN15,0)) … date >= dateadd(day,-7,…) and date < …)` | **cross-day aggregation writing to a boundary day** | `:184-196` |

A per-row expression evaluator will not do. The language needs windowed aggregation across an
employee's neighbouring days, which is exactly what `ARCHITECTURE.md:549` calls the "safe rules
expression language" and what `TLW-CLOCKING-MODEL.md:189-197` already flagged. Confirmed here.

### 6.4 Exceptions: 67 variants, 2 severities, 24 of them a ceiling

`Core\Enumeration\Enums.cs:86-155` — `ExceptionVariants`, 67 members. `:80-84` — `ExceptionType`
is just `Informational | Blocking`.

Of the 67, **24 are slot-duplicated**: `NoGeolocationSwipe1..12` (`:129-140`) and
`SwipeWithoutLocation1..12` (`:141-152`). The genuine list is **43 exception types**, covering
attendance (no swipes, odd number, more than 4), timing (late arrival, early departure, arrived
outside anticipated time), breaks (early/late pause start/end, long, short, missed), balances
(max/min exceeded), core hours, shift matching (`NoMatchForShiftMatching`), activities, and
`OverlappingSwipes`.

Muting is a **day-level** flag, not a per-exception one: `Clockings.ShouldHideExceptions`, exposed
on the view and filtered by the grid's `ShowOnlyMuted` / `ShowOnlyNotMuted`
(`DailyBrowserController.cs:178-179`), gated by `SoftwareSettings.ShouldShowMuteButton()` (`:356`).
Authorising is separate and per-exception: `dbo.ScoresAbnormalitiesAuthorized` (8 cols) records
`UserId` and `DateAuthorized`. **Mute and authorise are two different things** and WM should not
merge them.

"Blocked exception rules" is a **global** setting, not per-template: three modes deciding what a
blocking exception does to the day's hours (`Documentation\Blocked Exception Rules.md:11-14`) —
net 0 / debit 0, net 0 / debit −expected, or net = expected / debit 0. Three enum values, not a
screen's worth of configuration.

---

## 7. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A day is governed by a **template** chosen per employee per day | **Keep** | genuine domain truth; every calculation, roster and payroll export agrees |
| The clocking **snapshots** its template's windows (§6.1) | **Keep** — make it explicit | it is what stops a template edit rewriting history. Legacy gets this right by accident |
| Allocation is computed from **neighbouring days'** templates | **Keep** | correct, and already 008's position (`008:144`) |
| Mute (day) and authorise (exception) are **separate** | **Keep** | two genuinely different manager actions |
| `ModelType` — 11 variants in one 124-column table | **Improve** | the variants are real; one wide table with mutually-exclusive columns is not. Model as a discriminated shape, so a `ShiftMatching` template cannot carry `Exit2End` |
| Two IN/OUT pairs + morning/afternoon halves (31 cols, §3.1) | **Improve** | "segments of a day" as rows removes the ceiling *and* subsumes types 2, 4, 9 and 11 |
| Rounding policy stored **twice** (19 cols inline + `RoundingRules`) | **Improve** | one named, reusable policy referenced by the template. Never both |
| Schedule thresholds stored **twice** (4 cols inline + `GlobalScheduleThresholds`) | **Improve** | same fix, same reason |
| 16 exception threshold columns on the template + 10 on `DailyModelBreaks` | **Improve** | exception rules as rows keyed by type; the 43 real types then need no schema change to extend |
| `ShiftToSunday` (§5.3) | **Improve** | rename to what it is: `OffsetTransactionToNextDay` |
| `FirtHalfRequired`, `HoursRepartion`, `FixedShedule*` | **Improve** | migrate the typos; they are in the schema, the enum and the JS |
| Master-model window tested against yesterday/tomorrow (§5.4.2) | **Invert** | compare against the swipe's own date |
| `new DailyModel()` fallback for a missing template (§4.1) | **Invert** | a day whose template does not exist is a data defect. Surface it as an exception on the day, do not render a silent zero |
| Swipe with no pre-generated clocking is **discarded** (§5.4.6) | **Invert** | a punch is evidence. Accept it, create the day, raise an exception — never lose it |
| `BadgeTime1..12` / `CPTN01..20` / 6 shifts / `calc_*Shift1..6` (74 of 126 view cols) | **Invert** | rows, not columns. Already plan 002's proposal; this survey is the second independent confirmation |
| Per-template `CustomSql` (group R) | **Invert** | replace with the safe rules language whose bar §6.3 sets. Arbitrary `UPDATE` against `Clockings` cannot ship |
| `NoGeolocationSwipe1..12`, `SwipeWithoutLocation1..12` (24 of 67 exception variants) | **Invert** | one type carrying the punch reference |
| Blocked-exception rule as 3 global modes | **Keep** | small, clear, and genuinely global |
| `dbo.AdditionalModels` | **Drop** from Work Rules | it belongs to Planning (`Logic\Planning\PlanningService.cs:80-146`), not here |
| Day types / calendar day models | **Drop** | Access Control (§9.1) — already out of scope by invariant 3 |
| `ClockingService` injected unused in the controller | **Drop** | dead |

---

## 8. Sizing

| Question | Answer |
|---|---|
| How big is Work Rules — base? | **22 tables, 341 columns**, one 133 KB editor view, a 91 KB controller, and a ~700 KB calculation engine |
| How big is the Daily Browser? | 113 KB controller, 66 KB grid view, **48 selectable columns**, 26 injected services |
| What is the smallest thing that renders a Daily Browser? | **14 columns** — legacy's own shipped default (§4.3) — over a Clocking that already has stored totals. **No hours-calculation engine** (§4.1), no counters, no per-shift breakdown |
| What must land first? | The **Clocking aggregate** (plan 002 P1/P2). The Daily Browser is a read model over it |
| What does 002 need from Work Rules? | **Five columns and two small tables** (§5.2) — not the module |
| Is the daily template editor on the critical path? | **No.** 163 fields configure a calculation engine that a read-only Daily Browser never invokes |

---

## 9. Corrections made to WM's records

### 9.1 Day types and calendar day models are Access Control, not Work Rules

`SCREEN-TREE.md:70` listed *"Daily templates · Day types · Calendar day models"* under **Work
Rules — base**, and `TLW-INVENTORY.md:147` repeated it.

Measured:

| Screen | Controller | Table | Cols |
|---|---|---|---|
| Day types | `WebSite\Controllers\**AccessControl**\DayTypeController.cs` | `dbo.ac_day_type` | 5 |
| Calendar | `WebSite\Controllers\**AccessControl**\AccessControlCalendarController.cs` | `dbo.ac_calendar` | 5 |
| Period | `WebSite\Controllers\**AccessControl**\AccessControlPeriodController.cs` | `dbo.ac_period` | 5 |

The `ac_*` family (12 tables) is the door-access schema — `ac_security_group`,
`ac_securitygroup_readers`, `ac_timezone`, `ac_automode_terminals`. This is **the same error
`SCREEN-TREE.md:29` already corrected once** for `SecurityGroup`, and `008:155` corrected again for
`ac_timezone`. Third instance; both lines now fixed.

There is no Work Rules "day type". The nearest concept is `ModelType` (§2) and the system *Sans*
day-off template.

### 9.2 "Pay categories" and "pay periods" have no tables of those names

- **Pay category = `dbo.Counters`** (7 cols), value = `Clockings.CPTN01..20`.
- **Pay period = `dbo.Periods`** (6 cols, `PeriodsController.cs`) — distinct from `dbo.ac_period`,
  which is access control. Both lines annotated.

### 9.3 `SCREEN-TREE.md:78-83` overstated the Daily Browser's dependencies

Corrected in place: HoursCalculation and ClockingPauses are **write-path only**; the template
dependency **degrades** rather than blocking. See §4.1.

### 9.4 Plan 002 P3 said "the three legacy rules"

There are **five branches plus a master-template override** (§5.2, §5.4.1). `008:253-256` already
said five; 002 was stale relative to it. Corrected.

### 9.5 Not corrected — proposed only

`ARCHITECTURE.md:779` marks TimeAttendance **"◐ core built"**. Measured, the module is **9 files**:
`Punch.cs` (10 properties), `PunchService.cs`, a seeder, one migration, one contract — and **no test
project**. Against an area whose legacy footprint is a 249-column aggregate, a 341-column rules base
and a 700 KB calculation engine, "core built" describes punch capture only. `ARCHITECTURE.md` is
under an open PR (#62) and was not edited; the proposed change is in plan 010's open questions.

---

## 10. What this survey did **not** measure

Stated explicitly, because two previous surveys here overstated their coverage.

- **`HoursCalculationService.cs` (276 KB) was not read.** Only its file inventory, its entry points
  from the Daily Browser (`CalculateEmployee`, `RecalcuateBalanceForEmployees`) and its
  `CustomSqlExecutionResult` surface. **The rounding, break-deduction, night-hours and
  counter-allocation algorithms are unmeasured.** Any estimate of Phase 2 that leans on this
  document is leaning on an unread 276 KB file.
- **`DailyModelBreaks` (34 cols) was listed, not classified.** Its ten exception columns were
  counted; the break-deduction semantics were not traced.
- **Groups D, E, N of §3** — `DelayTolerance`/`ClearingOutput` (4), the eleven duration columns, and
  `HoursRepartion`/`CountHoursIn`/`CountGapsIn`/`Features` (4) — are named and grouped but their
  **semantics were not traced into the calculation engine**. `Features` is an `int` whose candidate
  enum is `FeatureType` (`Logic\Settings\DailyModelEnums.cs:42-49`, five members); that mapping was
  **not** confirmed. 19 of 124 columns are therefore classified by name, not by behaviour.
- **The periodic/weekly branch** (`WeeklyShifts`, `WeeklyModel*Counters`) and **Balances**
  (`FlexiBalances`, `Counters` allocation) were counted in §1.2 and otherwise not surveyed.
- **`_AddEditDailyModelControls.cshtml` (133 KB) was not read** — only measured. The field list in
  §3 comes from the schema and `Documentation\Daily Templates\Daily Templates.md`, not from the view.
- **`ProcessQueryProcessInOut`** — the procedure that writes the swipe into a slot once the day is
  chosen (`67.V5.13.0.0.sql:4005`) — was **not** read. Slot-selection rules (which `BadgeTimeN` a
  swipe lands in, and what happens on the 13th) are unmeasured.
- **`DailyBrowserController` actions other than `Index`/`GetGrid`/`Save`** were not read.
- Column counts come from the LINQ-to-SQL model, not from a live database. If a customer database
  has drifted, this document describes the model.
