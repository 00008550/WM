# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Active plan

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`.
Approved by user 2026-08-03, all 5 portions. Closes Phase 1b. Blocks 1c and 2: every later
query depends on scope being right.

**Next portion:** P2 — persist composed scope + migrate existing groups. **High risk**: the
migration must not widen any existing group's employee set. Blocked until PR #12 merges.

## Queue

| Plan | Title | Portions | Status |
|---|---|---|---|
| 001 | Compositional data scope (closes Phase 1b) | 5 (P1 done, P2 next) | in-review |

## Shipped

| Plan | Portion | PR | Merged |
|---|---|---|---|
| — | workflow + doc corrections | [#10](https://github.com/00008550/WM/pull/10) | ✅ merged to master |
| 001 | P1 — compositional scope model | [#11](https://github.com/00008550/WM/pull/11) | ⚠️ merged to the wrong branch — superseded by #12 |
| 001 | P1 — compositional scope model | [#12](https://github.com/00008550/WM/pull/12) | open, targets master |

Once #12 merges, `docs/scope-model-corrections` is fully contained in `master` and should be
deleted. See the stacking note in `CLAUDE.md` → Git for why #11 went astray.

## Conventions

- **Status:** `draft` → `approved` (user signed off) → `in-progress` → `in-review` → `merged`.
- A plan is only executable when its status is `approved`. `wm-builder` refuses `draft`.
- One portion = one branch = one PR. Never batch portions into a single PR.
- `wm-reviewer` updates the Shipped table when it opens a PR, and again when you merge.
