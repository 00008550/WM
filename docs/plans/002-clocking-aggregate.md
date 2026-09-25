# 002 — The Clocking daily aggregate

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 2 (Rules engine) — this is its missing prerequisite
Reference: [`TLW-CLOCKING-MODEL.md`](../TLW-CLOCKING-MODEL.md) — full measured anatomy
Refreshed: 2026-09-25 against `master` `d76c54a`, after 007 P1–P4, 008 (all), 010 P1–P2, 011 P9, 022 P1–P2
Legacy sources surveyed (every one opened for this refresh):
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — **579 `TableAttribute` mappings over 578
  distinct tables** (`dbo.PredefinedAbsences` is mapped twice), **8,173 columns**. `dbo.Clockings`
  **249** (`:20151`), `dbo.ClockingsLog` **97** (`:49104`), `dbo.DailyModels` 124 (`:11056`),
  `dbo.ScoresAbnormalities` 13 (`:19596`), `dbo.ScoresAbnormalitiesAuthorized` 8 (`:86218`),
  `dbo.ScoresAbnormalitiesSetup` 7 (`:19334`), `dbo.GenerateClockingsQueue` 5 (`:206219`),
  `dbo.Counters` 7 (`:53275`). Sized from **base tables only** — `ClockingApiView` (94),
  `ClockingReportView` (88) and the two 58-column exception views are report views and are not
  counted as Clocking scope (`COVERAGE-AUDIT.md`'s report-view inflation caveat).
- `Logic\Personnel\PersonnelService.cs:440-1098` — calendar generation (`CreateClockingsForEmployee`,
  `GenerateClockingsForPeriod`, `CreateClockingForEmployee`, `GetDailyModel`, `GetDefaultDailyModel`)
- `HorioService\ServiceTasks.cs:195-244` — the nightly "adding days" task
- `Logic\GenerateClockings\GenerateClockingsQueueService.cs`, `GenerateClockingsRequest.cs`;
  `HorioCalculationService\Jobs\CalculationQueueProcessorJob.cs:215-257`
- `SharedLogic\Enums\BadgeTimeGeneratedBy.cs:3-11` — the per-slot provenance enum
- `Core\Enumeration\Enums.cs:80-84`, `:1397-1434` (see correction C3)
- `Logic\HoursCalculation\ProxyClocking.cs` (224 properties), `Logic\CountersAllocation\BaseCountersHolder.cs:50-174`
- `Database\Versioning\37.V3.6.1.0.sql:748-868` — the **last** definition of
  `dbo.ProcessQueryGetClockingForSwipe` (an `ALTER`; earlier `CREATE`s at `30.V3.0.0.ProcessQuery module.sql:429`,
  `32.V3.1.5.0.sql:370`, `33.V3.3.0.0.sql:1082`); `28.V2.2.0.sql:6409-6411` (unique `IX_Clockings_EmployeeDate`);
  `27.V2.1.12.sql:390-501` (`dbo.Counters`, 20 seeded rows)
- Vault: `Swipe to clocking allocation.md:40-118`, `Troubleshooting\No Calendar (Clockings) for employee.md`

## What changed in this refresh, and why

| # | Stale claim (old text) | Now | Evidence |
|---|---|---|---|
| C1 | *"The decision this plan is blocked on … needs your sign-off before any portion runs"* (normalise vs mirror) | **Split in two.** Store-vs-derive is **decided** (`ARCHITECTURE.md §0a` decision 2: stored). Row-shape-vs-20-columns is **not** decided by §0a's wording and stays open as Q1 — but it no longer blocks P1–P4, which carry no counters | §0a:30-36 |
| C2 | *"002 is `draft` and blocked on 007 P1"* | 007 P1 merged (#61). `IEmployeeDirectory.ListEmployedOnUnscopedAsync` / `EmployeeSummary.IsEmployedOn` answer "who was employed on day D" | `EmployeeDirectory.cs:38-45`, `:91-100` |
| C3 | Exceptions *"configured per daily model (`Enums.cs:1414-1434`)"* | That range is **`AuditTrailLogsBitColumns`** (enum at `:1397`) — the list of `DailyModels` bit columns the audit **triggers** log. The flags are real `DailyModels` columns, but the citation is an audit enum. The exception catalogue is `Enums.cs:86-155` (67 values, 43 real — plan 010) and severity `Informational | Blocking` is `:80-84` | opened both |
| C4 | Authorised vs unauthorised evidenced by *"two report views (`HorioDB.designer.cs:5921`, `:5929`)"* | Those lines are `DataContext` table accessors for two **58-column report views**. The stores are `dbo.ScoresAbnormalities` (13 cols — raised) and `dbo.ScoresAbnormalitiesAuthorized` (8 cols — `UserId` **nullable**, `DateAuthorized`), both keyed by **`(EmployeeId, BadgeDate)`**, not by `ClockingId` | `:19596`, `:86218` |
| C5 | P3 *"Swipe→day allocation — the legacy branches reproduced in order"* | **Built by 010 P2** (#98, `9301d4f`): `DayAllocationService` behind `IOwningDayResolver`, over the port `IClockingDays`. What remains for 002 is the **real store** behind that port and the **eleven vault examples** against it. P3 is redefined accordingly | `DayAllocationService.cs:52-133`, `ClockingDays.cs:20-58` |
| C6 | P4 *"each punch retains raw device time, adjusted time, provenance … Rounding stays auditable"* scoped to "the punch model" as one low-risk slice | Re-cut: provenance of **punches** (P6) and of **stored calculated values** (P7) are separate portions; both are human-editable per §0a decision 1, so both need the audit journal (P4) first | §0a decisions 1-4 |
| C7 | *"Reassignment works when an adjacent day's template changes"* (old P3) | **Inverted by the user's Q4 = freeze (008, 2026-09-24).** A template edit moves nothing; `Punch.LocalDate` is frozen at record time. Moving a punch is an explicit, **audited recalculate** — owned here as P8 | `Punch.cs:262-275`, `008-a-day-has-a-place.md:474-485` |
| C8 | Open Q3 (finish 001 first?) and Q4 (`SoftwareMainOptions` as plan 003) | Obsolete. 001 P3 is superseded by 005 P4; `SoftwareMainOptions` became plan **012**. Removed | STATE.md |
| C9 | P2 *"Touches: `src/Worker`"* | `src/Worker` references **no module** (`WM.Worker.csproj:10-11`: PluginSdk and Licensing only). A job that writes `time_attendance` tables from the Worker would need a cross-module DB path (invariant 1). Generation is a TimeAttendance service; its trigger is Q3 | opened csproj |
| C10 | Legacy schema *"579 tables"* | 579 mappings, **578 distinct tables** (one duplicate). Column total 8,173 holds | measured |

Corrections made to WM's other records in the same change: `TLW-CLOCKING-MODEL.md` §2.3a (wrong
"only definition" of the allocation function; stored-vs-derived now superseded by §0a),
`TLW-ADMIN-AUDIT-MODEL.md` §2 (said `calc_*` would be derived), `ARCHITECTURE.md` §13 rows for
Clockings, calendar generation and swipe→day allocation, and an inbound note on `013` P5 (its
"the result is a projection" predates §0a decision 2).

## Ground truth

### Legacy (measured)

- **`dbo.Clockings`, 249 columns**, unique on `(EmployeeId, Date)` (`28.V2.2.0.sql:6411`, which
  drops the earlier non-unique index of the same name at `:6409`). `Date` is `DateTime`; every badge
  slot is **`Time NOT NULL`** — an empty slot is not `NULL` but a sentinel time plus
  `BadgeTimeNGeneratedBy = NoBadge` (`SwipesFixer6PairsOfSwipes.cs:30-74` tests that flag, slot by slot).
- **Per-slot provenance** (`BadgeTimeGeneratedBy.cs:5-10`): `Auto = 0` (swiped), `Preset` (source
  comment: *"very stupid idea, only for RFU client"*), `Manually`, `NoBadge`, `ManualTimesheet`,
  `Default`. Seven columns per slot × 12 (`TLW-CLOCKING-MODEL.md` §3a).
- **Calendar generation is two paths.**
  1. *On hire / import* — `CreateClockingsForEmployee` (`PersonnelService.cs:440-508`): the current
     week synchronously, the rest enqueued into `dbo.GenerateClockingsQueue` as a JSON
     `{employeeIds, fromDate, toDate}`; `CalculationQueueProcessorJob.cs:215-257` drains it and
     calls `GenerateClockingsForPeriod`, which **skips dates that already exist** (`:534-546`) and
     inserts in batches of 200 (`:550-555`).
  2. *Nightly* — `ServiceTasks.cs:195-244`: for every not-fired `Regular` employee, fill every
     missing date from `max(today, EnterDate)` to `today + Calculations.NumberOfDaysInAdvancedCalendar`.
- **The day's template comes from the weekly rota**, not from the day: `GetDailyModel`
  (`:1004-1089`) walks `Link_Employee_WeeklyShifts` as a cycle anchored on the week of `EnterDate`,
  and **falls back to the system daily model** (`Code == SYSTEM_DAILY_MODEL_CODE`, `:1091-1098`)
  when the employee has no rota — or when the date is before `EnterDate` (`:1010-1026`). The template's
  ten window columns are **snapshotted** onto the row (`:988-1002`) — the one piece of correct
  denormalisation (plan 010).
- **Exceptions** are separate rows keyed by `(EmployeeId, BadgeDate)`, with authorisation in a
  second table; muting is a **day** flag, `Clockings.ShouldHideExceptions`.
- **Audit** of a Clocking is `dbo.ClockingsLog` (97), a whole-row snapshot written by SQL triggers
  (`Clocking_Update_Trigger`, `30.V3.0.0.sql:608`) — `TLW-ADMIN-AUDIT-MODEL.md` §2.

### WM today (`master` `d76c54a`)

| Piece | Where | State |
|---|---|---|
| `Punch` with frozen `LocalDate` / `LocalZone`, `ReceivedAt`, `ClientUtcOffsetMinutes`, `Flags` | `Domain/Punch.cs:249-289` | built (007, 008) |
| `IClock`, `ZoneId`, `InstallationZone` | `SharedKernel/Time/` | built (008 P1) |
| `ISiteTimeZones` — home site → nearest ancestor with a zone → install default | `People/Contracts/SiteTimeZones.cs:17-45` | built (008 P2) |
| `DayTemplate` (allocation subset), `ShiftMatchingRule`, `MasterTemplateAssignment`, `IDayTemplateDirectory.ResolveAsync(employeeId, date, dayTemplateId)` | `Domain/`, `Contracts/IDayTemplateDirectory.cs:131-154` | built (010 P1) — **no endpoint writes a template** |
| `DayAllocationService : IOwningDayResolver` | `Services/DayAllocationService.cs:52` | built (010 P2) |
| `IClockingDays` port; interim `PunchBackedClockingDays` — days implicit in punches, **no template, `EnsureAsync` returns `false`** | `Services/ClockingDays.cs:20-58` | interim — **002 replaces it** |
| `PunchService.RecordAsync` calls `EnsureAsync` **before** the punch is added, and ignores its result | `Services/PunchService.cs:115` | 002 wires the result |
| Eleven `[Fact(Skip)]` vault placeholders | `Tests/Services/DayAllocationTests.cs:590-606` | 002 P3 un-skips |
| Dangling template id skipped silently (`EffectiveAsync` returns `null`) | `DayAllocationService.cs:128-132` | 002 P5 records it; 010 P3 renders the marker |
| Clocking entity, calendar job, exceptions, counters, day measures, audit | — | **nothing** |
| Audit sink | — | **nothing**; plan 018 P1 not landed and waits on 011 P7 (outbox) |
| TimeAttendance tests | `WM.Modules.TimeAttendance.Tests` on **EF InMemory** (`.csproj:15`) | InMemory does not enforce unique indexes — see P1 Tests |
| Worker | `src/Worker/WM.Worker.csproj:10-11` | references no module |

## Legacy behaviour (what we are replacing)

1. **A day exists before anything happens on it**, and allocation depends on that: a swipe whose
   resolved day has no row is discarded (`73.V5.19.0.0.sql:233-256`). WM already inverted this in
   010 P2 (A10: create the day, never lose the punch); 002 turns the "created" fact into an exception.
2. **Generation is idempotent by skip-if-exists**, backed by a unique index. Concurrency between the
   queue job and the nightly task is survived only because the index rejects the second insert and
   the per-employee `try` swallows it (`ServiceTasks.cs:222`).
3. **Generation is in server-local time** (`DateTime.Now` at `PersonnelService.cs:443`, `:446`,
   `ServiceTasks.cs:212`, `:226`) — one "today" for every site (the 008 Invert, recorded there for 002).
4. **Hire in the current year back-fills from 1 January** (`PersonnelService.cs:463-469`, ticket
   "4251"): an employee hired on 20 September with `NewEmployeeSwipesStartDate = 2017-01-01` gets
   Clockings from 1 January — days before employment, carrying the rota's template.
5. **The nightly task ignores a future `DischargeDate`** — it filters `NotFired()` and caps nothing
   (`ServiceTasks.cs:212-244`), while the hire path does cap (`PersonnelService.cs:455-459`).
6. **No rota → the system template** (`:1029-1032`, `:1097`): a day always has *some* template, so
   "no template" is indistinguishable from "the default one".
7. **Stored results are overwritten by recalculation**, and a manual edit survives only because
   `IsDTManualChanged` tells the engine to leave it (`TLW-CLOCKING-MODEL.md` §3e); the pre-edit value
   is recoverable only by diffing `ClockingsLog` snapshots.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| One aggregate per employee per working day, unique `(employee, date)` | **Keep** | domain truth; the unique key is also what makes generation idempotent |
| Days pre-generated ahead of time (a day with no attendance still exists) | **Keep** | absence (015 P8), exceptions ("no swipes") and the Daily Browser all read days that have no punches |
| Skip-if-exists generation | **Improve** | keep the rule, but make it a single `INSERT … ON CONFLICT DO NOTHING`-shaped operation so two concurrent runs cannot fail, rather than relying on a swallowed exception |
| Generation on server-local `DateTime.Now` | **Invert** | "today" is per employee, in the home-site zone (`ISiteTimeZones`, 008 Q2 = home site) |
| Back-fill from 1 January (ticket 4251) | **Invert** | never generate a day outside `EmployedFrom..EmployedUntil`; it hands the allocation's branches 1–3 a pre-employment neighbour |
| Nightly path ignores a future leave date | **Invert** | cap at `EmployedUntil` in every path |
| Fallback to the system template when there is no rota | **Invert** | a fail-open default. A day with no planned template has `DayTemplateId = null`; allocation already treats that as "offers nothing" (`ClockingDays.cs:11`), which is what the fallback did in practice only by accident |
| Template window snapshot on the row | **Keep, deferred** | correct denormalisation, but WM's `DayTemplate` has only the five allocation fields; the ten window fields arrive with the calculator (013), which owns the snapshot |
| `BadgeTime1..12` × 7 columns | **Improve** | punches stay rows (already true); per-value provenance becomes columns **on the punch**, no 12 ceiling |
| `BadgeTimeGeneratedBy.NoBadge` | **Drop** | a sentinel for an empty `Time NOT NULL` slot; an absent row says it |
| `BadgeTimeGeneratedBy.Preset` | **Drop** | one-customer hack, per the source's own comment |
| `DeviceBadgeTimeN` beside `BadgeTimeNAdjusted` | **Keep** (§0a decision 3) | `Punch.Timestamp` is the raw instant and is **never mutated**; the adjusted instant is a second nullable column |
| Manual edit overwrites the slot in place | **Invert** | a correction is a new punch that **supersedes** the original, which stays readable — the raw evidence is never lost |
| `CPTN01..20` stored on the row | **Keep stored** (§0a decision 2); **shape is Q1** | recommendation: stored **rows** keyed by counter, no ceiling |
| Per-shift ×6 measures | **Improve, deferred** | shifts as rows, owned by 013 / 016 — 002 builds no shift |
| `IsDTManualChanged` as a whole-day "don't touch" flag | **Improve** | provenance per **value**: a manual value keeps the calculated value beside it and names who/when/why |
| Exceptions keyed by `(EmployeeId, BadgeDate)` | **Improve** | keyed by the Clocking; authorisation is state on the exception, not a copy in a second table |
| Authorisation with a nullable `UserId` | **Invert** | an authorisation without an actor is not an authorisation |
| `ClockingsLog` full-row trigger snapshot | **Improve** | 018's uniform sink; until 018 lands, an in-transaction journal shaped like 018's `AuditEvent` (P4) |
| `GenerateClockingsQueue` as a SQL table polled by a job | **Improve** | RabbitMQ is WM's command/job transport (invariant 4); see Q3 |
| Allocation function | **Done** | 010 P2 |

## Edge cases

### The eleven vault examples — verbatim (`Swipe to clocking allocation.md:42-118`)

These run in 010 P2 against `FakeClockingDays` (`DayAllocationTests.cs:343-357`); **P3 runs them
against the real store**, replacing the placeholders at `:590-606`.

| # | Given | When | Then |
|---|---|---|---|
| V01 | Swipe on 2nd March 2023 at 02:00 | Daily Template for 1st March 2023 has Night Shift End Time 04:00 | allocated to clocking for 1st March 2023 |
| V02 | Swipe on 2nd March 2023 at 04:30 | Daily Template for 1st March 2023 has Night Shift End Time 04:00 | allocated to clocking for 2nd March 2023 |
| V03 | Swipe on 2nd March 2023 at 04:30 | Daily Template for 1st March 2023 has Night Shift End Time as empty | allocated based on the checks below |
| V04 | Swipe on 2nd March 2023 at 02:00 | Daily Template for 3rd March 2023 has Offset Transaction to Next Day switched on with time 01:00 | allocated to clocking for 3rd March 2023 |
| V05 | Swipe on 2nd March 2023 at 04:30 | Daily Template for 3rd March 2023 has Offset Transaction to Next Day switched on with time 05:00 | allocated to clocking for 2nd March 2023 |
| V06 | Swipe on 2nd March 2023 at 04:30 | Daily Template for **1st** March 2023 has Offset Transaction to Next Day switched off or time is empty | allocated based on the checks below |
| V07 | Swipe on 2nd March 2023 at 02:00 | No swipes on 2nd; ≥1 swipe on 1st; first swipe on 1st at 18:00; 1st's template has Allocate Transactions to Previous/Next Day 09:00 | allocated to clocking for 1st March 2023 |
| V08 | Swipe on 2nd March 2023 at 04:00 | same as V07 | allocated to clocking for 2nd March 2023 |
| V09 | Swipe on 2nd March 2023 at 03:00 | ≥1 swipe on 2nd; ≥1 swipe on 1st; first on 1st at 18:00; window 09:00 | allocated to clocking for 2nd March 2023 |
| V10 | Swipe on 2nd March 2023 at 02:00 | No swipes on 2nd; no swipes on 1st; window 09:00 | allocated to clocking for 2nd March 2023 |
| V11 | Swipe on 2nd March 2023 at 03:00 | No swipes on 2nd; ≥1 swipe on 1st at 18:00; window empty | allocated to clocking for 2nd March 2023 |

V06 names the **1st** March template, but the offset rule reads **tomorrow's** (the 3rd). A vault
typo; the test sets the switch off on the 3rd, as 010 P2's `V06` already does (`:348-352`).

### Generation

| # | Case | Required behaviour |
|---|---|---|
| G1 | The job runs twice, or two instances run at once | no duplicate day, no failure — the unique key absorbs the second insert |
| G2 | Employee hired on 20 Sep, job run on 25 Sep | days exist from 20 Sep, never earlier (inverts legacy 4). Days before the run are back-filled, because a punch may already have created some (G6) |
| G3 | Leave date 30 Sep, horizon reaches 10 Oct | nothing after 30 Sep (inverts legacy 5) |
| G4 | Rehire (a new employment window after a gap) | days in the gap are not generated. **Depends on how 007 models rehire** — today `EmployeeSummary` carries one window, so a gap is not representable; record, do not invent |
| G5 | Horizon at 23:30 UTC for a Tashkent (+05) and a Honolulu (−10) employee | "today" differs per employee; each horizon is computed in the employee's home-site zone, never UTC |
| G6 | A punch arrives for a day the job has not generated | the day is created by `EnsureAsync`, a `DayNotGenerated` exception is raised on it (P5), and a later job run leaves that day alone (G1) |
| G7 | No rota exists (017 P2 not landed) | days are generated with `DayTemplateId = null` — not a system default (Invert) |
| G8 | A rota exists and later changes | **already-generated days keep their template.** Re-planning a day is a human act (P4's endpoint, or 017's own), audited. The legacy nightly task also never rewrites an existing day (`ServiceTasks.cs:238`) |
| G9 | Suspended employee | days are still generated — suspension is not "not employed" (`EmployeeDirectory.cs:30-35`); whether they may punch is the punch boundary's call |

### The store behind allocation

| # | Case | Required behaviour |
|---|---|---|
| S1 | Two punches for the same ungenerated day arrive concurrently | exactly one Clocking; both punches kept; at most one `DayNotGenerated` exception |
| S2 | `EnsureAsync` creates the day but the punch save then fails | no orphan day **and** no orphan exception: day, exception and punch commit in one transaction |
| S3 | The day's template id points at a deleted template | allocation offers nothing (legacy inner join, kept) **and** a `DanglingTemplate` exception is recorded on that day — the silent skip at `DayAllocationService.cs:128-132` stops being silent |
| S4 | A punch is allocated to yesterday, and yesterday is the day before `EmployedFrom` | cannot happen once G2 holds: no day before employment exists, so no branch can see a template there. Test it, because it is the invariant that makes the employment check after allocation (`PunchService.cs:89-91`) safe |
| S5 | The interim `PunchBackedClockingDays` answered `FindAsync` with `null` for a day with no punches | the real store answers the **generated** day with zero punches. Branch 3's "no punches today" must still hold for a generated-but-empty day (V07, V10) |

### Provenance, stored values and audit

| # | Case | Required behaviour |
|---|---|---|
| P1 | A manager corrects a swiped punch from 08:07 to 08:00 | the swiped punch keeps 08:07 and is marked superseded; a new `Manual` punch holds 08:00 and names the actor, time and reason; one audit record |
| P2 | The correction is reverted | the manual punch is voided and the original is live again; nothing was ever deleted |
| P3 | Calculation writes counter 3 = 7.5 h; a manager overrides to 8.0 h; calculation runs again | the stored value stays **8.0 (manual)**, the recalculated **7.5** is kept beside it, and the day says it is overridden. Inverts the whole-day `IsDTManualChanged` |
| P4 | A 21st counter | stored like the first (if Q1 = rows) — no schema change |
| P5 | Counter written for a counter id no definition knows | refused, not silently stored (fail closed) |
| P6 | An edit on a day outside the caller's data scope | 404 (not 403 — no existence oracle), nothing written, nothing audited |
| P7 | A no-op edit (value unchanged) | nothing written, nothing audited |
| P8 | The audit write fails | the business write rolls back with it — one transaction |

### Recalculate (the audited move — 008 Q4)

| # | Case | Required behaviour |
|---|---|---|
| R1 | Yesterday's template gained `NightShiftEndTime = 04:00` after a 02:00 punch was frozen on today | nothing moves on its own (freeze). A recalculate over that range moves it to yesterday and audits the move with before/after dates |
| R2 | Recalculate run twice | the second run moves nothing and writes no audit |
| R3 | The move lands on a day that does not exist | the day is created (G6's path), audited as part of the same operation |
| R4 | Ordering inside one day | punches are re-resolved in instant order, so a branch-3 decision sees the same "first punch of yesterday" it would have seen live. Assert against a range where order matters |
| R5 | A punch moved onto a day outside the employment window | refused for that punch, reported, nothing else in the run is rolled back |

## Target design in WM

**Module:** TimeAttendance owns the Clocking (`ARCHITECTURE.md §8`, TimeAttendance "clocking
pairing, corrections, exception detection"). The Rules module does not exist; stored calculated
values are written through a TimeAttendance **contract** so 013 can write them from wherever the
calculator ends up living.

```
time_attendance.clockings            (id, employee_id, date, day_template_id NULL, origin,
                                      exceptions_muted, created_at, version)   UNIQUE (employee_id, date)
time_attendance.day_exceptions       (id, clocking_id, kind, severity, raised_at, detail,
                                      authorised_by NULL, authorised_at NULL, reason NULL)
time_attendance.clocking_values      (clocking_id, key, calculated NULL, manual NULL,
                                      manual_by, manual_at, manual_reason, calculated_at)  -- Q1
time_attendance.clocking_journal     (id, clocking_id, employee_id, at, actor, operation,
                                      changes jsonb, reason)                    -- P4, until 018
punches  + origin, adjusted_timestamp NULL, superseded_by NULL, voided_at NULL, voided_by NULL
```

- **Punches are not foreign-keyed to the Clocking.** They join by `(EmployeeId, LocalDate)` — the
  frozen day is already on the punch, so the recalculate in P8 is an update of `LocalDate`, not a
  re-parenting. The Clocking is the day; the punch says which day it is on.
- **Seams:** `IClockingDays` (existing, 010 P2) gets the real body. New:
  `IPlannedDayTemplates` (what template a generated day starts with — interim answers `null`;
  017 P2's weekly rota implements it), `IClockingResults` (the write contract for stored values,
  consumed by 013), `IClockingJournal` (P4 — 018 replaces the body).
- **Events:** `ClockingGenerated` is **not** published (a nightly horizon would flood the stream);
  `ClockingChanged` on every human write and every recalculate, on `wm.clockings`
  (`ARCHITECTURE.md §7`), fire-and-forget like `PunchRecorded` (011 P7 owns the outbox).
- **Endpoints** (all behind `timesheets.edit` **and** data scope, per §0a decision 1; 004 later
  replaces the permission with screen read+edit rights without changing the routes):
  `PUT /api/timeattendance/clockings/{employeeId}/{date}/template`,
  `POST …/{employeeId}/{date}/punches` (manual), `POST /api/timeattendance/punches/{id}/supersede`,
  `POST /api/timeattendance/punches/{id}/void`, `PUT …/{employeeId}/{date}/values/{key}`,
  `POST /api/timeattendance/exceptions/{id}/authorise`, `PUT …/{employeeId}/{date}/mute`,
  `POST /api/timeattendance/recalculate` (range, employees). Read endpoints are 010 P3's.
- **Audit (018 not landed).** Every **human** write and every recalculate move appends one
  `clocking_journal` row **in the same transaction** as the change, shaped as 018 P1's
  `AuditEvent` (entity, entity id, actor, at, operation, field changes, reason) so 018 can drain it
  into the uniform sink or take it over. **System writes are not audited but carry provenance:**
  calendar generation (`origin = Calendar`), `EnsureAsync` (`origin = Punch`, plus the
  `DayNotGenerated` exception), and calculator writes (`calculated_at` on the value). This mirrors
  what the audit is for — who changed what — without logging a nightly horizon of inserts, which
  is the volume that made `ClockingsLog` a 97-column table.

## Out of scope for this plan

- **The calculator** (hours, rounding, breaks, day/night, the 43 exception types) — plan 013.
  002 gives it a place to write and nothing to compute.
- **The template window snapshot** (`Enter1Start…Exit2End`, `DurationTheoretic`, `BreakDurationMin`)
  — 013, which adds those fields to `DayTemplate`.
- **Weekly rota / cycles** — plan 017 P2 implements `IPlannedDayTemplates`.
- **Half-day absence on the day** (`MorningAbsenceID` / `AfternoonAbsenceID`) — plan 015 P8, over
  a consumer contract into TimeAttendance. 002 adds no absence columns it cannot write.
- **Shifts as rows, per-shift cost centres** — 013 / 016.
- **The flat legacy projection** (`BadgeTime1..12`, `CPTN01..20` with day-carry) — moved to plan
  **014**, whose generic export builder is its only consumer. Recorded there as a prerequisite.
- **Daily Browser read model and grid** — 010 P3–P5.
- **Screen read+edit rights** — 004. 002 gates on `timesheets.edit` + scope until then.
- **The uniform audit sink** — 018. **Contract/employee thresholds** — 012 / 013.
- **Import of legacy Clockings** — no plan yet; A3 (master window) and the frozen-day rule
  (`010:354-356`) are the known traps.

## Portions

Order is by dependency; each leaves the build green and changes no production answer until P3.

### [ ] P1 — The Clocking row
**Touches:** `Domain/Clocking.cs`, `Data/TimeAttendanceDbContext.cs`, one migration,
`Data/ClockingBackfill.cs` (+ call beside `PunchLocalDateBackfill` in `src/Api/WM.Api/Program.cs:105`),
tests.
**Done when:** `time_attendance.clockings` exists with unique `(employee_id, date)`, nullable
`day_template_id`, `origin` (`Calendar | Punch | Backfill | Manual`), `exceptions_muted`, `version`;
every existing `(EmployeeId, LocalDate)` pair among punches has a row after startup (origin
`Backfill`). `IClockingDays` is **not** yet rewired.
**Tests:** the backfill is idempotent (run twice → same rows); unique key enforced — **on SQLite**,
because EF InMemory ignores unique indexes (add `Microsoft.EntityFrameworkCore.Sqlite` to the test
project, as `ApiTestHost.cs:278-296` already does); a day with no punches is a valid row.
**Risk:** low — additive.

### [ ] P2 — The calendar
**Touches:** `Services/ClockingCalendar.cs`, `Contracts/IPlannedDayTemplates.cs` (+ interim
`NoPlannedTemplates`), the trigger per Q3, tests.
**Done when:** `GenerateAsync(from, to)` creates every missing day for every employee employed on
it, from `max(EmployedFrom, from)` to `min(EmployedUntil, to)`, with the planned template (interim:
`null`); horizon = each employee's home-site today (`ISiteTimeZones` + `IClock.TodayIn`) +
a configured `Calendar:HorizonDays` (legacy `NumberOfDaysInAdvancedCalendar`); existing days are
never touched.
**Tests:** G1 (two concurrent runs, SQLite), G2, G3, G5, G7, G8, G9; an employee with no site zone
uses the installation default, as `ISiteTimeZones` resolves.
**Risk:** medium — unattended; this is the job that would silently stop, which is the vault's own
troubleshooting page.

### [ ] P3 — The real store behind allocation, and the vault examples
**Touches:** `Services/ClockingDays.cs` (new `ClockingStore : IClockingDays`; delete
`PunchBackedClockingDays`), `TimeAttendanceModule.cs` registration, `Services/PunchService.cs:115`,
`Tests/Services/DayAllocationTests.cs:584-606`.
**Done when:** `FindAsync` reads the Clocking row (its template id and the punches whose frozen
`LocalDate` is that date); `EnsureAsync` adds the row to the **same** `DbContext` unit of work as
the punch and returns `true` when it created one; `PunchService` commits day and punch together.
`VaultAllocationExamplesFor002` runs all eleven against `ClockingStore` over SQLite — no `Skip`.
**Tests:** V01–V11 (verbatim above), S1, S2, S4, S5. A regression: with no template on any day,
every production answer equals 008 P4's local date (what `PunchBackedClockingDays` guaranteed).
**Risk:** **high** — from this portion on, a day's template changes which day a punch is filed on,
which is a payroll answer.

### [ ] P4 — The audit journal, and the first human write: a day's template
**Touches:** `Domain/ClockingJournalEntry.cs`, `Services/ClockingJournal.cs` (`IClockingJournal`),
migration, `PUT …/clockings/{employeeId}/{date}/template` in `TimeAttendanceModule.cs`,
`EndpointAuthorizationInventoryTests`, tests.
**Done when:** setting a day's template writes the row and one journal entry in one transaction,
behind `timesheets.edit` + data scope; the endpoint refuses a template id that does not exist (the
write side fails closed; the read side's dangling id is P5's).
**Tests:** P6, P7, P8; journal carries actor, before/after template id, reason; an out-of-scope
employee → 404 and no journal row; the inventory test names the new route.
**Risk:** medium — the journal's shape is the one 018 must absorb (Q2).

### [ ] P5 — Day exceptions: the store, three raisers, mute and authorise
**Touches:** `Domain/DayException.cs`, migration, `Services/DayAllocationService.cs:128-132`
(report a dangling id instead of dropping it), `Services/PunchService.cs`, two endpoints, tests.
**Done when:** exceptions are rows on the Clocking with `kind`, `severity`
(`Informational | Blocking`), `raised_at`, and authorisation state. Three kinds are raised here:
`DayNotGenerated` (from `EnsureAsync` = `true`), `DanglingTemplate` (S3) and `UnpairedDirection`
(007 D4: a same-direction punch **outside** the dedupe window with no opposite punch between, on the
same day). Mute (day flag) and authorise (per exception, actor **required**) are journalled.
**Tests:** G6, S1 (one exception, not two), S3; D4: `IN 08:00, IN 08:20` raises, `IN 08:00, IN 08:00:10`
does not (dedupe); authorise without an actor is impossible by construction; muting hides nothing
from the API — it is a flag the reader applies.
**Risk:** medium. The other ~40 kinds are 013's; this portion fixes the shape they land in.

### [ ] P6 — Per-value provenance on punches
**Touches:** `Domain/Punch.cs`, migration, `Services/PunchService.cs`, three endpoints (manual add,
supersede, void), `PunchRecorded` consumers if the contract gains `Origin`, tests.
**Done when:** every punch has `Origin` (`Swiped | Manual | ManualTimesheet | Generated`, legacy
`Auto | Manually | ManualTimesheet | Default`; `NoBadge`/`Preset` dropped), a nullable
`AdjustedTimestamp` (written by 013's rounding; never by this plan), and supersession/void fields.
`Timestamp` is never updated by any code path. Reads (timesheet, recent, live) show the effective
set: not voided, not superseded.
**Tests:** P1, P2; a manual punch runs through the same allocation and employment boundary as a
swiped one; each human write journals exactly once; existing punches migrate as `Swiped`.
**Risk:** medium — every punch read changes its filter.

### [ ] P7 — Stored calculated values, with provenance
**Touches:** `Domain/ClockingValue.cs`, migration, `Contracts/IClockingResults.cs`, one endpoint
(manual override / clear override), tests. **Blocked on Q1.**
**Done when:** a calculator (a test double here; 013 in production) writes values per day through
`IClockingResults`; a manual override stores beside the calculated value, never over it;
recalculation updates `calculated` and leaves `manual` alone; clearing an override is journalled.
The value keys are the 20 legacy pay categories plus the `calc_*` day measures named in
`TLW-CLOCKING-MODEL.md` §3d — the set, not the arithmetic.
**Tests:** P3, P4, P5, P7; calculator writes are not journalled (system provenance: `calculated_at`),
overrides are.
**Risk:** medium — this is the shape 013 P5, 015 and 016 write into; get Q1 answered first.

### [ ] P8 — Recalculate: the audited move
**Touches:** `Services/ClockingRecalculation.cs`, `POST /api/timeattendance/recalculate`, tests.
**Done when:** for an employee set (scope-filtered) and a date range, every effective punch is
re-resolved through `IOwningDayResolver` in instant order; a punch whose answer differs gets a new
`LocalDate` and one journal entry (before/after date, branch); days are created as needed;
the operation reports moved/refused counts. Nothing else re-derives a frozen date.
**Tests:** R1–R5; a zone edit alone (008) and a template edit alone (P4) move nothing until this runs.
**Risk:** **high** — it rewrites payroll history on purpose. It is the only portion that does.

## Open questions for the user

1. **Counter storage shape — rows or twenty columns?** §0a decision 2 settles that calculated state is
   **stored**; its wording ("`CPTN01..20` … WM stores them the same way") does not say whether
   *stored* means *twenty columns*. Options: **(a)** rows keyed by counter id — no ceiling, a 21st
   pay category is data; **(b)** twenty nullable columns plus the day measures as columns — a
   one-to-one import, and the ceiling survives. `dbo.Counters` is exactly 20 seeded rows with no
   Create endpoint (`27.V2.1.12.sql:390-501`, `CountersController.cs`), so no customer has overflow
   to migrate; (a) costs nothing at import. **Recommendation: (a).** Blocks P7 only.
2. **Audit before 018.** 018 P1 waits on 011 P7 (outbox) and ADR 0001. Options: **(a)** 002 writes a
   module-local, in-transaction journal shaped as 018's `AuditEvent`, which 018 later drains or takes
   over (P4 as written); **(b)** P4–P8 wait for 018 P1, and 002 ships P1–P3 only; **(c)** build 018 P1
   now as part of 002. **Recommendation: (a)** — the human writes are the reason §0a decision 4 made
   audit load-bearing, and (b) parks the editable half of the plan behind two other plans.
3. **Where does the calendar run?** The Worker references no module, so it cannot write
   `time_attendance` without breaking invariant 1. Options: **(a)** a TimeAttendance
   `BackgroundService` in the API host, nightly plus on hire (safe on several instances because
   generation is idempotent by key); **(b)** a RabbitMQ command the Worker schedules and the API
   consumes — invariant 4's shape, but no command consumer exists in the API today;
   **(c)** give the Worker a reference to the TimeAttendance module. **Recommendation: (a) now, with
   the generation itself behind a service so (b) is a trigger change later.** (c) is not recommended.
4. **Horizon default.** Legacy `NumberOfDaysInAdvancedCalendar` is per install. Planning and
   absence (015) read days in the future, so the horizon is a product setting, not a constant.
   What default? Recommendation: 60 days, installation setting, until plan 012 gives settings a home.
5. **Proposed `ARCHITECTURE.md` change (propose-only, §8).** §8 lists TimeAttendance as ✅ with
   "clocking pairing, corrections, anomaly/exception detection". After this plan it owns the
   Clocking aggregate, its calendar and its exceptions. Diff:
   ```diff
   - 2. **TimeAttendance** ✅ — punch ingestion (Kafka), clocking pairing, **corrections**, …
   + 2. **TimeAttendance** ◐ — punch ingestion (Kafka), **the Clocking daily aggregate and its
   +    calendar (plan 002)**, swipe→day allocation (010 P2), clocking pairing, **corrections**, …
   ```
