# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Active plan

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`.
Approved by user 2026-08-03, all 5 portions. Closes Phase 1b. Blocks 1c and 2: every later
query depends on scope being right.

**Next portion:** P2 — persist composed scope + migrate existing groups. Unblocked: P1 landed on
`master` in PR #12. **High risk** — the migration must not widen any existing group's employee
set. The baseline it must reproduce, measured against the running stack on 2026-08-03:
admin sees 40 employees, manager sees 16, out-of-scope reads return 404.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 001 | Compositional data scope (closes Phase 1b) | 5 (P1 done, P2 next) | in-progress |
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

## In flight

| PR | What | Status |
|---|---|---|
| [#13](https://github.com/00008550/WM/pull/13) | Clocking model survey, schema sweep, plan 002, hardened surveyor | open |
| [#14](https://github.com/00008550/WM/pull/14) | .NET 10 LTS, EF Core 10, Angular 22 | open — MassTransit v9 licence decision pending |

`docs/scope-model-corrections` is fully contained in `master` and can be deleted. See the
stacking note in `CLAUDE.md` → Git for why #11 went astray.

## Conventions

- **Status:** `draft` → `approved` (user signed off) → `in-progress` → `in-review` → `merged`.
- A plan is only executable when its status is `approved`. `wm-builder` refuses `draft`.
- One portion = one branch = one PR. Never batch portions into a single PR.
- `wm-reviewer` updates the Shipped table when it opens a PR, and again when you merge.
