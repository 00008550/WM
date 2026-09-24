# 008 — A day has a place: time zones from the punch to the payslip

Status: merged             <!-- draft → approved → in-progress → in-review → merged -->
Approved: by the user 2026-09-24, all 5 portions — chosen so that 010 P2 is unblocked through 008 P4.
Roadmap: ARCHITECTURE.md §14 — cross-cutting; a prerequisite for **002** (the Clocking aggregate)
and a correction to **007 P1** (`PeopleModule.Today()`)
Legacy sources surveyed: full measurement in [`../TLW-TIME-MODEL.md`](../TLW-TIME-MODEL.md) —
`HorioDB.designer.cs` (temporal column census over all 240,262 lines),
`Database\Versioning\30.V3.0.0.ProcessQuery module.sql:429-528` (the swipe→day function),
`79.V5.25.0.0.sql:1416-1427`, `73.V5.19.0.0.sql:180-264`, `Logic\Settings\SoftwareOptionService.cs`,
`Core\Extensions\DateTimeExtensions.cs`, `WebSite\Helpers\ExternalAccessControllerHelper.cs:95-113`,
`HorioPeopleFirstIntegrationService\Helpers\TimeHelper.cs`,
`HorioSageHrIntegrationService\Services\TimeSheetService.cs`,
`TabletKiosk\AvoidDirectTimeUsageAnalyzer\AvoidDirectTimeUsageAnalyzer.cs`, plus five vault documents.
Full list of files opened: `TLW-TIME-MODEL.md` §11. **What was *not* measured: §9 of the same document.**

---

## Ground truth

### The measurement

| Fact | Value | Where |
|---|---|---|
| Legacy time zones, per **site** or **location** | **none.** `dbo.Locations` has 4 columns; `dbo.Buildings` 5; `dbo.ClientSites` 12; `dbo.[User]` 30 — no zone on any | `TLW-TIME-MODEL.md` §1b |
| Legacy time zones, per **installation** | **one** — `SoftwareMainOptions.SystemTimeZone`, `NVarChar(250)`, a **Windows** id | `HorioDB.designer.cs:70819` |
| Legacy time zones, per **device** | `Devices.TimeZoneCode`, `NVarChar(100)` — ingest/presentation only, dropped from WM scope | `:62112` |
| Attendance columns carrying an **offset** | **zero.** `time` 577 · `datetime` 457 · `date` 146 · `datetimeoffset` **6**, all six infrastructure | `TLW-TIME-MODEL.md` §1a |
| Raw clock reads in legacy | `DateTime.Now` **327** in `Logic` + **576** in `WebSite`; `GETDATE()` **401** in `Database` | §2a, §2c |
| Which day a punch belongs to | `dbo.ProcessQueryGetClockingForSwipe(@employeeid, @swipeTime datetime)` — **five** branches, all `time` vs `time`, **no zone** | `30.V3.0.0.ProcessQuery module.sql:429-528` |
| Pay-period boundary | plain `Date`/`DateTime` ranges; joins a clocking by its `Date`. **No local-midnight anchor exists** | §6 |
| DST handling in business logic | **none.** One file in all of `Source` reasons about DST, and it programs a Suprema terminal | `Communication.Suprema\BLL\Terminal.cs:163-166` |
| `Site.TimeZone` in WM | **written by the seeder, read by nothing.** Repo-wide grep over `src/**` and `frontend/**` finds `PeopleSeeder.cs:26-28` and the declaration, nothing else | `Employee.cs:9`, `20260720080022_Initial.cs:65` |

### Diff against WM

| Concern | WM today | File |
|---|---|---|
| Punch instant | `DateTimeOffset` — **already better than legacy**, do not regress | `Punch.Timestamp` |
| Punch → day | `DateOnly.FromDateTime(p.Timestamp.UtcDateTime)` — **UTC midnight** | `PunchService.cs:158` |
| Timesheet window | `new DateTimeOffset(from, TimeOnly.MinValue, TimeSpan.Zero)` — **UTC midnight** | `PunchService.cs:149-150` |
| Timesheet default "today" | `DateOnly.FromDateTime(DateTime.UtcNow)` | `TimeAttendanceModule.cs:60`, `:77` |
| Employment "today" | `DateOnly.FromDateTime(DateTime.UtcNow)` | `PeopleModule.cs:38` (`feat/007-p1`) |
| Site zone | IANA string, `text`, not null, default `"UTC"`, **unvalidated and unsettable** — there is no `POST`/`PUT /api/sites` | `Employee.cs:9`, `PeopleModule.cs:190-192` |
| Clock abstraction | none — **24** raw `DateTime[Offset].Now/UtcNow/Today` reads in non-test `src/**`, across 12 files (28 including tests) | measured 2026-08-14 |

**This is a live defect on the seeded demo, not a future concern.** Every seeded site is
`Europe/Ljubljana` (UTC+1/+2), so a punch after 22:00 or 23:00 local is filed on the *next* day's
timesheet, and the timesheet's default "today" flips one to two hours early.

### Corrections made to WM's records in this pass

1. **`ARCHITECTURE.md` §13** — the `Calculations` row said *"incl. the 8-window day-boundary
   matrix"*. It is **7** windows / 14 columns (`HorioDB.designer.cs:14979-15239`) and they are the
   **night-hours band per weekday pair**, not the day boundary (UI header
   `CalculationIndex_NightHours`, `Views\Calculation\Index.cshtml:88-104`; consumer
   `DataCache.cs:207-269`). The day boundary is `dbo.ProcessQueryGetClockingForSwipe`.
2. **`ARCHITECTURE.md` §13** — the *"Swipe→day allocation"* row named three rules; there are
   **five**, and they live in T-SQL, not in `Logic`. Row now names the function and its file.
3. **`ARCHITECTURE.md` §13** — **four new rows**: time-zone resolution, the stored-time model, the
   offline punch clock, and DST. Per STATE.md's convention, *"a WM-only capability has no row in the
   coverage matrix, so nothing prompts anyone to check it"* — `Site.TimeZone` is exactly that, and
   it went three phases unread. A §13 audit note records the pass.
4. **`TLW-CLOCKING-MODEL.md`** — new §2.3a with the measured function, the fifth (shift-matching)
   branch, the reject-when-no-row behaviour, the column↔UI-label mapping, and the fact that no time
   zone enters; §3a gains the `Time`-typed-slot fact and the day-carry defect that follows from it.
5. **`TLW-SCHEMA-SWEEP.md` §4** — the one-line *"Temporal: `SystemTimeZone`, …"* bullet gains the
   measurement: it is the only geographic zone outside `Devices`, it is a Windows id, it is a
   singleton, and it fails open in seven places.
6. **New reference document** — [`TLW-TIME-MODEL.md`](../TLW-TIME-MODEL.md).

**`STATE.md` is deliberately untouched** — a concurrent audit owns it. **This plan is not in the
queue table yet and must be added there before it can be approved.**

**Not corrected, deliberately:** `TLW-PEOPLE-MODEL.md:95` cites `78.V5.24.0.0.sql` as the latest
`ActiveEmployeesView` and 007 leans on its precedence bug. **There is a later revision that fixes
it** (`79.V5.25.0.0.sql:728-733`). A separate audit of 007's employment claims is in flight and owns
those files. Detail and the confirming line numbers: `TLW-TIME-MODEL.md` §10.

---

## Legacy behaviour (what we are replacing)

Full account in `TLW-TIME-MODEL.md`. The five things that bind this plan:

1. **Legacy never solved this; it avoided it.** One database and one website copy per customer
   (`Documentation\High Level Architecture.md:35,43`), `SoftwareMainOptions` read with `.Single()`.
   A deployment is single-timezone by construction. Within one customer, the only per-place zone is
   `Devices.TimeZoneCode`, and it only decides the wall clock a terminal stamps
   (`Device.cs:101-113`) — nothing downstream can recover it. If a customer spans zones, `Clockings`
   silently mixes wall clocks with **no discriminator column**.

2. **The owning day is a real calculation and it is right about the domain.**

   ```sql
   declare @date date = convert(date, @swipeTime)
   declare @time time = convert(time, @swipeTime)
   ```
   `30.V3.0.0.ProcessQuery module.sql:440-441`

   Then: yesterday if `@time <` yesterday's `NightShiftEndTime`; tomorrow if tomorrow's template has
   `ShiftToSunday = 1` and `@time > NightShiftStartTime`; yesterday if there were no swipes today and
   `yesterday + firstSwipe + InterswipeIntervalToMoveToYesterday > @swipeTime`; yesterday on a
   shift-matching (`ModelType = 7`) template whose matched model has `NightShiftEndTime > @time`;
   otherwise today. **All five compare `time` to `time`.** WM's
   `DateOnly.FromDateTime(...UtcDateTime)` is not a simplification of this — it is a different
   answer.

3. **`Date + BadgeTimeN` is not an instant, and two shipped exports prove it.** People First emits
   `End < Start` for every night shift (`TimeHelper.cs:198-200`); Sage HR throws `Time in > Time out`
   and drops the clocking (`TimeSheetService.cs:148`). Both use the same recipe
   `ToUtc(Clocking.Date + BadgeTimeN, SystemTimeZone)`. **Plan 002's flat projection must carry the
   day-carry explicitly or it inherits this.**

4. **The offline mobile path takes the phone's naked wall clock.**

   ```csharp
   //offline swipe was sent
   if (!model.SwipeDate.IsNullOrEmptyOrWhiteSpace() && !model.SwipeTime.IsNullOrEmptyOrWhiteSpace())
   {
       now = _externalAccessService.ParseDateFromString(model.SwipeDate)
           .AddTicks(TimeSpan.Parse(model.SwipeTime).Ticks);
   }
   return now.TrimSeconds();
   ```
   `WebSite\Helpers\ExternalAccessControllerHelper.cs:106-112`

   No offset transmitted, none stored, nothing validated. An online punch uses the QR point's zone;
   an offline one uses the phone's; the row does not say which. WM's punches are offline-queued
   (invariant 3), so this is the path WM inherits most directly.

5. **Legacy's own newest component bans the pattern.** `AvoidDirectTimeUsageAnalyzer` makes
   `DateTime.Now`, `DateTime.UtcNow` and `DateTime.Today` a **compile error** with the description
   *"Calling DateTime.Now in this project can lead to errors as application time zone can be
   different from system's"* (`AvoidDirectTimeUsageAnalyzer.cs:16-52`, `DiagnosticSeverity.Error`).
   They were right and they were 903 call sites too late. WM is at ~18.

---

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A day of attendance is keyed by a **calendar date** | **Keep** | genuine domain truth; every payroll period, timesheet row and absence day agrees |
| The owning day is **calculated** from neighbouring daily templates | **Keep** | `ProcessQueryGetClockingForSwipe` is right. 008 builds the seam; **002 fills in the template rules** |
| Discharge/employment as a date compared at a **reference date** | **Keep** | already 007 P1's design and it is correct — `IsEmployedOn(date)` needs no change, only a better default `date` |
| One time zone per **installation** | **Improve** | right idea, wrong grain. WM already put the column on `Site`; make it mean something |
| **Windows** time-zone ids | **Improve** | WM's IANA (`"Europe/Ljubljana"`) is correct for Linux containers (§13A) and for a Flutter client. But **validate it** — today a typo is stored and throws at first use |
| Storing attendance as naked local wall clock | **Invert** | WM's `DateTimeOffset` punch already inverts it. What 008 fixes is that WM *throws the offset away* the moment it projects to a day |
| "Today" = **UTC** everywhere | **Invert** | it is neither of the two coherent answers. `PeopleModule.cs:31-38` says so in its own comment |
| Offline punch = the phone's naked wall clock | **Invert** | the offset must be carried, and the server's receipt instant recorded alongside it |
| Fail-open to the server clock (**7 sites**, `TLW-TIME-MODEL.md` §7) | **Invert** | a missing or unparseable zone must fail at composition, never silently substitute a different clock. This is the same failure family as the access model's fail-opens |
| No DST handling (`IsAmbiguousTime`/`IsInvalidTime` appear **nowhere** in the domain) | **Invert** | ambiguous and invalid local times are real; decide them once, in one place, tested |
| Nightly batch on the **service host's** midnight, for every customer DB | **Invert** | `HorioService.cs:28-30`, `ServiceTasks.cs:124-125`. WM's Worker must either run per zone or state that it does not |
| `Devices.TimeZoneCode` | **Drop** | devices are out of scope (invariant 3). The *requirement* it met — "this punching point measures its day in zone X" — moves to `Site` |
| `dbo.ac_timezone` / `LapiTimeZone` (access-control windows) | **Drop** | not geographic zones at all; already correctly recorded at `SCREEN-TREE.md:431-432` |
| A **per-user** display zone | **Drop for now** | legacy has none (`dbo.[User]`, 30 columns). A manager in London reading a Tashkent site's timesheet is a *presentation* question, not a domain one. Open question 3 |

---

## Edge cases

Every one of these must have a test. The first three are lifted from
`Documentation\Swipe to clocking allocation.md` verbatim and are **002's** to satisfy in full — 008
must leave a seam they can be written against, and P4's test list says which of them 008 answers.

**Given/When/Then, lifted from the vault (`Swipe to clocking allocation.md:43-53`):**

> **Given**: Swipe on 2nd March 2023 at 02:00
> **When**: Daily Template for 1st March 2023 has Night Shift End Time 04:00
> **Then**: Swipe will be allocated to clocking for 1st March 2023

> **Given**: Swipe on 2nd March 2023 at 04:30
> **When**: Daily Template for 1st March 2023 has Night Shift End Time 04:00
> **Then**: Swipe will be allocated to clocking for 2nd March 2023

> **Given**: Swipe on 2nd March 2023 at 02:00
> **When**: Daily Template for 3rd March 2023 has Offset Transaction to Next Day switched on with time 01:00
> **Then**: Swipe will be allocated to clocking for 3rd March 2023

*(Eight more in the same file, `:63-118`. All eleven belong to plan 002. **Note the times in them
are wall clock in the installation's zone** — which is exactly the input 008 P4's seam must supply.)*

**Boundaries — 008's own:**

| Case | Required behaviour |
|---|---|
| Punch at 23:30 UTC, site `Asia/Tashkent` (+05) | lands on the **next** local day |
| Punch at 00:30 UTC, site `America/Los_Angeles` (−08) | lands on the **previous** local day |
| Two sites, `Pacific/Auckland` and `Pacific/Honolulu`, one API call | the two employees' "today" differ by a calendar day, and both are right |
| `EmployedUntil` = the employee's **local** today | still employed, matching `IsActiveEmployment`'s inclusive `<` (`76.V5.22.0.0.sql:33-36`) |
| Timesheet default range | ends on the employee's local today, not UTC today |

**DST — legacy handles none of these; WM must decide each:**

| Case | Required behaviour |
|---|---|
| **Fall back**: two punches at local 01:30, one hour apart | two distinct punches, both on the same local day, correctly ordered by their offsets. Neither is lost or merged |
| **Spring forward**: a queued offline punch claiming local 02:30 (a wall clock that never existed) | rejected or normalised — **decided and tested**, never silently shifted |
| A local day that is 23 or 25 hours long | the day's punch window is built from the zone, not from `+24h` |
| Zone rules change between the punch and the read (tzdata update) | the stored `DateTimeOffset` is authoritative; the day projection may move. Recorded as accepted, or pinned. Open question 4 |

**Absent data — the fail-open family:**

| Case | Required behaviour |
|---|---|
| `Site.TimeZone` is `""` or an unknown id | **fail closed at write**, 400. Never fall back to UTC at read (that is legacy fail-open #1) |
| A child site has no zone | inherit from the parent site chain; if none, the installation default; if that is unset, **refuse to compose** — not `DateTime.UtcNow` |
| An employee has a `SiteId` pointing at nothing | the punch path already 404s on the employee; the zone lookup must not turn that into a silent UTC |
| Installation default absent in configuration | the host does not start. Same shape as 006 P2's signing-key guard (`Program.cs`) |

**Ordering and precedence:**

| Case | Required behaviour |
|---|---|
| An employee moves site mid-period | the day projection uses the zone **at the time of the punch's site**, and the plan says which site that is. Open question 2 |
| A punch is replayed from Kafka months later | it re-projects to the **same** local day. The zone must be resolvable historically, or the projection must be stored |
| `Site.TimeZone` is edited retroactively | historical days move. Legacy has the identical hazard with templates (`TLW-CLOCKING-MODEL.md` §2.3) and does not warn. WM must, at minimum, audit the change |

**Ceilings:** none in this area — this is the rare survey that found no `Thing1..ThingN`.

---

## Target design in WM

**Module boundaries (invariant 1).** `Site` lives in People. TimeAttendance must **not** read
`people.Sites`; it asks through a contract, the way it already asks `IEmployeeDirectory`
(`PeopleModule.cs`, consumed at `PunchService.cs:31`).

```
People            owns Site.TimeZone; owns resolution (site → parent chain → installation default)
                  exposes IEmployeeZoneDirectory (or extends IEmployeeDirectory)
SharedKernel      IClock (TimeProvider-backed) + a validated ZoneId primitive
                  — cross-cutting primitive, exactly what §3 says SharedKernel is for
TimeAttendance    consumes the contract; projects punches and timesheets to local days
Api               composition-time guard on the installation default (same shape as 006 P2/P3)
```

**Endpoints:** no new resource. `GET /api/sites` already serialises `TimeZone` (it returns the
entity — `PeopleModule.cs:190-192`); it gains validation on write once a write exists, and the
resolved zone is exposed on the employee projection so a client can render local times.

**Events:** `wm.punches` payload gains nothing — `DateTimeOffset` already carries the instant, which
is why the replay story survives this plan intact (ARCHITECTURE.md §7A). What changes is the
*consumer's* projection.

**Screens:** the timesheet and punch list already render whatever the API returns; P4 makes what
they render correct. No new screen.

---

## Out of scope for this plan

- **Daily templates and the five allocation branches.** 008 P4 builds the seam
  `(instant, zone) → local date` and returns the naive local date. **Plan 002** replaces the body
  with `NightShiftEndTime` / `ShiftToSunday` / `InterswipeIntervalToMoveToYesterday` / shift matching.
  The eleven vault Given/When/Thens are 002's acceptance criteria, not 008's.
- **The Clocking aggregate itself.** 008 does not create it.
- **`SoftwareMainOptions` as a per-install configuration store** (227 columns). 008 needs exactly
  one setting and takes it from `IConfiguration`; the store is the standing "Per-install
  configuration" plan.
- **Per-user display zones.** Open question 3.
- **Absence, accrual and scheduling date semantics.** Not measured (`TLW-TIME-MODEL.md` §9); belongs
  with the Absence survey.
- **Re-projecting or recalculating already-frozen punch dates.** P4 does freeze every existing
  punch once, at startup (`PunchLocalDateBackfill`), in the zone of the punch's stored `SiteId`.
  What stays out of scope is moving a date once it is frozen — that is plan 002's audited
  recalculate, not a side effect of a zone edit or a tzdata update.
- **`src/Worker` batch scheduling per zone.** Named as an Invert in the classification because it is
  a real legacy defect, but WM has no nightly batch yet. It becomes real with 002's clocking
  generation job; recorded there rather than built here.

---

## Portions

### [x] P1 — One clock, and a zone that must be real
**Touches:** `src/SharedKernel/WM.SharedKernel/Time/` (new — `IClock`, `ZoneId`),
`src/SharedKernel/WM.SharedKernel.Tests/Time/`, `src/Api/WM.Api/Program.cs` (registration +
composition guard on the installation default),
`src/Api/WM.Api.Tests/Security/` (a new inventory-style test),
`appsettings.json` / `appsettings.Development.json`.
**Done when:**
- `IClock` (over `TimeProvider`) is registered and is the only sanctioned way to read now.
- A `ZoneId` primitive validates against `TimeZoneInfo.FindSystemTimeZoneById` on construction and
  cannot hold an invalid value.
- The installation default zone is configuration, **validated at composition**; the host refuses to
  start on a missing or unparseable value — the same mechanism as 006 P2's signing-key guard, which
  the reviewer verified by mutation.
- A test scans `src/**/*.cs` for `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow` and
  `DateTimeOffset.UtcNow` against a **hardcoded allow-list**, in the shape of
  `EndpointAuthorizationInventoryTests` — so a new raw clock read fails **by name** until someone
  adds it deliberately. (This is legacy's `AvoidDirectTimeUsageAnalyzer` without the analyzer
  infrastructure the repo does not have.)
- The allow-list starts populated with today's **24** non-test call sites across 12 files *(measured 2026-08-14; at build time, 2026-09-24, it was **27 in 13** — `DemoUserSeeder.cs` arrived with 011 P9. The allow-list in `RawClockReadInventoryTests` is authoritative, not this line)*
  (`Entity.cs`, `LicenseCodec.cs`, `User.cs`, `AuthService.cs`, `SecurityGroupService.cs`,
  `TokenService.cs`, `UserManagementService.cs`, `PeopleSeeder.cs`, `PeopleModule.cs`,
  `PunchSeeder.cs`, `PunchService.cs`, `TimeAttendanceModule.cs`); P2–P5 empty the ones that matter.
  Identity's are audit/expiry stamps and are legitimately UTC — they move to `IClock` for
  testability, not for correctness, and the plan says which is which.
**Tests:** `ZoneId` rejects `""`, `"Europe/Nowhere"` and a Windows id on Linux; accepts
`"Europe/Ljubljana"` and `"UTC"`. The host fails to compose on a blank/garbage default —
**verified by mutation**, not by reading. The scanner fails when a raw `DateTimeOffset.UtcNow` is
added to a file not on the list, and fails when a list entry no longer exists (no stale entries).
**Risk:** low

---

### [x] P2 — `Site.TimeZone` becomes real, behind a People contract
**Touches:** `src/Modules/People/WM.Modules.People/Domain/Employee.cs` (`Site.TimeZone` → `ZoneId`),
`Services/` (resolver), `PeopleModule.cs` (contract registration + expose on the employee
projection), a People migration (validate/normalise existing rows),
`src/Modules/People/WM.Modules.People.Tests/` (created by 003 P2b — extend it),
`src/Modules/People/WM.Modules.People/Data/PeopleSeeder.cs`.
**Done when:**
- A contract answers *"in which zone is this employee's day measured?"* — resolved as
  **site → nearest ancestor site with a zone → installation default**, and it reports **which** of
  the three answered.
- `Site.TimeZone` becomes nullable so "unset, inherit" is expressible; `"UTC"` stops being a
  default that means "nobody decided". The migration says so in a comment.
- No cross-module read: TimeAttendance can obtain the zone without a project reference into
  People's internals.
- `GET /api/employees` projects the resolved zone.
- *(Q2 answered 2026-09-24: an admin sets zones in Settings.)* An authorized write sets or clears a
  site's `TimeZone`, validated through `ZoneId` (IANA only), under an existing permission. Clearing
  means "inherit". A portal field only if a site settings screen already exists.
**Tests:** child site unset → inherits parent; whole chain unset → installation default and says so;
a cycle in `ParentId` terminates rather than stack-overflowing; two sites in different zones resolve
differently in one request; the migration is exercised up **and** down on a seeded database.
*(Note: STATE.md records there is no Postgres test harness — 001 P2's promised migration test was
never written for exactly this reason. Either add the harness here or state plainly in the PR that
the migration is untested. Do not claim it.)*
**Risk:** medium — it is the first cross-module contract addition since 003 P2b.

---

### [x] P3 — Employment resolves at the employee's local today
**Touches:** `src/Modules/People/WM.Modules.People/PeopleModule.cs` (`Today()` → zone-aware),
`src/Modules/People/WM.Modules.People.Tests/`.
**Ordering:** **must land after 007 P1**, which introduces `Today()`, `EmployedFrom`/`EmployedUntil`
and `Employment.IsEmployedOn`. P3 changes only the *default reference date*; the predicate itself is
already correct and is not touched.
**Done when:**
- `Today()` is gone. The employment question without an explicit `employedOn` resolves at the
  employee's local today.
- The comment at `PeopleModule.cs:31-38` — which describes this exact defect and defers it — is
  replaced by the fix, and the PR quotes the comment it closes.
- `?employedOn=` still wins when supplied, unchanged.
**Tests:** an employee at `Pacific/Auckland` whose `EmployedUntil` is their local today is **still
employed** when the request is served at 12:00 UTC (UTC has already rolled over; Auckland has not…
and vice versa at 12:00 UTC in June). The mirror case at `Pacific/Honolulu`. A list spanning both
sites returns different derived statuses **in the same response**, which is the whole point.
Inclusivity is preserved (last day counts) — the legacy rule at `76.V5.22.0.0.sql:33-36`.
**Risk:** low — one module, one method.
**What P3 did not fix (recorded at review, 2026-09-24):** P3 moves only the People endpoints off UTC.
Three other "employed today" reads still take UTC's date and are **not** P3's:
`PunchService` live feed (`ListEmployedOnAsync(DateOnly.FromDateTime(DateTime.UtcNow))`, ~`:147`)
and presence (~`:180`) belong to **P4**; `DemoUserSeeder` (~`:31`, demo-only) is left as a
recorded seeder-only UTC read, owned by P4 together with `PunchSeeder`. P3 also changed POST's
default `EmployedFrom` to the site's local today — a necessary consequence of removing `Today()`.

---

### [x] P4 — A punch belongs to a local day
**Touches:** `src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Services/PunchService.cs`
(`:149-150` range, `:158` grouping), `TimeAttendanceModule.cs:60,77` (range defaults),
`src/Modules/TimeAttendance/WM.Modules.TimeAttendance.Tests/` (*exists since 010 P1 / #92 — extend it; it was
"new project" when this plan was written*). Line numbers in this portion and P5 date from 2026-08-14; re-locate by symbol.
**Done when:**
- The timesheet window is built from the employee's zone, not `TimeSpan.Zero`.
- Punches group by **local** date.
- The grouping goes through one named seam — `IOwningDayResolver.Resolve(instant, zone)` or
  similar — whose body today is "the local calendar date" and whose XML doc names the five legacy
  branches and says plan 002 owns them. **`Punch.Timestamp` stays `DateTimeOffset`.**
- *(Q4 answered 2026-09-24: freeze.)* The punch stores its **resolved local date** (and the zone
  it was resolved in) when it is recorded, from the employee's **home-site** zone (Q2 (a)); grouping
  reads the stored date, not a re-derivation. This needs a TimeAttendance migration. How existing demo
  punches get their date is P4's to state (see Out of scope); a later zone edit never moves them.
- The default "today" for a timesheet is the employee's local today.
- *(Added at 008 P3 review.)* The live feed and presence reads in `PunchService` stop asking
  `ListEmployedOnAsync(<UTC date>)`. People exposes a per-employee "employed at their local today"
  contract (e.g. `ListEmployedAtLocalTodayAsync`), resolved in one query batch — not one date for
  every employee. `DemoUserSeeder`'s UTC read moves to the same contract or is re-labelled a
  deliberate seeder read. The raw-clock allow-list entries for these files name P4, not P3.
**Tests:** a punch recorded, then its site's zone edited, still reports its original local date;
23:30 UTC at `Asia/Tashkent` (+05) → next local day; 00:30 UTC at
`America/Los_Angeles` (−08) → previous local day; a `Europe/Ljubljana` punch at 22:30 UTC in July →
23 July local not 22 July (the seeded-demo defect, pinned); **DST fall-back** — two punches an hour
apart both reading local 01:30 stay two punches on one local day, ordered correctly; **DST
spring-forward** — a 23-hour local day yields the right window and loses nothing; a zone that
resolves to UTC behaves exactly as today (no regression for a single-zone install).
**Risk:** medium — it changes what every existing timesheet response says.
**Follow-up (recorded at P4 review):** once every install has booted past P4, a later migration makes
`Punch.LocalDate`/`LocalZone` NOT NULL and removes `PunchLocalDateBackfill` from `Program.cs`.
Until then a null row — for example one written by an old instance during a rolling deploy — is
invisible to timesheets until the next start backfills it.

---

### [x] P5 — An offline punch carries its own offset — PR [#97](https://github.com/00008550/WM/pull/97)
**Touches:** `src/Modules/TimeAttendance/WM.Modules.TimeAttendance/Services/PunchService.cs:36-37`,
the punch request contract, `Domain/Punch.cs` (+ `ReceivedAt`), a TimeAttendance migration,
`WM.Modules.TimeAttendance.Tests/`.
**Done when:**
- A supplied `Timestamp` **must** carry an offset; a bare local `DateTime` is a 400, not a silent
  UTC interpretation. (Legacy's `GetTimeForSwipe:106-110` does the silent thing and cannot be
  audited afterwards.)
- The punch stores **both** the client instant and the server receipt instant, so skew is visible
  after the fact rather than inferred.
- The existing 5-minute future guard (`:37`) becomes symmetric: a punch far in the **past** is
  accepted — offline queues are the point — but flagged, with the threshold as configuration, not a
  `const`. (006 P3 set the precedent: `AccountLockoutOptions`, defaults unchanged.)
- The API documents which clock it trusts, so the Flutter client has a contract rather than a habit.
**Tests:** a request with an offset-less timestamp is rejected; a queued punch 30 hours old is
accepted and flagged; a punch 10 minutes in the future is rejected as today; client and server
instants are both persisted and both surface on the punch DTO; a punch whose client offset
contradicts its home site's resolved zone by more than the DST maximum is flagged, not dropped.
**Risk:** medium — it is a breaking contract change on a public endpoint. Ship the rejection behind
the same review that documents it.
**Follow-up (recorded at P5 review, 2026-09-25):** P5 applies only to `POST /api/punches`, the one
endpoint that accepts a `timestamp`. The self-service `POST /api/me/punch` (`SelfPunchRequest`) takes
**no timestamp**; the server always stamps it. So a Flutter employee punching for **themselves** cannot
queue offline yet: a queued self-punch would be dated when it arrives. **Owner: the Flutter
self-service plan** (not yet written). It adds an optional offset-required `timestamp` to
`SelfPunchRequest` and routes it through the same `ClientTimestamp` parse, flags and `receivedAt`. P5
did not add it because that would widen this portion's contract change to a second endpoint.

---

## Open questions for the user

**1. Is WM multi-zone at all? This is the one that shapes the plan.**
Legacy is single-zone by construction (one DB, one website copy per customer —
`High Level Architecture.md:35,43`) and WM's deployment model (§13A: on-prem, database per customer)
is the same shape. So *"one installation zone, like legacy but explicit"* is a defensible answer,
and it is cheaper: P2 collapses to a single validated setting and `Site.TimeZone` is **deleted**.

- **(A) Single installation zone.** Cheapest. Matches legacy and §13A. `Site.TimeZone` goes.
- **(B) Per-site zone with inheritance** — what this plan is written for. WM already put the column
  on `Site`; it makes the column mean something; it costs one contract and one resolver.
- **(C) Status quo — UTC.** Not recommended and not defensible as "what TLW did": legacy uses the
  installation's *local* zone, never UTC, and its 903 `DateTime.Now` reads are server-local.

**What must not happen is (C) by default.** Today `Site.TimeZone` sits in a shipped migration
looking like a feature and is read by nothing — that is worse than either real answer, because it
tells the next reader the question is settled.

**2. If (B): which site's zone owns a punch — the employee's site, or the site they punched at?**
Legacy answers "the device's", which for a fixed terminal *is* the place of work
(`SkiplyUbiqodApiController.cs:98-102`). WM has dropped devices and a phone travels. Candidates:
(a) the employee's home site — stable, wrong for genuine travel; (b) a zone derived from the punch's
geolocation — WM already captures coordinates, but this makes a day's punches potentially span two
local dates; (c) the employee's home site, with travel handled as an explicit exception. I lean (a)
for P4 and would name (c) as the seam. **This changes the Clocking aggregate's key**, so 002 needs
the answer.

> **Answered by the user, 2026-09-24 — (a), home site.** An administrator sets each site's time zone
> in Settings. A punch's day is decided by the employee's **home site** zone (via P2's resolver:
> site → nearest ancestor with a zone → installation default). The zone is **never** derived from
> geolocation. Reason: employees rarely work across time zones. Travel (option c) stays a named
> seam for later and is not built now.

**3. Should a *user* have a display zone, distinct from the employee's day zone?**
Legacy has none (`dbo.[User]`, 30 columns, no zone). But a payroll administrator in London reading a
Tashkent site's timesheet needs to know which clock the times are in. My proposal: **no user zone**;
the API returns instants plus the resolved zone, and the SPA renders in the *data's* zone with the
zone shown. Cheap, unambiguous, no new column. Confirm or overrule.

**4. Retroactivity: when tzdata changes or `Site.TimeZone` is edited, may history move?**
The stored `DateTimeOffset` is always authoritative, but the *day a punch was filed on* can shift.
Legacy has the identical hazard with daily templates and does not warn about it
(`TLW-CLOCKING-MODEL.md` §2.3). Options: accept it and audit the setting change; or store the
resolved local date on the punch so history is frozen and only new punches see the new zone. The
second is more storage and is the honest answer for payroll. **This one interacts with §7A's replay
design** — a replay that re-derives the day will disagree with a frozen one — so it may deserve an
ADR rather than a plan line.

> **Answered by the user, 2026-09-24 — freeze.** A punch stores its resolved local date when it is
> recorded; a later `Site.TimeZone` edit or tzdata change affects only new punches. History changes
> only through an explicit, audited **recalculate** action — wanted ("we can recompute if we want"),
> but not part of 008. Recorded as a follow-up for the plan that owns replay (§7A / **002**): a
> recalculate re-derives stored local dates deliberately and audits it; replay reads the frozen one.
> **008 P4 must implement the frozen local date on the punch** (see P4 below).

**5. Proposed `ARCHITECTURE.md` change — propose-only, not applied** (§1–§12 are design sections).
§3 lists `src/SharedKernel` as *"cross-cutting primitives only — not a dumping ground"*. P1 adds
`IClock` and `ZoneId` there. Concrete diff:

```diff
  src/SharedKernel  cross-cutting primitives only — not a dumping ground
+                   (includes IClock and ZoneId: WM reads the clock through one
+                    abstraction and never through DateTime.UtcNow — see plan 008 P1)
```

Also §7A (Kafka replay) should state whether a replayed punch re-derives its owning day or reads a
frozen one — that is question 4's answer, and §7A is currently silent.
