# 010 — The Daily Browser, and the five columns it takes to get there

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 2 (Rules engine) — the smallest useful slice of it, pulled forward
Reference: [`TLW-WORK-RULES.md`](../TLW-WORK-RULES.md) — full measured anatomy, written by this survey

Legacy sources surveyed (files actually opened):

- `Logic\Entities\HorioDB.designer.cs` — **578 tables / 8,173 columns** measured;
  `dbo.DailyModels` **124** (`:11056`), `dbo.DailyBrowserView` **126** (`:118931`), both confirmed
- `WebSite\Controllers\DailyBrowserController.cs` (113 KB — `:64-90`, `:124-360`, `:740-1060`, `:1230-1290`, `:1700-1740`)
- `WebSite\Models\DailyBrowser\DailyBrowserDetailsViewModel.cs` (`:50-280`, `:700-806`)
- `WebSite\Models\DailyBrowser\DailyBrowserSwipeViewModel.cs` (`:28-68`)
- `Logic\Scores\DailyBrowserColumn.cs` · `Core\Constants.cs:48`
- `Logic\Entities\DailyModel.cs` · `Logic\Entities\BusinessRules\DailyModels.cs` ·
  `Logic\Entities\BusinessRules\DailyBrowserClocking.cs`
- `Logic\Settings\DailyModelService.cs` (`:625-682`) · `Interfaces\IDailyModelService.cs` ·
  `Logic\Settings\DailyModelEnums.cs` · `SharedLogic\Enums\ShiftType.cs`
- `Logic\Settings\UserPreferenceProvider.cs` (`:916-1128`)
- `Core\Enumeration\Enums.cs` (`:80-155`, `:1140-1201`, `:1400-1444`)
- `Horio.SyServerDll\SwipeProcessor.cs` · `SqlClient.cs:315-365` · `Constants.cs:5-21` ·
  `Horio.SwipeProcessing.Core\SwipeProcessingDLLWrapper.cs` · `SwipesProcessing.Application\ProcessInOutSwipe.cs`
- `WebSite\Controllers\CountersController.cs` (whole file) · `Controllers\AccessControl\DayTypeController.cs` (located)
- `Database\Versioning\67.V5.13.0.0.sql:3939` · **`73.V5.19.0.0.sql:75-263`** ·
  **`37.V3.6.1.0.sql:748-868`** · `27.V2.1.12.sql:390-501` — the append-only log was walked;
  §5.1 of the reference records how the latest definition of each object was established
- `Documentation\`: `Swipe to clocking allocation.md`, `Daily Templates\Daily Templates.md`,
  `Custom SQL\Daily Template SQL.md`, `Blocked Exception Rules.md`,
  `Troubleshooting\No Calendar (Clockings) for employee.md`

---

## Ground truth

**Work Rules — base is 22 tables and 341 columns** (`TLW-WORK-RULES.md` §1.1). `dbo.DailyModels`
alone is 124 of them — 36% of the area in one table — and the calculation engine behind it,
`Logic\HoursCalculation\`, is **79 files and ~700 KB of C#**, of which `HoursCalculationService.cs`
is 276 KB.

That is the whole module. This plan does not build it. Three measurements say it does not have to.

**1. The Daily Browser renders without a calculation engine.** Counting call sites in
`DailyBrowserController.cs`, `IHoursCalculationService` is used **twice** (`:1257`, `:1270`), both
inside `CalculateClockings`, reached only from `Save`. `IClockingPausesService` is used **once**
(`:1725`), also in the save loop. `ClockingService` is injected and **never used**. The grid reads
stored `calc_*` columns. `SCREEN-TREE.md:78-83` — *"Daily Browser cannot render without these"* —
is right about `IScoreService` (15 uses, it is the row repository) and wrong about the rest.

**2. The daily-template dependency degrades, it does not block.**

```csharp
var dailyModel = dailyModels.SafeGetValue(Clocking.DailyModelID, new DailyModel());//just in case
```
`DailyBrowserDetailsViewModel.cs:61`

A missing template yields `ModelType == 0` and all-null times; `DailyModel.Duration` special-cases
zero to avoid its own `throw` (`Logic\Entities\DailyModel.cs:12-13`, `:69`). The grid renders.

**3. Legacy ships fourteen columns, not 126.** The selectable set is 48 members
(`Logic\Scores\DailyBrowserColumn.cs`); the shipped default is
`"2,3,4,5,6,9,10,11,12,20,22,24,28,29,30"` (`Core\Constants.cs:48`) = EmployeeName, Date,
DailyModel, Swipes, Exceptions, NetAttendance, Difference, Corrections, Absences,
DurationTheoretic, Expenses, CurentBalance, CalcDifference, Notes. **No pay-category counters. No
per-shift breakdown.** (Id `29` is in the default list and absent from the enum — see Edge cases.)

And **74 of the view's 126 columns are four fixed-width arrays**: 24 badge slots, 12 shift slots,
18 per-shift measures, 20 counters (`TLW-WORK-RULES.md` §4.2, classified programmatically, 0
unclassified). Plan 002 already proposes replacing all four with rows.

### The finding that changes the sequencing

`TLW-CLOCKING-MODEL.md:57` and plan 002 P3 say swipe→day allocation "depends on the neighbouring
days' daily templates". Measured, it depends on **five columns of the 124**:

```
NightShiftEndTime · NightShiftStartTime · ShiftToSunday · InterswipeIntervalToMoveToYesterday
ModelType (only tested for = 7)
+ dbo.DailyModelShiftMatchingRules (8 cols) · dbo.EmployeeMasterDailyModels (5 cols)
```

Source: `dbo.ProcessQueryGetClockingForSwipe`, latest definition `37.V3.6.1.0.sql:748-868`, reached
from `ProcessInOutSwipe` → `ProcessSwipeValidateData` (`73.V5.19.0.0.sql:232`).

**The Daily Browser is not blocked behind an unplanned module. It is blocked behind five columns and
the Clocking aggregate.**

### Corrections made to WM's records by this survey

| Record | Was | Now |
|---|---|---|
| `SCREEN-TREE.md:70` | "Daily templates · Day types · Calendar day models" under Work Rules | Day types / calendar are **Access Control** (`Controllers\AccessControl\DayTypeController.cs`, `dbo.ac_day_type`). Third instance of the `ac_*` misattribution, after `SecurityGroup` (`:29`) and `ac_timezone` (`008:155`) |
| `SCREEN-TREE.md:74` | "Pay categories · Pay periods" | annotated: pay category = `dbo.Counters` (7); pay period = `dbo.Periods` (6), **not** `dbo.ac_period` |
| `SCREEN-TREE.md:78-83` | "cannot render without these" | corrected to name the read path vs the write path |
| `TLW-INVENTORY.md:147-148` | same day-type / pay-category claims | corrected, with table names and counts |
| `002-clocking-aggregate.md:99-100` | "the three legacy rules" | **five branches + a master-template override**; `008:253-256` already said five |

Not corrected: `ARCHITECTURE.md:779` (`TimeAttendance ◐ core built`). PR #62 is open against that
file. Proposal in Open questions.

---

## Legacy behaviour (what we are replacing)

**`ModelType` is eleven templates in one table** (`SharedLogic\Enums\ShiftType.cs:4-17`), plus a
twelfth identified by code — the system *Sans* day-off model
(`Logic\Entities\DailyModel.cs:79`, `DailyModelService.cs:670-673`). Four of the eleven return
`TimeSpan.Zero` from `Duration` (`:19-23`) and take expected hours from elsewhere.

**Thirty-one of 124 columns encode "a day has two halves"** — two IN/OUT pairs, two limit sets,
`DebitCredit1/2`, `DurationTheorMorning/Afternoon`, `MorningPauseId/AfternoonPauseId`,
`FirtHalfRequired/SecondHalfRequired`, two schedule-threshold pairs. `ModelType` 9 and 11 then work
around that assumption with six child models and twelve swipe slots.

**Twenty-three columns exist twice.** `dbo.RoundingRules` (21) is *exactly* `Id/Code/Name` plus 18
columns that are also on `DailyModels` — verified by set comparison, zero columns on either side
without a partner. `dbo.GlobalScheduleThresholds` (7) is the same pattern with 4. Nothing in the
schema records which wins.

**The clocking snapshots its template.** Ten `DailyModels` columns are copied onto every
`Clockings` row (`Enter1Start`…`Exit2End`, `DurationTheoretic`, `BreakDurationMin`) and preferred on
read: `Clocking.BadgeTime1 ?? (Clocking.Enter1Start ?? dailyModel.Enter1Start)`
(`DailyBrowserDetailsViewModel.cs:92`). Editing a template does not rewrite history. This is the one
piece of denormalisation in the area that is correct.

**Exceptions: 67 variants, 43 real.** `Core\Enumeration\Enums.cs:86-155`. Twenty-four are
`NoGeolocationSwipe1..12` and `SwipeWithoutLocation1..12`. Severity is binary —
`Informational | Blocking` (`:80-84`). Muting is a **day** flag (`Clockings.ShouldHideExceptions`,
filtered at `DailyBrowserController.cs:178-179`); authorising is **per exception** with a user and
timestamp (`dbo.ScoresAbnormalitiesAuthorized`, 8 cols). Two different actions.

**The 20-pay-category ceiling has no escape valve at all.** `dbo.Counters` is created and seeded
with **exactly 20 rows** in one block, named via localization keys
(`Database\Versioning\27.V2.1.12.sql:390-501` — 20 `insert` statements, counted), and
`WebSite\Controllers\CountersController.cs` exposes **only `Index` and `EditCounter`** — no Create,
no Delete. Customers can rename a pay category; nobody has ever been able to add one. This bears on
plan 002's open question 2: no customer overflows the ceiling, because overflowing is impossible.
The migration is 20 named rows, and normalising buys headroom rather than rescuing existing data.

**The escape hatch.** `CustomSql` + 3 control columns run arbitrary SQL during calculation;
`IDailyModelService.CopySqlToDailyModels(...)` exists to bulk-copy one script across templates.
The documented examples need cross-day windowed aggregation, not per-row expressions
(`TLW-WORK-RULES.md` §6.3).

---

## Keep / Improve / Invert / Drop

Scoped to what this plan touches. The full table is `TLW-WORK-RULES.md` §7.

| Structure | Class | Reason |
|---|---|---|
| A day is governed by a template chosen per employee per day | **Keep** | genuine domain truth |
| The clocking **snapshots** its template's windows | **Keep**, made explicit | it is what stops a template edit rewriting history |
| Allocation computed from **neighbouring days'** templates | **Keep** | correct; already 008's position (`008:144`) |
| Mute (day) and authorise (exception) separate | **Keep** | two different manager actions |
| Daily Browser as a **read model** with a small default column set | **Keep** | legacy ships 14 of 48; WM should not open with 126 |
| `ModelType` — 11 variants, one 124-col table | **Improve** | variants are real; mutually-exclusive columns in one table are not. Only the discriminator lands here; the shape lands in Phase 2 |
| Two IN/OUT pairs + morning/afternoon (31 cols) | **Improve** | day segments as rows; subsumes types 2, 4, 9, 11 — **Phase 2, not this plan** |
| Rounding + thresholds stored twice (23 cols) | **Improve** | one named policy, referenced. **Phase 2** |
| `ShiftToSunday` | **Improve** | rename to `OffsetTransactionToNextDay`. It has never meant Sunday: every non-generated use pairs it with `NightShiftStartTime` (`37.V3.6.1.0.sql:795`, `DailyBrowserSwipeViewModel.cs:46`, `ManualTimesheetSwipeViewModel.cs:37`, `FireReport.cs:810`, `DailyModelControllerBase.cs:556`) |
| `FirtHalfRequired`, `HoursRepartion`, `FixedShedule*` | **Improve** | migrate the typos rather than inherit them |
| Master-model window tested against **yesterday and tomorrow** (`37.V3.6.1.0.sql:768-769`) | **Invert** | compare against the swipe's own date. Today a one-day assignment never applies |
| `new DailyModel()` for a missing template (`:61`) | **Invert** | a day whose template does not exist is a data defect. Raise it; do not render a silent zero |
| Swipe with no pre-generated clocking → **discarded** (`73.V5.19.0.0.sql:233-256`) | **Invert** | a punch is evidence. Create the day, raise an exception, never lose it |
| `BadgeTime1..12` / `CPTN01..20` / 6 shifts / per-shift `calc_*` — 74 of 126 view cols | **Invert** | rows. Already 002's proposal; this is the second independent confirmation |
| "First swipe of yesterday" = first **non-null slot**, not earliest time (`:823-825`) | **Invert** | order by time |
| Read path mutating the entity (`DailyBrowserDetailsViewModel.cs:70-79`) | **Invert** | the *reinterpretation* is right; a getter mutating shared state is not. Make it a tested projection rule |
| Per-template `CustomSql` | **Invert** | safe rules language. **Phase 2** — out of scope here |
| `NoGeolocationSwipe1..12`, `SwipeWithoutLocation1..12` | **Invert** | one type carrying the punch reference |
| Day types / calendar day models | **Drop** | Access Control; already out by invariant 3 |
| `dbo.AdditionalModels` | **Drop** from Work Rules | it is Planning's (`Logic\Planning\PlanningService.cs:80-146`) |
| The 163-field template editor | **Drop from this plan** | it configures an engine a read-only browser never calls |
| `ClockingService` injected unused | **Drop** | dead |

---

## Edge cases

### Allocation — the eleven vault examples are 002's; these are the ones the vault omits

The Given/When/Thens in `Documentation\Swipe to clocking allocation.md:43-118` belong to plan 002 P3
and `008:166-181` already lifts three verbatim. **Do not duplicate them here.** The following are
behaviours found in `37.V3.6.1.0.sql` that the vault does *not* describe, and they are P1's tests.

| # | Case | Required behaviour |
|---|---|---|
| A1 | Employee has a master template; the day's own template has `TreatAsMasterDailyModel = 0` | the **master's** `NightShiftEndTime` / `NightShiftStartTime` / `InterswipeInterval…` are used, not the day's (`:775-778`, `:788-791`, `:812-815`) |
| A2 | Same, but the day's template has `TreatAsMasterDailyModel = 1` | the day's own template wins |
| A3 | Master assignment `StartDate = EndDate =` the swipe date | **legacy: never applies** (window is tested against yesterday *and* tomorrow, `:768-769`). **WM: applies.** Test both the legacy behaviour and the inverted one, and record the divergence |
| A4 | Swipe at exactly `NightShiftEndTime` | stays on its own day — `<` is strict (`:784`) |
| A5 | Swipe at exactly `NightShiftStartTime` | stays on its own day — `>` is strict (`:798`) |
| A6 | `NightShiftStartTime` set but `ShiftToSunday = 0` | branch 2 does **not** fire (`:795`) |
| A7 | Yesterday is `ModelType = 7` (shift matching), swipe would complete a rule whose target template has `NightShiftEndTime > @time` | → yesterday (`:839-864`). `MatchType = 1` matches on end only; `MatchType = 2` on start **and** end |
| A8 | Yesterday's badge slots are out of time order | legacy anchors branch 3 on the first **non-null slot** (`:823-825`), not the earliest time. WM orders by time; assert the two differ and WM is right |
| A9 | All five branches miss | → the swipe's own date (`:867`) |
| A10 | No clocking row exists for the resolved date | **legacy discards the swipe** (`73.V5.19.0.0.sql:233-256`, `'Clock Record not found'`). WM creates the day and raises an exception. Assert no punch is ever lost |

### Rendering

| Case | Required behaviour |
|---|---|
| `Clocking.DailyModelId` references a template that does not exist | the row renders **and** carries a visible defect marker. Not a silent zero (`:61`), not a 500 |
| A day with no punches at all | renders — expected hours, absence, zero net. Legacy's `AllowNoSwipes` exists precisely for this |
| A day with no template assigned at all | renders; no theoretical times |
| Two-shift template, exactly two punches, no absence | punches present as *shift 1 IN* / *shift 2 OUT*, not *shift 1 IN/OUT* (`DailyBrowserDetailsViewModel.cs:70-79`). **With** a half-day absence, the opposite |
| More punches than the legacy 12 | renders all of them. This is the ceiling WM exists to remove; assert no truncation |
| An unpaired trailing IN | renders as an open pair, not a crash. `PrepareSwipePairs` (`:706-779`) starts a new pair on any IN |
| Employee left mid-range | days before the leave date render; days after do not. Depends on 007 P1's `IsEmployedOn(date)` |
| Column id in the saved preference that no longer exists | ignored silently. Legacy's own default contains id `29`, which is absent from `DailyBrowserColumn` (`CalcDifference = 28`, `Notes = 30`) — the shipped default is 14 columns, not 15 |
| User has never opened the screen | the 14-column default applies, per install, overridable per user (`UserPreferenceProvider.cs:963-1022`) |

### Precedence — the "who wins" questions this area raises

| Contest | Legacy answer | Where |
|---|---|---|
| Master template vs the day's own template | master, unless `TreatAsMasterDailyModel = 1` | `37.V3.6.1.0.sql:775-778` |
| Clocking's snapshotted window vs the template's current window | the snapshot | `DailyBrowserDetailsViewModel.cs:92` |
| Inline rounding columns vs `RoundingRuleId` → `RoundingRules` | **unrecorded anywhere.** Phase 2 must decide | `TLW-WORK-RULES.md` §3.1 |
| Inline schedule thresholds vs `GlobalScheduleThresholdId` | **unrecorded.** Phase 2 | same |
| Manual correction vs recalculation | `IsDTManualChanged` on the clocking (`Enums.cs:1410`); semantics unmeasured | — |

---

## Target design in WM

**Module boundaries (invariant 1).** The allocation rule reads template fields. Those fields belong
to **Rules** (`ARCHITECTURE.md:780`), which does not exist yet. Creating an empty module to hold
five columns is worse than the alternative: land the five columns as a **`DayTemplate` read
contract owned by TimeAttendance now**, with an explicit note that Rules takes ownership in Phase 2
and TimeAttendance then consumes it through a contract — the same move People/`IEmployeeDirectory`
already made (`PunchService.cs:31`). This is called out in Open questions because it is a boundary
decision, not a coding one.

```
TimeAttendance   Clocking (002 P1/P2) · DayTemplate (the allocation subset) ·
                 allocation service · Daily Browser read model + endpoint
People           unchanged — employee/site data reached via the existing contract
SharedKernel     unchanged — 008 P4's (instant, zone) → local date seam is the input
frontend/portal  the Daily Browser grid, 14 columns
```

**Endpoints.** `GET /api/timeattendance/daily-browser?from&to&employeeId|departmentId` returning
the 14-column projection, paged, under a policy (invariant 5) and through Access scope filters
(plan 001). A write endpoint is **out of scope** — see below.

**Events.** Nothing new on `wm.punches`. A `ClockingDayChanged` event is Phase 2's, when something
can change a day.

**Screens.** One: the Daily Browser grid, read-only, in the existing Control Room shell. Column
selection persisted per user, defaulting to the 14.

---

## Out of scope for this plan

- **Editing a day.** No save, no correction, no manual punch entry, no pause editing. Every one of
  those calls the calculation engine in legacy (`DailyBrowserController.cs:1257`), and the engine is
  Phase 2. This plan ships a screen you can *read*.
- **The 163-field daily template editor.** Templates are seeded/imported for now.
- **`HoursCalculationService`** — 276 KB, unread by this survey (`TLW-WORK-RULES.md` §10). Rounding,
  break deduction, night hours and counter allocation are all Phase 2.
- **Pay categories / counters, per-shift breakdown, flexi balances, weekly & periodic models,
  split/multi-shift, tariffs.** Legacy's own default column set excludes all of them.
- **Exceptions beyond *displaying* what exists.** Generating the 43 types is Phase 2. P4 renders the
  exception column and the mute flag only.
- **The safe rules expression language** replacing `CustomSql`.
- **Plan 002's own portions.** 010 P1 delivers the five-column subset **002 P3 consumes**; it does
  not build the Clocking aggregate, the calendar job, or badge-slot provenance.

---

## Portions

Ordered so the build stays green and the app runnable. **P1 is deliverable before 002 is approved**
and is the only portion 002 is waiting on.

### [ ] P1 — `DayTemplate`: the five columns allocation reads
**Touches:** `src/Modules/TimeAttendance/…/Domain/DayTemplate.cs`,
`…/Domain/ShiftMatchingRule.cs`, `…/Domain/MasterTemplateAssignment.cs`, one migration,
`…/Contracts/IDayTemplateDirectory.cs`, new `src/Modules/TimeAttendance/WM.Modules.TimeAttendance.Tests/`
wired into `WM.sln`
**Done when:** a `DayTemplate` persists `NightShiftEndTime`, `OffsetTransactionToNextDay` (the
renamed `ShiftToSunday`), `OffsetAfterTime` (`NightShiftStartTime`), `AllocateToPreviousDayWindow`
(`InterswipeIntervalToMoveToYesterday`), a `TemplateKind` discriminator and
`OverriddenByMasterTemplate` (`TreatAsMasterDailyModel`); shift-matching rules and dated master
assignments are child rows; a contract resolves the **effective** template for (employee, date)
applying the master override.
**Tests:** A1, A2, A3 (both legacy and inverted, divergence recorded), A6. Effective-template
resolution with and without a master assignment, and with an expired one.
**Risk:** low — additive schema, no behaviour depends on it yet.
**Note:** this is the whole of 002 P3's Work Rules dependency. Nothing else in the 124 columns is
needed.

### [ ] P2 — The allocation function
**Touches:** `…/Services/DayAllocationService.cs`, consumed by `PunchService`
**Done when:** given a punch instant, a zone (008 P4's seam) and the neighbouring days' effective
templates, the five branches resolve an owning date in legacy's order, with the documented fallback.
A punch whose resolved day has no Clocking **creates** it rather than being discarded.
**Tests:** A4, A5, A7, A8, A9, A10. The eleven vault Given/When/Thens if 002 P1/P2 have landed;
otherwise against a test double for the Clocking store, with a `[Fact(Skip)]` placeholder naming
each so 002 inherits the list.
**Risk:** **high** — a swipe on the wrong day is a payroll error. Also the portion with the most
inverted behaviour (A3, A8, A10).
**Depends on:** P1; 008 P4 for the local-date seam.

### [ ] P3 — Daily Browser read model + endpoint
**Touches:** `…/Queries/DailyBrowserRow.cs`, `…/Services/DailyBrowserQuery.cs`,
`TimeAttendanceModule.cs` endpoint registration
**Done when:** `GET /api/timeattendance/daily-browser` returns one row per employee per day for a
range, carrying the 14 default fields, paged, behind an authorization policy and plan 001's scope
filters. A day with no punches, no template, or a dangling template id all return a row.
**Tests:** the four "Rendering" absent-data cases; scope filtering (a user who cannot see an
employee gets no row — fail **closed**); paging.
**Risk:** medium — first read model over the Clocking aggregate.
**Depends on:** 002 P1 (the aggregate) and P2 (the calendar job). **This is the real blocker.**

### [ ] P4 — Punch pairing and the exception/mute columns
**Touches:** `…/Queries/DailyBrowserRow.cs` (swipe pairs), read-only exception projection
**Done when:** punches present as ordered IN/OUT pairs with no 12-slot ceiling; an unpaired trailing
IN renders open; the two-shift/two-punch reinterpretation is an explicit, tested rule rather than a
getter side-effect; the row carries an exception count and the day-level mute flag.
**Tests:** every "Rendering" case; explicitly, 13+ punches render without truncation; the
two-punch reinterpretation with and without a half-day absence.
**Risk:** medium.

### [ ] P5 — The grid
**Touches:** `frontend/portal` — one route, one grid component, column-preference persistence
**Done when:** the Daily Browser renders in the portal against the endpoint, 14 default columns,
user-selectable from the full set, date range and employee/department filter. Verified in the
browser via preview_start, not by asking the user.
**Tests:** `ng test` for the column-preference reducer, including an unknown column id being ignored
(the id-`29` case).
**Risk:** low.

---

## Open questions for the user

1. **Where do the five template columns live?** *Target design* proposes TimeAttendance owns
   `DayTemplate` now, with Rules taking it in Phase 2 behind a contract. The alternative is to open
   `src/Modules/Rules` for five columns. I recommend the former — a module whose entire content is
   the subset another module reads is not a boundary, it is ceremony — but creating and later moving
   an aggregate crosses invariant 1, so it is your call.

2. **A3 — do we fix the master-template date window?** Legacy tests the assignment window against
   *yesterday and tomorrow* (`37.V3.6.1.0.sql:768-769`), so a one-day master assignment never
   applies. I read that as a defect and P1 inverts it. If any customer has built around it, say so
   and P1 mirrors instead.

3. **A10 — a punch whose day was never generated.** Legacy discards it
   (`73.V5.19.0.0.sql:233-256`). P2 inverts this: create the day, raise an exception. That means WM
   has no "reject the punch" path at all, which is what I think is right for a phone-first product,
   but it changes the calendar job from load-bearing to an optimisation. Confirm.

4. **Read-only first?** This plan ships a Daily Browser you cannot edit, because editing means the
   calculation engine. A read-only attendance screen is genuinely useful (it is what a manager opens
   each morning) but it is not what `SCREEN-TREE.md:83` calls "a usable attendance product". Is a
   read-only P3–P5 worth shipping ahead of Phase 2, or should 010 stop at P2 and wait?

5. **Proposed `ARCHITECTURE.md` change — not applied (PR #62 is open on that file).**
   `:779` currently reads:

   > `| 5 | **TimeAttendance** | punches, clockings, pauses, corrections, manual timesheets, daily browser, geolocation, QR punch, period locking | ◐ core built |`

   Measured, the module is 9 files — `Punch.cs` (10 properties), `PunchService.cs`, a seeder, one
   migration, one contract — and has **no test project**. Proposed:

   > `| 5 | **TimeAttendance** | punches, clockings, pauses, corrections, manual timesheets, daily browser, geolocation, QR punch, period locking | ◐ **punch capture only** — clockings, pauses, corrections, timesheets, daily browser and period locking are all ▢. Plans 002, 010 |`

6. **Phase 2 estimate.** `ARCHITECTURE.md:549` puts the Rules engine at 10–16 weeks. That estimate
   is against a ~700 KB calculation engine this survey deliberately did **not** read
   (`TLW-WORK-RULES.md` §10). I am not proposing a number — I am flagging that nobody has opened
   `HoursCalculationService.cs` (276 KB) and the estimate predates the measurement. Worth its own
   survey before Phase 2 is scheduled.
