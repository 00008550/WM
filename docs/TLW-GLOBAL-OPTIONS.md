# TLW global options — `dbo.SoftwareMainOptions` measured

Surveyed 2026-08-30. Sources: `E:\Tlw\Source`, `E:\Tlw\Database`, `E:\Tlw\Documentation`.
Every claim below carries a `file:line`. WM's own documents were not used as evidence.

This document replaces the one-line record in `TLW-CLOCKING-MODEL.md:30`
("*global settings — WM has no equivalent*"), which was the entire extent of what WM knew.

---

## 1. The measurement

| Fact | Value | Where |
|---|---|---|
| Table | `dbo.SoftwareMainOptions` | `HorioDB.designer.cs:69348` |
| Columns | **227** — counted, not quoted | extraction over `:69348`–`:74807` |
| First column | `Id` `Int NOT NULL IDENTITY` | `:70279` |
| Last column | `ShouldGenerateExceptionForOverlappingActivities` `Bit NOT NULL` | `:74807` |
| Rank in estate | 3rd, behind `Devices` (268) and `Clockings` (249) | schema sweep |
| Cardinality | **singleton — one row per database** | `SoftwareOptionService.cs:62,241-245` |
| Read via | `select * from SoftwareMainOptions` (whole row, always) | `SoftwareOptionService.cs:25` |

**Classification is complete and non-overlapping.** 227 columns assigned, **0 unclassified,
0 duplicated, 0 phantom** — asserted mechanically by set difference against the extracted schema
list, not by eye.

| Group | Cols | What it is |
|---|---:|---|
| `MOBILE` | 48 | Synergy App / QR feature and icon toggles |
| `DEVICES-DROPPED` | 25 | Sy400, IR, BioStar, Suprema, ANPR, card printing — out of scope per CLAUDE.md §3 |
| `COSMETIC-UI` | 25 | screen defaults, page size, branding |
| `DEAD` | 18 | written by the settings screen, **read by nothing** |
| `FIRE-EMERGENCY` | 17 | muster/evacuation report composition |
| `RULES-POLICY` | 15 | what is mandatory, who may approve, what is allocatable |
| `SECURITY` | 12 | IP allow-lists, 2FA, SSO, session lifetime |
| `RULES-SWIPE` | 11 | **how a punch becomes a clocking** |
| `RULES-CALC` | 10 | **how hours are calculated and when** |
| `RETENTION` | 10 | log cleaner, leaver deletion |
| `NOTIFICATIONS` | 10 | absence-request and document e-mails |
| `INFRA-IMPORT` | 9 | identity, legacy file-import format, paths, version |
| `RULES-ACTIVITY` | 7 | activity/timesheet closure semantics |
| `EXPENSES` | 6 | expense linkage and defaults |
| `RULES-TEMPORAL` | 4 | **time zone, year start, week numbering, hours format** |

**47 of 227 columns (21%) are rules logic wearing a configuration costume.**
Those are the five `RULES-*` groups. They are the answer to "our rules lack some distinctive logic".

---

## 2. Who reads it

| Tier | Occurrences | Files | Note |
|---|---:|---:|---|
| `Logic\` (domain) | 43 | 18 | `SoftwareOptionService.cs` alone is 16 |
| `WebSite\` | 380 | 227 | mostly the settings screen + report generators |
| **T-SQL stored procedures** | see §2.1 | — | **a consumer tier a C#-only survey misses entirely** |

Heaviest domain consumers, by distinct settings read:

| File | Settings read | Which |
|---|---:|---|
| `Logic\HoursCalculation\HoursCalculationService.cs` | 11 | `BlockCalculationBeforeDate`, `ShouldOverrideClockingValues`, `ShouldOverwriteCostCentres`, `SkipNetHoursCalculationForFutureDays`, `QrGenerateExceptionForSwipesWithoutGeo`, `DisableAutoSwipesForNoBadge`, `ShouldGenerateExceptionForOverlappingActivities`, `IsDailyModelLimitsAppliesToActivity`, `EnableCounterHoursCapping`, `PreProcessSwipesWithBreaksInSwipeAndGo`, `MoveOpenBreakToLastSwipe` |
| `Logic\HoursCalculation\HoursCalculationServiceUtils.cs` | 2 | `IsBalanceCalculationFromContractsDisabled:918`, `BlockCalculationBeforeDate:1697` |
| `Logic\HoursCalculation\DataCache.cs` | 1 | `SystemTimeZone:70,71` — **defines what `Now` means for the whole engine** |
| `Logic\Settings\SoftwareOptionService.cs` | ~8 | the accessor itself |
| `WebSite\Controllers\SoftwareOptionController.cs` | all 227 | the settings screen |

The load pattern is **whole-row, per calculation run**: `DataCache.LoadData` does
`context.SoftwareMainOptions.Single()` alongside `context.Calculations.Single()`
(`DataCache.cs:81-83`). The 227 columns are one object handed to the engine.

### 2.1 The T-SQL consumer tier — a correction to my own first pass

My first sweep excluded `/e/Tlw/Database/` as "DDL noise" and produced a dead-column list of 25.
Four of those turned out to be **live, and live in the most important place in the product**:

| Column | Read at |
|---|---|
| `SkipReprocessingAfterEachSwipe` | `Database\Versioning\73.V5.19.0.0.sql:115` — `set @shouldCalculate = ~(ISNULL((select top 1 SkipReprocessingAfterEachSwipe from SoftwareMainOptions), 0))` |
| `NumberOfMinutesForDoubleSwipe` | `73.V5.19.0.0.sql:116`, `Database\Support\process swipes for period.sql:28` |
| `NextSwipeEndsCurrentActivity` | `Database\Versioning\75.V5.21.0.0.sql:868` (latest redefinition; earlier at `67`, `58`, `55`, `52`, `51`, `50`) |
| `FirstActivityStartAsClockIn` | T-SQL only |

**Swipe→clocking allocation is implemented in stored procedures, not C#.** Any survey of TLW
behaviour that reads only `Source\Logic` will systematically under-report the punch pipeline.
Recorded here so the next survey does not repeat it.

Confirmed live in T-SQL and nowhere else, or in both: `ShouldOverwriteCostCentres`,
`SuspendSynergyFingerprintSynchronization`, `LastInSwipeNumberOfDaysCalc`,
`LastOutSwipeNumberOfDaysCalc`, `NewEmployeeSwipesStartDate`, `SystemTimeZone`.

---

## 3. Is it one row or many?

**One row per database.** Three independent mechanisms enforce it, none of them a constraint:

1. `SoftwareOptionService.Add` calls `ValidateOnAddition()`, which throws
   `"There are should be the only SoftwareMainOption object in db"` (`SoftwareOptionService.cs:239-246`).
2. Every read is `.Single()` — it throws rather than picks (`:62`, `:390`, `:403`, `:416`, `:488`, `:506`, `:524`, `:602`; `DataCache.cs:83`).
3. **Every release script repairs it**: `delete from SoftwareMainOptions where Id > (select top 1 Id from SoftwareMainOptions)` — `Database\Versioning\95.V6.7.0.0.sql:96`. A repair statement shipped in
   every version is evidence duplicate rows have actually happened in the field.

There is **no `SiteId`, `TenantId`, `CompanyId` or `LocationId`** on the table. Tenancy in TLW is
**one database per customer**: the options cache is keyed by connection string —
`private static readonly ConcurrentDictionary<string, string> TimeZones` keyed on
`connectionString ?? string.Empty` (`SoftwareOptionService.cs:27`, `:611-624`).

**Consequence for WM.** WM has never settled multi-tenancy. This table forces the question:
legacy answers it by database separation, so "global" and "per tenant" are the same thing there and
WM cannot inherit the shape. And nothing in TLW is per-site — see §6.

### 3.1 Two caches, one of them never invalidated

- `_mainOption` is an **instance** field, cleared by `ClearOptionsCache()` on every write
  (`:53`, `:66`, `:569-572`, `:606`). Per-instance, so other DI scopes keep stale values until
  their own next miss.
- `TimeZones` is a **static** dictionary with **no invalidation path at all** (`:611-624`).
  Changing `SystemTimeZone` in the UI does not take effect until the process restarts.
- `GetOptionsAsCache` swallows the exception and substitutes `new SoftwareMainOption()` on failure
  (`:544-553`) — i.e. **a database error silently becomes "all defaults"**, which for
  `bool` columns means `false` everywhere. That is a fail-quiet, not a fail-closed.

---

## 4. The 47 rules columns, and what WM does with each

This is the section the pause was called for. "WM state" is measured against
`src/**` in this worktree.

### 4.1 `RULES-CALC` (10) — when and whether hours are computed

| Column | Legacy behaviour | WM |
|---|---|---|
| `BlockCalculationBeforeDate` (`DateTime`) | hard floor on recalculation. `CalculateBalance` returns early if `toDate < value`, and **silently clamps `fromDate` up to it** otherwise (`HoursCalculationService.cs:4867-4877`). Also gates per clocking in `ShouldCalculate` (`:1561-1569`) and `:316`, `:5003` | **absent.** WM has no calculation floor |
| `LockDataBeforeDate` (`DateTime NULL`) | a *second*, separate date floor — data locking rather than calculation blocking. `GetLockDataBeforeDate()` (`SoftwareOptionService.cs:591-594`); it is the only setting with unit tests (`Logic.Tests\DataLocking\DataLockCheckerTests.cs`, 7 cases) | **absent** |
| `ShouldOverrideClockingValues` (`Bit NULL`) | when true, recalculation **overwrites the stored clocking's template-derived fields** — `DailyModelID`, `BreakDurationMin`, `DurationTheoretic`, `Enter1Start/End`, `Enter2Start/End`, `Exit1Start/End`, and resets `IsDTManualChanged` — from the current daily model (`HoursCalculationService.cs:541-559`). **Split/multi-shift always overrides regardless of the setting** (`:542-543`) | **absent.** This is precisely the "does history change on replay?" question WM's replay design must answer |
| `SkipNetHoursCalculationForFutureDays` | skips calculation for `clocking.Date > cache.Now` (`:587`, `:4862`, `:4880`) | **absent**; WM has no future-day concept |
| `EnableCounterHoursCapping` | caps a counter at the daily model's limit. The code comment is the whole story: *"introduced capping of counter hours to daily models limits now breaks adjustments on many clients / it should limit if only enabled in main options"* (`:4136-4138`). A behaviour change shipped behind a flag to avoid breaking customers | **absent** |
| `ShouldOverwriteCostCentres` (default **1**) | resets cost-centre allocation on every recalc (`:566`, `HoursCalculationServiceMultiModel.cs:35`); also read in T-SQL | **absent** |
| `IsDailyModelLimitsAppliesToActivity` (default 0) | whether the template's in/out limits clamp *activity* times as well as clocking times (`:3915`) | **absent** |
| `IsBalanceCalculationFromContractsDisabled` | switches balance between the contract-derived and the employee-default path (`HoursCalculationServiceUtils.cs:918`). This is the two-level `…Effective(contracts)` resolution, toggled globally | **absent** |
| `AutoCalculateBalanceFromDailyBrowser` | whether editing in the Daily Browser triggers balance recalculation | **absent** (plan 010 territory) |
| `IsGuiHoursRecalculationEnqueueEnabled` | whether a GUI edit enqueues recalculation or runs it inline | **absent** |

### 4.2 `RULES-SWIPE` (11) — how a punch becomes a clocking

| Column | Legacy behaviour | WM |
|---|---|---|
| `NumberOfMinutesForDoubleSwipe` (`int`, shipped default **2**) | inter-swipe dead time; a second punch inside it is discarded as a double. `ADD NumberOfMinutesForDoubleSwipe INT NOT NULL DEFAULT (2)` (`Merge ER2 and TLW\43...sql:8386`); consumed as `@minInterSwipeIntervalMinutes` → `dbo.ProcessQueryIsDoubleSwipe` (`73.V5.19.0.0.sql:116`) | **absent — WM accepts every punch.** Nothing dedupes |
| `SkipReprocessingAfterEachSwipe` (default 0) | when set, a punch does **not** trigger recalculation — batch instead. `set @shouldCalculate = ~(ISNULL(...))` (`73.V5.19.0.0.sql:115`) | **absent** |
| `MoveOpenBreakToLastSwipe` | repairs an unclosed break, but **only for past days, only if the open swipe is within the first four, and only if there is nothing after the fourth**: `HasNotClosedSwipeWithinFirstFour && HasNoSwipesAfterFourthOne` (`HoursCalculationService.cs:1571-1580`). The "4" is hard-coded — a `BadgeTime1..12` fixed-slot artefact | **absent** |
| `PreProcessSwipesWithBreaksInSwipeAndGo` | gates a whole parallel pre-pass (`MaxDegreeOfParallelism = 4`) over swipe-and-go break swipes (`:4968`) | **absent** |
| `DisableAutoSwipesForNoBadge` | suppresses synthesised punches for badge-less employees (`:859`, `:2399`) | **absent** |
| `IsSwipeAndGo` | selects the whole punch-capture mode; 76 references across the product | **absent** — WM has one punch mode |
| `LastInSwipeNumberOfDaysCalc` / `LastOutSwipeNumberOfDaysCalc` (default **1** each, `77.V5.23.0.0.sql`) | how many days back to look for the matching in/out punch — the **night-shift lookback window** | WM hard-codes `AddHours(-18)` (`PunchService.cs:133`) |
| `NewEmployeeSwipesStartDate` | earliest date punches are accepted for a newly imported employee; falls back to `DateTime.Now.AddMonths(-1)` when null (`EmployeesImportService.cs:166`, `:1671`; `SharedPlanningServiceMethods.cs:59`; also T-SQL, `HorioCegidService\EmployeeHelper.cs:174`) | **absent** |
| `QrGenerateExceptionForSwipesWithoutGeo` | raise an exception when a mobile punch has no geolocation (`:829`) | WM geofences but raises nothing |
| `ShouldGenerateExceptionForOverlappingActivities` (default 0, `75.V5.21.0.0.sql`) | overlapping activities → exception (`:2761`) | **absent** |

### 4.3 `RULES-ACTIVITY` (7)

`NextSwipeEndsCurrentActivity` is documented behaviour with worked examples —
`Documentation\Next Transaction Ends Previous Activity.md`. When enabled, any new
activity/pause/swipe closes the open one, so nothing is ever left open. Read in T-SQL only
(`75.V5.21.0.0.sql:868`, twice at `43...sql:23383,23423`). Also here:
`InheritParentActivityCode`, `ActivityByTotalTimeOnly` (read by the kiosk API via
`COALESCE((SELECT TOP 1 ActivityByTotalTimeOnly FROM SoftwareMainOptions), 0)` —
`KioskExternalAccessService.cs:646-647`), `FirstActivityStartAsClockIn`,
`EnableActivitiesFunction`, `ApprovingTimesheetEnabled`, `AllowAbsencesInManualTimesheets`.
WM has none of these; WM has no activity concept at all.

### 4.4 `RULES-TEMPORAL` (4) — the four that decide what a number means

| Column | Legacy | WM |
|---|---|---|
| `SystemTimeZone` (`NVarChar(250)`, Windows id) | **defines `Now` for the entire calculation engine**: `DataCache.Now` returns `ConvertToLocal(SystemTimeZone, CreationTime)` and **falls through to the server clock when empty** (`DataCache.cs:65-72`). Conversion failure returns `null` from a silent `catch` (`SoftwareOptionService.cs:376-382`) | UTC everywhere; `Site.TimeZone` written and read by nothing (plan 008) |
| `YearStartDate` (`DateTime`) | fiscal/annual boundary, 83 references | **absent** |
| `WeekNoCalculationMethod` (`int NULL`) | ISO vs other week numbering. **No reader found outside the settings screen** — candidate dead, but see §7.2 | **absent** |
| `IsDecimal` (`Bit NOT NULL`) | HH:MM vs decimal hours, propagated as an RDL parameter into every report (75 references, e.g. `Horio.ServerReports\TimesheetWithCategoriesContractsReport.rdl:28587+`; `SoftwareOptionService.cs:276`) | WM hard-codes decimal: `Math.Round(hours, 2)` (`PunchService.cs:188,196`) |

### 4.5 `RULES-POLICY` (15) — including a fail-open

`AllowAccessAllocation` is read by `IsAllowAccessAllocation()`, which **returns `true` when the
options row does not exist** (`SoftwareOptionService.cs:399-408`) — an access-allocation permission
that fails **open**. Its two neighbours in the same file, `IsShowAllocateFields` (`:387-397`) and
`IsEnableTarifInPersonnelScreen` (`:410-420`), both return `false` in the same situation. So the
inconsistency is not a convention; it is one setting getting it backwards. Same family as the three
fail-opens `TLW-AUTHORIZATION-MODEL.md` already records.

The rest of the group are "compulsory field" switches — `IsEmployeeContractCompulsory`,
`IsEmploymentTypeCompulsory`, `IsEmployeeLocationCompulsory`, `IsDefaultActivityCompulsory`,
`IsDailyBrowserNotesCompulsory`, `EmployeeHasMandatoryWeekSelection`, five `MandatoryExpense*` —
plus `AllowAbsenceRequestsInPast`, `ManagerCanAuthorizeOwnExpense` (a real segregation-of-duties
control, globally toggled), and `DisableAutoGenerationInVisualPattern` (`SoftwareOptionService.cs:294`).

**These are validation rules stored as configuration.** WM validates nothing conditionally.

---

## 5. Shipped defaults, and their provenance

Defaults were extracted from `ALTER TABLE … ADD … DEFAULT` across
`Database\Versioning\*.sql`, latest-numbered script wins. **36 of 227 recovered this way** — the
remainder were added in DDL forms this extraction does not match (see §7.1).

Non-zero defaults, i.e. the ones where "WM hard-codes something" is a *change*, not an assumption:

| Column | Default | Script |
|---|---|---|
| `NumberOfMinutesForDoubleSwipe` | **2** | `Merge ER2 and TLW\43.V4.0.0.0...sql:8386` |
| `LastInSwipeNumberOfDaysCalc` | **1** | `77.V5.23.0.0.sql` (`DF_LastInSwipeNumberOfDaysCalc`) |
| `LastOutSwipeNumberOfDaysCalc` | **1** | `77.V5.23.0.0.sql` |
| `ShouldOverwriteCostCentres` | **1** | `70.V5.16.0.0.sql` |
| `QrSequentialPunchingEnabled` | **1** | `92.V6.4.0.0.sql` |
| `DefaultDocumentUploadNotification` | **1** | `93.V6.5.0.0.sql` |
| `WeeklyAbsencesPendingDigestEmailEnabled` | **1** | `94.V6.6.0.0.sql` |
| `GlobalNotificationRefreshSeconds` | **60** | `42.V3.6.6.0.sql` |
| `MomentaryOpenDoorDurationSeconds` | **5** | `84.V5.30.0.0.sql` |

Everything else recovered defaults to `0` / `(0)`.

The original 16 columns are seeded to all-zeros by an unconditional insert in every release:
`INSERT INTO [SoftwareMainOptions] (…) VALUES ('',0,0,0,0,0,0,0,0,0,0,'',0,0,0,getdate())`
(`95.V6.7.0.0.sql:90-93`), followed immediately by the duplicate-row delete at `:96`.

**Values WM hard-codes in the same decision space, with no legacy counterpart to check against:**
`DateTimeOffset.UtcNow.AddMinutes(5)` future-punch tolerance (`PunchService.cs:37`),
`AddHours(-18)` stale-punch cutoff (`:133`), `Math.Round(hours, 2)` (`:188`).

---

## 6. Overlaps with plans 008 and 010 — and a third settings table

### 6.1 Plan 008 (per-site time zones)

`SystemTimeZone` is the only installation-wide zone, as 008 recorded (`:70819`). Measured here,
**the rest of the table does not complicate 008 — it complicates it in one specific way**:
`DataCache.Now` (`DataCache.cs:65-72`) uses that single zone to decide *what "today" is for the
entire calculation batch*, and `LoadData` loads one `DataCache` per batch of employees. A per-site
zone means "now" is no longer a property of the run; it is a property of each employee's site.
008's inheritance model must therefore reach into the calculation entry point, not just the read
models. Nothing else in the 227 columns is zone-aware; `LogCleanerTime` (`Time`), `YearStartDate`
and `CompanyStartDate` are all naked local values.

### 6.2 Plan 010 (daily templates)

**No duplication found.** None of `NightShiftEndTime`, `NightShiftStartTime`, `ShiftToSunday`,
`InterswipeIntervalToMoveToYesterday`, `ModelType`, `RoundingRules` or `GlobalScheduleThresholds`
appears among the 227. Only `DailyBrowserViewModeSoftwareOption`,
`DailyBrowserModeSelectDaysSoftwareOption` (screen defaults) and `IsDailyBrowserNotesCompulsory`
touch 010's surface, and those are display/validation, not calculation.

### 6.3 `dbo.Calculations` — the *other* singleton, 45 columns, unsurveyed

`DataCache.LoadData` loads **two** settings singletons side by side:
`CalculationOptions = context.Calculations.Single()` and
`SoftwareOptions = context.SoftwareMainOptions.Single()` (`DataCache.cs:81-83`).

`dbo.Calculations` (`HorioDB.designer.cs:14682`, 45 columns) holds the calculation rules that are
*not* in `SoftwareMainOptions`, and it is where 010's third-copy risk actually lives:

- **Fourteen columns — `MonTueStart/End`, `TueWedStart/End`, `WedThuStart/End`, `ThuFriStart/End`,
  `FriSatStart/End`, `SatSunStart/End`, `SunMonStart/End` — are a per-weekday night-hours band**,
  selected by a seven-arm `switch (clocking.Date.DayOfWeek)` with a
  `default: nightStartTime = nightEndTime = TimeSpan.Zero` arm, and a wrap rule
  `if (nightStartTime >= nightEndTime) nightEndTime = nightEndTime.Add(TimeSpan.FromDays(1))`
  (`DataCache.cs:207-268`). This is a **third** representation of night-shift boundaries alongside
  the two plan 010 measured, and its precedence against them is not recorded anywhere.
- `RoundMinutes` — a **global** rounding grain, next to `DailyModels`' per-template rounding and
  `RoundingRules`. Three copies, no recorded precedence.
- `WeekStartsOn` (`DayOfWeek`) — cached statically per connection string
  (`WeeklyShiftModelService.cs:17,252-260`), read by payroll exports, accruals, contracts, personnel
  and the hours engine. Falls back to `DayOfWeek.Monday` when the row is missing
  (`EmployeesImportService.cs:1714`).
- `CalculationException` (enum) — `NetPresence0Ecart0` / `NetPresence0EcartTheoretic` /
  `NetPresenceTheoreticEcart0`: **what a day is worth when calculation cannot be performed**
  (`HoursCalculationService.cs:802`, `:2684-2696`). Direct answer to "what happens with absent data".
- `IsManageSQL` — an **escape hatch** flag; `IsCalculateNightHours`,
  `IsCalculateNightHoursOnlyIfModelByNight`, `NoModelHoursCounter`, `NoModelEcartCounter`,
  `HolidayHoursCounter`, `HolidayEcartCounter`, `FinishTheCurrentGap`, `RepeatabilityTreat`,
  `AutoBalanceReset`+`Month`/`Day`/`Value`, `BreakDelay`, `BreakBack`, `IsBreaks`.

**`Calculations` is denser in rules per column than `SoftwareMainOptions` is.** It should be
surveyed before, or with, any global-settings build.

---

## 7. What I did **not** measure

1. **Defaults for 191 of 227 columns.** The extraction matches only
   `ALTER TABLE … SoftwareMainOptions … ADD <col> <type> [NOT NULL] DEFAULT (v)`. Columns added by
   `1_horio_structure.sql`'s original `CREATE TABLE`, by a separate
   `ALTER TABLE … ADD CONSTRAINT … DEFAULT`, or by the ER2 merge scripts, were not recovered.
   The nine non-zero defaults in §5 are what the method found; there may be more.
2. **`WeekNoCalculationMethod` is unresolved.** No reader found outside the settings screen, but it
   is an `int NULL` with a name implying ISO-vs-other. I did not check the RDL reports or the
   `EmployeeSchedulingPortal` / `HealthWebSite` sub-applications for a reader, and my T-SQL
   read-detection is line-based (a `SELECT @x = Col` on one line and `FROM SoftwareMainOptions` on
   the next would be missed). **Do not delete it on my say-so.**
3. **The 18 `DEAD` columns are "no reader found", not "provably dead".** The same method wrongly
   condemned four columns before I added the T-SQL tier (§2.1). What I checked: all of
   `Source\**` (16,697 files) and all `Database\**\*.sql` (571 files). What I did not: compiled
   binaries, TeamCity artefacts, the Azure and Docker trees, and any reader that builds a column
   name as a string at runtime.
4. **Per-customer values.** No live database was read. Every "default" here is the *shipped*
   default; what customers actually have set is unknown, and for a 20-year product that gap
   matters — a setting whose default is 0 may be 1 on every real install.
5. **`dbo.Calculations` is named and sampled, not surveyed.** §6.3 names 25 of its 45 columns from
   the schema and reads four call sites. The other 20 columns are unexamined.
6. **The mobile group (48 columns) was classified but not traced.** I did not verify that each
   `Qr*` toggle has a reader; the group is classified by name and by the settings screen's
   `Processing`/`WorkFromHome` tabs, not by call site.
7. **The settings screen's own permissions.** Twelve tabs each with a view and an edit permission
   (`SoftwareOptionController.cs:409-433`), audited to `AuditTrailLogsTables.SoftwareMainOptions`
   (`:1152`, `:1169`). I recorded the tab names but did not map which of the 227 columns sits on
   which tab — that mapping is what a WM settings screen would need.
8. **`CustomHomePage` (`NVarChar(MAX)`)** is an escape hatch — arbitrary stored markup served as the
   home page (`SoftwareOptionService.cs:586-589`). I did not look for examples of what customers
   put in it, which is the measure of the expressive power WM would have to replace.

---

## 8. Corrections made to WM's records

- `docs/TLW-CLOCKING-MODEL.md:30` — "global settings — WM has no equivalent" now points here.
- `docs/TLW-SCHEMA-SWEEP.md` §4 — hypothesis replaced by measurement; the claim that
  `SkipReprocessingAfterEachSwipe` is "recalculation control" is right but it omitted that the
  control lives in T-SQL, and §4 listed `NumberOfDaysToKeepLogs` / `RequireFullLogging` as
  live "retention" when both are dead.
- Plan numbering: the brief said to take **011**; `docs/plans/011-production-readiness.md` already
  exists and is in flight. This survey's plan is **012**.
