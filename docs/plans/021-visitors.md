# 021 — Visitors (web reception + phone self-check-in)

Status: draft
Roadmap: ARCHITECTURE.md §14 phase 8 (Visitors) — with a proposed re-sequencing note: Visitors is a
**hard dependency of Safety/mustering (phase 6b)**, see `TLW-VISITORS-MODEL.md` safety-seam section.

Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — measured: **10 `Visitor*` mapped objects =
  8 base tables (102 cols) + 2 views (57 cols)**; `VisitorSettings` 51 (L131910),
  `VisitorFormQuestions` 12 (L133330), `VisitorTypes` 9 (L114216), `VisitorFormAnswers` 9 (L133887),
  `VisitorSessions` 7 (L205865), `VisitorCards` 5 (L114580), `VisitorNotes` 5 (L134264),
  `VisitorFormQuestionOptions` 4 (L133712). The visitor **entity** lives on `dbo.Employees`
  (`EmployeeType=Visitor`) + `dbo.sy_ac_data`.
- `E:\Tlw\Source\Logic\Visitors\VisitorsService.cs` (627 lines) — sessions, sign-in/out rules,
  fire-report feed `:319-324`.
- `…\Visitors\VisitorEmailSender.cs`, `IcsGenerator.cs`, `CalendarInvite.cs` — host/visitor
  notification + ICS.
- `…\ExternalAccess\KioskExternalAccessService.cs` — the dropped Synergy Touch kiosk front end.
- `E:\Tlw\Source\WebSite\Helpers\FireReportControllerHelper.cs:55` — muster roll merges visitors.

Full survey: `docs/TLW-VISITORS-MODEL.md`.

## Ground truth
See `TLW-VISITORS-MODEL.md`. The audit's "9 tables / 127 cols" is wrong: 10 objects, base 102 /
view 57, plus the entity hidden in `Employees`. WM has **zero** Visitors code in `src/`
(only build artifacts match). Domain survives invariant 3; the kiosk hardware does not.

## Legacy behaviour (what we are replacing)
Pre-registered or walk-in visitor → `Employee(Visitor)` with host, expected/actual times, site,
company, car plate. Sign-in/out land as `sy_ac_data` rows; `VisitorSessions` links them by
`SessionGuid`. A dynamic questionnaire (`VisitorFormQuestions`) can capture a signature and
**deny entry** (`AnswerBlocksSignIn`). Host/visitor emails + ICS invites on check-in/out. The
Fire Report merges the live visitor roll for emergency mustering.

## Keep / Improve / Invert / Drop
Full table in `TLW-VISITORS-MODEL.md`. Headlines:
- **Keep** the visit record, sessions, dynamic form (incl. entry-denial), notes, host/ICS emails.
- **Invert** the entity off `Employees` into a first-class `Visitor` owned by the Visitors module
  (WM invariant 1); fail-closed the report scope filters.
- **Improve** `VisibleVisitorTypeIds` CSV → join table; `VisitorCustomField1..10` → the dynamic form.
- **Drop** the Synergy Touch kiosk, fingerprint, badge/card printing, thermal, proximity cards,
  ANPR — all hardware (invariant 3). Replaced by a web reception surface + phone self-check-in.

## Edge cases
See `TLW-VISITORS-MODEL.md` "Edge cases" (double sign-in, no-guid sign-out fallback, guid collision,
auto sign-out, pre-registered no-show must not read as on-site, entry-denied roll status, null host,
retention window). These are the required test cases.

## Target design in WM
New module `src/Modules/Visitors` (schema-isolated, invariant 1). A `Visit` aggregate
(visitor details + host + site + expected/actual times + status). Sign-in/out raise punch/attendance
**events on Kafka** (§4) rather than writing device-log rows; the muster projection consumes them.
REST endpoints (§ API-first) for pre-registration, check-in, check-out, activity list, settings, and
the form builder. Host notification via the Notifications module (§9) over events — not a direct
call. Angular reception + activity screens under the existing Visitors screen node
(`SCREEN-TREE.md:145`). Mustering (phase 6b) consumes the visitor roll via a published contract, not
a cross-module DB read.

## Out of scope for this plan
- The kiosk/Synergy Touch app, fingerprint, badge printing, proximity cards, thermal, ANPR (dropped).
- The mustering/Fire Report module itself (phase 6b) — this plan only **publishes** the roll it needs.
- Payroll/EPOS/student — unrelated buckets.
- "Deliveries" (SCREEN-TREE) — no backing table; excluded pending a product decision.

## Portions

### [ ] P1 — Visitor + Visit aggregate and migration
**Touches:** `src/Modules/Visitors/**` (new module), `WM.sln`, migration, `Visitors.Tests` (new).
**Done when:** a `Visit` (visitor identity, host id, site, company, car plate, expected window,
status, pre-registered flag) persists with its own schema; module is wired into the API host.
**Tests:** aggregate invariants; migration applies; no `Employees` overload.
**Risk:** medium

### [ ] P2 — Pre-registration + host + REST
**Touches:** Visitors module service + endpoints, contracts, tests.
**Done when:** an admin/host can pre-register a visit (mints a session token), list and cancel it,
via authorized REST endpoints.
**Tests:** null-host allowed unless type requires host; no-show never reads as on-site.
**Risk:** low

### [ ] P3 — Check-in / check-out as events
**Touches:** Visitors service, Kafka event contracts (§4), tests.
**Done when:** check-in and check-out transition a Visit and publish attendance events; sessions
open/close by token.
**Tests:** double sign-in refused; guid-collision fail-closed; no-token sign-out fallback rule
(explicit, chosen not inherited); auto sign-out closes stale visits.
**Risk:** medium

### [ ] P4 — Dynamic check-in form (builder + capture, entry-denial)
**Touches:** form question/option/answer entities + join table for type visibility, endpoints, tests.
**Done when:** questions with conditional display and required flags render; answers persist a
signature; an answer flagged `blocks entry` denies check-in.
**Tests:** conditional display; required enforcement; blocked answer denies check-in and roll status
is correct.
**Risk:** medium

### [ ] P5 — Host & visitor notification + ICS
**Touches:** Visitors → Notifications (§9) event contract, ICS generation, tests.
**Done when:** check-in/out and pre-registration emit "visitor arrived/left" host notifications and
an optional calendar invite, gated by settings.
**Tests:** settings gate each notification; ICS attached only when configured.
**Risk:** low

### [ ] P6 — Muster roll contract + reception/activity UI
**Touches:** published `OnSiteVisitors` contract for mustering; Angular reception + activity screens.
**Done when:** the live on-site visitor roll is queryable via a contract (for phase-6b mustering),
and reception staff can see/manage today's activity in the portal.
**Tests:** roll excludes no-shows and checked-out visits, includes checked-in; scope fail-closed;
first Angular `*.spec.ts` for the reception screen.
**Risk:** medium

## Open questions for the user
1. **Entry-denied roll status.** When a form answer blocks check-in, is the visitor "on site" for
   mustering? Legacy still lists them via the activity view; WM should decide deliberately.
2. **Sign-out fallback.** Legacy's no-token sign-out closes "the first open session today". Keep, or
   require the token (safer, but breaks the walk-in-with-no-phone case)?
3. **Re-sequencing.** Given the mustering dependency, should Visitors (P1–P3 at least) precede or run
   alongside Safety phase-6b rather than sit in phase 8? This is a roadmap change — proposed, not
   applied (`ARCHITECTURE.md` §14).
4. **Reception surface.** Confirm the model: web reception (staffed tablet as a plain browser, not a
   hardware kiosk) + phone self-check-in. No dedicated kiosk firmware, consistent with invariant 3.
</content>
