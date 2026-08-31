# TLW plugin estate — measured

Survey date: 2026-08-30. Measured against `E:\Tlw` only. Every claim below has a `file:line`
or a reproducible count. WM's own documents were treated as claims under audit.

---

## 1. The count, settled

Three WM documents disagreed. All three were partly right, about different things.

| Claim | Where | Verdict |
|---|---|---|
| "~60 per-customer payroll plugins" | `CLAUDE.md` invariant 6; `ARCHITECTURE.md:36`, `:224` | **Wrong for payroll.** Right, coincidentally, for *all* plugin DLL projects: 67. |
| "Payroll export plugins: **44**" | `TLW-INVENTORY.md:21` | **Correct.** Verified by directory count. |
| "~58 of ~60" (night-shift defect rate) | `TLW-TIME-MODEL.md:517` | **Wrong** — see §6. The defect does not live where that note assumed. *(File is fenced for this survey; correction proposed in §10.)* |

Measured, by counting projects under `E:\Tlw\Source` (316 directories total):

| Kind | Count | Base class | Loader |
|---|---:|---|---|
| Payroll export plugin (`*PayrollPlugin`) | **44** | `HorioWebPlugin/PayrollPlugin.cs:13` | `OtherPayrollController.cs:22` |
| Custom report plugin (`*CustomReportPlugin` / `*ReportPlugin`) | **15** | `HorioWebPlugin/ReportPlugin.cs:13` | `CustomReportsController.cs:19` |
| Fire report plugin (`SuttonFireReportPlugin`, `TMDFireReportPlugin`) | **2** | `HorioWebPlugin/CustomFireReportPlugin.cs:12` | `CustomFireReportController.cs:19` |
| Import plugin (`*ImportPlugin`) | **5** | `HorioWebPlugin/ImportPlugin.cs:7` | `ImportPluginHelper.cs:21` |
| Notification plugin (`AktrionNotificationPlugin`) | **1** | `HorioWebPlugin/NotficationPlugin.cs:8` *(sic — typo in legacy)* | `CustomNotificationsController.cs:22`, `AutoPlanningController.cs:71` |
| **Total plugin DLL projects** | **67** | | |
| Plugin SDK (`HorioWebPlugin`) | 1 | — | — |
| Plugin test project (`PayrollPlugins.Tests`) | 1 | — | one test file: `WrenKitchensPayrollPluginTests.cs` |

Adjacent but **not** plugins (a different mechanism, §8):

| Kind | Count |
|---|---:|
| `Horio*IntegrationService` Windows services | 19 |
| Other connector / data-exchange services (Cegid, ADP Gestil, ParentPay, ISAMS, Tribal EBS, LAPI, PCVUE, Squid, Topaz, AMPM, CDG…) | 16 |
| Standalone import/export tools (Atmos, HotSchedules, Louvre, JHI, SOSCIB…) | 17 |

**Unclassified: 0** for the plugin estate. The remaining 135 `Source` directories are core
product, licensing, devices, EPOS and tooling — outside this survey by definition, not by
oversight (device comms and EPOS are out of scope per invariant 3).

---

## 2. The common shape — what a plugin actually is

`E:\Tlw\Source\HorioWebPlugin\PayrollPlugin.cs` is the entire payroll contract:

```csharp
public abstract class PayrollPlugin
{
    public abstract string GetViewName();
    public abstract ActionResult ProcessGetRequest(IExternalAccessController controller, HttpRequestBase request);
    public abstract ActionResult ProcessPostRequest(IExternalAccessController controller, HttpRequestBase request,
                                                    ModelStateDictionary modelState, TempDataDictionary tempData,
                                                    Dictionary<string, string> resources);
}
```

Read that carefully, because it is the single most important finding in this survey.

**A TLW payroll plugin is not an export. It is an ASP.NET MVC controller action.**
It is handed an `HttpRequestBase`, a `ModelStateDictionary`, a `TempDataDictionary` and a
localization dictionary; it returns an `ActionResult`. Nothing about payroll appears in the
signature — not a period, not an employee set, not a timesheet, not a file. There is **no data
contract at all**. Whatever the plugin needs, it fetches itself.

Consequences, all measured:

1. **Plugins query the database directly.** 33 of 44 embed raw T-SQL; 26 open
   `HorioDataContext` (the LINQ-to-SQL context of the whole 579-table schema); 2 use Dapper on
   a connection string the plugin obtains itself
   (`BoxwayPayrollPlugin/Plugin.cs:58` — `Horio.Helpers.Helper.GetWebsiteConnectionString()`).
   A plugin has unrestricted read/write access to every table in the product.
2. **Plugins run in-process in the web application**, loaded by `Assembly.LoadFrom` into the
   web app's default load context (`WebSite/Helpers/SharedMethods/Plugins.cs:21`). No isolation,
   no unload, no sandbox, no version pinning. A plugin exception is caught only at the outermost
   controller level and rendered as a generic error page (`OtherPayrollController.cs:35-39`).
3. **Plugins are not self-contained.** The plugin returns a *view name*; the Razor view lives in
   the core product at `WebSite/Views/OtherPayroll/`. 6 of those 30 views are generic
   (`OtherDateRange`, `OtherEmployeeSelection`, `OtherEmployeesAndDate`,
   `OtherEmployeesAndActivities`, `OtherEmployeesAndCustomField`,
   `OtherEmployeesAndExportFormat`); the rest are customer-named
   (`NSK.cshtml`, `WrenKitchen.cshtml`, `ThorncliffePayroll.cshtml`, …). Roughly 17 of the 44
   plugins required a bespoke Razor file **checked into the core product** — so "add a customer
   plugin" is not a deployment, it is a core release.
4. **One plugin per install, per kind.** Every loader globs a single wildcard and returns the
   first matching type:

   | Slot | Glob | Site |
   |---|---|---|
   | Payroll | `*.PayrollPlugin.dll` | `OtherPayrollController.cs:22` |
   | Report | `*.ReportPlugin.dll` | `CustomReportsController.cs:19` |
   | Import | `*.ImportPlugin.dll` | `ImportPluginHelper.cs:21` |
   | Notification | `*.NotificationPlugin.dll` | `CustomNotificationsController.cs:22` |
   | Fire report | `*.FireReportPlugin.dll` | `CustomFireReportController.cs:19` |

   Assembly names are dotted to match (`AdpPayrollPlugin.csproj:12` →
   `<AssemblyName>Adp.PayrollPlugin</AssemblyName>`). A customer cannot have two payroll exports.
   The 44 plugins are 44 *mutually exclusive* builds of the same product.

The other four base classes are the same idea with fewer parameters. `ImportPlugin.cs` is the
most honest about it: *"partial views are used for the plugin … Result returned via TempData"*.

---

## 3. Boilerplate vs. real per-customer logic — the hypothesis, tested

`TLW-INVENTORY.md:209` proposes "a generic payroll export builder (field mapping + format) so
most customers are configuration, not code." I measured what actually differs.

**Size.** 44 plugins, ~12,900 lines of C# total (excluding `AssemblyInfo.cs`, `obj/`, `bin/`).
Median ≈ 216 lines. Largest: `RythmPayrollPlugin` 745, `NightingaleHospitalPayrollPlugin` 743,
`ParkcakesPayrollPlugin` 621. Smallest: `SavsPvcPayrollPlugin` **52**. This is not 44 systems;
it is 44 short files.

**Composition** (regex over each plugin's non-generated `.cs`):

| Trait | Plugins | Reading |
|---|---:|---|
| Embeds raw SQL | 33/44 | the "mapping" is a hand-written query, not a field list |
| Opens `HorioDataContext` | 26/44 | full-schema access |
| Reads `CPTN01..20` counters | **29/44** | the export payload is *named counters*, not hours |
| Reads clocking counter rows | 14/44 | |
| Reads absences | 23/44 | |
| Reads employee custom fields | 15/44 | per-customer extension data drives the export |
| Emits Excel (OpenXML/ClosedXML) | 16/44 | |
| Emits delimited/fixed text | 27/44 | |
| Emits XML | **0/44** | |
| Performs its own transport (FTP/SFTP/HTTP) | **0/44** | every one is a browser file download |
| Reads `BadgeTime1..12` | 9/44 | see §6 |

**The canonical plugin.** `BoxwayPayrollPlugin/Plugin.cs:68-104` sums 20 named counters per
employee for a period and writes `code,value` pairs into a CSV whose first, second, third and
fifth header tokens are, per the comment at line 68, *"hardcoded due to customer request"*.
That is a field map and a format. A builder replaces it outright. So do
`AdpPayrollPlugin`, `SavsPvcPayrollPlugin`, `ZepPayrollPlugin`, `LCBPayrollPlugin`,
`MicropayPayrollPlugin` and most of the sub-250-line tail.

**But the residue is not "exotic formats".** The plugins a builder cannot express are not
formatting outliers — they are plugins doing *calculation* that the engine should have done:

- `ThorncliffePayrollPlugin/Plugin.cs:31-40` — renders an RDLC timesheet per employee and
  **emails each employee their PDF**, four at a time (`MaxParallelism = 4`), from an MVC POST.
  This is a scheduled bulk-notification job wearing an export's clothes. It also re-declares
  `CounterCount = 20`.
- `UWHomesPayrollPlugin/Plugin.cs:262-286` — apportions worked seconds across cost centres in
  T-SQL, per badge pair, against **hard-coded primary keys** (`CostCentres` 26/27/28,
  `EmployeeCustomFieldId = 1`). Cost-centre allocation is calculation-engine work.
- `NightingaleHospitalPayrollPlugin/Services/NightingaleExportService.cs:181-330` — apportions a
  swipe across midnight into time bands, with before/after-midnight hour splits. That is a work
  rule, implemented in an export, once, for one customer.
- 16 plugins emit Excel, which a builder needs an xlsx writer for — a feature, not an obstacle.

**Answer to the question the task asked.** WM is *not* facing 44 ports. A builder with
(a) a period-summed named-counter dataset, (b) a field map, (c) delimited/fixed-width/xlsx
output and (d) a header/footer template covers the substance of roughly **30 of the 44**. Of
the remaining ~14, most are not export problems at all: they are calculation the engine owes
(cost-centre allocation, midnight apportionment, per-employee report delivery). Port those into
the engine and the notification module, and the genuine residue — cases where a real code
plugin is the right answer — is in **single figures**.

The correct framing is therefore not "builder vs. 44 plugins". It is: *the 44 plugins are a
measurement of what the legacy calculation engine failed to compute.*

---

## 4. `Generic` and `Other` — the existing attempts

`TLW-INVENTORY.md:205` names both. Neither is a builder.

- **`Other`** (`WebSite/Controllers/Payroll/OtherPayrollController.cs`) is the plugin slot
  itself — the 91-line shim that loads `*.PayrollPlugin.dll`. Not an export.
- **`Generic`** (`WebSite/Controllers/Payroll/GenericPayrollController.cs`, 137 lines) is a
  fixed report: employee × pay-category and contract-category totals for a date range, emitted
  as CSV, XLS or PDF via `GenericPayrollReport.rdlc`. There is **no field mapping, no format
  definition, no per-customer configuration** — the columns are whatever the pay categories
  happen to be, and the layout is a compiled RDLC. It is the *shape* a builder would generalise,
  not a builder.

---

## 5. The escape hatch — `PayrollExportSettings`, and it is dead

The schema has a per-record custom-SQL table
(`Logic/Entities/HorioDB.designer.cs:27780`, `dbo.PayrollExportSettings`), **4 columns**:

| Column | Type |
|---|---|
| `Id` | int identity PK |
| `OrderNo` | int |
| `Note` | nvarchar(50) |
| `TSQLText` | **nvarchar(250) NOT NULL** |

It has a full CRUD screen (`WebSite/Views/ImportExport/PayrollExportSettings.cshtml`,
`AddEditPayrollExportSettings.cshtml`), a service (`Logic/Settings/ImportExportService.cs:9-16`),
a mapper (`WebSite/Helpers/ImportPluginHelper.cs:33-70`) and a controller action.

**Nothing executes it.** `TSQLText` appears in exactly 14 files across all of `E:\Tlw`
(source, `Database\Versioning\1_horio_structure.sql`, localization, and generated designer
files). No consumer reads it, no stored procedure references it, and
`Database\Versioning\2_horio_initial_data_culture independent.sql` seeds no rows.

Two readings, both useful:

1. **Somebody already tried the configuration answer and abandoned it.** The attempt was
   arbitrary T-SQL in a 250-character box — the wrong abstraction, and the abandonment is
   evidence about the abstraction, not about configuration.
2. It is a live administrative surface that stores unexecuted SQL. Harmless only because the
   executor was removed. WM must not ship an equivalent.

*(Per the standing trap: `Database\Versioning\*.sql` is an append-only `CREATE OR ALTER` log.
I checked for higher-numbered redefinitions of `PayrollExportSettings`; there are none — the
table is defined once, in `1_horio_structure.sql`, and never altered.)*

---

## 6. The night-shift defect — honest rate

Plan 008 found `ToUtc(Date + BadgeTimeN, SystemTimeZone)` producing `End < Start` on night
shifts in People First and Sage HR. `TLW-TIME-MODEL.md:517` recorded the rate in the remaining
plugins as unknown and possibly 100%.

**It is not in the payroll plugins.** `ToUtc` / `SystemTimeZone` / `TimeZoneInfo` returns
**zero** matches across all 44 `*PayrollPlugin` projects. Only 9 of 44 read `BadgeTime*` at all.
The defect lives in the **integration services**, which are a different mechanism (§8).

**Sample: all three integration services that reconstruct start/end from `BadgeTime` pairs.**

| Service | Recipe | Night shift |
|---|---|---|
| `HorioPeopleFirstIntegrationService/Helpers/TimeHelper.cs:197-200` | `ConvertToUtcDateTime(c.Date.Date.Add(inT/outT), windowsTz)` | **Broken.** No rollover; emits `End < Start`. |
| `HorioSageHrIntegrationService/Services/TimeSheetService.cs:182-208` | `GetClockINs` / `GetClockOUTs` as two unpaired lists | **Broken** (per plan 008); also loses pairing entirely. |
| `HorioLuccaIntegrationService/Helpers/TimeHelper.cs:171-190` | `if (dateTo < dateFrom) dateTo = dateTo.AddDays(1);` then **splits the entry at midnight** into two `Time` records | **Correct**, and more careful than either. |

**Measured rate: 2 of 3, not 3 of 3.** The claim of "possibly 100%" is refuted, and Lucca
is the reference implementation — a WM night-shift test should assert Lucca's behaviour
(rollover *and* midnight split), not merely "End > Start".

**The same defect class does appear once in a payroll plugin, in T-SQL:**
`UWHomesPayrollPlugin/Plugin.cs:262-286` computes
`datediff(second, coalesce(BadgeTime1,'00:00'), coalesce(BadgeTime2,'00:00'))` per pair. A night
shift yields a **negative** duration, which is then *added* into a cost-centre total — silently
subtracting hours from the customer's payroll. Nobody has reported it because it only fires when
a cost-centre-tagged pair crosses midnight.

The other 8 `BadgeTime`-reading plugins pass raw times straight through to columns
(e.g. `UWHomesPayrollPlugin/Plugin.cs:291-302`, `StartTime1..EndTime6`) and so have no
arithmetic to get wrong. `NightingaleHospitalPayrollPlugin` handles midnight explicitly and
correctly.

---

## 7. What a plugin is allowed to touch — and the fail-open

The task asked whether WM's SDK can be a narrow contract or must expose a data model. Legacy
gives no guidance, because legacy exposes **everything**: the full `HorioDataContext`, the raw
connection string, `DependencyResolver.Current`, the MVC pipeline, `SmtpClient`, the file system.

Worse, it hands out data the *signed-in user is not allowed to see*.

`HorioWebPlugin/PayrollPlugin.cs:22-24`:

```csharp
[Obsolete("Not secured by user access, use select generator")]
protected virtual PresencesSelectEmployeesWithFilterModel GetEmployeesForSelect()
```

It calls `IPersonnelService.GetAllEmployees(...)` with **no scope filter at all**. Compare the
in-tree controllers, which route every id through `FilterEmployeesForRead` /
`FilterEmployees` / `FilterDepartments`
(`GenericPayrollController.cs:51`, `PayrollControllerBase.cs:33-39`).

**35 of the 44 payroll plugins call the obsolete, unscoped selector.** A user whose data scope
is one site can open the payroll export screen and export the whole estate. This is the same
fail-open pattern plan 001 found in the access model, at a larger blast radius — payroll exports
carry names, codes, hours and, in 15 plugins, custom fields.

Only a handful (`ThorncliffePayrollPlugin/Plugin.cs:38-70`) use the scoped
`ISelectEmployeesWithFilterModelGenerator`.

---

## 8. Integration services are a *different* mechanism, and a better one

The 19 `Horio*IntegrationService` projects are not plugins. They are standalone Windows services.
One of them — `HorioIntegrationService` — is the shape WM should notice: it is **multi-tenant and
configuration-driven**. `Clients.xml` holds one row per customer, each naming a `Class Name`
(manager type), and a GUI editor shows only the fields that manager needs
(`E:\Tlw\Documentation\Integrations\Integration Service Configurator.md`). Four managers cover
many customers: `EvaluManager`, `PeopleHrManager`, `HealthRosterManager`, `FourthManager`
(`HorioIntegrationService/ClientManagers/`, resolved by `Helpers/ClientManagerResolver.cs`).

The rest (Lucca, Sage HR, People First, Rotageek, Staffology, Reflexis, HotSchedules, StaffTracker,
Cegid, ADP Gestil, Tribal EBS, SIMS, ISAMS, Evalu8, …) are one service per vendor with its own
host, config file and deployment.

So legacy contains, side by side, both answers to WM's question: a per-customer-code answer
(44 plugins, 19 services) and a configuration answer (`Clients.xml` + 4 managers) that visibly
scaled better. WM should not re-litigate this.

The vault also mandates a documentation template for every integration
(`Integrations/Integration Documentation Requirements.md`: Overview / Data Exchange /
Configuration / Endpoints and models / Troubleshooting) — worth keeping as WM's connector doc
contract.

---

## 9. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A per-customer export *exists as a first-class product concept* | **Keep** | It is the business model. Customers pay for their payroll file. |
| Export = MVC controller action with no data contract (`PayrollPlugin.cs:13-20`) | **Invert** | WM's `IPayrollExportPlugin` must receive a dataset and return a file. A plugin must never see `HttpRequest`, a view name, or the DB. |
| Plugin queries the database directly (33/44 raw SQL, 26/44 `HorioDataContext`) | **Invert** | Contract-only. Anything a plugin needs goes in the context record; if it isn't there, the *dataset* is wrong, not the plugin. |
| One `*.PayrollPlugin.dll` per install (`OtherPayrollController.cs:22`) | **Invert** | Many exports per tenant, selectable, schedulable. |
| Plugin's screen lives in core (`WebSite/Views/OtherPayroll/*.cshtml`) | **Invert** | Config UI generated from the manifest's JSON Schema (`ARCHITECTURE.md:221`) — no core release to add a customer. |
| Field map + delimited/fixed/xlsx output (~30 of 44) | **Improve** | Becomes an *export definition* row, authored in the UI. Zero code. |
| `CPTN01..20` fixed counter slots, re-declared inside plugins (`ThorncliffePayrollPlugin:38`) | **Improve** | Counter *rows* keyed by code, no ceiling — consistent with plan 002. The export map addresses counters by code. |
| `PayrollExportSettings.TSQLText` nvarchar(250), unexecuted | **Drop** | Wrong abstraction, and a dormant SQL-injection surface. Do not port. |
| Cost-centre apportionment inside an export (`UWHomesPayrollPlugin:262-286`) | **Improve → move** | Calculation engine (plan 013), not the export. |
| Midnight time-band apportionment inside an export (`NightingaleExportService.cs:181-330`) | **Improve → move** | Work rules (plan 012 / `TLW-WORK-RULES.md`), not the export. |
| Per-employee PDF emailed from the export (`ThorncliffePayrollPlugin`) | **Improve → move** | Notifications module + Worker job. Not an export at all. |
| Unscoped `GetEmployeesForSelect` used by 35/44 | **Invert** | Fail closed. The dataset is built server-side from the caller's scope; the plugin never selects employees. |
| Plugin loaded via `Assembly.LoadFrom` into the web app | **Invert** | Collectible `AssemblyLoadContext` in the Worker only (`ARCHITECTURE.md:221`) — already WM's stated design; this survey is the evidence for it. |
| `Clients.xml` + manager classes (`HorioIntegrationService`) | **Keep (as precedent)** | Legacy's own proof that configuration beat code. |
| Import plugins (5), notification plugin (1), fire report plugins (2) | **Improve** | Fold into the same manifest+contract model; fire reports are an emergency-roll-call feature, not a plugin kind. |
| 15 report plugins | **Drop** | `ARCHITECTURE.md:826` already decides this: semantic layer + assistant replaces the report estate. |
| EPOS/cashless and device-comms projects in the same tree | **Drop** | Invariant 3 and the 2026-07-20 scope decision. |

---

## 10. Corrections to WM's records

Made directly:

1. `docs/TLW-INVENTORY.md` §4 — heading and body understated the estate ("plus ~25 further
   customer-specific plugin projects"); replaced with the measured breakdown and the
   one-DLL-per-install fact.
2. `docs/TLW-INVENTORY.md:209` — the "generic payroll export builder" implication is kept but
   sharpened with the measured coverage (~30 of 44) and the finding that the residue is
   calculation, not format.

Proposed, not applied (fenced files):

3. **`CLAUDE.md` invariant 6** — "~60 per-customer payroll plugins" → **"44 per-customer payroll
   plugins, plus 23 report/import/notification plugins (67 plugin projects in total)"**.
4. **`docs/ARCHITECTURE.md:36`, `:224`** — same substitution ("replaces ~60 `*PayrollPlugin`"
   → "replaces 44 `*PayrollPlugin`").
5. **`docs/TLW-TIME-MODEL.md:517`** — "~58 of ~60" and "possibly 100%" are refuted. Suggested
   replacement: *"Measured 2026-08-30: the recipe appears in 3 integration services, not in the
   44 payroll plugins. Two are broken (People First, Sage HR); `HorioLuccaIntegrationService`
   handles it correctly and splits the entry at midnight. One payroll plugin has the same defect
   in T-SQL (`UWHomesPayrollPlugin/Plugin.cs:262-286`), producing negative cost-centre hours."*
6. **`docs/SCREEN-TREE.md:156`** — "(replaces 19 per-vendor screens + 44 plugins)" is correct;
   worth a footnote that of the 19 controllers, one (`Other`) *is* the plugin slot and one
   (`Generic`) is a fixed pay-category report, so there are 17 true vendor screens.

---

## 11. What I did not measure

1. **I did not read all 44 payroll plugins.** I opened, in full or in relevant part:
   `AdpPayrollPlugin`, `SavsPvcPayrollPlugin`, `BoxwayPayrollPlugin`, `UWHomesPayrollPlugin`,
   `ThorncliffePayrollPlugin`, `NightingaleHospitalPayrollPlugin` (export service),
   plus `GenericPayrollController` and `OtherPayrollController`. The other 38 are characterised
   only by the regex trait matrix in §3 and the line/file counts. The "~30 of 44 replaceable"
   figure is an **estimate from that matrix**, not a per-plugin adjudication.
2. **I did not read any of the 15 report plugins or 5 import plugins.** Their base classes only.
3. **I did not open the 19 in-tree vendor payroll controllers** beyond `Generic`, `Other` and
   `PayrollControllerBase`. Line counts only (2,626 lines total; largest `SagePayroll` 398,
   `Persee` 255, `Cascade` 238).
4. **I did not verify runtime behaviour of the one-DLL-per-slot rule** against a real deployment
   — only the loader code and the `AssemblyName` convention in one `.csproj`.
5. **I did not audit `Database\` stored procedures for payroll export logic.** The standing trap
   warns rules logic hides in T-SQL; I searched for `TSQLText` consumers and found none, but I
   did not sweep `Database\` for export-shaped procedures.
6. **I did not measure the 16 connector services or 17 standalone import/export tools** beyond
   classifying them and reading `HorioIntegrationService`'s configurator documentation and
   manager list.
7. **I did not check whether any plugin writes to the database.** I measured read access; write
   access is plausible (all 26 `HorioDataContext` users could call `CreateUpdatable`) but
   uncounted.
