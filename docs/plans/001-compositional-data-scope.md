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
is scattered across the codebase rather than centralised — which is precisely why legacy
needed `DataAccessScopeDiagnostics` to explain its own decisions.

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

### [ ] P2 — Persist composed scope + migrate existing groups
**Touches:** `Identity/Domain/SecurityGroup.cs`, `IdentityDbContext`, new migration, `IdentitySeeder`
**Done when:** a group persists multiple dimension constraints; the migration maps every
existing row to its exact current meaning (`ScopeKind = Sites` → a Site dimension with the same
ids, etc.) with **no widening**; migration is reversible.
**Tests:** migration up/down on a seeded DB; a round-trip test asserting a pre-migration group
resolves to the identical employee set post-migration.
**Risk:** **high** — this is the one that can silently widen access. Reviewer should treat any
row whose post-migration employee set differs from its pre-migration set as a blocking finding.

### [ ] P3 — Resolver + query filter honour the composed rule
**Touches:** `Identity/Services/DataScopeResolver.cs`, `People/Services/EmployeeScopeExtensions.cs`,
`SecurityGroupService`
**Done when:** `WithinScope()` applies intersection within a group and union across groups;
D1 and D2 both demonstrably fixed end-to-end; existing scoped endpoints and the punch feed
still behave (out-of-scope reads still 404, not 403).
**Tests:** integration — a user in two groups of different dimensions sees the union; a group
with departments ∩ site sees only the intersection; the existing 1b assertions still hold
(manager 16 employees / admin 40, 0 out-of-scope punches).
**Risk:** medium.

### [ ] P4 — Explicit employee list + Building dimension
**Touches:** `People/Domain/Employee.cs` (+ `Building` entity), People migration,
`SecurityGroup` dimensions, `WithinScope()`
**Done when:** legacy's `ByEmployees` equivalent works (a group scoped to a named employee
set), and Building works as a third real dimension intersecting the others.
**Tests:** employee-list scope; building ∩ department; an employee with null building is
invisible to a building-scoped group.
**Risk:** medium — adds a People entity and a migration.
**Note:** if Building looks like scope creep at review time, split it out — the employee list
is the part legacy customers depend on.

### [ ] P5 — Group editor + diagnostics explain the composed rule
**Touches:** `security-groups.component.ts`, `security-groups.api.ts`, `users.component.ts`,
diagnostics endpoint
**Done when:** the editor lets an admin build a multi-dimension group (add/remove dimensions,
not pick one kind), and the diagnostics screen explains a decision as the composed rule —
"visible because group *Ops North* grants departments A,B ∩ site C" — rather than naming a
single kind. Verified in the browser via preview_start.
**Tests:** `npm run test` for the editor; browser verification with screenshot.
**Risk:** low.

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
dimension can be built for completeness rather than urgency, and Building becomes the more
valuable half of that portion.
