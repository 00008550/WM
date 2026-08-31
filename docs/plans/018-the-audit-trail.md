# 018 — The audit trail becomes real (and one uniform mechanism, not four)

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 item 19 (Admin), §16.1 (`wm.audit`), §0a (editable Daily Browser)
Legacy sources surveyed: `HorioDB.designer.cs` (schema measurement, `:49104` `ClockingsLog`,
`:86448` `AuditTrailLogs`, `:161590` `UserActionLogs`), `Entities\HorioDataContext.cs:105-124`,
`AuditTrail\AuditTrailLogService.cs`, `Entities\AuditTrail\*.cs` (~22 partials),
`Database\Versioning\30.V3.0.0.sql:582-608`, `License\LicenseInfo.cs`,
`Maintenance\MaintenanceService.cs:218-283`. Full survey: `docs/TLW-ADMIN-AUDIT-MODEL.md`.

## Ground truth

**The bucket is two tables in a trench coat.** `SoftwareMainOptions` (227) + `ClockingsLog` (97) are
86% of the 376 columns; the rest is ~13 small config/audit tables. The count is **not** view-inflated
(~372 base + ~4 view), unlike Activities/Scheduling. Measurement and classification (0 unclassified)
in `TLW-ADMIN-AUDIT-MODEL.md §1`.

**Legacy audit is a four-mechanism patchwork** (`TLW-ADMIN-AUDIT-MODEL.md §3`): a generic
field-level store (`AuditTrailLogs`) written by three uncoordinated paths (SQL trigger with a
hand-maintained column→int map, a C# ChangeSet interceptor, and manual calls); per-entity full-row
mirror tables (`ClockingsLog`, `AbsenceRequestsLogs`); a login/security log (`UserActionLogs`); and,
for most tables, nothing. Coverage is ~22 opt-in entities.

**WM today has neither the store nor the emission.** `SharedKernel` `Entity` carries only
`CreatedAt`/`UpdatedAt` (per `ARCHITECTURE.md §13:431`). `wm.audit` is designed (§16.1) and unbuilt
(§13:443, "topic designed; store+browser planned"). Every ✅ is a design claim, not code.

**Why now:** the 2026-08-18 ruling (`§0a`) makes the Daily Browser editable. The moment a human can
override a calculated value, "who changed what, from what, to what" is a correctness requirement, not
hygiene. This plan builds the store and the emission before 010's edit path lands.

## Legacy behaviour (what we are replacing)

- `ClockingsLog`: full-row snapshot per operation, SQL-trigger-written, carrying `calc_*` and
  `CPTN01..20` (`TLW-ADMIN-AUDIT-MODEL.md §2`). Provenance = a snapshot trail, not a diff.
- `AuditTrailLogs`: `OldValue`/`NewValue` per column, but `TableId`/`ColumnId` are **integers** kept
  in sync by hand across a SQL `#ColumnKeys` map and C# enums; the `Employee` C# path logs only a
  name string, not a diff (`AuditTrail\Employee.cs:13-21`).
- De-dupe: skip when `OldValue == NewValue` except personnel-delete (`AuditTrailLogService.cs:30`).
- `UserActionLogs`: login/logout/failed-login with IP + browser + device.

## Keep / Improve / Invert / Drop

- **Improve** — one uniform, append-only audit store keyed by **type + entity name**, not integer
  maps. Replaces mechanisms A and B with a single event shape.
- **Improve** — capture **field-level before/after** as structured data (a set of changed
  properties), not a whole-row mirror and not a name string. Answers the Daily Browser directly.
- **Keep** — the actor, timestamp, action (insert/update/delete), and the login/security events.
- **Invert** — audit is **opt-out, not opt-in**. Legacy audited ~22 entities and missed the rest;
  WM audits every mutation by default and lets a slice suppress noise deliberately.
- **Invert** — `EmailSettings` plaintext password and licensing fail-open (allow-all) both fail
  **closed** in WM (out of this plan's scope; recorded for §5/§13 corrections).
- **Drop** — `sy_dbsettings`, `AuditTrailLogsView`, per-entity mirror tables.

## Edge cases

- **A no-op update writes no audit row** (legacy's de-dupe). Same rule.
- **A delete records the pre-image**; an insert records the post-image only.
- **Actor is the authenticated user**; a system/job mutation records a system principal, never null.
- **Retroactive edits** (Daily Browser editing a past day) audit the edit at *now*, tagged with the
  business date they touched — the audit is immutable even when the data it describes is replayed.
- **A mutation inside a failed transaction emits no audit row** — emission must be transactional with
  the write (this is why it rides the outbox, not a fire-and-forget publish).
- **Concurrency**: an audited update that loses an optimistic-concurrency check (011 P5) must not
  emit — no write, no audit.

## Target design in WM

- A `SharedKernel` audit primitive: an `AuditEvent { EntityType, EntityId, Action, Actor,
  OccurredAt, Changes[] }` and an interception point in each module's `DbContext.SaveChanges` that
  reads the EF `ChangeTracker` (the modern, typed analogue of `HorioDataContext.SubmitChanges`).
- Emission is **transactional via the SharedKernel outbox** — the small outbox ADR 0001 defines
  (STATE.md D1, plan 011 P7). Audit is the outbox's first real payload. **Hard dependency: 011 P7 /
  ADR 0001 land first.**
- A persisted, append-only audit store (hash-chained per §16.1) queried by an Admin endpoint.
- Endpoints: `GET /api/audit` (scoped, permission `audit.view`), filterable by entity + actor + date.
- Login/security events (`UserActionLogs` successor) emitted from Identity into the same store.
- Screen: an Admin audit browser (later portion / or defer to §14 item 19).

## Out of scope for this plan

- `SoftwareMainOptions` port (plan 012 / a settings plan).
- Licensing fail-open fix and `EmailSettings` secret handling — recorded as §5/§13 corrections; a
  Licensing-service plan owns them.
- The Daily Browser edit UI (plan 010) — this plan gives it the audit it will emit into.
- Retention execution (a worker plan) — this plan sets the store's retention *policy* only.
- `wm.audit` Kafka streaming to external consumers; the store is the first deliverable.

## Portions

### [ ] P1 — the audit event and the store
**Touches:** `src/SharedKernel` (AuditEvent, IAuditSink), a new `WM.Modules.Admin` audit store +
migration, `WM.sln`, `src/SharedKernel/WM.SharedKernel.Tests`.
**Done when:** an `AuditEvent` can be persisted append-only and read back, hash-chained so a tampered
row is detectable.
**Tests:** round-trip; hash-chain detects an edited row; append-only rejects update/delete.
**Risk:** medium — depends on ADR 0001's outbox shape; if 011 P7 has not landed, P1 stops.

### [ ] P2 — automatic capture from the change tracker
**Touches:** a module `DbContext` base (People first), the SaveChanges interception, tests project.
**Done when:** every People insert/update/delete emits one `AuditEvent` with field-level changes and
an actor; a no-op update emits nothing.
**Tests:** update one field → one event, one change; no-op → zero; delete → pre-image; actor is the
caller; a failed transaction emits nothing.
**Risk:** medium — the transactional boundary is the whole point; needs the Postgres harness (009 P4)
to prove, per STATE.md's note on 011 P7.

### [ ] P3 — login and security events
**Touches:** `WM.Modules.Identity` (AuthService login/logout/lockout paths), the audit sink.
**Done when:** login, logout and failed-login (lockout) emit typed security audit events with IP.
**Tests:** each path emits exactly one event; a failed login records the reason and IP.
**Risk:** low.

### [ ] P4 — the audit query endpoint
**Touches:** `WM.Modules.Admin` endpoint, `EndpointAuthorizationInventoryTests`, Api tests.
**Done when:** `GET /api/audit` returns scoped, filtered audit rows behind `audit.view`; the new
transport is named in the inventory test.
**Tests:** permission required; scope applied; filter by entity/actor/date; the inventory table names
it (a new authorized transport fails until listed).
**Risk:** low.

## Open questions for the user

1. **Store location.** Audit is cross-cutting. Put the store in a new `WM.Modules.Admin`, or in
   `SharedKernel` infrastructure that every module writes to? §16.1/§13 imply an Admin-owned store
   with a browser; invariant 1 (no cross-module DB reads) pushes toward a dedicated audit module that
   others reach only via the sink contract. Recommend: **an Admin/Audit module owning the store; the
   sink is a SharedKernel contract.** Confirm.
2. **Field-level vs summary for large aggregates.** For `Clocking` (249 cols), a full field diff per
   edit is heavy. Legacy stored a whole-row snapshot; WM's replay makes most columns derived. Should
   the Daily Browser audit record only the **human-entered inputs and overrides** (recommended,
   cheap, and the meaningful thing) rather than every recalculated `calc_*`? This is the §0a-relevant
   decision and it is a product call.
3. **Hash-chaining now or later.** §16.1 promises tamper-evidence. It adds cost and a per-store
   sequence. Ship P1 with the chain, or ship the store first and chain in a follow-up? Recommend:
   **chain from P1** — retrofitting integrity over existing rows is worse than building it in.
4. **Sequencing.** This plan hard-depends on **ADR 0001 + 011 P7** (the outbox) for transactional
   emission, and on **009 P4** (Postgres harness) to prove P2's transaction boundary. It should be
   sequenced after both. Confirm the plan waits, rather than building a non-transactional stopgap.
