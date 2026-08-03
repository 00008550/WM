---
description: Drive one step of the WM surveyor → builder → reviewer cycle
---

Read `docs/plans/STATE.md`, work out where the cycle currently stands, and drive the next step. Arguments (optional): $ARGUMENTS — a plan number, a portion, or `status` to just report.

You are the **orchestrator**. Subagents cannot call each other, so every handoff goes through you and through `docs/plans/`. Dispatch via the Agent tool, one at a time, and read each agent's report before deciding the next move.

## State machine

| Current state | Next action |
|---|---|
| No plan, or active plan fully shipped | `wm-surveyor` → writes a new `draft` plan. **Stop and show the user the portion list for approval.** |
| Plan is `draft` | Stop. Only the user marks a plan `approved`. Show them what needs deciding. |
| Plan `approved`, unchecked portion, no branch in flight | `wm-builder` Mode A on the next unchecked portion only. |
| Portion built, not reviewed | `wm-reviewer` on that branch. |
| Reviewer says **fix first** | `wm-builder` Mode B with the findings, same branch. Then `wm-reviewer` again. |
| Reviewer says **rethink** | Back to `wm-surveyor` to revise the plan — not to the builder. Tell the user why. |
| Reviewer says **ship** | It opens the PR itself. Report the URL and stop — the user reviews. |
| PR has unaddressed user comments | `wm-builder` Mode C with the PR number. Then `wm-reviewer` again. |
| PR merged | Next unchecked portion, or `wm-surveyor` if the plan is done. |

## Rules

- **One portion in flight at a time.** Never dispatch the builder for portion N+1 while N is unmerged.
- **Never skip the reviewer**, even for a one-line portion. The whole value is that the judge didn't write the code.
- Cap the builder↔reviewer loop at **3 rounds**. If a portion can't pass in three, the plan is wrong — stop, escalate to the user, and suggest `wm-surveyor` revises it. Do not grind.
- If the reviewer's findings look wrong to you, say so rather than passing them through blindly — the builder is allowed to dispute, and so are you.
- Stop and hand back to the user at every gate: plan approval, PR opened, 3-round cap, or any design change the surveyor proposed.
- Report honestly at each step: what ran, what it found, what's next. If a build or test failed, show the output.
