# TLW time model — how legacy resolves a date against a time zone

*Measured 2026-08-14 against `E:\Tlw` at SWF **v6.7** (latest versioning script
`Database\Versioning\95.V6.7.0.0.sql`). Every claim below carries a `file:line`. Where this
document disagrees with another WM document, this one was measured and the other was not.*

> **Why this exists.** Building 007 P1 left a comment in shipped code
> (`PeopleModule.cs:31-38` on `feat/007-p1`) saying WM computes "today" as UTC, that
> `Site.TimeZone` exists and nothing resolves against it, and that the decision "belongs to
> whichever plan makes WM timezone-aware". This is the measurement that plan needs. It answers six
> questions and, equally important, **records what was not measured** in §9.

---

## 0. The one-paragraph answer

Legacy has **no per-site time zone and no concept of one**. It has exactly one geographic zone per
installation — `dbo.SoftwareMainOptions.SystemTimeZone`, a **Windows** id — plus a per-*device*
`Devices.TimeZoneCode` that only decides what wall clock a terminal shows and stamps. Attendance
data is stored as **naked local wall clock**: 577 `time` columns and 457 `datetime` columns against
**6** `datetimeoffset` columns, none of which is attendance. Which day a punch belongs to is decided
by a T-SQL function on `convert(date, @swipeTime)` / `convert(time, @swipeTime)` and the neighbouring
days' daily templates — **no time zone enters that calculation at any point**. A legacy deployment is
single-timezone *by construction*: one database and one website copy per customer
(`Documentation\High Level Architecture.md:35,43`). So legacy never had to answer this question, and
WM — one deployment, `Site.TimeZone` in the model, `DateTimeOffset` punches — has already chosen a
different architecture and implemented half of it.

**Therefore: WM's UTC-everywhere is not inherited from legacy and is not defensible as "what TLW
did". It is an unmade decision.** The recommendation is in §8; the product call is the user's.

---

## 1. The measurement

### 1a. Temporal column census — the whole LINQ-to-SQL model

From `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (6.1 MB, 240,262 lines), counting every
`ColumnAttribute` `DbType`:

| SQL type | Columns | What it can express |
|---|---|---|
| `time`, `time(0)`, `time(7)` | **577** | a time of day. No date, no zone, no offset |
| `datetime` | **457** | a wall clock. No zone, no offset |
| `date` | **146** | a calendar date |
| **`datetimeoffset`** | **6** | an instant |

**All six offset-aware columns are infrastructure, not attendance:**

```
dbo.User2FAHistory.CreatedAt                    HorioDB.designer.cs:163878
dbo.RotageekIntegrationSettings.SettingsUpdatedAt          :179974
dbo.LuccaIntegrationSettings.SettingsUpdatedAt             :181728
dbo.Evalu8IntegrationSettings.SettingsUpdatedAt            :182054
dbo.SynergyAppAuthenticationTokens.IssueDate               :206478
dbo.RsaKeys.CreatedAt                                      :232366
```

**Not one attendance column in the product records an offset.** That is the single most important
fact in this document, and it is the fact WM's `DateTimeOffset` punch already improves on.

### 1b. Where a zone is stored

| Column | Type | Grain | Line |
|---|---|---|---|
| `dbo.SoftwareMainOptions.SystemTimeZone` | `NVarChar(250)` | **one per installation** | `HorioDB.designer.cs:70819` |
| `dbo.Devices.TimeZoneCode` | `NVarChar(100)` | one per physical terminal | `:62112` |

That is the complete list. Verified absent:

| Table | Cols | Has a zone? |
|---|---|---|
| `dbo.Locations` (the employee's location FK) | **4** — `Id, Code, DisplayName, IsActive` | no (`:182985-183045`) |
| `dbo.Buildings` | **5** | no (`:113825-113905`) |
| `dbo.ClientSites` | 12 | no (`:127844-128088`) |
| `dbo.Departments` | — | no |
| `dbo.Employees` (153 cols) | — | no. `OffsetEcart` is a flexi-balance opening figure (`HoursCalculationServiceUtils.cs:485`), not a UTC offset |
| `dbo.[User]` | 30 | no — a legacy user has no personal zone |

**False positives, checked and dismissed.** `dbo.ac_timezone`, `dbo.ac_timezone_details`,
`dbo.LapiTimeZone`, `ac_securitygroup_readers.TimezoneID`, `sy_terminal_readers.Timezone` are
**access-control time windows** — "Office Hours = Mon–Fri 08:00–18:00" — not geographic zones
(`Documentation\Access Control\Access Control.md:77-84`). `SCREEN-TREE.md:431-432` already draws
this distinction correctly; recorded here so nobody re-discovers it a fourth time.

### 1c. It is a Windows id, not IANA, not an offset

The value is picked from `TimeZoneInfo.GetSystemTimeZones()` in a single dropdown on
System Setup → Options (`WebSite\Models\SoftwareOptionModels\SoftwareOptionModel.cs:28-36`,
rendered as `TimeZoneCode`, persisted to `SystemTimeZone` at
`WebSite\Controllers\SoftwareOptionController.cs:812`). It is consumed with
`TimeZoneInfo.FindSystemTimeZoneById` (`Core\Extensions\DateTimeExtensions.cs:72`) and, in SQL, with
`AT TIME ZONE` (`Database\Versioning\79.V5.25.0.0.sql:1426`) — both Windows-id APIs. The unit tests
name the ids literally: `"GMT Standard Time"`, `"West Asia Standard Time"`, `"Israel Standard Time"`,
`"Pacific Standard Time"` (`Core.Tests\Extensions\DateTimeExtensionsTests.cs:19-33`).

Where an integration needs IANA, legacy converts at the boundary with `TZConvert.TryWindowsToIana`
and **gives up if the mapping fails** (`HorioPeopleFirstIntegrationService\Helpers\TimeHelper.cs:67-72`;
documented as a support symptom in `Documentation\Integrations\People First Integration.md:244`).

---

## 2. How legacy computes "today"

There are **two competing house styles**, and the older one dominates.

### 2a. `DateTime.Now` — the server's local clock

| Project | `DateTime.Now` | `DateTime.Today` | `DateTime.UtcNow` |
|---|---|---|---|
| `Source\Logic` | **327** | 30 | 33 |
| `Source\WebSite` | **576** | 101 | 12 |

`PlanningService.cs:41` and `:53` — cited in the task that commissioned this survey — read:

```csharp
(!e.DischargeDate.HasValue || e.DischargeDate >= DateTime.Now.Date || includeFiredEmployees)
```

**This is house style, not an outlier.** It is one of 903 raw clock reads across the two projects,
and because it is a LINQ-to-SQL constant expression it is evaluated in the **web server's** process
and shipped as a parameter — so it does not even see `SystemTimeZone`.

### 2b. `SoftwareOptionService.CurrentTime()` — the newer style

```csharp
public DateTime CurrentTime(string connectionString)
{
    var clientTimeZone = GetOrLoadTimeZone(connectionString);
    return string.IsNullOrEmpty(clientTimeZone) ? DateTime.Now : ConvertToLocal(clientTimeZone);
}
```
`Logic\Settings\SoftwareOptionService.cs:317-324`

The zone is cached in a `static ConcurrentDictionary<string,string>` **keyed by connection string**
(`:27`, `:616-629`) — because one Windows service process serves every customer database on the box
(§5). ~40 call sites in `Logic` (audit trail, absence requests, mustering, proximity cards, e-sign,
expenses, employee contracts).

### 2c. SQL side

`GETDATE()` appears **401** times across `E:\Tlw\Database\**\*.sql`; `GETUTCDATE()` **twice**;
`AT TIME ZONE` **once**. That once is the DB-side equivalent of `CurrentTime()`:

```sql
CREATE OR ALTER FUNCTION GetSystemDateTime()
RETURNS datetime
BEGIN
    DECLARE @SystemTimezone nvarchar(100)
    SELECT TOP 1 @SystemTimezone = SystemTimeZone FROM SoftwareMainOptions
    IF @SystemTimezone IS NULL
        RETURN GETDATE()
    RETURN GETUTCDATE() AT TIME ZONE 'UTC' AT TIME ZONE @SystemTimezone
END
```
`Database\Versioning\79.V5.25.0.0.sql:1416-1427` — used in 6 views/procs.

**And the two styles disagree inside a single view.** `UnifiedEmployeesReportView` opens with
`WITH config AS (SELECT dbo.GetSystemDateTime() AS CurrentTime)` at `:1433`, then computes the
leaver flag 22 lines later as `dbo.IsActiveEmployment(e.DischargeDate, GETDATE()) as IsLeaver`
(`:1455`). One view, two different "now".

### 2d. The nightly batch does not use either

`HorioService` — the process that recalculates yesterday and pre-generates the clocking calendar —
fires when `DateTime.Now` passes a configured `StartupTime` (`HorioService.cs:28-30`, `:125`) and
windows its work on `DateTime.Now.Date` (`ServiceTasks.cs:124-125`, `:212`, `:226`). That is the
**Windows service host's** local midnight, applied to **every customer database on that server**,
whatever each one's `SystemTimeZone` says. `SystemTimeZone` moves audit stamps and export
conversions; it does not move the batch window.

---

## 3. Which day a punch belongs to

`TLW-CLOCKING-MODEL.md` §2.3 records that a swipe's owning day is a calculation over the
neighbouring days' daily templates. **Verified, and it is a T-SQL scalar function** —
`dbo.ProcessQueryGetClockingForSwipe(@employeeid int, @swipeTime datetime)`,
`Database\Versioning\30.V3.0.0.ProcessQuery module.sql:429-528`. That is the **only** definition in
the whole `Database` tree; later scripts only call it (`67.V5.13.0.0.sql:3321`, `:3898`;
`73.V5.19.0.0.sql:232`). Nothing in `Source\Logic` implements it.

It opens by throwing the instant away:

```sql
declare @date date = convert(date, @swipeTime)
declare @time time = convert(time, @swipeTime)
declare @yesterday date = DATEADD(d, -1, @date)
declare @tomorrow  date = DATEADD(d,  1, @date)
```
`:440-443`

Then five branches, first match wins:

| # | Rule | Column | Line |
|---|---|---|---|
| 1 | → **yesterday** if `@time < ` yesterday's template `NightShiftEndTime` | `DailyModels.NightShiftEndTime` | `:449-454` |
| 2 | → **tomorrow** if tomorrow's template has `ShiftToSunday = 1` and `@time > NightShiftStartTime` | `ShiftToSunday`, `NightShiftStartTime` | `:457-463` |
| 3 | → **yesterday** if there are no swipes today, yesterday has a first swipe, and `yesterday + firstSwipe + interval > @swipeTime` | `InterswipeIntervalToMoveToYesterday` | `:465-494` |
| 4 | → **yesterday** if yesterday's template is `ModelType = 7` (shift matching) and a matching rule's target model has `NightShiftEndTime > @time` | `DailyModelShiftMatchingRules` | `:496-524` |
| 5 | → **today**, otherwise | — | `:527` |

**Every comparison is `time` against `time`.** No zone, no offset, no UTC anywhere in the function
or in its inputs.

**Column ↔ UI label mapping** (the vault documents the labels, the schema has the names —
`WebSite\Views\DailyModel\_AddEditDailyModelControls.cshtml:1527-1539`):

| Vault label (`Swipe to clocking allocation.md:31-33`) | Column |
|---|---|
| Night Shift End Time | `DailyModels.NightShiftEndTime` |
| Offset Transaction to Next Day | `DailyModels.ShiftToSunday` (a `Bit`; the name is historical) |
| If the swipe is after | `DailyModels.NightShiftStartTime` |
| Allocate Transactions to Previous/Next Day | `DailyModels.InterswipeIntervalToMoveToYesterday` |

**Two things the vault document does not say and the code does:**

1. **Branch 4 (shift matching) is a full fifth rule**, not the aside the vault gives it
   (`Swipe to clocking allocation.md:55` mentions it in one sentence).
2. **A swipe whose resolved day has no pre-generated clocking row is rejected outright** —
   `ProcessQueryGetClockingForSwipe` returns `NULL`, and the caller answers
   `'Clock Record not found'` and files the swipe as unsuccessful (`73.V5.19.0.0.sql:233-256`).
   This is the mechanism behind `Documentation\Troubleshooting\No Calendar (Clockings) for employee.md`.

**And the time-of-day survives the move.** After allocation the caller re-composes:

```sql
set @date = (select top 1 date from Clockings where ClockingId = @clockingId)
set @dateTime = CONVERT(datetime, @time) + CONVERT(datetime, @date)
```
`73.V5.19.0.0.sql:259-260`

So a 02:00 swipe allocated to the previous day is stored as **02:00 on that previous day** — a
`datetime` that is deliberately not the instant it happened. The slot is the meaning; the instant is
not recoverable from the row.

---

## 4. Stored times: local wall clock, and the recipe that turns one into an instant

`dbo.Clockings` (249 columns) holds:

- `Date` — `DateTime` (`:21426`), the calendar day the row *is*
- `BadgeTime1..12` — `Time` (`:21446`…`:22946`)
- `DeviceBadgeTime1..12` — `Time` (`:22146`…`:22786`)
- `BadgeTime1..12Adjusted` — `Time` (`:23690`…`:23910`)
- `Enter1Start/End`, `Exit1Start/End`, `Enter2Start/End`, `Exit2Start/End`, `DurationTheoretic`,
  `BreakDurationMin`, `PauseDuration` — all `Time`
- `BadgeTime1AdjustedShift1..6`, `BadgeTime2AdjustedShift1..6` — all `Time`

Same everywhere else: `EmployeeSwipes` splits `Date DateTime` + `Time Time` (`:117196-117216`);
`ManualTimesheets.Date DateTime` + `BadgeTime3..12 Time` (`:130648`…); `ClockingActivities.EndTime`,
`DriveStartTime`, `DriveEndTime` are `Time`; `ClockingPauses.BeginTimeAdjusted` is `Time`;
`ClockingData.Scan_Time` is `DateTime` (`:19191`). **All 124 `DailyModels` columns that carry a time
are `Time`** — a daily template is expressed entirely in wall-clock time of day.

The 15 `*Utc`-named columns are device telemetry and integration sync stamps, and legacy says so:

```csharp
// Interpret Unspecified as UTC, if it is coming from DB (GETUTCDATE()).
// Currently used only for CreatedAt, LastHeartbeat, LastSyncStart and LastOfflineStart
// columns from Devices table.
```
`Logic\Settings\SoftwareOptionService.cs:368-370`

### 4a. The canonical recipe — and it is wrong for night shifts, in shipped code

Two integrations independently derive the same formula
`instant = ToUtc(Clocking.Date + BadgeTimeN, SystemTimeZone)`:

```csharp
StartDateTimeValue = DateTimeExtensions.ConvertToUtcDateTime(c.Date.Date.Add(inT.Value),  windowsTz),
EndDateTimeValue   = DateTimeExtensions.ConvertToUtcDateTime(c.Date.Date.Add(outT.Value), windowsTz),
```
`HorioPeopleFirstIntegrationService\Helpers\TimeHelper.cs:198-200`
(helper at `Core\Extensions\DateTimeExtensions.cs:77-86`; Sage HR does the same at
`HorioSageHrIntegrationService\Services\TimeSheetService.cs:150-154`).

**For an overnight shift this produces an end 22 hours before the start**, because `BadgeTime2 =
02:00` belongs to `Date + 1` and nothing in the row says so. People First silently emits
`End < Start`. Sage HR throws and **drops the whole clocking**:

```csharp
if (timeINs[i] > timeOUTs[i]) { throw new InvalidOperationException($"Time in > Time out, ClockingId:{clocking.Id}"); }
```
`TimeSheetService.cs:148`

This is the flat model's defining limitation made concrete: **`Date + BadgeTimeN` is not an
instant, and legacy has no column that would make it one.** Any WM projection that reproduces the
flat 12-slot shape (plan 002) must carry the day-carry explicitly or it will reproduce this bug.

### 4b. DST is not handled anywhere in the business logic

Across all of `E:\Tlw\Source` excluding NuGet packages, `IsDaylightSavingTime` / `IsAmbiguousTime` /
`IsInvalidTime` / `GetUtcOffset` appear in **exactly one file**:
`Communication.Suprema\BLL\Terminal.cs:163-166` — computing the UTC offset to program into a
Suprema terminal, which WM has dropped.

Consequences, unhandled: on fall-back, 01:30 happens twice and both punches store as `01:30`,
indistinguishable and unorderable. On spring-forward, `02:30` is a wall clock that never existed;
`ConvertToUtcDateTime` will silently shift it on export.

---

## 5. Multi-site, multi-zone customers

**A legacy deployment is single-timezone by construction.**

- One database **and one file-system copy of the website** per customer
  (`Documentation\High Level Architecture.md:35`, `:43`).
- `SoftwareMainOptions` is a singleton — read with `.Single()`
  (`SoftwareOptionService.cs:390`, `:602`; `GetSystemDateTime` uses `SELECT TOP 1`).
- Shared Windows services iterate every customer database on the box, which is precisely why the
  zone cache is keyed by connection string (`SoftwareOptionService.cs:27`, `:616-629`). The
  sentence *"A single instance of the service is installed on a server, the service works with all
  clients' databases located on the server"* appears **21 times** in that one document — once per
  service (`:59`, `:77`, `:83`, `:89`, `:99`, `:126`, `:132`, `:138`, `:149`, `:160`, `:173`,
  `:187`, `:206`, `:212`, `:216`, `:222`, `:234`, `:240`, `:246`, `:252`, and once more). It is the
  architecture, not an aside.

Within one customer, the only per-place zone is `Devices.TimeZoneCode`, and it acts **only at
ingest and only on presentation**:

```csharp
public DateTime CurrentTimeOnDevice =>
    string.IsNullOrEmpty(TimeZoneCode) ? DateTime.Now
        : TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.Now, TimeZoneCode);
```
`Logic\Entities\BusinessRules\Device.cs:101-113`

An inbound UTC instant is converted to the device's wall clock and then stored naked
(`WebSite\Controllers\Hooks\V1\SkiplyUbiqodApiController.cs:94-102`, `:112-113`). Nothing
downstream can recover which zone produced the row.

**So: a legacy customer *can* physically span zones, and if they do, `Clockings` silently mixes wall
clocks with no discriminator column.** The product has no answer; it has an assumption. That
assumption is safe for legacy's deployment shape and is not safe for WM's.

### 5a. The offline mobile path — the one WM inherits directly

Legacy's phone app is the closest analogue to WM's phone-only punching, and it is the worst case:

```csharp
internal DateTime GetTimeForSwipe(Device device, BaseSwipeModel model)
    => GetTimeForSwipe(device.CurrentTimeOnDevice, model);

internal DateTime GetTimeForSwipe(DateTime serverTime, BaseSwipeModel model)
{
    var now = serverTime;
    //offline swipe was sent
    if (!model.SwipeDate.IsNullOrEmptyOrWhiteSpace() && !model.SwipeTime.IsNullOrEmptyOrWhiteSpace())
    {
        now = _externalAccessService.ParseDateFromString(model.SwipeDate)
            .AddTicks(TimeSpan.Parse(model.SwipeTime).Ticks);
    }
    return now.TrimSeconds();
}
```
`WebSite\Helpers\ExternalAccessControllerHelper.cs:95-113`

- **Online**: the wall clock of the *QR point's* configured zone.
- **Offline**: the *phone's* wall clock, taken verbatim as two strings. **No offset is transmitted,
  none is stored, and nothing validates it.**

Two punches at the same instant land on different wall clocks depending only on which path they
took, and the row does not record which. Also note `TrimSeconds()`: legacy punches are
**minute-resolution**.

Inbound integrations expose the same gap from the other side. Skiply sends UTC and legacy needs an
out-of-band header to shift it — first `x-swipe-time-adjustment`, *a whole number of hours*, then
`x-swipe-timezone-code`, a Windows id
(`Documentation\Integrations\Skiply Ubiqod Mustering Swipes Processing.md:40-41`).

---

## 6. Pay periods and daily boundaries

**Periods are plain calendar-date ranges. Nothing anchors to any local midnight.**

| Object | Shape | Line |
|---|---|---|
| `dbo.RecapPeriods` | `FromDate DateTime`, `ToDate DateTime`, `Locked`, `Active`, `Format`, `ShowInAbsence` | `:18559-18719` |
| `dbo.Periods` | `StartDate Date`, `EndDate Date`, `ExportCode`, `SchedulerId` | `:182139-182243` |

A clocking joins a period by its `Date` column, so **the pay-period question reduces entirely to the
day-allocation question of §3**. There is no separate period-boundary time.

Other anchors, all zone-free:

- **Monthly counters**: `EmployeeContract.MonthlyCountersPeriodStartDay` (`Int`, `:48505`), resolved
  as `employeeAssignedContractForDate?.MonthlyCountersPeriodStartDay ?? 1`
  (`Logic\EmployeeContractsCalculation\CalculatedEmployeesContractsForPeriod.cs:69-75`) — a
  day-of-month, and a **two-level resolution** (contract overrides, default 1).
- **Week start**: `Calculations.WeekStartsOn` (`Int`), resolved against a hard-coded reference date
  `'2013-04-01'` with the comment *"get reference date to be independent from server settings"*
  (`30.V3.0.0.ProcessQuery module.sql:381-402`). Someone already worried about host-dependence here
  and fixed it — **for the week only**.
- **Period locking**: `SoftwareMainOptions.LockDataBeforeDate` and `BlockCalculationBeforeDate`,
  compared as naked `.Date` against the record's `.Date`
  (`Logic\DataLocking\DataLockChecker.cs:53-63`; `HoursCalculationService.cs:4867-4876`).

### 6a. A correction to `ARCHITECTURE.md` §13

§13's `Calculations` row said *"incl. the 8-window day-boundary matrix"*. **Both halves are wrong.**

They are **7** windows / **14** columns — `MonTueStart/End`, `TueWedStart/End`, `WedThuStart/End`,
`ThuFriStart/End`, `FriSatStart/End`, `SatSunStart/End`, `SunMonStart/End`
(`HorioDB.designer.cs:14979-15239`) — and they are the **night-hours band per weekday pair**, not
the day boundary. The UI section header is `CalculationIndex_NightHours`
(`WebSite\Views\Calculation\Index.cshtml:88-104`) and the consumer reads them as
`nightStartTime`/`nightEndTime`, adding a day when start ≥ end:

```csharp
if (nightStartTime >= nightEndTime) { nightEndTime = nightEndTime.Add(TimeSpan.FromDays(1)); }
```
`Logic\HoursCalculation\DataCache.cs:263-266` (selector at `:207-262`)

The day boundary is `dbo.ProcessQueryGetClockingForSwipe` (§3). Row corrected in this pass.

---

## 7. Fail-opens in the time path

None of these is in `TLW-AUTHORIZATION-MODEL.md` §10's fourteen — that set is about access, this
one is about clocks. Every one substitutes an unrelated clock rather than refusing.

| # | Site | Behaviour on missing/invalid zone |
|---|---|---|
| 1 | `Core\Extensions\DateTimeExtensions.cs:66-75` | blank zone → returns the offset **unchanged** (server local silently substituted) |
| 2 | `Logic\Settings\SoftwareOptionService.cs:317-324` | unset `SystemTimeZone` → `DateTime.Now` |
| 3 | `Database\Versioning\79.V5.25.0.0.sql:1423-1424` | `SystemTimeZone IS NULL` → `GETDATE()` |
| 4 | `Logic\Entities\BusinessRules\Device.cs:101-113`, `:184-200` | empty **or invalid** code → server local; the exception is swallowed |
| 5 | `Logic\Settings\SoftwareOptionService.cs:364-382` | any failure → returns **`null`**, not the input. A *different* fallback from every other site |
| 6 | `HorioSageHrIntegrationService\Services\TimeSheetService.cs:170-180` | invalid zone → emits **local time labelled UTC** to the payroll system |
| 7 | `HorioRotageekIntegrationService\Helpers\HorioDataService.cs:154-157` | logs `Fatal "SystemTimeZone is not defined in settings."` and **continues**; documented as *"If no time zone defined time will be sent as is"* (`Documentation\Integrations\Rotageek Integration.md:49`) |

#7 is the most instructive: a missing setting silently ships every clock-in to an external payroll
system offset by the installation's real UTC offset, and the only signal is a log line.

---

## 8. Keep / Improve / Invert / Drop, and what WM has today

### What WM has

| Thing | State | Evidence |
|---|---|---|
| `Site.TimeZone` | **exists, written by the seeder, read by nothing** | declared `Employee.cs:9`; column `20260720080022_Initial.cs:65` (`text`, not null); set only at `PeopleSeeder.cs:26-28`. A repo-wide grep over `src/**` and `frontend/**` finds no other reference |
| `Site.TimeZone` values | IANA (`"Europe/Ljubljana"`) | `PeopleSeeder.cs:26-28` — **better than legacy's Windows ids**, and correct for WM's Linux containers |
| `Site.TimeZone` validation | none | free `text`; there is no `POST`/`PUT /api/sites` at all, so it cannot even be set through the API |
| `Punch.Timestamp` | `DateTimeOffset` | **strictly better than legacy** — an instant, not a wall clock |
| Punch → day | **UTC midnight** | `PunchService.cs:158` `GroupBy(p => DateOnly.FromDateTime(p.Timestamp.UtcDateTime))` |
| Timesheet range | **UTC midnight** | `PunchService.cs:149-150`; defaults `TimeAttendanceModule.cs:60`, `:77` |
| "Today" for employment | **UTC** | `PeopleModule.cs:38` on `feat/007-p1` |

**For the seeded demo this is already wrong today.** Every site is `Europe/Ljubljana` (UTC+1/+2), so
a punch between 22:00/23:00 and midnight local is filed on the *next* day's timesheet, and the
timesheet's default "today" flips one to two hours early.

### Classification

| Structure | Class | Reason |
|---|---|---|
| A day's attendance is keyed by a **date**, not an instant range | **Keep** | genuine domain truth, and every payroll system on earth agrees |
| The owning day is a **calculation** over neighbouring templates, not `date(timestamp)` | **Keep** | `ProcessQueryGetClockingForSwipe` §3 is right about the domain. Plan 002 must implement it, not `DateOnly.FromDateTime` |
| Storing attendance as naked local wall clock (577 `time` cols) | **Invert** | it destroys the instant. WM's `DateTimeOffset` punch is already the inversion; the job is to stop *discarding* it at the day boundary |
| One `SystemTimeZone` per installation | **Improve** | right idea (a day is measured in *some* zone) at the wrong grain. WM already put the column on `Site` |
| **Windows** time-zone ids | **Improve** | WM's IANA is correct for Linux and for a Flutter client. Keep, but *validate* — today a typo is accepted and blows up at first use |
| `Devices.TimeZoneCode` | **Drop** | devices are out of scope (invariant 3). But the *requirement* it met — "this punching point measures its day in zone X" — moves to `Site` |
| Offline punch = the phone's naked wall clock (`GetTimeForSwipe:106-110`) | **Invert** | WM's offline queue must carry the offset. Legacy's is unrecoverable ambiguity by construction |
| Fail-open to the server clock (all 7 in §7) | **Invert** | a missing zone must fail closed at composition, not silently substitute a different clock |
| No DST handling at all (§4b) | **Invert** | ambiguous and invalid local times are real and must be decided, not ignored |
| The batch window on the service host's midnight (§2d) | **Invert** | a nightly job must run per zone, or say explicitly that it does not |
| `DateTime.Now` as house style | **Invert — and legacy agrees** | see below |

### Legacy's own verdict on `DateTime.Now`

The newest legacy component ships a Roslyn analyzer that makes `DateTime.Now`, `DateTime.UtcNow`
**and** `DateTime.Today` a **compile error**:

```csharp
id: "SYTCHDT0001",
title: "Avoid direct time access",
messageFormat: "Use Time Abstraction instead of '{0}'",
defaultSeverity: DiagnosticSeverity.Error,
description: "Calling DateTime.Now in this project can lead to errors as application time zone can be different from system's.",
```
`Source\TabletKiosk\AvoidDirectTimeUsageAnalyzer\AvoidDirectTimeUsageAnalyzer.cs:16-52`
(rationale restated at `Documentation\Synergy Touch\Synergy Touch project overview.md:11`)

The legacy team reached this document's conclusion and enforced it — in exactly one project, twelve
years too late to move the other 903 call sites. **WM can enforce it on day one, and that is
plan 008 P1.**

### The recommendation

**A day is measured in the zone of the place the work happened.** Resolve it as: the employee's
site's zone → inherited from the parent site if unset → the installation default. That degenerates
to legacy's single-zone behaviour when every site shares a zone, and it makes `Site.TimeZone`
mean something instead of sitting in the schema looking like a feature.

**It is the user's call whether WM is multi-zone at all** — WM's deployment model (§13A: on-prem,
database per customer) is the same shape as legacy's, so single-zone is defensible. But it must be
*decided*: "UTC everywhere" is neither of the two coherent answers. Open question 1 of plan 008.

---

## 9. What was **not** measured

Recorded so "not mentioned" is never later read as "checked" — the failure that has already cost
this project twice.

| Not measured | Why it matters | Cost to measure |
|---|---|---|
| **Any real customer database.** No production or demo instance was inspected | So I cannot say how many installs actually *set* `SystemTimeZone` (all 7 fail-opens in §7 are reachable only when it is blank), nor whether any customer runs devices in two zones. §5's "can happen" is a schema fact, not a field observation | needs a DBA, out of reach here |
| **The Angular/Kendo frontend's date handling.** `Source\WebSite\Views\**` client-side parsing/rendering was not read | The legacy SPA may apply a browser-local shift on top of everything above. `ToJavascriptTicks` at `WebSite\Helpers\SharedMethods\Date.cs:73-78` calls `ToUniversalTime()` on a value that is already local wall clock, which looks wrong, but I did not trace its callers | ~1 hour |
| **~58 of ~60 payroll plugins.** I read the People First and Sage HR exports and the Rotageek helper | §4a's night-shift defect was found in 2 of 2 exports examined. The rate in the other 58 is unknown and could be 100% | ~4 hours |
| **`SynergyApp` (Flutter/native) and `SynergyTouch` client code.** Only the server-side contract (`ExternalAccessControllerHelper.cs`) and the analyzer were read | What the phone actually puts in `SwipeDate`/`SwipeTime` — device zone, UTC, or something else — is inferred from the server's parsing, not observed in the client | ~2 hours |
| **`Reprocessor` and `HorioCalculationService`.** Not opened | The recalculation entry points may have their own "now". `HoursCalculationService` was read only around the day-boundary and lock-date code | ~2 hours |
| **Absence, accrual and scheduling date handling.** Out of scope of this question | `AbsenceRequests.cs` uses `CurrentTime()` in places (`:1544`, `:1578`, `:2582`) and `DateTime.Now` elsewhere; whether an absence day means the same day as a clocking day was not established | belongs with the Absence survey |
| **Whether `SystemTimeZone` is ever migrated when a customer moves servers.** `Database\Postmigration scripts` not read | A restore onto a box in another zone silently reinterprets every historical clocking | ~30 min |

---

## 10. Found en route — **not this document's subject, hand to the employment audit**

While counting `GETDATE()` call sites I read all four definitions of `dbo.ActiveEmployeesView`.
`TLW-PEOPLE-MODEL.md:95` cites `78.V5.24.0.0.sql:74-80` as the *latest* revision and plan 007 builds
on the operator-precedence defect there. **There is a later revision, and it fixes the defect:**

```sql
-- 76.V5.22.0.0.sql:40-45   WHERE IsActive = 1 AND DischargeDate IS NULL OR DischargeDate >= CAST(GETDATE() AS date);
-- 77.V5.23.0.0.sql:1547-52 (same, unparenthesised)
-- 78.V5.24.0.0.sql:74-79   (same, unparenthesised)
-- 79.V5.25.0.0.sql:728-733
    WHERE IsActive = 1
        AND (DischargeDate IS NULL OR DischargeDate >= CAST(GETDATE() AS date));
```

Four definitions, no fifth; the schema is current to v6.7. So the bug was real and shipped for three
releases (V5.22–V5.24) and was **fixed in V5.25**. 007's *Keep/Improve/Invert/Drop* row
*"`ActiveEmployeesView`'s `AND … OR …` → **Invert**"* is still the right call — WM should compute
employment once, in one tested expression — but the supporting claim that legacy has it wrong
*today* is stale, and the shipped commit message for 007 P1 repeats it.

**Deliberately not edited here.** A separate audit of 007's employment claims is in flight and owns
`TLW-PEOPLE-MODEL.md` and `007-the-person-record.md`; two agents correcting the same sentence is how
`docs/scope-model-corrections` happened.

One thing this survey *does* confirm for that audit: `dbo.IsActiveEmployment` treats the last day as
**inclusive** and a `NULL` discharge date as employed — `WHEN @dischargeDate < @referenceDate THEN 0
ELSE 1` over `DATE` parameters (`76.V5.22.0.0.sql:25-38`, the only definition), and every one of its
60+ call sites passes raw `GETDATE()`, never `dbo.GetSystemDateTime()`.

---

## 11. Sources actually opened

```
E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs                       (full column census, 240,262 lines)
E:\Tlw\Source\Logic\Settings\SoftwareOptionService.cs                  :27, 300-345, 364-382, 595-629
E:\Tlw\Source\Logic\Entities\HorioDataContext.cs                       :143-154
E:\Tlw\Source\Logic\Entities\BusinessRules\Device.cs                   :101-113, 184-200
E:\Tlw\Source\Logic\HoursCalculation\DataCache.cs                      :40-130, 197-269
E:\Tlw\Source\Logic\DataLocking\DataLockChecker.cs                     (whole)
E:\Tlw\Source\Logic\Planning\PlanningService.cs                        :1-80
E:\Tlw\Source\Logic\ExternalAccess\SYQR.cs                             :270-340
E:\Tlw\Source\Logic\EmployeeContractsCalculation\CalculatedEmployeesContractsForPeriod.cs :69-75
E:\Tlw\Source\Core\Extensions\DateTimeExtensions.cs                    :47-115
E:\Tlw\Source\Core.Tests\Extensions\DateTimeExtensionsTests.cs         (whole)
E:\Tlw\Source\WebSite\Helpers\ExternalAccessControllerHelper.cs        :95-134
E:\Tlw\Source\WebSite\Helpers\SharedMethods\Date.cs                    (whole)
E:\Tlw\Source\WebSite\Models\SoftwareOptionModels\SoftwareOptionModel.cs :1-60
E:\Tlw\Source\WebSite\Controllers\API\SynergyApp\SynergyAppSwipesApiController.cs :30-160
E:\Tlw\Source\WebSite\Controllers\ExternalAccess\SYQR\Swipes.cs        :27-90
E:\Tlw\Source\WebSite\Controllers\Hooks\V1\SkiplyUbiqodApiController.cs :60-128
E:\Tlw\Source\WebSite\Views\Calculation\Index.cshtml                   :85-110
E:\Tlw\Source\WebSite\Views\DailyModel\_AddEditDailyModelControls.cshtml :1527-1539
E:\Tlw\Source\HorioService\HorioService.cs, ServiceTasks.cs            (clock reads)
E:\Tlw\Source\Horio.SwipeProcessing.Core\SwipeProcessingDLLWrapper.cs  (whole)
E:\Tlw\Source\HorioPeopleFirstIntegrationService\Helpers\TimeHelper.cs (whole)
E:\Tlw\Source\HorioSageHrIntegrationService\Services\TimeSheetService.cs :130-190
E:\Tlw\Source\HorioRotageekIntegrationService\Helpers\HorioDataService.cs :140-190, 250-290
E:\Tlw\Source\TabletKiosk\AvoidDirectTimeUsageAnalyzer\AvoidDirectTimeUsageAnalyzer.cs (whole)

E:\Tlw\Database\Versioning\30.V3.0.0.ProcessQuery module.sql           :330-531 (allocation + week start)
E:\Tlw\Database\Versioning\73.V5.19.0.0.sql                            :180-264
E:\Tlw\Database\Versioning\76.V5.22.0.0.sql                            :20-48
E:\Tlw\Database\Versioning\77.V5.23.0.0.sql                            :1545-1554
E:\Tlw\Database\Versioning\78.V5.24.0.0.sql                            :72-82
E:\Tlw\Database\Versioning\79.V5.25.0.0.sql                            :726-734, 1390-1460
(plus greps across all of E:\Tlw\Database for GETDATE/GETUTCDATE/AT TIME ZONE)

E:\Tlw\Documentation\Swipe to clocking allocation.md                   (whole)
E:\Tlw\Documentation\High Level Architecture.md                        :1-60
E:\Tlw\Documentation\Integrations\Rotageek Integration.md              :49, 278
E:\Tlw\Documentation\Integrations\People First Integration.md          :244
E:\Tlw\Documentation\Integrations\Skiply Ubiqod Mustering Swipes Processing.md :38-54
E:\Tlw\Documentation\Synergy Touch\Synergy Touch project overview.md   :11
E:\Tlw\Documentation\Access Control\Access Control.md                  :77-84
```

WM side: `src/Modules/People/WM.Modules.People/{Domain/Employee.cs, PeopleModule.cs,
Data/PeopleSeeder.cs, Data/Migrations/20260720080022_Initial.cs}`,
`src/Modules/TimeAttendance/WM.Modules.TimeAttendance/{Services/PunchService.cs,
TimeAttendanceModule.cs}`, `src/SharedKernel/WM.SharedKernel/Domain/Entity.cs`, and
`git show e482f83:src/Modules/People/WM.Modules.People/PeopleModule.cs` for 007 P1's `Today()`.
