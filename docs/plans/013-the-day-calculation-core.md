# 013 — The day calculation core

Status: draft
Roadmap: ARCHITECTURE.md §14 item **2 — Rules engine** (which this survey shows must be split; see
`TLW-CALCULATION-ENGINE.md` §9 for the proposed 2a/2b/2c/2d breakdown)

Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — measured: **579 tables, 8,173 columns**;
  `dbo.Calculations` 45, `dbo.DailyModels` 124, `dbo.Clockings` 249, `dbo.SoftwareMainOptions` 227
- `E:\Tlw\Source\Logic\HoursCalculation\HoursCalculationService.cs` (5,665 lines) — pipeline
  `:163-285`, `:290-517`, `:525-966`; exceptions region `:2573-3829`; counters `:3944-4663`
- `…\HoursCalculationServiceUtils.cs` (1,794) — `ResetClocking` `:1552-1609`, balance `:143-1262`
- `…\DataCache.cs` — `LoadData` `:75-115`, `GetNightTime` `:207-269`
- `…\Adjusters\BaseSwipesFixer.cs`, `GraceAdjuster.cs`
- `…\Calculators\` — `GrossAttendanceCalculator.cs`, `NetAttendanceCalculator.cs`,
  `DifferenceCalculator.cs`, `ActualWorkHoursCalculator.cs`, `DurationTheoreticCalculator.cs`,
  `NightDurationCalculator.cs`, `BaseNightDurationCalculator.cs`,
  `NightDuration6PairsOfSwipesCalculator.cs`
- `…\ClockingCalculationStrategy.cs`, `ClockingCalculationOptions.cs`, `IHoursCalculationService.cs`
- `E:\Tlw\Source\Logic\Scores\ClockingEnums.cs` — `BadgeTimeGeneratedBy`
- `E:\Tlw\Source\Logic\Settings\DailyModelService.cs:319-345` — `IsDTManualChanged` respected
- `E:\Tlw\Source\Logic\Planning\Auto.cs:492,604,775-792` — `IsDTManualChanged` written
- `E:\Tlw\Database\Versioning\45.V4.0.0.0.sql:2438-2537` — `dbo.ExecuteCustomSql` (highest-numbered
  definition; `29`, `33`, `38`, `39` superseded)
- `E:\Tlw\Database\Custom Scripts\Daily Models\` — 85 scripts; `Pommier_Rounding_Script.sql` read
- `E:\Tlw\Documentation\Daily Templates\Daily Templates - Rounding rules.md` (worked examples)
- `E:\Tlw\Documentation\Custom SQL\Daily Template SQL.md`

Full findings: [`../TLW-CALCULATION-ENGINE.md`](../TLW-CALCULATION-ENGINE.md).

**Depends on:** plan **002** (Clocking daily aggregate) and **010 P1** (the five-column daily
template subset), both still `draft`. This plan is not executable until both have landed.

## Ground truth

- The engine is `E:\Tlw\Source\Logic\HoursCalculation\` — 866 KB, 38 root files + 46 in
  `Adjusters`/`Calculators`/`DAL`.
- **22% of the 5,665-line main service is exception detection, not arithmetic** (`:2573-3829`,
  1,256 lines, 22 named `#region` rules).
- **The minimum viable calculation is 8 stages, ~600 lines of C#** — see
  `TLW-CALCULATION-ENGINE.md` §7. That, not 700 KB, is what the first calculation portion costs.
- The engine is data-driven: **101 distinct `DailyModel` members** drive it, out of 124 columns.
  Only **13** `SoftwareMainOptions` columns and **17** `Calculations` columns reach it.
- `dbo.Calculations`, all 45 columns classified, asserted mechanically against the schema (45 = 45,
  exact set match, 0 dupes): **17 LIVE-ENGINE, 6 LIVE-ELSEWHERE, 19 DEAD, 3 HOUSEKEEPING.**
- **The night-band precedence question is settled: there is one live night band.**
  `Calculations`' 14 per-weekday columns, keyed on the clocking's own `DayOfWeek`
  (`DataCache.cs:207-269`). `DailyModels.NightShiftStartTime` and
  `DailyModels.IsCalculateNightHours*` have **zero read sites**; `NightShiftEndTime` is used only
  to detect midnight crossing in exception checks; `GlobalScheduleThresholds` is not a night band
  at all.
- WM currently has **`Punch` and nothing else** in `src/Modules/TimeAttendance/` (9 files, one
  entity, one service). No clocking, no template, no calculation.

**Corrections made to WM's records:**

1. `docs/TLW-INVENTORY.md` §8 Sources — "`Logic/Entities` (247 entities)" → "**579 tables, 8,173
   columns**", with the measuring command. This is the same figure `TLW-SCHEMA-SWEEP.md` was
   created to replace; it survived in this document's own Sources section.
2. `docs/TLW-CALCULATION-ENGINE.md` created — the pipeline map, the 45-column classification, the
   night-band ruling, the editable-path answer, and the estimate correction.

**Corrections proposed but not applied** (`ARCHITECTURE.md` is fenced for this survey; concrete
diffs in `TLW-CALCULATION-ENGINE.md` §13):

3. §14 line 558 — **the 10–16 week Phase 2 estimate is low by ~3×.** Measured item by item it is
   **37–54 weeks**. Split into 2a/2b/2c/2d.
4. §13 line 397 — append that 19 of `Calculations`' 45 columns are dead, `IsManageSQL` foremost.
5. §13 — add a row for the calculation engine itself; replace the coarse line 408.

## Legacy behaviour (what we are replacing)

The 30-stage pipeline is mapped stage by stage with line numbers in
`TLW-CALCULATION-ENGINE.md` §2.4. The parts this plan replaces:

- **`ResetClocking` (`Utils.cs:1552-1609`)** runs first in every calculation and unconditionally
  nulls every `calc_*` column, zeroes all 20 `CPTN`, and zeroes all 12 `*Adjusted` badge times.
  No provenance is consulted.
- **Grace then rounding** (`GraceAdjuster.cs`, and the documented ordering at
  `Daily Templates - Rounding rules.md:32`): grace first, rounding only if the swipe fell outside
  grace.
- **Net = presenceCorrected + corrections + absence − pauses**, then rounded to the template's
  `RoundingPrecisePresence`/`NetPresenceRoundingCutOff`, then capped to `DurationMax` — but
  positive corrections are added back *on top of* the cap (`NetAttendanceCalculator.cs:53-71`).
- **Difference = net − (morning theoretical + afternoon theoretical)**, each falling back to
  `DurationTheoretic / 2` when the template's half-day values are unset —
  `DifferenceCalculator.cs:15-21`, comment: *"it's possible that they forget to put the value"*.
- **Day = net − night.** Night is computed from `*Adjusted` swipe pairs against the global weekday
  band. `NightDurationCalculator.cs:12-24` sums **only pairs 1/2 and 3/4** — see Edge cases.
- **A blocking exception is not "no result"**: stages 9–22 are skipped, `calc_balance` is nulled,
  and if `Calculations.CalculationException == NetPresence0EcartTheoretic` then
  `calc_netAttendance = −DurationTheoretic` (`:792-807`). A bad day becomes a full-day deficit that
  flows into the running balance and then into payroll.

## Keep / Improve / Invert / Drop

Full table in `TLW-CALCULATION-ENGINE.md` §10. The decisions this plan acts on:

| Structure | Class | Reason |
|---|---|---|
| The 8-stage day pipeline and its calculator decomposition | **Keep** | `Calculators/` is the best-factored code in TLW; port shape-for-shape, one class per stage |
| Grace/tolerance with Absolute / Relative / None / Anticipation | **Keep** | clean, documented, worked examples exist |
| `BadgeTimeNGeneratedBy` provenance per swipe | **Keep** | the right answer to "who set this time" |
| Corrections as the additive human-adjustment mechanism | **Keep** | survives recalculation; the correct basis for an editable Daily Browser |
| `BadgeTime1..12` + `…Adjusted` + `…GeneratedBy` (36 slot columns) | **Improve** | punch rows with an ordinal; removes the pairs-3–6 night bug by construction |
| Global weekday night band | **Improve** | keep the weekday-varying idea — it is genuinely used — but make it resolvable per site/template, which TLW cannot do at all |
| Rounding: one before/after cut-off pair | **Improve** | too weak; 85 customers wrote SQL to get time-banded lookup tables. Deferred to a later plan, but the model must not preclude it |
| `ResetClocking` wiping all output unconditionally | **Invert** | in WM calculation output is a projection of immutable punches (`ARCHITECTURE.md:274`) — there is nothing to wipe, and a human override is an input |
| `IsDTManualChanged` written-but-never-read | **Invert** | honour a manual flag or refuse; never silently recompute over it |
| Blocking exception → net = −theoretical | **Invert** | an unprocessable day is *unknown*, not a deficit |
| Engine mutating swipes / deleting pause rows (`:321-352`) | **Invert** | inputs immutable |
| The 19 dead `Calculations` columns | **Drop** | do not migrate settings nothing reads |
| `DailyModels.NightShiftStartTime`, `IsCalculateNightHours*` | **Drop** | zero read sites |
| `GlobalScheduleThresholds` as a night-band source | **Drop** | it is not one |
| Split/multi-shift as separate pipelines; cost centres; activities; counters; custom SQL | **Drop from this plan** | each is its own plan — see Out of scope |

## Edge cases

Lifted verbatim from `E:\Tlw\Documentation\Daily Templates\Daily Templates - Rounding rules.md`.
These become tests directly.

**Ordering (`:32`)** — *"first the grace rules will be applied, then the rounding policy. Rounding
policy will be applied only if swipe was outside of grace rules."*

**Entry grace, Absolute**, tolerance 10, expected IN 08:00 (`:58`, `:62`):
- Given arrival 08:00–08:09, then net hours are calculated as if on time.
- Given arrival 08:10 or later, then net hours use the exact arrival time.

**Entry grace, Relative**, tolerance 10, expected IN 08:00 (`:72`, `:78`):
- Given arrival 08:00–08:10, then as if on time. (Boundary **inclusive** —
  `GraceAdjuster.cs:52-54`, *"for relative - check is inclusive"*.)
- Given arrival 08:11 or later, then net uses *exact arrival − 10 min*.

**Exit grace, Absolute**, tolerance 10, expected OUT 17:00 (`:96`, `:100`):
- Leaves 16:51–17:00 → as if on time. Leaves 16:50 or earlier → exact time.
  (Boundary **exclusive** — `GraceAdjuster.cs:100`, `outSwipeTime > toleratedOutSwipeTime`.
  Entry-Absolute is also exclusive, `:41`. The Absolute/Relative inclusivity asymmetry is real and
  must be tested both ways.)

**Exit grace, Relative** (`:110`, `:116`):
- Leaves 16:50–17:00 → as if on time. Leaves 16:49 or earlier → *exact + 10 min*.

**Rounding, Before-IN, 10-min interval, cut-off 5 (`:146-152`):**
`07:50–07:55 → 07:50` · `07:56–08:00 → 08:00` · `07:40–07:45 → 07:40` · `07:46–07:50 → 07:50` ·
`07:30–07:35 → 07:30` · `07:36–07:40 → 07:40`

**Rounding, After-IN, 15-min interval, cut-off 10 (`:166-172`):**
`08:01–08:10 → 08:00` · `08:11–08:15 → 08:15` · `08:16–08:25 → 08:15` · `08:26–08:30 → 08:30` ·
`08:31–08:40 → 08:30` · `08:41–08:45 → 08:45`

**Rounding, Before-OUT (`:186-192`):**
`16:50–16:55 → 16:50` · `16:56–17:00 → 17:00` · `16:40–16:45 → 16:40` · `16:46–16:50 → 16:50`

**Rounding, After-OUT (`:206-212`):**
`17:01–17:10 → 17:00` · `17:11–17:15 → 17:15` · `17:16–17:25 → 17:15` · `17:26–17:30 → 17:30`

**Undocumented, found in code** (full table in `TLW-CALCULATION-ENGINE.md` §11.1). The ones in
scope here:

| Case | Legacy | WM must |
|---|---|---|
| Shift crosses midnight | `badgeEnd < badgeStart` → +24 h; night window `22:00–06:00` needs a *third* branch adding a further day (`BaseNightDurationCalculator.cs:36-39`, `:53-67`) | handle all three branches; test a 22:00→06:00 shift against a 22:00–06:00 band |
| Work in badge pairs 3–6, non-6-pairs template | **zero night hours**, all booked as day (`NightDurationCalculator.cs:12-24`) | have no ceiling — punch rows, all segments counted |
| Net above `DurationMax` | capped, but positive corrections added on top of the cap (`NetAttendanceCalculator.cs:53-71`) | reproduce, and test the negative-correction case that must *not* cap |
| Capping suppresses its own exception | pre-cap value carried in a local purely to raise "Maximum Duration Exceeded" (`:604-606`, `:739`) | keep pre-cap and post-cap as distinct outputs, not a local |
| Half-day theoretical unset on template | falls back to `DurationTheoretic / 2` | reproduce; test with both halves set, one set, neither set |
| No swipes at all | gross = 0, net = 0 + corrections; blocking exception "No Swipes" (`:2661`) unless template allows | return a result marked *unknown*, never a deficit |
| Employee not employed on the day | legacy **wipes** already-calculated days after `DischargeDate` (`:296-306`) | refuse to calculate and say so; never write |
| Future-dated day | calculated in full unless `SkipNetHoursCalculationForFutureDays` | calculate, but mark the result provisional |
| Template edited retroactively | history changes silently — `DataCache` loads current templates only (`:83`) | the template version used must be recorded on the result |
| Human overrides a calculated value | destroyed on next recalculation (`Utils.cs:1552`) | record as an input adjustment; recalculation must preserve it |

## Target design in WM

Module: `src/Modules/TimeAttendance` (owner of the punch, per `CLAUDE.md` invariant 1). It has
**no test project** — one is created in P1 (`CLAUDE.md`, Commands).

- **`DayCalculation`** — a pure, dependency-free calculator: takes an immutable
  `DayCalculationInput` (ordered punch segments + resolved template + corrections + employment
  window) and returns a `DayCalculationResult`. No DB access, no `Clocking` type, no EF. This is
  the `Calculators/` decomposition kept (§10 Keep) and is what makes replay possible.
- **Result is a projection**, per `ARCHITECTURE.md:274` — recomputable from `wm.punches` at any
  time. Nothing in the result is authoritative input.
- **Contract/event:** `DayCalculated` on Kafka (`CLAUDE.md` invariant 4), carrying employee, date,
  the result, and the template version used.
- **Endpoint:** `POST /api/time-attendance/calculations/preview` (calculate without persisting)
  and `GET /api/time-attendance/days/{employeeId}/{date}`. Both carry an authorization policy
  (`CLAUDE.md` invariant 5) and are data-scoped.
- **Screen:** the Daily Browser from plan 010 gains calculated columns; the editable path is
  P5's adjustment record, not a writable `calc_*`.

## Out of scope for this plan

Each is a separate plan, sized in `TLW-CALCULATION-ENGINE.md` §9:

- The **22 exception rules** (`:2573-3829`) — plan 2b. This plan returns *unknown* where legacy
  would raise a blocking exception, and does not classify why.
- **`CPTN01..20` pay categories** and hour/band counter allocation — plan 2b.
- **Cost-centre counters** (`:4203-4663`) — plan 2b.
- **Split shift / multi-model / 6-pairs variants** — plan 2c.
- **Flexi balances, the running balance chain, week/period capping, `AutoBalanceReset`** — plan 2c.
  This plan computes a day in isolation and emits no balance.
- **Weekly models** — plan 2c.
- **The rules expression language** replacing 85 custom SQL scripts — plan 2d.
- **Absences** — concurrent survey, plan 015. This plan takes an absence duration as an *input*.
- **Payroll plugins** — concurrent survey, plan 014.
- **Activities** (`ClockingActivities`, 53 columns) — unsurveyed, no plan yet.
- Migrating any of the 19 dead `Calculations` columns.

## Portions

### [ ] P1 — The calculator, pure and tested
**Touches:** `src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Calculation/` (new:
`DayCalculationInput.cs`, `DayCalculationResult.cs`, `GrossAttendanceCalculator.cs`,
`DurationTheoreticCalculator.cs`, `DifferenceCalculator.cs`); new test project
`src/Modules/TimeAttendance/WM.Modules.TimeAttendance.Tests/` wired into `WM.sln`.
**Done when:** given an ordered list of in/out segments and a template with theoretical durations,
the calculator returns gross attendance, theoretical duration and difference, with **no ceiling on
segment count** and no database access. `dotnet test WM.sln` runs the new project.
**Tests:** Σ(OUT−IN) over 1, 2, 6 and 9 segments; unpaired trailing IN contributes zero
(`GrossAttendanceCalculator.cs:66-74`); half-day theoretical set / one set / neither set
(`DifferenceCalculator.cs:15-21`); zero segments.
**Risk:** low

### [ ] P2 — Grace, then rounding
**Touches:** `Calculation/GraceAdjuster.cs`, `Calculation/RoundingPolicy.cs`, template fields for
tolerance + rounding, their tests.
**Done when:** an adjusted segment boundary is produced from a raw one by applying grace first and
the rounding policy only if the swipe fell outside grace, per
`Daily Templates - Rounding rules.md:32`.
**Tests:** every Given/When/Then in the Edge cases section above, verbatim — 4 grace scenarios
(Absolute/Relative × entry/exit) including the **inclusive-vs-exclusive boundary asymmetry**
(`GraceAdjuster.cs:41` exclusive vs `:52` inclusive), and all 20 documented rounding transitions.
`None` and `Anticipation` tolerance types adjust nothing.
**Risk:** low

### [ ] P3 — Breaks and net attendance
**Touches:** `Calculation/BreakDeduction.cs`, `Calculation/NetAttendanceCalculator.cs`, template
break definitions, tests.
**Done when:** net attendance = adjusted presence + corrections + absence − breaks, rounded to the
template's precision and cut-off, then capped to the template's maximum — with both the pre-cap and
post-cap values on the result.
**Tests:** cap not applied when unset; cap applied; positive correction added on top of the cap;
negative correction below the cap not capped (all four branches of
`NetAttendanceCalculator.cs:53-71`); rounding precision 0 is identity; break minimum-duration and
maximum-deduction clamps (`HoursCalculationService.cs:1081-1090`).
**Risk:** medium — `ProcessDailyModelBreaks` (`:1023-1343`) is the piece
`TLW-CALCULATION-ENGINE.md` §12 records as **not fully traced**. Budget a re-read of that region
before building; if pause-to-template matching proves deeper than one portion, split it out.

### [ ] P4 — Day and night, with a resolvable band
**Touches:** `Calculation/NightBand.cs`, `Calculation/NightDurationCalculator.cs`, a migration for
the night band (per-weekday start/end, scoped), tests.
**Done when:** night duration is the overlap of *all* work segments with the band for the day's
weekday, day = net − night, and the band is resolvable at more than one scope.
**Tests:** the three midnight branches of `BaseNightDurationCalculator.cs:36-67` — segment inside
the band, segment straddling midnight, and a 22:00–06:00 band against a 03:00–10:00 segment;
**segments 5 and 6 contribute night hours** (the legacy bug at `NightDurationCalculator.cs:12-24`
must not reproduce); weekday selection picks the right pair for each of seven days; band unset →
zero night, all day.
**Risk:** medium — the third branch is the one legacy needed a comment to explain.

### [ ] P5 — The result is a projection, and an override is an input

> **Inbound from plan 002 (refresh 2026-09-25): this title predates `ARCHITECTURE.md §0a`
> decision 2 (2026-09-03), which made calculated state *stored* on the Clocking.** The intent
> survives (recalculation never destroys an override) but the mechanism is 002 P7: the calculator
> writes `calculated` through `IClockingResults`, a human override is stored beside it as `manual`
> with actor, time and reason, and recalculation touches only `calculated`. Re-cut this portion
> against 002 P7a/P7b before approval.
>
> **Updated 2026-09-25 (002 decisions 6–7, user).** 013 no longer adds `calc_*` columns: **002 P7b
> creates all 75** (15 daily + 60 per-shift, legacy names and types, calculated-only) and 013
> **fills** them, with the counters, in one `IClockingResults.WriteCalculatedAsync(employeeId, date,
> ClockingCalculation, ct)` call. Two requirements are **binding on 013** (002 → *Binding
> requirements*): **B1, owned by 013 P1** — a template edit rewrites the window snapshot on
> today/future days only (home-site today), freezes the past, queues nothing; **B2, owned by 013 P5**
> — a per-day edit queues recalculation from that day to today, past days only, triggered by
> `ClockingChanged`.
**Touches:** `Calculation/DayAdjustment.cs` + migration, `Services/DayCalculationService.cs`,
`Contracts/DayCalculated.cs`, endpoint + policy in `TimeAttendanceModule.cs`, tests.
**Done when:** calculating a day never destroys a human adjustment. An adjustment is a stored
record with provenance (who, when, why) that the calculator consumes as input; recalculating from
punches reproduces the same result. A day that cannot be calculated returns *unknown* — never a
negative net attendance.
**Tests:** adjust → recalculate → adjustment still applied and still attributed (the direct
inversion of `Utils.cs:1552-1609`); employee not employed on the date → refused, nothing written
(inverting `:296-306`); no punches → unknown, not a deficit (inverting `:792-807`); the template
version used appears on the result; endpoint denies an out-of-scope employee.
**Risk:** medium — this is the portion that decides the editable Daily Browser's shape.

## Open questions for the user

1. **The 10–16 week Phase 2 estimate is wrong by roughly 3×.** Measured item by item this survey
   gets **37–54 weeks** for what §14 line 558 calls "2 — Rules engine"
   (`TLW-CALCULATION-ENGINE.md` §9). The estimate priced the *arithmetic* — which really is 2–3
   weeks — and labelled it the engine, which is eleven things. May I apply the 2a/2b/2c/2d split to
   `ARCHITECTURE.md` §14? The diff is in `TLW-CALCULATION-ENGINE.md` §13.

2. **Night band scope — Improve or mirror?** TLW has exactly one night band for the whole estate
   (§4), and it varies only by weekday. For a product sold in France, the UK and West Africa that
   looks like a defect rather than a decision, but making it resolvable (site → template →
   employee) is real cost in P4 and in every consumer afterwards. Mirror TLW's single global band
   for now, or build the resolution in P4? **This is a product call, not mine.**

3. **The 19 dead `Calculations` columns — is `IsManageSQL` known to be inert?** Users see a
   "Manage SQL" switch on the settings screen; `dbo.ExecuteCustomSql` never reads it. If any
   customer believes that switch disables custom SQL, they are wrong today. Worth telling them
   before WM ships without it?

4. **What does an editable Daily Browser edit?** P5 assumes the answer is *an adjustment record*,
   which is what legacy's own `ClockingCorrection` mechanism is and what survives recalculation —
   not a writable calculated value, which legacy destroys on the next run
   (`Utils.cs:1552-1609`). Confirm that is the intended UX before P5 is built: it means the grid
   shows "8.0 (+0.5 adjusted)" rather than an editable "8.5".

5. **Rounding lookup tables — in scope for 2d or earlier?** Legacy's single before/after cut-off
   pair was weak enough that customers wrote 85 SQL scripts, at least one of which
   (`Pommier_Rounding_Script.sql`) is a four-table time-banded rounding lookup. P2 builds the
   legacy-equivalent policy. Should the *model* accommodate lookup tables from the start, even if
   the UI comes later?
