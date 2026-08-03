# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Active plan

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`.
Approved by user 2026-08-03, all 5 portions. Closes Phase 1b. Blocks 1c and 2: every later
query depends on scope being right.

**Next portion:** P1 — compositional scope model in SharedKernel (+ the repo's first test project).

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 001 | Compositional data scope (closes Phase 1b) | 5 (P1 next) | in-progress |

## Shipped

| Plan | Portion | PR | Merged |
|---|---|---|---|
| — | — | — | — |

## Conventions

- **Status:** `draft` → `approved` (user signed off) → `in-progress` → `in-review` → `merged`.
- A plan is only executable when its status is `approved`. `wm-builder` refuses `draft`.
- One portion = one branch = one PR. Never batch portions into a single PR.
- `wm-reviewer` updates the Shipped table when it opens a PR, and again when you merge.
