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
  `DataContext` disposals to find connection leaks. It was cited in **five** places (three docs,
  two code comments) as the evidence for two design decisions. Corrected in the docs; the two
  code comments (`ScopeModel.cs:223`, `SecurityGroupEndpoints.cs:61`) need a one-line fix from
  whichever portion next touches those files.

**One decision is now waiting on the user** — `TLW-AUTHORIZATION-MODEL.md` §13–§14: adopt TLW's
model **fully** (one object *and* one group per user, option **A**, recommended) or keep WM's
multi-membership and write down the two precedence rules legacy never needed (option **B**).
The concrete §4 replacement text is drafted for A. **§4 itself was not edited** — it is a design
section, so the change is a proposal.

## Active plan

**003 — Enforcement gaps** (`003-enforcement-gaps.md`) — `in-progress`, approved 2026-08-04, and
**ordered ahead of 001 P3 by the user**. A1 and A2 are defects in running code; 001 P3 corrects a
model no endpoint consults yet. Fix what is bleeding first.

**P1 passed review 2026-08-05 and is open as [#18](https://github.com/00008550/WM/pull/18)** — A1 is
closed: the punch feed addresses scope groups instead of broadcasting, the hub requires
`attendance.view`, and a scope change re-groups the user's open sockets within the session.

**P2 was split into P2a + P2b on 2026-08-05 at the user's direction** — same scope, two branches.
The plan now has 5 portions; the approval as given still covers it.

**Next portion:** **003 P2a — fail closed by default** (`FallbackPolicy`). Identity module only,
correct under every access model, independent of everything below.

**Then 003 P2b — scope the employee writes.** It was to wait on a measurement of whether legacy's
read and write scope are distinct decisions. **They are not** — same TVF, one carve-out — so P2b
is cleared: reuse `WithinScope`, do not invent a write-scope model, and leave a named
`CanEditOwnRecord` seam for legacy's `CanModifySelf` should the user want it.

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`, paused
after P2. Approved by user 2026-08-03, all 5 portions. **P3 is now ⛔ on hold** — it implements
union-across-groups, and the survey measured that legacy has no union because it has no
multi-membership. P3 is not wrong so much as *undecided*: option A shrinks it to a lookup, option
B keeps it but adds two precedence rules the plan does not have. **P4 was amended** — legacy's
`ByDepartments` and `ByEmployees` are mutually exclusive on save (`UpdateRole:872-883`), so P4's
"intersecting with the other dimensions" is a WM improvement with no legacy precedent, which the
builder needs to know before hunting for one. No portion added or removed; the approval stands.

**⚠️ The baseline in this file was wrong.** It said *"admin sees 40 employees, manager sees 16"*.
The seeded manager belongs to **no security group** — `DemoUserSeeder.cs` assigns roles and never
groups — so today they resolve to `Self` and see **1**, not 16. Admin sees 40 via the "All
employees" system group (`IdentitySeeder.cs:82-92`). Re-measure against the running stack before
P3 changes resolution, or the regression test asserts a number nobody produced.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 003 | Enforcement gaps found by the phase audit | 5 (P1 done, **P2a next**) | **in-progress — active** |
| 001 | Compositional data scope (the model half of Phase 1b) | 5 (P1–P2 done, **P3 ⛔ on hold**) | in-progress, paused after P2 |
| 004 | Screen-level rights (the second half of Phase 1b) | 4 | **draft — not approvable until §4 is decided** |
| 002 | The Clocking daily aggregate (Phase 2 prerequisite) | 5 | **draft — blocked on a design decision** |

**Ordering, decided by the user 2026-08-04:** 003 P1 and P2 run before 001 P3. They are defects in
running code rather than missing capability, and they are independent of 001 — 003 touches the API
host and the People endpoints, 001 touches the resolver and the filter.
**Reinforced 2026-08-05:** 003 P2a and P2b are correct under either access model, so they can
proceed while the §4 question is open. 001 P3 cannot.

Suggested but not yet written, from the schema sweep (`docs/TLW-SCHEMA-SWEEP.md`):
**005** — tariffs, employee contracts, calculation settings and the counter formula language;
**006** — per-install configuration (`SoftwareMainOptions`).
*(Renumbered again 2026-08-05: 004 is now the screen-rights plan.)*

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
| 003 | P1 — scope the realtime punch feed | [#18](https://github.com/00008550/WM/pull/18) | open — reviewed and passed 2026-08-05 |

## In flight

| PR | What | Status |
|---|---|---|
| [#18](https://github.com/00008550/WM/pull/18) | 003 P1 — scoped punch feed, hub permission, in-session scope revocation | open — review passed round 2, targets `master` |

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
