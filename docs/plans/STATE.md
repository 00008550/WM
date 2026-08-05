# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Phase audit — 2026-08-04

A retrospective audit of **everything merged so far** ran before the next portion was started.
Findings: [`../PHASE-AUDIT.md`](../PHASE-AUDIT.md). Headline:

- **Two blocking findings, both in code merged before this cycle existed.** The realtime punch
  feed broadcasts every punch to every authenticated client with no data scope and no permission
  check (`EventStreamProducers.cs:109`), and employee create/update are not scope-checked
  (`PeopleModule.cs:77-113`, `:139`).
- **Both portions that went through the cycle check out.** 001 P1 fully; 001 P2 with one process
  gap — its stated "migration up/down on a seeded DB" test was never written, and the review
  passed it anyway. Behaviour is correct; the SQL half of the migration is untested.
- **Plan 001's claims about legacy held up** on every point checked, and gained four corrections:
  a fourth fail-open, a fourth (SQL) implementation that disagrees with the C# on the fail-open
  case, three unmodelled `Role` columns, and the finding that **Building is not an employee
  attribute in legacy at all** — P4 was amended in place to drop it.
- **21 claims were verified correct** and are listed, so "not mentioned" never again reads as
  "not checked".

## Authorization survey — 2026-08-05

The user's read of the cycle: *"we don't get correct groups and we have only a vague idea about
groups and what an employee can see and edit."* A focused survey of legacy's **entire**
authorization surface answered that, and produced
[`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md) — now the single place that
question is answered, replacing five partial accounts.

Headline, all measured with `file:line`:

- **`/Groups` edits exactly one `dbo.Role`**, carrying screen rights *and* all seven managed
  dimensions *and* nav icons, dashboard widgets and exclusions. §4's "one object, mirroring TLW"
  is **correct**.
- **A legacy user belongs to exactly one of them.** §4's "several groups, union, mirroring TLW"
  is **wrong and was never TLW** (`AuthorizationService.cs:1265-1280`, `:773-786`, `:1008-1041`).
  WM's multi-membership is a WM choice nobody made deliberately.
- **Screen rights resolve deny-over-allow, default deny** (`:695-718`) — the opposite of the
  "most permissive wins" WM's docs promise.
- **`AccessType.Read` vs `.Edit` changes the answer in exactly one case** — your own record under
  `CanModifySelf = false`. Legacy's read scope *is* its write scope. `AccessType` is not
  vestigial, but it is nearly so.
- **`Role.IsDepartmentOnly` is dead** — written, localized, never read. Settled; do not model it.
- **`dbo.WebPages` is a localization table** and `FormAccess`/`SiteItemPermission` are C# types.
  The screen-rights store is `dbo.AccessControlEntry`. §13 named the wrong things for three
  revisions.
- **Twelve fail-opens in the in-scope surface, not four.**
- **`DataAccessScopeDiagnostics` has nothing to do with authorization** — it counts
  `DataContext` disposals to find connection leaks. It was cited as the evidence for two design
  decisions in three docs and — **corrected 2026-08-05 while planning 005** — **four** code
  comments, not two: `ScopeModel.cs:220-223`, `SecurityGroupEndpoints.cs:61-62`,
  `SecurityGroup.cs:10-13` (which also repeats C5 and asserts the orthogonality option A has now
  reversed) and `EmployeeScopeExtensions.cs:9-11`. A fifth, milder instance sits in
  `DataScope.cs:5-6`, in a file 005 P4 deletes. Each is folded into the *Touches* of the 005
  portion that opens that file.

### Decisions taken (user, 2026-08-05)

1. **Option A — one object, one membership.** A security group carries screen rights, data scope,
   a mode (`Normal` / `SelfOnly`) and `CanEditOwnRecord`; a user belongs to exactly one, non-null.
   `ARCHITECTURE.md` §4 **has been rewritten** to match, including *why* exclusivity is part of the
   design rather than an accident of it. Plan 004's ⛔ block is cleared.
   *(005 models the mode as `ScopeRuleKind.Self` rather than a separate column — under exclusivity
   a mode is a rule kind, and two fields for one meaning is the shape the survey criticised. Raised
   as 005 open question 1 in case you want the column.)*
2. **Adopt `CanEditOwnRecord`** (legacy `CanModifySelf`): a user may see their own record without
   being able to edit it. 003 P2b already carries the named seam for it.

**What A costs, and what it does not.** 001 P1/P2 survive intact — intersection-within-a-group is
still the right shape and is shipped under test. What dies is the *union across groups*: D2, and
with it 001 P3's premise. `003 P1`'s `AttendanceAudience` simplifies but is not wrong.

### The refactor is now planned — 005, drafted 2026-08-05

[`005-one-membership.md`](./005-one-membership.md) — `draft`, 6 portions. It **supersedes 001 P3**
(the resolver rewrite and the union deletion are one edit to one method; splitting them would mean
writing a one-membership resolver that still switches on `ScopeKind`, then rewriting it).

Measured blast radius: **22 source files, 5 frontend files, 7 test files, 3 migrations.** It is a
large plan and the plan says so.

Four things it settles rather than defers:

- **The collapse rule: collapse by resolved *effect*, not by stored configuration.** A
  *configuration* union across groups is genuinely unrepresentable in one group (X on Site S1 and Y
  on Department D9 mean "S1 **or** D9"; one group intersects its constraints). The *effect* always
  is — because `DataScopeResolver.cs:50-69` **already** reduces every user to a single
  `DataScopeKind` plus one dimension's id set, and every consumer reads it that way. So the mapping
  is total and **no user's combination lacks a clean answer.** One group is materialised per
  distinct (permission set, scope) pair, deduped, named from its contributing combinations, with a
  report row per user. The one thing *not* preserved is a membership that was already doing
  nothing — the old rows survive to P6 and the report survives forever, so an admin can re-grant
  deliberately. Restoring access nobody had is a product decision, not a migration's job.
- **`EffectiveDataScope`/`DataScopeKind` die; `ScopeConstraint`/`ScopeRule`/intersection survive
  untouched.** `DataScope` collapses from a list of rules to one. 001 P3's three sanctioned
  deviations: #1 is **cancelled**, #2 and #3 are **resolved by construction**.
- **Ordering:** 003 P2a and P2b both land **before** 005 (P2b is a live defect and carries the
  `CanEditOwnRecord` seam 005 P4 wires up); 003 P3 either side; 001 P4/P5 and 004 after.
- **Reversibility:** P1–P5 are genuinely reversible because they are purely additive — the four
  old tables are read and never written, exactly as 001 P2 was reversible. **P6 drops them and is
  one-way**, which is why it is a separate portion sequenced last. Its `Down` recreates empty
  tables and says so rather than implying a restore.

Two process facts it also records: **`ClaimTypes.Role` is minted at `TokenService.cs:43` and read
nowhere** in `src/**` or `frontend/**`, so the JWT change is a deletion rather than a redesign; and
there is **no Postgres test harness in the repo** (`WM.Modules.Identity.Tests` is EF InMemory),
which is why 001 P2's promised migration test was never written — so 005 does the collapse in a
tested C# runner rather than untestable SQL.

## Active plan

**003 — Enforcement gaps** (`003-enforcement-gaps.md`) — `in-progress`, approved 2026-08-04, and
**ordered ahead of the model work by the user**. A1 and A2 are defects in running code; the model
work corrects something no endpoint consults yet. Fix what is bleeding first. *(The user's 2026-08-04
ordering named "001 P3"; that portion is now 005 P4 and the ordering carries over unchanged.)*

**P1 passed review and merged 2026-08-05 as [#18](https://github.com/00008550/WM/pull/18)
(`3389525`)** — A1 is closed: the punch feed addresses scope groups instead of broadcasting, the
hub requires `attendance.view`, and a scope change re-groups the user's open sockets within the
session. §13's `⚠️ built but unscoped` row for the realtime feed is corrected accordingly.

**P2 was split into P2a + P2b on 2026-08-05 at the user's direction** — same scope, two branches.
The plan now has 5 portions; the approval as given still covers it.

**P2a passed review 2026-08-05 and is open as [#21](https://github.com/00008550/WM/pull/21)** — A2 is
closed: an endpoint mapped without `RequireAuthorization` now answers 401, the anonymous surface is
exactly four transports (`/api/auth/login`, `/refresh`, `/logout`, `/health`) and a test asserts that
list against the composed host, so adding a fifth is a decision with a reviewer attached.

**Next portion:** **003 P2b — scope the employee writes.** People module; see the clearance note
below.

**Then 003 P2b — scope the employee writes.** It was to wait on a measurement of whether legacy's
read and write scope are distinct decisions. **They are not** — same TVF, one carve-out — so P2b
is cleared: reuse `WithinScope`, do not invent a write-scope model, and leave a named
`CanEditOwnRecord` seam for legacy's `CanModifySelf`. **The user has since adopted
`CanEditOwnRecord`** (2026-08-05), so that seam is no longer speculative: 005 P1 adds the group
flag and 005 P4 wires it to this predicate. P2b must therefore leave a real, named seam rather
than hard-coding today's behaviour, and should land **before** 005 P4.

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`, paused
after P2. Approved by user 2026-08-03, all 5 portions. **P3 is now ⏹ superseded by 005 P4** — the
union it implemented cannot exist under one membership, and "resolve one group" and "honour the
composed rule" are the same edit to `DataScopeResolver.cs:25-70`. P3 was amended in place on
2026-08-05 with a table of where each of its parts went. **P4 and P5 stand, and move behind 005**
in the queue. P4 was also amended earlier — legacy's `ByDepartments` and `ByEmployees` are mutually
exclusive on save (`UpdateRole:872-883`), so P4's "intersecting with the other dimensions" is a WM
improvement with no legacy precedent, which the builder needs to know before hunting for one. P5
additionally inherits the drop of the legacy single-kind scope columns, which 005 deliberately
leaves alone because they are still the group editor's contract.

**⚠️ The baseline in this file was wrong, and 005 P3 is where it gets fixed.** It said *"admin sees
40 employees, manager sees 16"*. The seeded manager belongs to **no security group** —
`DemoUserSeeder.cs:37` assigns a role and never a group — so today they resolve to `Self` and see
**1**, not 16. Admin sees 40 via the "All employees" system group (`IdentitySeeder.cs:82-92`).
005 P3 gives the demo manager a real `Site managers` group and **re-measures the number against
the running stack**; do not copy 16 forward into any test.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 003 | Enforcement gaps found by the phase audit | 5 (P1, P2a done, **P2b next**) | **in-progress — active** |
| 005 | One object, one membership (the Identity refactor option A requires) | 6 | **draft — awaiting approval** |
| 001 | Compositional data scope (the model half of Phase 1b) | 5 (P1–P2 done, **P3 ⏹ superseded by 005 P4**) | in-progress, paused after P2 |
| 004 | Screen-level rights (the second half of Phase 1b) | 4 | draft — §4 now decided; **needs 005 to land first** |
| 002 | The Clocking daily aggregate (Phase 2 prerequisite) | 5 | **draft — blocked on a design decision** |

**Ordering, decided by the user 2026-08-04:** 003 P1 and P2 run before 001 P3. They are defects in
running code rather than missing capability, and they are independent of 001 — 003 touches the API
host and the People endpoints, 001 touches the resolver and the filter.
**Reinforced 2026-08-05:** 003 P2a and P2b are correct under either access model, so they can
proceed while the §4 question is open. 001 P3 cannot — and it no longer exists (005 P4).

**Full sequence recommended by 005:**
**003 P2a → 003 P2b → 005 P1…P5 → 003 P3 → 001 P4 → 001 P5 → 005 P6 → 004.**
003 P2a is model-independent and is next regardless. 003 P2b lands before 005 P4 because it is a
live defect and because it carries the `CanEditOwnRecord` seam that 005 P4 wires to the real flag —
built afterwards it would be written against a type that is moving under it. **005 P6 is the only
irreversible portion and is deliberately detached from the rest of its plan's cadence.**

**Next portion:** **003 P2b — scope the employee writes.** P2a is built, reviewed and open as #21.

Suggested but not yet written, from the schema sweep (`docs/TLW-SCHEMA-SWEEP.md`):
**006** — tariffs, employee contracts, calculation settings and the counter formula language;
**007** — per-install configuration (`SoftwareMainOptions`).
*(Renumbered again 2026-08-05: 004 is the screen-rights plan and 005 is the one-membership
refactor, so the two schema-sweep suggestions shift to 006 and 007.)*

## Shipped

| Plan | Portion | PR | Status |
|---|---|---|---|
| — | workflow + doc corrections | [#10](https://github.com/00008550/WM/pull/10) | ✅ merged to master |
| 001 | P1 — compositional scope model | [#11](https://github.com/00008550/WM/pull/11) | ⚠️ merged to the wrong branch — superseded by #12 |
| 001 | P1 — compositional scope model | [#12](https://github.com/00008550/WM/pull/12) | ✅ merged to master |
| — | clocking survey + schema sweep + plan 002 | [#13](https://github.com/00008550/WM/pull/13) | ✅ merged to master |
| — | .NET 10 LTS, EF Core 10, Angular 22 | [#14](https://github.com/00008550/WM/pull/14) | ✅ merged to master |
| — | coverage audit | [#15](https://github.com/00008550/WM/pull/15) | ✅ merged to master |
| 001 | P2 — persist composed scope + migrate | [#16](https://github.com/00008550/WM/pull/16) | ✅ merged to master (`a9fdcc3`) |
| — | phase audit + plan 003 + doc corrections | [#17](https://github.com/00008550/WM/pull/17) | ✅ merged to master (`2557921`) |
| 003 | P1 — scope the realtime punch feed | [#18](https://github.com/00008550/WM/pull/18) | ✅ merged to master (`3389525`) |
| — | authorization survey + §4 rewrite + the one-membership ruling | [#19](https://github.com/00008550/WM/pull/19) | ✅ merged to master (`0613836`) |
| — | plan 005 + 001 P3 superseded + §4A corrections | [#20](https://github.com/00008550/WM/pull/20) | ✅ merged to master (`3f05071`) |
| 003 | P2a — fail closed by default (`FallbackPolicy`) | [#21](https://github.com/00008550/WM/pull/21) | open — reviewed and passed 2026-08-05 |

## In flight

| PR | What | Status |
|---|---|---|
| [#21](https://github.com/00008550/WM/pull/21) | 003 P2a — `FallbackPolicy`, `/health` anonymous, endpoint inventory test | open — review passed, conflict with #20 resolved |

`master` is at `3f05071`.

`docs/scope-model-corrections` is fully contained in `master` and can be deleted. See the
stacking note in `CLAUDE.md` → Git for why #11 went astray.

## Conventions

- **Status:** `draft` → `approved` (user signed off) → `in-progress` → `in-review` → `merged`.
- A plan is only executable when its status is `approved`. `wm-builder` refuses `draft`.
- One portion = one branch = one PR. Never batch portions into a single PR.
- `wm-reviewer` updates the Shipped table when it opens a PR, and again when you merge.
- **The review pass is not optional and the builder never opens its own PR.** P2 was built,
  self-assessed and published without one; the review was run afterwards, retroactively. On P1
  that same step caught a real defect, so it earns its place. If a portion reaches a PR without a
  review recorded here, that is a process failure worth treating as a finding.
- **A portion is not done until the tests its own plan named exist.** The 2026-08-04 audit found
  001 P2 shipped without the "migration up/down on a seeded DB" test its plan required, and the
  review passed it. The behaviour turned out to be correct, so nothing broke — this time. Check
  the portion's **Tests:** line against the test files, item by item.
- **Count transports, not routes.** "29 endpoints" excluded the SignalR hub, and the hub was the
  one thing with no authorization scoping at all. Anything a client can reach is surface.
- **A WM-only capability has no row in the coverage matrix, so nothing prompts anyone to check
  it.** The realtime feed is better than legacy — legacy has no push at all — which is exactly
  why it escaped scrutiny for three phases. §13 now carries rows for WM additions.
- **"Mirroring TLW" is a claim, and it needs a `file:line` like any other.** §4 asserted that
  TLW users belong to several groups whose access unions. TLW does the opposite, and the claim
  survived into two plans, the screen tree, the coverage matrix and two code comments before
  anyone opened `GetUserRole`. Ditto `DataAccessScopeDiagnostics`, cited five times as evidence
  for two design decisions, which turned out to be a connection-leak counter. **If a doc says
  legacy does X, either it cites the line or it is a hypothesis.**
- **Check what a legacy identifier actually names before planning against it.** Three of the four
  things §13 listed as the screen-rights schema were not tables: `WebPages` is localization,
  `FormAccess` is a static class, `SiteItemPermission` is a DTO. One `Glob` for the file name
  would have caught it in any of three earlier passes.
