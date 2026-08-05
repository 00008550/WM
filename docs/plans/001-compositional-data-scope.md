# 001 — Compositional data scope (closes Phase 1b)

Status: approved         <!-- draft → approved → in-progress → in-review → merged -->
Approved by user 2026-08-03, all 5 portions.
Roadmap: ARCHITECTURE.md §14 Phase 1b — Access model (currently ◐ in progress)
Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Security\AccessControl\RoleBasedEmployeeFilterService.cs`
- `E:\Tlw\Source\Logic\Entities\Role.cs`
- `E:\Tlw\Source\Logic\Security\AccessControl\` (IAuthorizationService `Managed*ByRole` accessors)

## Why this is the next plan

§14's own sequencing principle says every later query depends on scope being right, and
1b is the only phase still open. The survey found the current implementation is not merely
missing dimensions — **its shape cannot express the configurations legacy customers already
run**, and it silently drops granted access. Every phase from 1c onward adds query surface
on top of it. This is the cheapest it will ever be to fix.

## Legacy behaviour (what we are replacing)

`RoleBasedEmployeeFilterService` switches on `Role.ManagementType`:

- **`ByDepartments`** — applies managed departments **and then** managed locations as
  successive `.Where()` calls. That is an **intersection**: "these departments, at these
  locations". Confirmed at `RoleBasedEmployeeFilterService.cs:34-46`.
- **`ByEmployees`** — filters to an explicit list of managed employee ids
  (`RoleBasedEmployeeFilterService.cs:48-54`). **WM has no equivalent of this at all**, and
  it is how legacy expresses "this manager owns this named set of people".

`IAuthorizationService` exposes **six** managed dimensions, not the two this filter honours:
`ManagedDepartments`, `ManagedLocations`, `ManagedBuildings`, `ManagedWorkingActivities`,
`ManagedCostCentres`, `ManagedCaterers` (EPOS — dropped vertical), plus `ManagedEmployees`.
Buildings / working activities / cost centres are read by *other* call sites, so enforcement
is scattered across the codebase rather than centralised.

> **Correction 2026-08-05.** This paragraph used to end *"— which is precisely why legacy needed
> `DataAccessScopeDiagnostics` to explain its own decisions."* **That is false.**
> `DataAccessScopeDiagnostics` is `DataContext` create/dispose accounting for connection leaks
> (`Logic/DAL/DataContextTracking/DataAccessScopeDiagnostics.cs:10-55`;
> `DataAccessScopeDiagnosticsController.cs:70-87` renders `ActiveScopes`, `TotalDisposalErrors`,
> `AutoDisposalRate`). Legacy ships **no** tool that explains an access decision. The scatter is
> real and centralising it is still right; the cited evidence was not.
> See [`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md) §11 C2.

**Three fail-open behaviours, all deliberate in legacy, all to be rejected in WM:**

1. `if (managedDepartments.Any())` — an **empty list applies no filter**, so a
   misconfigured role sees *everyone*. ARCHITECTURE.md already calls this out.
2. `e.EmployeeLocationId == null || managedLocations.Contains(...)` — employees with **no
   location are visible to every role**. Not previously recorded anywhere in WM's docs.
3. The `default:` branch logs `Fatal` and **returns the query unfiltered**, with a comment
   saying this is intentional "to avoid an error getting to the user"
   (`RoleBasedEmployeeFilterService.cs:56-61`).

The same filter body is duplicated three times for three marker interfaces
(`IFilterableByDepartmentEmployees`, `IEmployeeRelatedRecord`, `IEmployeeReference`).
WM's single `WithinScope()` extension is the right answer to that and stays.

## What WM has today

| | |
|---|---|
| `SharedKernel/Security/DataScope.cs` | `DataScopeKind` enum: None \| Self \| Departments \| Sites \| All |
| `Identity/Domain/SecurityGroup.cs` | one `ScopeKind` per group + `Sites` / `Departments` lists |
| `Identity/Services/DataScopeResolver.cs` | resolves across a user's groups, widest kind wins |
| `People/Services/EmployeeScopeExtensions.cs` | `WithinScope()` — switch on the single kind |

Blast radius: 7 source files (+ migrations) and 3 Angular files
(`security-groups.component.ts`, `users.component.ts`, `security-groups.api.ts`).

### Two defects this plan fixes

**D1 — intersection is unrepresentable.** `SecurityGroup.ScopeKind` is a single enum, so a
group is *either* Sites *or* Departments. "Departments A and B, but only at site C" — a
configuration `SCREEN-TREE.md` §3 calls common, and which legacy supports — cannot be
expressed. Not a missing feature; a wrong shape.

**D2 — the union across groups silently drops access.** `EffectiveDataScope` carries one
`Kind`, and resolution takes the widest. A user in group X (`Sites: [S1]`) *and* group Y
(`Departments: [D9]`) resolves to `Kind = Sites`, and **D9 is discarded** — the user loses
employees they were explicitly granted. Additive membership is documented as the intended
behaviour in `DataScope.cs:22-26`; the implementation does not deliver it.

## Target design in WM

Replace the single-kind enum with a **composed rule**:

- A group carries a set of **dimension constraints**. Within a group they combine as an
  **intersection** (legacy's department ∩ location, generalised).
- Across a user's groups, resolved scopes combine as a **union** (fixes D2, and finally
  matches the "widest grant wins" promise in the doc).
- `All` and `Self` stay as distinct kinds — they are not dimensions and must not be
  modelled as one. `None` remains the fail-closed default.
- **An empty dimension list means "constrains nothing" only when the dimension is absent
  from the group entirely.** A dimension present with zero ids matches **nothing**. This is
  the explicit inversion of legacy fail-open #1, and it needs a test naming it.
- **A null discriminator on the employee never widens visibility** — inversion of legacy
  fail-open #2. An employee with no department is invisible to a department-scoped group.
- No `default:` fall-through that returns an unfiltered query. Unknown state → empty
  (`EmployeeScopeExtensions.cs:23-24` already does this correctly; keep it).

Dimensions **in scope for this plan**: Site, Department, Employee (explicit list), Building.
Dimensions the shape must accommodate but that ship later, because the entities do not exist
yet: **CostCentre** (arrives with Rules, Phase 2) and **WorkActivity** (arrives with
Activities, Phase 7 — outside the chosen defensible-core scope). `Caterer` is dropped with
EPOS.

Adding Building in P4 is deliberate: it is a cheap real third dimension, and it proves the
extension point works before Phase 2 has to rely on it.

## Out of scope for this plan

- Cost centre and work activity dimensions (no entities yet — design for them, don't build).
- Any change to **permissions/roles**. Roles = what you may do, groups = what you may see;
  that orthogonality is settled and stays.
- Screen-level (`WebPage`/`FormAccess`) permissions.
- Backfilling `WithinScope()` onto modules beyond People/TimeAttendance.
- The audit/diagnostics *store* — only the existing diagnostics endpoint's explanation changes.

## Portions

### [x] P1 — Compositional scope model in SharedKernel
**Touches:** `SharedKernel/Security/ScopeModel.cs` (new), `WM.SharedKernel.Tests` (new), `CLAUDE.md`
**Done when:** the model expresses intersection-within-group and union-across-groups;
`CanSee` agrees with the query filter for every combination; `All`/`Self`/`None` preserved.
Pure model change — no DB, no endpoints, nothing else compiles against it yet.

**Built as:** a new `ScopeModel.cs` alongside the legacy `DataScope.cs` rather than replacing it.
`DataScopeKind` is load-bearing in `SecurityGroupService` (persistence — P2) and the diagnostics
`Explain` (P5); replacing it in P1 would pull both portions forward. The legacy pair is deleted
in P3 when the resolver and query filter move over. **No behaviour change in P1** — deliberate,
so P2's migration can be validated against today's semantics rather than a moving baseline.
**Tests:** first test project in the repo — create `src/SharedKernel/WM.SharedKernel.Tests/`
(xUnit), wire into `WM.sln`. Cover: D2's dropped-grant case (Sites group ∪ Departments group
sees both), empty-dimension-matches-nothing, null-discriminator-never-widens, unknown → empty.
**Risk:** low — no callers yet.

### [x] P2 — Persist composed scope + migrate existing groups
**Touches:** `Identity/Domain/SecurityGroup.cs`, `IdentityDbContext`, new migration, `IdentitySeeder`
**Done when:** a group persists multiple dimension constraints; the migration maps every
existing row to its exact current meaning (`ScopeKind = Sites` → a Site dimension with the same
ids, etc.) with **no widening**; migration is reversible.
**Tests:** migration up/down on a seeded DB; a round-trip test asserting a pre-migration group
resolves to the identical employee set post-migration.
**Risk:** **high** — this is the one that can silently widen access. Reviewer should treat any
row whose post-migration employee set differs from its pre-migration set as a blocking finding.

### ~~[ ] P3 — Resolver + query filter honour the composed rule~~  ·  ⏹ **SUPERSEDED by 005 P4**

> **Resolved 2026-08-05. Do not build this portion — build [`005-one-membership.md`](./005-one-membership.md) P4 instead.**
>
> P3 was put on hold because its central act was **union across a user's groups**, and the
> authorization survey measured that legacy has no union because a legacy user belongs to exactly
> one group (`AuthorizationService.cs:1265-1280` `SingleOrDefault`; `:773-786`; `:1008-1041`
> replaces on assign). **The user ruled on 2026-08-05: option A — one object, one membership.**
> `ARCHITECTURE.md` §4 (`:157-160`) is rewritten and normative.
>
> **Why superseded rather than amended.** Under option A, "the resolver honours the composed rule"
> and "the resolver reads one membership" are the same edit to the same method
> (`DataScopeResolver.cs:25-70`). Doing them separately means writing a one-membership resolver
> that still switches on `ScopeKind`, then rewriting it — two migrations of one method, and a
> middle state nobody would want to review. So the work moved wholesale into 005 P4, which
> additionally does what P3 could not: deletes `DataScopeKind`/`EffectiveDataScope`, retypes the
> People and realtime consumers, and collapses `DataScope` from a list of rules to one.
>
> **What survives from this portion, and where it went:**
>
> | P3 said | Under option A |
> |---|---|
> | intersection **within** a group | **survives unchanged** — it is the whole model now (005 P4) |
> | union **across** groups (fixes D2) | **deleted.** There is one group, so there is nothing to union. D2 cannot recur because it cannot be configured. |
> | deviation 1 — "`Self` stops being outranked" | **cancelled.** A `Constrained` rule grants exactly its constraints, so a site-scoped manager still does not see their own record unless they fall inside it — which is also what legacy's TVF does. 005 P4 asserts the cancellation. |
> | deviation 2 — site ids stop leaking between groups | **resolved by construction** — no other group to leak from. 005 P2's collapse *preserves* today's leaked ids as data, so nobody loses visibility, and the shape then makes it unrepresentable. |
> | deviation 3 — `IncludeChildSites` stops leaking between groups | same as deviation 2. The collapse carries `true` onto the generated Site constraint whenever any contributing group had it (`SecurityGroup.cs:30` defaults it to `true`). |
> | the baseline warning (manager sees **1**, not 16, because `DemoUserSeeder.cs` assigns roles and never groups) | **still true, and now owned by 005 P3**, which finally gives the demo manager a real group and re-measures the number. |
> | the 003 P1 duplicate-delivery warning | **inverted and owned by 005 P5.** Under a union the hazard was a punch delivered twice; under one membership with an *intersection* rule it becomes a punch delivered to someone who should not see it, because SignalR group fan-out is union semantics. 005 P5 settles it (evaluate the rule per open connection) and rewrites the assertion rather than deleting it. |
>
> **The user's approval of this plan stands.** This portion is not being cancelled for want of
> approval — it is being executed elsewhere, in a form the same ruling requires.

**P4 and P5 below are unaffected in substance but move in the queue: both now run after plan 005.**
P4's employee-list dimension and P5's multi-dimension editor both build on the resolver 005 P4
rewrites. P5 additionally inherits one task 005 deliberately leaves undone — dropping
`SecurityGroup.ScopeKind`, `IncludeChildSites`, `SecurityGroupSite` and `SecurityGroupDepartment`,
which remain the group editor's contract until P5 replaces it.

### [ ] P4 — Explicit employee list ~~+ Building dimension~~
**Touches:** `SecurityGroup` dimensions, `WithinScope()`, `SecurityGroupService`
**Done when:** legacy's `ByEmployees` equivalent works — a group scoped to a named employee set,
intersecting correctly with the other dimensions.
**Tests:** employee-list scope; employee-list ∩ site; a group whose employee list is empty
matches nobody (the fourth fail-open, `RoleBasedEmployeeFilterService.cs:50`).
**Risk:** low–medium — no new entity now that Building is out.

> **Amended 2026-08-05 by the authorization survey — "intersecting correctly with the other
> dimensions" has no legacy analogue, and that is fine, but say so.**
> Legacy's two management types are **mutually exclusive, enforced on save**:
> `UpdateRole` wipes `managedEmployees` when the type is `ByDepartments`, and wipes
> `managedDepartments` **and** `managedLocations` when it is `ByEmployees`
> (`AuthorizationService.cs:872-883`). The SQL agrees — `dbo.EmployeeIdsManagedByRole` takes one
> branch or the other, never both (`80.V5.26.0.0.sql:895-931`). A legacy role therefore **cannot**
> say "the Warehouse department *and also* these three named contractors".
>
> WM's intersecting employee-list dimension is a genuine **Improve**, not a port. Two consequences
> for the builder:
> 1. There is **no legacy behaviour to preserve** for employee-list ∩ site. Choose the semantics
>    deliberately and test them; do not look for a precedent.
> 2. The migration story is trivial in the other direction: any legacy role maps to *either* a
>    department/location constraint pair *or* an employee-list constraint, never both.

> **Amended 2026-08-04 by the phase audit — Building is removed from this portion.**
> The original note hedged ("if Building looks like scope creep at review time, split it out").
> It is worse than scope creep: it has no legacy meaning. Measured in
> `HorioDB.designer.cs`, `dbo.Employees` has 153 columns including `DepartmentId`,
> `EmployeeLocationId` and `CostCentreId` — and **no `BuildingId`**. Every table carrying
> `BuildingId` is physical plant: `Devices`, `GetDoorStatuses`, `ac_security_group`,
> `AnprEventsView`, `EposTills`, `LapiCameras`, `FireMarshalMusterPoints`. All are dropped by
> invariant 3 except muster points, which arrive in phase 6b.
>
> `ManagedBuildingsByRole` exists, but there is nothing on an employee for it to match. Building
> would therefore be a WM invention wearing a legacy label — the exact failure mode "improve,
> don't transcribe" is meant to prevent. `ScopeDimension.Building` stays declared in
> `ScopeModel.cs` (it is already fail-closed and costs nothing); nothing resolves it.
>
> **If a real third dimension is wanted to prove the extension point, use `CostCentre`** — it *is*
> an employee column (`Employees.CostCentreId`, plus `CostCentreGroupId`) and it is the one
> Phase 2 needs anyway. That is a user decision, recorded in *Open questions* below.

### [ ] P5 — Group editor + diagnostics explain the composed rule
**Touches:** `security-groups.component.ts`, `security-groups.api.ts`, `users.component.ts`,
diagnostics endpoint, **`People/PeopleModule.cs` (new `GET /api/departments`)**,
**`workforce.api.ts`**
**Done when:** the editor lets an admin build a multi-dimension group (add/remove dimensions,
not pick one kind), and the diagnostics screen explains a decision as the composed rule —
"visible because group *Ops North* grants departments A,B ∩ site C" — rather than naming a
single kind. Verified in the browser via preview_start.
**Tests:** `npm run test` for the editor; browser verification with screenshot.
**Risk:** low.

> **Amended 2026-08-04 by the phase audit — P5 has a hard prerequisite nobody noticed.**
> The department dimension is **unreachable from the UI today and has been since PR #7**: the
> scope dropdown offers None / Self / Sites / All only
> (`security-groups.component.ts:113-116` — `DataScopeKind.Departments` is absent), and there is
> **no `/api/departments` endpoint** anywhere in the API to populate a picker. `Department` is a
> seeded entity (`PeopleSeeder.cs:31-39`) with no read surface.
>
> So P5 must add `GET /api/departments` (People module, `employees.view`, scoped — a manager must
> not enumerate departments they cannot see) before the multi-dimension editor can exist. Budget
> for it here or split it out as its own portion; do not discover it mid-build.

---

## Amendments (phase audit, 2026-08-04)

This plan was re-checked against `E:\Tlw` and against the shipped code. Its claims about legacy
held up on every point that was checked — the three fail-opens are at
`RoleBasedEmployeeFilterService.cs:36-39`, `:44` and `:56-61` exactly as cited, the six managed
dimensions exist at `IAuthorizationService.cs:22-23, 37-42`, and `ByEmployees` is real. Four
things were added or corrected:

- **A fourth fail-open of the same shape** — `if (managedEmployees.Any())` at `:50, :89, :128`.
  The model already handles it; P4 now has a test that names it.
- **A fourth implementation of the rule** — the T-SQL function `dbo.EmployeeIdsManagedByRole`
  (`E:\Tlw\Database\Versioning\80.V5.26.0.0.sql:880-934`), which is what the authoritative
  `IsEmployeeManagedByRole` path actually calls. It **fails closed** on an unknown management
  type where the C# fails open, so WM's inversion agrees with half of legacy already.
- **`Role.IsSelfOnly` / `IsDepartmentOnly` / `CanModifySelf`** — three of `dbo.Role`'s six columns,
  unmodelled and not mentioned anywhere in WM. `IsSelfOnly` is a narrowing override that beats
  the managed lists; WM's `Self` is a widening union member. Tracked as `PHASE-AUDIT.md` B3, for
  the user to rule on; not folded into a portion because it is a product decision.
- **P4's Building dimension has no legacy basis** and is removed (see above).

## Decisions (user, 2026-08-03)

1. **`IncludeChildSites` moves onto the Site dimension.** A group may include descendants for
   one site set and not another. P1 models it as a per-dimension flag; P2's migration carries
   the existing group-wide value onto every Site dimension it creates, preserving current
   behaviour exactly (default was `true`, so this must not silently narrow anyone either).

2. **Doc corrections applied** in a separate `docs/scope-model-corrections` commit, not in
   this plan's PR: §13 row marked `◐ partial`, §14 Phase 1b annotated, §14 decision 5 extended
   with the three inverted fail-opens and the unmodelled `ByEmployees` list, `SCREEN-TREE.md`
   §3 corrected to six dimensions with the "only two enforced centrally" note.

## Still open

**Is any customer today configured with a `ByEmployees` role?** Doesn't block anything —
P4 is approved and builds it regardless. If the answer turns out to be "none", P4's employee-list
dimension can be built for completeness rather than urgency.

**Replace Building with CostCentre in P4?** (raised by the phase audit, 2026-08-04) Building has
no employee-side existence in legacy, so P4 now ships the employee list alone. If the intent of
the second half was to prove the dimension extension point before Phase 2 depends on it,
`CostCentre` does that honestly: `Employees.CostCentreId` is a real column,
`CostCentresManagedByRole` is a real table, and Rules needs it anyway. This adds a People entity
and a migration to P4 — the cost the Building half was going to carry. **User's call.**

**Does WM want legacy's `IsSelfOnly` narrowing override?** See `PHASE-AUDIT.md` B3. Legacy can
say "this role sees *only* its own record, whatever else it was granted". WM's union cannot
express a narrowing, by design. The question is whether that configuration is one customers use,
in which case it needs a home (a group flag, or a role property) rather than a shrug.

> **Reframed 2026-08-05 by the authorization survey.** This question was posed as "can WM's union
> express a narrowing?" The measured answer is that **the union is the anomaly, not the
> narrowing**. Legacy needs no narrowing *operator* because a user has one group, so `IsSelfOnly`
> is simply that group's mode — a lookup, not an override that must defeat other grants
> (`AuthorizationService.cs:220-224, 236-240, 275-279`).
>
> `IsSelfOnly` is unambiguously **live** — six call sites, plus the group editor narrows its own
> employee picker by it (`GroupsController.cs:616-619`) — and it is how legacy expresses the
> self-service user. WM needs it. **How** it is expressed depends on the §4 decision, which is now
> the real open question: `TLW-AUTHORIZATION-MODEL.md` §13.
>
> Also settled: `CanModifySelf` is live and is the *only* read/write asymmetry in legacy's record
> layer (`:281-286`). `IsDepartmentOnly` is **dead** — written, localized, never read. Do not
> model it.
