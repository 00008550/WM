# Coverage audit — TLW measured against WM's docs and code

*2026-08-04. Every one of the 578 legacy tables bucketed by domain and mapped to a WM module and
a document. Companions: [`TLW-SCHEMA-SWEEP.md`](./TLW-SCHEMA-SWEEP.md) (what legacy has),
[`TLW-CLOCKING-MODEL.md`](./TLW-CLOCKING-MODEL.md) (the central entity),
[`TLW-INVENTORY.md`](./TLW-INVENTORY.md) (services, screens, integrations).*

> **Purpose.** Earlier reviews found gaps one at a time. This is the closing check: bucket the
> whole schema, confirm nothing is unaccounted for, and state honestly how much of the retained
> product WM has built.

---

## 1. Both sides, measured in the same units

| | Legacy (TLW) | WM today |
|---|---|---|
| Database tables | **578** | **8 entity classes** |
| Mapped columns | **8,173** | — |
| Modules / services | 59 Windows services | **3 modules** (Identity, People, TimeAttendance) |
| HTTP endpoints | — | **29** |
| Test projects | — | 1 (`WM.SharedKernel.Tests`, 22 tests) |
| Stack | .NET Framework, LINQ-to-SQL, SQL Server, MVC/Silverlight | .NET 10 LTS, EF Core 10, PostgreSQL, Angular 22 |

**Dropped by decision: 121 tables / 1,502 columns** — devices & access control (54 / 719) and
EPOS/cashless (67 / 783). That is ~21% of tables and ~18% of columns, so the **retained surface is
roughly 457 tables / 6,671 columns**.

Against that, WM's 8 entities are **under 2%** of the retained data model. The earlier "3–5%"
estimate was optimistic. Functionally WM is further along than 2% suggests — auth, scoping, punch
capture and the realtime pipeline are real and working — but no plan should be sized from the
optimistic number.

---

## 2. Full bucket map

Every table is in exactly one bucket; the counts sum to 578.

| Bucket | Tables | Columns | WM module | Documented? |
|---|---:|---:|---|---|
| **T&A + Clocking core** | 76 | 1,748 | TimeAttendance + Rules | ✅ `TLW-CLOCKING-MODEL.md`, plan 002 |
| **People / HR** | 95 | 1,074 | People, HR | ◐ People partly; **HR barely** |
| EPOS (dropped) | 67 | 783 | — | ⏹ decided |
| Devices / AC (dropped) | 54 | 719 | — | ⏹ decided |
| **Reporting** | 13 | 697 | Insight (assistant) | ◐ §16 decision, no table-level survey |
| **Absence & accruals** | 44 | 526 | Absence | ◐ named in §14, **not surveyed** |
| **Admin / config / audit** | 16 | 376 | Admin | ◐ `SoftwareMainOptions` found, rest **not surveyed** |
| **Activities / job costing** | 20 | 345 | Activities (phase 7) | ◐ named, not surveyed |
| **Rules / calc / money** | 35 | 341 | Rules | ✅ tariffs + counters + `Calculations` found |
| Unbucketed (see §3) | 48 | 329 | mixed | ◐ **five new findings below** |
| **Scheduling** | 16 | 251 | Scheduling | ◐ named in §14, **not surveyed** |
| **Safety** | 2 | 204 | Safety (6b) | ◐ `FireMarshalMusterPoints` is 177 cols — far bigger than "muster points" implies |
| **Notifications** | 29 | 180 | Notifications | ◐ §9 designed; **`Notifications.SqlQuery` changes the design** |
| Expenses | 10 | 135 | Expenses | ◐ named, not surveyed |
| Visitors | 9 | 127 | Visitors (phase 8) | ◐ named, not surveyed |
| Identity / access | 19 | 122 | Identity | ✅ plan 001 |
| Documents | 14 | 114 | Documents | ◐ §10 designed, not surveyed |
| Import / export / integration | 11 | 102 | Connectors | ◐ `TLW-INVENTORY.md` §2.2 lists services |

**Read this as a survey backlog.** The buckets marked "not surveyed" have been *named* in the
roadmap but never measured the way T&A and Rules now have been — which is exactly the condition
that hid the Clocking aggregate.

---

## 3. Five things still unaccounted for anywhere

Found in the 48 unbucketed tables. Small, but each is real and none appears in any WM document.

| Table(s) | Why it matters |
|---|---|
| **`FileVirusScanQueue`** | Uploaded documents are **virus-scanned**. WM's Documents module (§10) accepts uploads to object storage with no scanning step. For an on-prem product taking employee file uploads this is a security control, not a nicety. |
| **`CalculationProcessingQueue`** | Calculation is **queued**, confirming `HorioCalculationService` runs async after each swipe. WM's replay design should adopt the queue explicitly rather than rediscover the need. |
| **`SalaryDeductions`** | Payroll-adjacent employee data. Nothing in WM's plan holds deductions. |
| **`Currencies`, `Cultures`** | Multi-currency and multi-culture are **first-class tables**, not a formatting concern. Relevant to expenses, tariffs and the UK/France customer base. |
| **`ApiKeys`, `RsaKeys`, `SynergyAppAuthenticationTokens`** | A separate API-auth surface for the mobile app and integrations, distinct from user login. WM's Flutter plan assumes the same JWT path as the web; legacy did not. |

Also in that group and correctly out of scope: the schools vertical (`AMPMAttendance`, `Marks`,
`SchoolCalendars`), and device remnants (`ProximityCards`, `GetDoorStatuses`, `ThermalPipView`).

---

## 4. What the docs now cover well

Genuine strengths after this round:

- **The Clocking aggregate** — 249 columns, its 12/20/6 ceilings, pre-generation, and swipe→day
  allocation are documented with legacy citations and worked examples.
- **The money path** — `TariffValues` (20 rate + 20 charge-rate pairs, date-versioned) and its
  link to clockings via `HourlyRateId`.
- **Global calculation settings** — including the eight-window weekday-pair day-boundary matrix
  and the "which counter absorbs hours when there's no template" answers.
- **The five escape hatches** — daily template SQL, flexi balance SQL, notification SQL, payroll
  T-SQL, and the counter formula language.
- **The access model** — plan 001, with legacy's three fail-opens explicitly inverted and shipped
  under test.
- **The process itself** — the surveyor now measures before planning, classifies
  Keep/Improve/Invert/Drop, and hunts edge cases.

## 5. Recommended queue

1. **Finish 001** (P2–P5). Short, and every later query depends on scope being right.
2. **Settle 002's design decision** — mirror legacy's fixed slots or normalise and project. The
   ceiling propagating across 13 tables argues strongly for normalising.
3. **Plan 003 — Rules inputs**: tariffs, employee contracts and `…Effective` resolution,
   `Calculations` settings, the counter formula language.
4. **Plan 004 — per-install configuration** (`SoftwareMainOptions`, 227 columns).
5. **Survey before planning** the four named-but-unmeasured areas, biggest first:
   **People/HR (95 tables)**, **Absence & accruals (44)**, **Notifications (29)**,
   **Scheduling (16)**. HR is the largest unexamined bucket in the product.

Two design decisions should be taken before their phases start, not during:
**`Notifications.SqlQuery`** (legacy notifications are partly SQL-defined; WM's event-driven hub
is a behaviour change) and **`PayrollExportSettings.TSQLText`** (the export builder must absorb
customer T-SQL or customers cannot migrate).
