# 014 — Payroll plugins & the generic export builder

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 (Plugins & payroll exports); §13 module 14 (Plugins / payroll); invariant 6 (`src/PluginSdk`)
Legacy sources surveyed: see `docs/TLW-PLUGINS.md` (survey 2026-08-30 — **measurement already
done, not re-measured here**). Estate counted by directory over `E:\Tlw\Source` (316 project dirs);
plugin contract read from `HorioWebPlugin/PayrollPlugin.cs:13-24`; loaders from
`OtherPayrollController.cs:22`, `CustomReportsController.cs:19`, `ImportPluginHelper.cs:21`; trait
matrix (§3) by regex over each plugin's non-generated `.cs`; night-shift defect from the three
integration `TimeHelper`/`TimeSheetService` recipes (§6) and `UWHomesPayrollPlugin/Plugin.cs:262-286`.

## Ground truth
Full measurement in `docs/TLW-PLUGINS.md`. Load-bearing facts:
- **67 plugin DLL projects in total; 44 are payroll export plugins** — the "~60 per-customer
  payroll plugins" in `CLAUDE.md` invariant 6 / `ARCHITECTURE.md:36,224` is **wrong for payroll**
  (44), right only by coincidence for *all* plugin kinds (67 = 44 payroll + 15 report + 5 import
  + 2 fire + 1 notification). Correction proposed, not applied — see Open questions.
- **The generic-export-builder hypothesis holds.** 44 short files, ~12,900 lines total, median 216
  (largest 745, smallest 52). A builder with (a) a period-summed named-counter dataset, (b) a field
  map, (c) delimited/fixed-width/xlsx output, (d) a header/footer template covers the substance of
  **~30 of 44**. Of the residue ~14, most are not export problems: they are calculation the engine
  owes. Genuine "real code plugin" residue is in **single figures**.
- **A legacy plugin is an ASP.NET MVC controller action with no data contract** — handed
  `HttpRequestBase`, `ModelStateDictionary`, `TempDataDictionary`, a resources dict; returns an
  `ActionResult`. It fetches its own data (33/44 raw SQL, 26/44 open the full 579-table
  `HorioDataContext`). This is exactly what `src/PluginSdk` must *invert* into a narrow contract.
- **`Generic` and `Other` are not builders** — `Other` is the plugin slot shim; `Generic` is a
  fixed pay-category RDLC report. They are the *shape* a builder generalises, not a builder.
- **`PayrollExportSettings.TSQLText` (nvarchar(250)) is a dead escape hatch** — full CRUD screen and
  service, but nothing executes it. Do not port; a dormant SQL-injection surface.

## Legacy behaviour (what we are replacing)
- **Contract** (`HorioWebPlugin/PayrollPlugin.cs:13-20`): `GetViewName` +
  `ProcessGetRequest`/`ProcessPostRequest`. No period, employee set, timesheet or file in the
  signature. The Razor view lives in the *core* product (`WebSite/Views/OtherPayroll/*.cshtml`), so
  ~17 of 44 plugins needed a bespoke view checked into core — "add a customer" is a core release.
- **One DLL per install per kind** (`OtherPayrollController.cs:22` globs `*.PayrollPlugin.dll` and
  returns the first match; assembly names dotted to match). 44 mutually-exclusive builds.
- **Loaded in-process via `Assembly.LoadFrom`** into the web app (`WebSite/Helpers/SharedMethods/
  Plugins.cs:21`) — no isolation, no unload, no version pinning.
- **Payload is named counters, not hours** — 29/44 read `CPTN01..20`; the canonical plugin
  (`BoxwayPayrollPlugin/Plugin.cs:68-104`) sums 20 named counters per employee per period and writes
  `code,value` CSV with header tokens "hardcoded due to customer request". 27/44 emit delimited/fixed
  text, 16/44 Excel, **0/44 XML, 0/44 their own transport** (every one is a browser download).
- **Fail-open scope** (§7): `PayrollPlugin.GetEmployeesForSelect` is `[Obsolete("Not secured by
  user access")]` and calls `GetAllEmployees(...)` with **no scope filter**. **35 of 44** use it —
  a site-scoped user can export the whole estate. Same pattern as plan 001's access-model fail-opens,
  larger blast radius (names, codes, hours, custom fields).
- **The night-shift defect** (§6): `ToUtc(Date + BadgeTimeN, tz)` with no midnight rollover yields
  `End < Start`. It is **not** in the 44 payroll plugins (`ToUtc`/`SystemTimeZone` = zero matches);
  it lives in the **integration services** — 2 of 3 that rebuild start/end are broken (People First,
  Sage HR), `HorioLuccaIntegrationService/Helpers/TimeHelper.cs:171-190` is correct (rollover **and**
  midnight split). One payroll plugin has the same defect in T-SQL
  (`UWHomesPayrollPlugin/Plugin.cs:262-286`): a cross-midnight cost-centre pair yields a **negative**
  duration silently subtracted from payroll. The builder must reproduce **none** of this.
- **Integration services are a different, better mechanism** (§8): 19 Windows services;
  `HorioIntegrationService` is multi-tenant and **configuration-driven** (`Clients.xml` + 4 manager
  classes cover many customers). Legacy contains, side by side, both answers to WM's build-vs-config
  question — and the config one visibly scaled better.

## Keep / Improve / Invert / Drop
Full table in `TLW-PLUGINS.md §9`. Headlines:
- **Keep:** a per-customer export as a first-class product concept (it is the business model); the
  `Clients.xml` + manager-class pattern as *precedent* that configuration beat code.
- **Improve:** field-map + delimited/fixed/xlsx output (~30 of 44) → an **export-definition row**
  authored in the UI, zero code; `CPTN01..20` fixed counter slots → counters addressed **by code**
  (consistent with plan 002/003); the import (5), notification (1) and fire-report (2) plugin kinds
  → the same manifest+contract model.
- **Improve → move:** cost-centre apportionment (`UWHomesPayrollPlugin:262-286`) → calc engine
  (plan 013); midnight time-band apportionment (`NightingaleExportService.cs:181-330`) → work rules
  (plan 012); per-employee PDF email (`ThorncliffePayrollPlugin`) → Notifications + Worker job.
- **Invert:** export = MVC action with no data contract → `IPayrollExportPlugin` receives a
  **dataset**, returns a **file**, never sees `HttpRequest`/a view/the DB; one-DLL-per-install →
  many exports per tenant, selectable + schedulable; plugin's screen in core → config UI generated
  from the manifest JSON Schema; unscoped `GetEmployeesForSelect` (35/44) → **fail closed**, dataset
  built server-side from the caller's scope; `Assembly.LoadFrom` in-web → collectible
  `AssemblyLoadContext` in the **Worker** only.
- **Drop:** `PayrollExportSettings.TSQLText` (dead + injection surface); the 15 report plugins
  (`ARCHITECTURE.md:826` — semantic layer + assistant replaces the report estate); EPOS / device-comms
  projects (invariant 3).

## Edge cases
- **Night shift:** the WM export/dataset must assert `End > Start` **and** the Lucca behaviour
  (rollover + split the entry at midnight) — not merely "End > Start". Cross-midnight cost-centre
  pairs must never yield a negative duration.
- **Counter by code, no ceiling:** a `CPTN21` (21st counter) must round-trip through the field map
  — the map addresses counters by code, so there is no slot ceiling to hit.
- **Scope:** an empty employee scope must export **nothing**, not everything (invert F-pattern).
- **Absent data:** an employee with no counter rows for the period appears with zeros / is omitted
  per the definition's rule, never as a query error.
- **One export, many formats:** the same definition must be renderable as delimited, fixed-width and
  xlsx without code (the `Generic` RDLC's CSV/XLS/PDF fan-out is the legacy precedent).
- **Excel is a feature, not an outlier** — 16/44 need an xlsx writer; it is in-scope for the builder.

## Target design in WM
`src/PluginSdk` expresses the **inverted** contract; the builder lives as a
configuration-driven export engine; execution is in the **Worker** (invariant 6, `ARCHITECTURE.md:221`).
- **Contract (`src/PluginSdk`):** `IPayrollExportPlugin` receives an immutable export-context record
  (period, scoped employees, per-employee named-counter totals, absences, selected custom fields) and
  returns a file (bytes + name + content type). No `HttpRequest`, no view name, no DB handle. A
  manifest carries a JSON Schema for the plugin's configuration (generates the config UI).
- **Generic export builder:** an **export-definition** entity (field map keyed by counter code +
  format: delimited / fixed-width / xlsx + header/footer template), authored via API/UI, executed by
  the builder with no per-customer code. Covers ~30 of 44.
- **Dataset service:** builds the export context server-side from the **caller's scope** (fail
  closed), reading counters by code from the Rules/TimeAttendance projection — never a raw
  cross-module DB read (invariant 1).
- **Endpoints (API-first, invariant 2):** CRUD export definitions; run an export (sync download +
  async Worker job); list available plugins/manifests; validate a definition.
- **Execution host:** collectible `AssemblyLoadContext` in the Worker; RabbitMQ job to run;
  every endpoint carries an authorization policy (invariant 5).
- Report plugins are **not** built here (Drop); integration-service connectors are their own future
  bucket (they are a different mechanism — §8).

## Out of scope for this plan
The 15 report plugins (semantic layer, `ARCHITECTURE.md:826`). Integration/connector services (19
`Horio*IntegrationService` + 16 connectors + 17 standalone tools — a different mechanism, their own
bucket; but the night-shift acceptance test derives from Lucca). The 19 in-tree vendor payroll
controllers beyond `Generic`/`Other`. Cost-centre / midnight / notification calculation now moved to
plans 013 / 012 / Notifications — this plan consumes their output, it does not re-implement it. EPOS
and device comms (invariant 3).

> **Inbound from plan 002 (refresh 2026-09-25).** The **flat legacy projection** of the Clocking —
> `BadgeTime1..12` and `CPTN01..20` in the legacy column layout — moved here from 002, because this
> plan's generic export builder is its only consumer. It must carry the **day-carry** explicitly (a
> slot's calendar date, not `Date + time`; `TLW-CLOCKING-MODEL.md` §3a) and must handle more than 12
> punches or more than 20 counters **explicitly**, never by silent truncation. Prerequisites: 002 P6
> (punch provenance) and P7 (stored values).

## Portions

### [ ] P1 — `IPayrollExportPlugin` contract + manifest in `src/PluginSdk`
**Touches:** `src/PluginSdk` (contract, export-context record, manifest + JSON Schema), `PluginSdk.Tests` (new).
**Done when:** the inverted contract compiles — plugin receives a dataset record, returns a file; no `HttpRequest`/view/DB reachable from the contract surface.
**Tests:** a trivial in-memory plugin round-trips a context to a file; manifest schema validates a sample config; contract exposes no DB/HTTP type.
**Risk:** low

### [ ] P2 — Export-definition entity + CRUD (field map + format + template)
**Touches:** `src/Modules/Plugins` (new) or the Worker-side export module, migration, module service, endpoint, screen, tests.
**Done when:** CRUD for an export definition (field map keyed by counter code, format = delimited/fixed/xlsx, header/footer template); JSON-Schema-driven config UI renders.
**Tests:** definition validates; counter-by-code map accepts a `CPTN21` with no ceiling; format switch persists.
**Risk:** medium

### [ ] P3 — Scoped dataset service (fail closed)
**Touches:** export module, TimeAttendance/Rules counter contract, Identity scope contract, tests.
**Done when:** the export context is built from the caller's scope and per-employee counter-by-code totals via contract only (no cross-module DB read); empty scope exports nothing.
**Tests:** empty scope → empty dataset (invert of the 35/44 fail-open); site-scoped user cannot see out-of-scope employees; counters resolved by code.
**Risk:** high

### [ ] P4 — Generic builder: delimited + fixed-width output
**Touches:** export module, builder engine, endpoint (run + download), tests.
**Done when:** a definition renders a scoped dataset to delimited and fixed-width files reproducing the Boxway-style `code,value` shape from configuration alone.
**Tests:** Boxway-equivalent CSV matches golden output from a definition (no code); night-shift/cross-midnight counters never negative; absent-data employee handled per rule.
**Risk:** medium

### [ ] P5 — xlsx output
**Touches:** export module, xlsx writer, tests.
**Done when:** the same definition renders to xlsx; covers the 16/44 Excel emitters.
**Tests:** xlsx cell values equal the delimited render for the same dataset; header/footer template honoured.
**Risk:** medium

### [ ] P6 — Worker execution host + async run job
**Touches:** `src/Worker` (collectible `AssemblyLoadContext`), RabbitMQ job, endpoint (async run + status), tests.
**Done when:** a code plugin (the single-figures residue) loads in a collectible context in the Worker and runs as a RabbitMQ job; the load context unloads after; a plugin exception is isolated.
**Tests:** load/run/unload; plugin exception does not crash the host; job status round-trips; authorization policy enforced.
**Risk:** high

## Open questions for the user
1. **Proposed protected-doc corrections (propose, not applied):**
   `CLAUDE.md` invariant 6 "~60 per-customer payroll plugins" → **"44 per-customer payroll plugins,
   plus 23 report/import/notification plugins (67 plugin projects in total)"**;
   `ARCHITECTURE.md:36,224` "replaces ~60 `*PayrollPlugin`" → "replaces 44";
   `TLW-TIME-MODEL.md:517` "~58 of ~60 / possibly 100%" → the measured 2-of-3 integration-service
   rate with Lucca as the reference (`TLW-PLUGINS.md §10` item 5);
   `SCREEN-TREE.md:156` footnote that of 19 controllers one (`Other`) is the plugin slot and one
   (`Generic`) a fixed report, so 17 true vendor screens. Apply in the consolidated pass?
2. **Builder coverage boundary:** the "~30 of 44" is an estimate from the §3 trait matrix, not a
   per-plugin adjudication (44 plugins were not all read — `TLW-PLUGINS.md §11`). Is a builder that
   covers ~30 and a single-figures code-plugin residue the accepted target, or should the residue be
   individually adjudicated before committing?
3. **Where do the moved calculations land?** Cost-centre allocation → plan 013, midnight
   apportionment → plan 012, per-employee PDF email → Notifications. Confirm those owners before P3
   builds a dataset that assumes the counters already carry them.
4. **Config vs code for the residue:** legacy proves configuration (`Clients.xml`) beat code. Do we
   commit that a *new* customer is only ever an export-definition row (code plugins frozen to the
   existing single-figures set), or is authoring new code plugins a supported path?
