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

## Active plan

**003 — Enforcement gaps** (`003-enforcement-gaps.md`) — `in-progress`, approved 2026-08-04, all 4
portions, and **ordered ahead of 001 P3 by the user**. A1 and A2 are defects in running code; 001 P3
corrects a model no endpoint consults yet. Fix what is bleeding first.

**P1 passed review 2026-08-05 and is open as [#18](https://github.com/00008550/WM/pull/18)** — A1 is
closed: the punch feed addresses scope groups instead of broadcasting, the hub requires
`attendance.view`, and a scope change re-groups the user's open sockets within the session.

**Next portion:** 003 P2 — fail closed by default, and scope the writes (A2).

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`, paused
after P2. Approved by user 2026-08-03, all 5 portions. **P3, P4 and P5 were amended in place by
the audit** (behaviour-change list for P3; Building removed from P4; departments-endpoint
prerequisite for P5) — no portion was added or removed, and the approval still stands as given.
Resumes at P3 once 003 P1–P2 have landed.

**⚠️ The baseline in this file was wrong.** It said *"admin sees 40 employees, manager sees 16"*.
The seeded manager belongs to **no security group** — `DemoUserSeeder.cs` assigns roles and never
groups — so today they resolve to `Self` and see **1**, not 16. Admin sees 40 via the "All
employees" system group (`IdentitySeeder.cs:82-92`). Re-measure against the running stack before
P3 changes resolution, or the regression test asserts a number nobody produced.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 003 | Enforcement gaps found by the phase audit | 4 (P1 done, P2 next) | **in-progress — active** |
| 001 | Compositional data scope (the model half of Phase 1b) | 5 (P1–P2 done, P3 next) | in-progress, paused after P2 |
| 002 | The Clocking daily aggregate (Phase 2 prerequisite) | 5 | **draft — blocked on a design decision** |

**Ordering, decided by the user 2026-08-04:** 003 P1 and P2 run before 001 P3. They are defects in
running code rather than missing capability, and they are independent of 001 — 003 touches the API
host and the People endpoints, 001 touches the resolver and the filter.

Suggested but not yet written, from the schema sweep (`docs/TLW-SCHEMA-SWEEP.md`):
**004** — tariffs, employee contracts, calculation settings and the counter formula language;
**005** — per-install configuration (`SoftwareMainOptions`).
*(Renumbered from 003/004 by the audit, which took 003.)*

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
