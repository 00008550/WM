# 012 — The rules behind the settings

Status: draft
Roadmap: ARCHITECTURE.md §14 — "Per-install options (`SoftwareMainOptions`, 227 cols) | Admin | ▢ not started"
(`ARCHITECTURE.md:398`)

> **Plan number.** The brief said "take 011"; `011-production-readiness.md` already exists and is in
> flight, so this is **012**.

Legacy sources surveyed (files actually opened):

- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — `dbo.SoftwareMainOptions` at `:69348`,
  **227 columns** extracted `:70279`–`:74807`; `dbo.Calculations` at `:14682`, **45 columns**
- `E:\Tlw\Source\Logic\Settings\SoftwareOptionService.cs` (630 lines, read in full in parts)
- `E:\Tlw\Source\Logic\HoursCalculation\HoursCalculationService.cs` (§§ around `:316`, `:535-600`,
  `:802`, `:1561-1580`, `:2684-2696`, `:3905-3925`, `:4130-4170`, `:4855-4890`, `:4960-4980`)
- `E:\Tlw\Source\Logic\HoursCalculation\DataCache.cs:52-95`, `:195-268`
- `E:\Tlw\Source\Logic\HoursCalculation\HoursCalculationServiceUtils.cs` (call sites only)
- `E:\Tlw\Source\WebSite\Controllers\SoftwareOptionController.cs:396-450`, `:800-1000`, `:1150-1170`
- `E:\Tlw\Source\Logic\ExternalAccess\KioskExternalAccessService.cs:646`
- `E:\Tlw\Database\Versioning\95.V6.7.0.0.sql:80-100`; `73.V5.19.0.0.sql:115-116`;
  `75.V5.21.0.0.sql:868`; `77.V5.23.0.0.sql`; `Merge ER2 and TLW\43.V4.0.0.0.Pre_UpgradeEduregDb.sql:8209-8386`
- `E:\Tlw\Documentation\Next Transaction Ends Previous Activity.md`

Full measurement: **[`docs/TLW-GLOBAL-OPTIONS.md`](../TLW-GLOBAL-OPTIONS.md)**.

---

## Ground truth

| Fact | Value |
|---|---|
| `dbo.SoftwareMainOptions` | **227 columns**, 3rd widest in the estate |
| Cardinality | singleton — one row per database; **no tenant/site key** |
| Enforcement | `.Single()` + a validator + a `delete … where Id > …` repair shipped in every release |
| Classification | 227 assigned, **0 unclassified, 0 duplicated, 0 phantom** (asserted by set difference) |
| **Rules columns** | **47** — `RULES-CALC` 10, `RULES-SWIPE` 11, `RULES-POLICY` 15, `RULES-ACTIVITY` 7, `RULES-TEMPORAL` 4 |
| Dead columns | **18** (no reader found in 16,697 source files or 571 SQL files) |
| Out of scope by prior decision | `DEVICES-DROPPED` 25 |
| `dbo.Calculations` | a **second** settings singleton, 45 columns, denser in rules, unsurveyed |

**What WM has today: nothing.** There is no settings entity, service, table or endpoint in
`src/**`. Where legacy has a customer-tunable value, WM has a literal:

| WM literal | File | Legacy's tunable |
|---|---|---|
| `DateTimeOffset.UtcNow.AddMinutes(5)` future-punch tolerance | `PunchService.cs:37` | none — WM invented this |
| `AddHours(-18)` stale-punch cutoff | `PunchService.cs:133` | `LastInSwipeNumberOfDaysCalc` / `LastOutSwipeNumberOfDaysCalc`, default **1 day** each |
| `Math.Round(hours, 2)` | `PunchService.cs:188,196` | `IsDecimal` (HH:MM vs decimal), 75 references |
| every punch accepted | `PunchService.cs` | `NumberOfMinutesForDoubleSwipe`, default **2 min** |
| `DateOnly.FromDateTime(DateTime.UtcNow)` = "today" | `PunchService.cs:109,142` | `SystemTimeZone` → `DataCache.Now` |

### Corrections made to WM's records

- `docs/TLW-CLOCKING-MODEL.md:30` and `:216` — the bare "global settings — WM has no equivalent"
  replaced with the measurement and a pointer.
- `docs/TLW-SCHEMA-SWEEP.md` §4 — a correction banner: `NumberOfDaysToKeepLogs` and
  `RequireFullLogging` were listed as live retention settings and **both are dead**; and
  `SkipReprocessingAfterEachSwipe` is live **in T-SQL**, which the section implied was C#.
- New document `docs/TLW-GLOBAL-OPTIONS.md`.
- **Not touched, as instructed:** `ARCHITECTURE.md`, `docs/plans/STATE.md` (PR #65 open).
  Proposed edits for both are in *Open questions*.

---

## Legacy behaviour (what we are replacing)

**One row, whole-row, per calculation run.** `SELECT * FROM SoftwareMainOptions`
(`SoftwareOptionService.cs:25`), loaded into `DataCache` next to `Calculations`
(`DataCache.cs:81-83`) and handed to the engine as one object.

**The punch pipeline is stored procedures.** `SkipReprocessingAfterEachSwipe`,
`NumberOfMinutesForDoubleSwipe`, `NextSwipeEndsCurrentActivity` and `FirstActivityStartAsClockIn`
are read only from T-SQL (`73.V5.19.0.0.sql:115-116`, `75.V5.21.0.0.sql:868`). This is the single
most important structural fact for anyone porting swipe→clocking allocation.

**Three caches, one uninvalidatable.** `_mainOption` is per-instance, cleared on write
(`:53,66,569`); `TimeZones` is `static` with **no invalidation path** (`:27`, `:611-624`) —
changing the system time zone requires a process restart; `WeekStartsOn` is likewise a static
per-connection-string dictionary (`WeeklyShiftModelService.cs:17`).

**A read failure becomes silent defaults.** `GetOptionsAsCache` catches, logs `Fatal`, and returns
`new SoftwareMainOption()` — every `bool` `false` (`:544-553`).

**One fail-open.** `IsAllowAccessAllocation()` returns `true` when the row is missing
(`:399-408`), while its two sibling helpers return `false` in the same case (`:387-397`, `:410-420`).

---

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Installation-scoped configuration existing at all | **Keep** | 47 columns of it are genuine domain truth. Customers really do differ on rounding, dedup, day boundaries and what is mandatory. WM shipping literals is the gap the pause was called for |
| One 227-column singleton row | **Improve** | a settings *namespace* with typed, individually-versioned, individually-audited entries. A wide row cannot record who changed which setting when — legacy compensates with `AuditTrailLogsTables.SoftwareMainOptions` diffing whole rows (`SoftwareOptionController.cs:1152`) |
| Configuration as a plain bag of scalars | **Improve** | separate **rules** (47) from **preferences** (the rest). Rules belong to the calculation module and must be *effective-dated*: changing `NumberOfMinutesForDoubleSwipe` today must not silently rewrite last month's clockings on the next replay. Legacy has no such notion — this is where WM's replay design earns its keep |
| No tenant/site key; tenancy by separate database | **Improve** | WM has never settled multi-tenancy. Model settings with an explicit resolution scope — `global → tenant → site` — with a recorded precedence, rather than inheriting "a database is a tenant" |
| `IsAllowAccessAllocation()` returning `true` when unconfigured | **Invert** | an access control that fails open. Absent configuration must deny |
| `GetOptionsAsCache` swallowing a DB error into all-`false` defaults | **Invert** | a settings read failure must fail the request, not silently change behaviour. All-`false` is *not* a safe default: it turns `EnableCounterHoursCapping` off and `ShouldOverrideClockingValues` off, silently changing every calculation |
| `MoveOpenBreakToLastSwipe`'s hard-coded "first four swipes" (`:1571-1580`) | **Improve** | a `BadgeTime1..12` fixed-slot artefact. WM's punch stream has no slots; express the rule as "the open interval", not "within the first four" |
| Static, never-invalidated `TimeZones` cache | **Invert** | a setting you cannot change without a restart is not a setting |
| Boolean feature toggles (`MOBILE` 48, `EXPENSES` 6, parts of `COSMETIC-UI`) | **Improve** | these are **licensing/feature-package** concerns, not settings. CLAUDE.md §6 and the licensing module already own "is this sold". Do not build 48 booleans |
| `DEVICES-DROPPED` (25) | **Drop** | CLAUDE.md invariant 3, decision 2026-07-20 |
| `DEAD` (18) | **Drop** | but see *Edge cases* — "no reader found" already condemned four columns wrongly once |
| `INFRA-IMPORT` (9: `ImportFormat`, `IgnoreLines`, `Synchronization`, `EndImport`, `SalFolder`, `HorioSyServerDllPath`, `ApplicationUrl`, `ProductVersion`) | **Drop** | deployment configuration belongs in `appsettings`/environment, not a database row |
| `CustomHomePage` (`NVarChar(MAX)` of stored markup) | **Drop for now, record** | an escape hatch. Replacing it needs to know what customers put in it, which was not measured |
| `dbo.Calculations` (45 cols) | **Keep — and survey it** | denser in rules than this table. `CalculationException`, the 14 per-weekday night bands, `RoundMinutes`, `WeekStartsOn` |

---

## Edge cases

Every one of these is a decision WM currently makes implicitly.

1. **Absent configuration.** Legacy: `.Single()` throws, three helpers disagree
   (`false`, `false`, **`true`**), and `GetOptionsAsCache` substitutes all-defaults on any exception.
   WM: a missing setting must resolve to a **declared** default, and the resolution must be
   auditable. No silent `false`.
2. **The double-swipe boundary.** `NumberOfMinutesForDoubleSwipe` default **2**. Given two punches
   at 09:00:00 and 09:01:59 → second discarded. At 09:02:00 → **boundary; legacy's
   `dbo.ProcessQueryIsDoubleSwipe` decides inclusivity and I did not read its body.** Test both
   sides and record which we chose.
3. **Night lookback.** `LastInSwipeNumberOfDaysCalc` = 1. A punch out at 02:00 on Tuesday matched
   against Monday's in-punch. WM's `AddHours(-18)` gives a *different* answer for a shift starting
   before 08:00. Test 22:00→06:00 and 03:00→11:00.
4. **Retroactive setting change.** Change `NumberOfMinutesForDoubleSwipe` from 2 to 10 today, then
   replay last month. Legacy silently recalculates with the new value. **Does WM?** This is the
   ordering/re-entrancy question and the answer must be explicit, tested, and the same one
   `ShouldOverrideClockingValues` raises (`HoursCalculationService.cs:541-559`).
5. **Calculation floor.** `BlockCalculationBeforeDate` **silently clamps `fromDate` upward**
   (`:4873-4877`) rather than rejecting the request. A caller asking for January gets February and
   is not told. WM should refuse or report the clamp.
6. **Two floors, not one.** `BlockCalculationBeforeDate` and `LockDataBeforeDate` are separate
   columns with separate meanings. Which wins when both are set is not recorded in legacy. Decide
   and test.
7. **Time-zone change with a warm process.** Legacy: no effect until restart (`:611-624`).
   WM: must take effect on the next read, and the test must prove it.
8. **`DataCache.Now` when `SystemTimeZone` is empty** falls through to the server clock
   (`DataCache.cs:69-71`); conversion failure returns `null` from a silent `catch` (`:376-382`).
   Two distinct silent fallbacks in the definition of "today".
9. **`switch (clocking.Date.DayOfWeek)` default arm** in `GetNightTime` sets both bounds to
   `TimeSpan.Zero` (`DataCache.cs:257-262`) — a zero-width night band, i.e. no night hours, from a
   branch that should be unreachable.
10. **Concurrent settings edit.** Legacy: last writer wins on a whole 227-column row, so two admins
    on different tabs silently overwrite each other. Plan 011 P5 already established WM's answer
    (409 on concurrent edit); settings must use it.

Documented Given/When/Then worth lifting verbatim, from
`E:\Tlw\Documentation\Next Transaction Ends Previous Activity.md` (setting
`NextSwipeEndsCurrentActivity` enabled):

> Employee swiped IN at 09:01 → SWF creates IN swipe at 09:01
> Employee started activity Installation at 09:10 → SWF creates activity Installation started at 09:10
> Employee started Lunch break at 12:00 → SWF closes activity Installation with end time 12:00; SWF creates Lunch break started at 12:00

(The doc's later cases are image-only screenshots; the text cases above are the machine-readable part.)

---

## Target design in WM

A **Settings** capability owned by a new `Admin` module (ARCHITECTURE.md §14 already assigns
`SoftwareMainOptions` to "Admin"), with the rules subset **projected as a contract** that
TimeAttendance consumes — never a cross-module DB read (CLAUDE.md invariant 1).

- **Storage:** `admin.settings` — `(key, scope, scope_id, value_json, effective_from, version, updated_by, updated_at)`.
  Not 227 columns. Not a JSON blob either: one row per setting so audit and effective-dating are
  per-setting.
- **Registry:** settings are *declared* in code (key, type, default, scope levels, permission,
  whether it is replay-affecting), not implied by a column existing. The registry is the thing that
  makes "0 unclassified" true by construction next time.
- **Contract:** `IRulesSettings` in `SharedKernel` or an `Admin.Contracts` package, resolved for a
  given `(site, instant)`. TimeAttendance asks for `PunchDedupeWindow`, not for a table.
- **Events:** `SettingChanged` on Kafka (CLAUDE.md invariant 4) so caches invalidate — the answer to
  legacy's restart-required time zone.
- **Endpoints:** `GET/PUT /api/admin/settings`, `GET /api/admin/settings/{key}`, each behind an
  authorization policy (CLAUDE.md invariant 5). Legacy's twelve view/edit permission pairs
  (`SoftwareOptionController.cs:409-433`) are the precedent for per-group permissions.
- **Screen:** an Admin → Settings page, grouped, not a 227-field form.
- **Tests:** `WM.Modules.Admin.Tests` — new project. TimeAttendance still has **no test project**
  (CLAUDE.md); P3 must create `WM.Modules.TimeAttendance.Tests` and wire it into `WM.sln`.

---

## Out of scope for this plan

- The 48 `MOBILE` toggles — those are licensing/feature-package, not settings.
- All 25 `DEVICES-DROPPED` columns.
- All 17 `FIRE-EMERGENCY` columns (phase 6b).
- The 6 `EXPENSES` and 10 `NOTIFICATIONS` columns — they belong with their own modules.
- The 25 `COSMETIC-UI` columns — user preferences, a later plan.
- `dbo.Calculations` (45 cols) — needs its own survey; named here so it is not forgotten.
- Multi-tenancy itself. This plan *models* a scope hierarchy; it does not decide WM's tenancy.

---

## Portions

### [ ] P1 — A setting exists, and is declared
**Touches:** new `src/Modules/Admin/WM.Modules.Admin/` (module, `AdminDbContext`, `Setting` entity,
initial migration, `SettingRegistry`), `WM.sln`, `src/Api` composition.
**Done when:** the app runs with an `admin.settings` table; a hard-coded registry declares
**exactly the 10 `RULES-CALC` settings** with their measured legacy defaults; `GET /api/admin/settings`
returns them with their defaults and no row required in the table.
**Tests:** new `WM.Modules.Admin.Tests` — registry declares 10 keys, no duplicates; a key with no
stored row resolves to its declared default; an unknown key is a 404, **not** a silent default.
**Risk:** medium (new module, new migration).

### [ ] P2 — A setting can be changed, safely
**Touches:** `SettingsService`, `PUT /api/admin/settings/{key}`, authorization policy, audit entry,
`SettingChanged` contract.
**Done when:** an authorized user changes a value; an unauthorized one gets 403; a concurrent edit
gets **409** (matching plan 011 P5, not last-writer-wins); the change is audited with actor and
old/new value; a type-invalid value is rejected with 400.
**Tests:** 403, 409, 400, audit row written, `SettingChanged` published. Explicitly test that a
settings **read failure surfaces as an error** and does not fall back to defaults (inverting
`SoftwareOptionService.cs:544-553`).
**Risk:** medium.

### [ ] P3 — The punch pipeline stops hard-coding its rules
**Touches:** `IRulesSettings` contract, `PunchService.cs`, new `WM.Modules.TimeAttendance.Tests`.
**Done when:** `PunchDedupeWindowMinutes` (default **2**, from `43…sql:8386`) and
`PunchLookbackDays` (default **1**, from `77.V5.23.0.0.sql`) are read from settings; the literals
`AddMinutes(5)` and `AddHours(-18)` are either replaced by declared settings or documented in code
as deliberate WM choices with no legacy counterpart. A second punch inside the window is refused.
**Tests:** the edge cases above — 09:00:00/09:01:59 discarded, 09:02:00 boundary (both sides,
chosen behaviour recorded); 22:00→06:00 and 03:00→11:00 lookback; changing the window mid-test
changes behaviour on the next punch without a restart.
**Risk:** high — this is the first time WM's punch behaviour becomes configurable, and
TimeAttendance has no existing tests to protect it.

### [ ] P4 — "Today" stops being UTC by accident
**Touches:** `IRulesSettings`, `PunchService` "today" call sites (`:109`, `:142`),
`TimeAttendanceModule.cs:60,77`, `PeopleModule.cs:38`.
**Done when:** a `SystemTimeZone` setting exists (IANA, **not** legacy's Windows id) and is the
default for "today"; per-site override is *declared in the scope model* and deferred to plan 008 —
this portion does not implement site resolution, it stops the hard-coded UTC.
**Tests:** an install in `Europe/London` sees a 23:30 UTC punch as the following day; an empty/
invalid zone value **fails validation at write time** rather than silently falling back to the
server clock (inverting `DataCache.cs:69-71` and `SoftwareOptionService.cs:376-382`).
**Risk:** high — touches three modules' notion of "today". Coordinate with plan 008.

### [ ] P5 — Settings screen
**Touches:** `frontend/portal` Admin → Settings page + its first `*.spec.ts`.
**Done when:** the declared settings render grouped by their registry group, with type-appropriate
inputs, a per-group permission check, and a 409 surfaced as a real message rather than a lost edit.
**Tests:** first frontend specs for this area — render, permission-hidden group, 409 handling.
**Risk:** low.

---

## Open questions for the user

1. **Effective-dating of rules settings — the product call.** Should changing
   `PunchDedupeWindowMinutes` today alter what a replay of last month produces? Legacy says yes
   (silently). WM's replay design argues for no. **This decides P1's schema** (whether
   `effective_from` is real or ornamental) and I should not decide it.
2. **Scope model.** I propose `global → tenant → site` with most-specific-wins. WM has never
   settled tenancy; if the answer is "one deployment per customer", the `tenant` level is dead
   weight and should not be built. Which is it?
3. **Proposed `ARCHITECTURE.md` edit** (not applied — PR #65 is open). At `:398`:
   ```diff
   -| **Per-install options (`SoftwareMainOptions`, 227 cols)** | Admin | ▢ **not started** — period locking, recalc control, QR toggles |
   +| **Per-install options (`SoftwareMainOptions`, 227 cols)** | Admin | ▢ **not started.** Measured 2026-08-30 (`TLW-GLOBAL-OPTIONS.md`): 227 classified, **47 are calculation/swipe rules**, 18 dead, 25 dropped with devices, 48 are mobile feature toggles that belong to licensing. Plan **012** |
   +| **Global calculation settings (`dbo.Calculations`, 45 cols)** | Rules | ▢ **not surveyed** — 14 per-weekday night bands, `RoundMinutes`, `WeekStartsOn`, `CalculationException`, `IsManageSQL`. Denser in rules than `SoftwareMainOptions`. **Needs a survey before 012 P3** |
   ```
4. **Proposed `STATE.md` edit** (not applied): add `012-the-rules-behind-the-settings.md` as
   `draft` to the queue, and note that the plan-011 slot the brief referred to is occupied.
5. **Should `dbo.Calculations` be surveyed before 012 is approved?** My honest reading is **yes**.
   Its 14 per-weekday night-band columns are a third representation of night-shift boundaries
   alongside the two plan 010 measured, with no recorded precedence, and `CalculationException`
   answers "what is a day worth when we cannot calculate it" — a question WM has not asked. 012 P3
   can proceed without it; 012's *scope* may be wrong without it.
6. **The 18 dead columns.** I recommend dropping them, but my method wrongly condemned four columns
   before I added the T-SQL tier. Is "no reader found in 16,697 source files and 571 SQL scripts"
   enough, or do you want a live-database value survey first?
