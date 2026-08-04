# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Active plan

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`.
Approved by user 2026-08-03, all 5 portions. Closes Phase 1b. Blocks 1c and 2: every later
query depends on scope being right.

**Next portion:** P3 — resolver + query filter honour the composed rule. This is where D1 and D2
are actually fixed and the legacy `DataScopeKind` / `EffectiveDataScope` pair is deleted. Blocked
until PR #16 merges.

The baseline every portion must reproduce, measured against the running stack:
**admin sees 40 employees, manager sees 16, out-of-scope reads return 404.** From P3 onward this
stops being a baseline and becomes a regression test — P3 changes resolution, so these numbers
must hold *because* the new model is right, not because the old one is still in charge.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 001 | Compositional data scope (closes Phase 1b) | 5 (P1–P2 done, P3 next) | in-progress |
| 002 | The Clocking daily aggregate (Phase 2 prerequisite) | 5 | **draft — blocked on a design decision** |

Suggested but not yet written, from the schema sweep (`docs/TLW-SCHEMA-SWEEP.md`):
**003** — tariffs, employee contracts, calculation settings and the counter formula language;
**004** — per-install configuration (`SoftwareMainOptions`).

## Shipped

| Plan | Portion | PR | Status |
|---|---|---|---|
| — | workflow + doc corrections | [#10](https://github.com/00008550/WM/pull/10) | ✅ merged to master |
| 001 | P1 — compositional scope model | [#11](https://github.com/00008550/WM/pull/11) | ⚠️ merged to the wrong branch — superseded by #12 |
| 001 | P1 — compositional scope model | [#12](https://github.com/00008550/WM/pull/12) | ✅ merged to master |
| — | clocking survey + schema sweep + plan 002 | [#13](https://github.com/00008550/WM/pull/13) | ✅ merged to master |
| — | .NET 10 LTS, EF Core 10, Angular 22 | [#14](https://github.com/00008550/WM/pull/14) | ✅ merged to master |
| — | coverage audit | [#15](https://github.com/00008550/WM/pull/15) | ✅ merged to master |
| 001 | P2 — persist composed scope + migrate | [#16](https://github.com/00008550/WM/pull/16) | open, reviewed |

## In flight

| PR | What | Status |
|---|---|---|
| [#16](https://github.com/00008550/WM/pull/16) | 001 P2 — persist composed scope + migrate existing groups | open, review passed |

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
