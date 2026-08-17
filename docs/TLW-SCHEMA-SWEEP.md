# TLW schema sweep — one trip over every table

*Measured 2026-08-04 from `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs`.*
*Companion to [`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md); corrects [`TLW-INVENTORY.md`](./TLW-INVENTORY.md).*

> **Why.** Earlier surveys planned from `TLW-INVENTORY.md` and §13 rather than from the schema,
> and both were wrong — the inventory undercounted tables by half, and §13 marked the product's
> central entity as built. This document is the measurement that should have come first, done
> once over everything so the remaining gaps are known rather than discovered one at a time.

## Method (reproducible)

```powershell
$f='E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs'; $lines=Get-Content $f -Encoding utf8
$cur=$null; $counts=@{}
foreach ($l in $lines) {
  if ($l -match 'TableAttribute\(Name="([^"]+)"') { $cur=$matches[1]; if(-not $counts.ContainsKey($cur)){$counts[$cur]=0} }
  elseif ($cur -and $l -match 'ColumnAttribute\(') { $counts[$cur]++ }
}
$counts.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 40
```

**578 tables · 8,173 columns · 1,147 associations. 39 tables have ≥40 columns.**

---

## 1. Headline: the 20-counter ceiling is systemic

`CPTN01..CPTN20` is not a quirk of one table. The same fixed 20 slots are repeated across
**13 tables and views**:

| Table | Family |
|---|---|
| `dbo.Clockings` | `CPTN01..20` |
| `dbo.ClockingsLog` | `CPTN01..20` |
| `dbo.ClockingActivities` | `CPTN01..20` |
| `dbo.CostCentreCounters` | `CPTN01..20` |
| `dbo.DailyBrowserView` | `CPTN01..20` |
| `dbo.DailyActivityRecordsView` | `CPTN01..20` |
| `dbo.DailyClockingActivityRecordsView` | `CPTN01..20` |
| `dbo.ClockingsWithAdditionalBalancesView` | `CPTN01..20` |
| `dbo.UnifiedTimesheetReportView` | `Counter1..20` |
| `dbo.UnifiedActivitiesReportView` | `Counter1..20` |
| `dbo.UnifiedJobCostingReportView` | `Counter1..20` |
| **`dbo.TariffValues`** | **`CounterRate1..20`** |
| **`dbo.TariffValues`** | **`CounterChargeRate1..20`** |

Raising the ceiling in legacy means altering 13 tables, every view built on them, the hard-coded
`switch` in `BaseCountersHolder`, and every report and payroll plugin that reads a `Counter*`
column. **This is the strongest argument yet for normalising in WM** (plan 002's open question):
the cost of the fixed shape is not one table, it is a design that propagates through the whole
read surface.

### Every numbered-slot family ≥10 slots

65 families of ≥4 slots exist. The significant ones:

| Slots | Table | Family |
|---|---|---|
| 20 | 13 tables above | counters / rates |
| 12 | `Clockings`, `ClockingsLog`, `DailyBrowserView`, `ClockingReportView` | `BadgeTime1..12` |
| 12 | `Clockings`, `ClockingsLog` | `DeviceBadgeTime1..12` |
| 12 | `Clockings` | `SwipeSource`, `SwipeSourceId`, `SwipeReaderType` |
| 12 | `dbo.ClockingSwipeWorkLocations` | `SwipeWorkLocation1..12` |
| 10–12 | `ManualTimesheets`, `ManualTimeSheetsView` | `BadgeTime1..12` |
| 6 | `Clockings`, `DailyBrowserView`, unified views | per-shift measures, `CostCentreId1..6`, `Out1..6` |

Three hard ceilings, all customer-visible: **20 pay categories, 12 swipes per day, 6 shifts.**

---

## 1b. The escape hatch is **five** surfaces, not one

`ARCHITECTURE.md` §14 decision 6 plans to replace "per-template custom SQL" with a safe rules
expression language. The schema shows customer-authored executable code lives in **five distinct
places**, and the rules language only replaces the first:

| Surface | Column(s) | What it means |
|---|---|---|
| Daily template | `DailyModels.CustomSql` (+ `CustomSqlForNoRecalculating`, `ShouldCustomSqlReturnMessage`, `RecalculateAfterCustomSql`) | the known one — runs during calculation, mutates the clocking |
| Flexi balance | `FlexiBalances.SqlStatement` | balance logic as SQL (own doc: `Custom SQL\Flexi balance SQL.md`) |
| **Notifications** | `Notifications.SqlQuery` | **the notification hub is SQL-driven** — the "~70 notification types" are at least partly SQL definitions, not code |
| **Payroll export** | `PayrollExportSettings.TSQLText` | **exports carry raw T-SQL** — part of the 44-plugin story is per-customer SQL, not per-customer C# |
| **Counter formulas** | `Formula`, `CostCenterFormula`, `WorkActivityFormula` on `EmployeeWeeklyCounters`, `EmployeeMonthlyCounters`, `EmployeeAnnualCounters` and their `EmployeeContract*` twins | a **separate formula language** (TLW `FormulaEvaluator`), not SQL |

Consequences:

- **The rules expression language is scoped too narrowly.** It must cover the counter *formula*
  language too — which is a different language from the SQL hatch, evaluated per counter, with
  cost-centre and work-activity variants.
- **Notifications are not purely event-driven in legacy.** If ~70 notification types are defined
  by `SqlQuery`, WM's event-driven hub (§9) is a *behaviour change*, not a port. That needs
  deciding explicitly rather than discovering during Phase 4.
- **Payroll export needs a SQL-authoring story or a migration path.** §14's "generic export
  builder" has to absorb `TSQLText`, or existing customers cannot move.
- Each surface duplicates the same problems: unauditable, unreplayable, injection-prone. Solving
  it once generically is worth more than four separate escape hatches.

---

## 2. `dbo.TariffValues` — the hours→money path WM has nothing for

44 columns: `Id`, `TariffId`, `DateFrom`, `DateTo`, `CounterRate1..20`, `CounterChargeRate1..20`.

- A **tariff is a date-effective set of 20 pay rates plus 20 charge rates**, one pair per pay
  category. Pay category *N* ↔ rate *N* ↔ charge rate *N*, positionally.
- `DateFrom`/`DateTo` make rates **historically versioned** — recalculating an old period must use
  the rate that applied *then*, not today's.
- `Clockings.HourlyRateId` links a day to its tariff.
- Two rates per counter = **pay rate vs charge rate** — what the employee is paid vs what the
  client is charged. That is the job-costing/billing distinction, and nothing in WM models it.

`dbo.TariffService.cs` is 32 KB — the largest settings service in the product.

---

## 3. `dbo.Calculations` (45 cols) — global calculation settings

A singleton nobody has surveyed. Contents that change every computed result:

- `WeekStartsOn`, `NumberOfDaysInAdvancedCalendar` (**how far ahead clockings are pre-generated**),
  `CalculationEndDateInclusive`, `NumberOfDaysBeforeArchiving`.
- **A day-boundary matrix**: `MonTueStart/End`, `TueWedStart/End`, … `SunMonStart/End` — eight
  weekday-pair windows deciding where the boundary between two days falls. This is the
  night-shift edge case, configured per weekday transition.
- `RoundMinutes`, `RoundInMonthlyRecord`, `IsCalculateNightHours`,
  `IsCalculateNightHoursOnlyIfModelByNight`, `BreakDelay`, `BreakBack`, `IsBreaks`.
- Counter assignments for special cases: `NoModelHoursCounter`, `NoModelEcartCounter`,
  `HolidayHoursCounter`, `HolidayEcartCounter` — **which pay category absorbs hours when there is
  no template, or on a holiday.** Direct answer to "what happens when data is absent".
- `IsGenerateAnomalyAndStopCalculation`, `FinishTheCurrentGap`, `RepeatabilityTreat`,
  `IsManageSQL`, `CalculationException`.
- `AutoBalanceReset` + `Month`/`Day`/`Value` — annual balance reset.

---

## 4. `dbo.SoftwareMainOptions` (227 cols) — per-install configuration

The second-largest unsurveyed surface. Not "settings" in a trivial sense — it carries behaviour:

- **Period locking**: `BlockCalculationBeforeDate`.
- **Recalculation control**: `ReboundClockingsOnStartup`, `MakeGeneralCalculationOnStartup`,
  `CalculateFormulaSummaryOnStartup`, `SkipReprocessingAfterEachSwipe`,
  `SkipNetHoursCalculationForFutureDays`, `ShouldOverrideClockingValues`.
- **QR punching is a whole feature set**: `UseQrScanning`, `QrInOutEnabled`,
  `QrSequentialPunchingEnabled`, `QrCheckInOutEnabled`, `QrBreaksEnabled`, `QrActivitiesEnabled`,
  `QrExpensesEnabled`, `QrDocumentsEnabled`, `QrAbsencesEnabled`, `QrStatisticsEnabled`,
  `QrSwipeRadius`, `QrProcessSwipeWithoutGeo` — 12 toggles. WM plans "QR punching" as one line.
- **Temporal**: `SystemTimeZone`, `CompanyStartDate`, `YearStartDate`, `WeekNoCalculationMethod`.
  **Measured 2026-08-14:** `SystemTimeZone` (`NVarChar(250)`, `:70819`) is the **only** geographic
  time zone in the product outside `Devices.TimeZoneCode` — there is none on `Locations` (4 cols),
  `Buildings` (5), `ClientSites` (12), `Employees` (153) or `[User]` (30). It is a **Windows** id,
  it is a singleton (`.Single()`), and it silently falls back to the server's clock in **seven**
  places when unset. Across the whole model: `time` **577** · `datetime` **457** · `date` **146** ·
  `datetimeoffset` **6**, and none of the six is attendance —
  [`TLW-TIME-MODEL.md`](./TLW-TIME-MODEL.md).
- **Retention/audit**: `NumberOfDaysToKeepLogs`, `RequireFullLogging`.
- **Policy**: `IsEmployeeContractCompulsory`, `AllowAbsenceRequestsInPast`, `AllowedIps`.
- Display defaults: `ShowNonActiveByDefault`, `ShowFiredByDefault`, `PageSize`, …

WM's "everything is a licensable feature" covers *whether* a module is sold, not *how* an install
behaves. These are different axes and WM currently models neither.

---

## 5. Other tables worth naming

| Table | Cols | Note |
|---|---|---|
| `dbo.WorkActivities` | 53 | job costing (phase 7) |
| `dbo.ClockingActivities` | 53 | activity time per clocking — carries its own `CPTN01..20` |
| `dbo.VisitorSettings` | 51 | visitors (phase 8) |
| `dbo.ManualTimesheets` | 41 | manual entry, own `BadgeTime1..12` |
| `dbo.Absence` | 35 | absence definitions |
| `dbo.DailyModelBreaks` | 34 | break rules per template |
| `dbo.CostCentreCounters` | 23 | `ClockingId` + `CostCentreId` + `CPTN01..20` — **counters split per cost centre** |
| `dbo.ClockingsLog` | 97 | full change audit of clockings |
| `dbo.[User]` | 30 | WM's is comparable |
| `dbo.FireMarshalMusterPoints` | 177 | phase 6b — far larger than "muster points" suggests |
| `dbo.SimpleTimesheetsReportHost` | 54 | report host tables — a whole reporting pattern |

Roughly **20 `Unified*ReportView` views** (25–167 cols each) exist. They are the reporting layer's
denormalised read models — evidence the AI-assistant/semantic-layer decision (§16) is replacing
something substantial and well-defined, and a good source for what the semantic model must cover.

## Project buckets (317 top-level)

~85 core/other · 43 payroll plugins · 25 service projects · 23 integration/import-export ·
22 reporting · 18 web/UI · 18 test · 15 device/comms (dropped) · 4 licensing · 4 EPOS (dropped).
Broadly consistent with the inventory; projects were not the blind spot — the schema was.

---

## 6. Gaps this sweep adds to the record

Nothing below exists in WM in any form.

| Missing | Where it belongs | Suggested |
|---|---|---|
| `Clockings` daily aggregate | TimeAttendance + Rules | **plan 002** (written) |
| Pay categories / counters (normalised) | Rules | plan 002 |
| **Tariffs + rate/charge-rate pairs, date-versioned** | Rules | **plan 003** |
| **Global calculation settings (`Calculations`)** | Rules | **plan 003** |
| **Per-install options (`SoftwareMainOptions`)** | Admin | **plan 004** |
| Employee contracts + thresholds + `…Effective` resolution | People/Rules | plan 003 |
| Cost-centre counter split (`CostCentreCounters`) | Rules | plan 002/003 |
| Clocking change audit (`ClockingsLog`) | Admin + `wm.audit` | later |
| Manual timesheets | TimeAttendance | later |
| Day-boundary matrix (8 weekday-pair windows) | Rules | plan 003 |
| QR punching as 12 configurable behaviours | TimeAttendance | later |
| **Counter formula language** (`Formula`/`CostCenterFormula`/`WorkActivityFormula`) | Rules | **plan 003** |
| **SQL-defined notifications** (`Notifications.SqlQuery`) | Notifications | decide before Phase 4 |
| **Payroll export T-SQL** (`PayrollExportSettings.TSQLText`) | Payroll export builder | decide before Phase 6 |

**Recommended next plans:** 003 — Tariffs, contracts and calculation settings (the money path and
its inputs); 004 — per-install configuration. Both are Phase-2 prerequisites in the same way the
Clocking aggregate is, and both were invisible until the schema was measured.
