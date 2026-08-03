# WM — working agreement

**Workforce Management Platform** — .NET 9 + Angular rebuild of the legacy TLW (Time & Labour Workforce) suite.
Repo: `E:\Work\GitProjects\WM` → `github.com/00008550/WM` (private). Default branch: `master`.

## Source of truth

| Question | Read |
|---|---|
| What should we build / how is it designed? | `docs/ARCHITECTURE.md` (§13 = TLW→WM coverage matrix, §14 = roadmap) |
| What does the legacy product actually do? | `docs/TLW-INVENTORY.md` |
| What screens exist? | `docs/SCREEN-TREE.md` |
| What is being built right now? | `docs/plans/` — see below |
| Where is the legacy code? | `E:\Tlw` (read-only reference: `Source`, `Database`, `Documentation`) |

Never contradict `ARCHITECTURE.md` silently. If a task requires a design change, propose it and stop.

## Encoding — read this before reading anything

This machine's ANSI codepage is **windows-1251**. The repo's docs are **UTF-8**.

- **Use the Read tool** for files, not `Get-Content`. PowerShell 5.1 defaults to the ANSI codepage and will render every em-dash and box-drawing character as garbage (`вЂ”`, `в”Њ`). That garbage is a *display artifact* — do not "fix" it in the file.
- If you must use PowerShell on a text file, pass `-Encoding utf8` to both `Get-Content` and `Out-File`/`Set-Content`.
- Use the Write/Edit tools for file changes. They write UTF-8.

## Architecture invariants

1. **Modular monolith.** Modules in `src/Modules`, one deployable API. A module owns its schema and reaches other modules **only via contracts/events** — never a direct cross-module DB read, project reference into another module's internals, or shared entity type.
2. **API-first.** Every capability is a REST endpoint the future Flutter app can call. No logic reachable only from the Angular SPA.
3. **No physical devices.** Suprema / SyFace / Salto / ANPR / thermal / fingerprint / biometrics / Device Gateway are **out of scope** (decision 2026-07-20). The phone is the only "device"; punches come from web + Flutter, geofenced and offline-queued.
4. **Messaging split.** RabbitMQ = commands/jobs. Kafka = event stream (punches, clockings, audit, notifications).
5. **Security from day one.** RBAC + fine-grained permissions, audit trail, OWASP hardening. No endpoint ships without an authorization policy.
6. **Plugins** (payroll exports, connectors, custom reports, notification channels) go through `src/PluginSdk` — the legacy business model is ~60 per-customer payroll plugins.

## Layout

```
src/Api           API host + gateway composition
src/Modules       domain modules (schema-isolated)
src/Worker        jobs, plugin execution, notifiers
src/Licensing     signed license keys, feature packages
src/PluginSdk     plugin contracts
src/SharedKernel  cross-cutting primitives only — not a dumping ground
frontend/portal   Angular SPA ("Control Room" design system, Tailwind)
```

## Commands

```
dotnet build WM.sln                      # from repo root
dotnet test                              # NOTE: 0 test projects exist today
cd frontend/portal && npm run build
cd frontend/portal && npm run test       # ng test
```

**There are no test projects yet.** The first slice that adds real behaviour creates `src/<Area>/…Tests/` (xUnit) and wires it into `WM.sln`. "Tests pass" is meaningless until then — say "no tests exist for this" rather than reporting a green `dotnet test`.

For UI work, run the portal via preview_start (`.claude/launch.json` → `portal`, port 4200) and verify in the browser. Never ask the user to check manually.

## Plans — how work is queued

`docs/plans/` holds numbered plan files: `NNN-<slug>.md`. Each plan is one coherent chunk of the roadmap, broken into **portions** — each portion small enough to build, test, and review on its own.

- `docs/plans/STATE.md` is the index: which plan is active, which portion is next, what shipped.
- Portions are checkboxes. A portion is checked only after review passes.
- Plans are written by `wm-surveyor`, executed one portion at a time by `wm-builder`, and judged by `wm-reviewer`.

## Git

- `master` is the default branch — **never commit directly to it.** Branch as `feat/<plan>-<portion>` or `fix/<plan>-<portion>`.
- Commit or push only when the workflow calls for it. PRs are opened by `wm-reviewer` after a passing review.
