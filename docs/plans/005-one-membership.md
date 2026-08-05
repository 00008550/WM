# 005 — One object, one membership (the Identity refactor option A requires)

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 1b — Access model. Plan 001 is the *scope model*, 003 the
*enforcement*, 004 the *screen rights*; this is the *identity* half, and it is the prerequisite
none of them contains. It **supersedes 001 P3**.

> **This plan exists because of a ruling, not a survey.** The user decided on 2026-08-05, from
> [`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md) §13: **option A — one object,
> one membership**, plus **adopt `CanEditOwnRecord`**. `ARCHITECTURE.md` §4 has been rewritten and
> is now normative (`ARCHITECTURE.md:157-160`). Nothing here re-opens that decision; this plan
> costs it out and sequences it.

Legacy sources surveyed: **none newly opened.** The legacy measurement this plan rests on was
taken on 2026-08-05 and is recorded with `file:line` in `TLW-AUTHORIZATION-MODEL.md`; re-reading
`E:\Tlw` would have been waste. The four legacy facts this plan actually leans on, and where they
were measured:

| Fact | Legacy citation | Recorded in |
|---|---|---|
| A user holds exactly one role; assigning replaces | `AuthorizationService.cs:1265-1280`, `:773-786`, `:1008-1041` | §3 |
| The Administrator role is read-only in the editor | `Role.cs` `IsReadonly`; `SetBulkPermissions:616-619`, `UpdateRole:860-863`, `GroupsController.cs:152-155, 256-263` | §3 |
| `IsSelfOnly` is the self-service **mode**, checked before the managed lists | `AuthorizationService.cs:220-224, 236-240, 275-279` | §6 |
| `CanModifySelf` is the only read/write asymmetry in the record layer | `AuthorizationService.cs:281-286` | §5, §6 |

Everything else below was measured **in WM's own tree**, on 2026-08-05, against `master` at
`0613836`.

---

## Ground truth

### The blast radius, measured

`User.Roles` is a list at `src/Modules/Identity/WM.Modules.Identity/Domain/User.cs:16`. Turning it
into one non-null membership touches these files. Every line reference below was opened.

**SharedKernel — the scope types**

| File | What changes |
|---|---|
| `src/SharedKernel/WM.SharedKernel/Security/DataScope.cs` (68 lines) | **whole file dies.** `DataScopeKind`, `EffectiveDataScope`, and the `IDataScopeResolver` signature at `:61-68` |
| `src/SharedKernel/WM.SharedKernel/Security/ScopeModel.cs:174-226` | `DataScope` stops being a **union container**: `IReadOnlyList<ScopeRule> Rules` (`:189`) → one `ScopeRule`; `CanSee` (`:225`) drops its `.Any(`; `SeesEverything` (`:202`) / `SeesNothing` (`:208`) / `From(...)` (`:198`) collapse |
| `ScopeModel.cs:27-172` | **untouched.** `ScopeDimension`, `ScopeSubject`, `ScopeConstraint`, `ScopeRuleKind`, `ScopeRule` all survive intact — intersection-within-a-group is exactly the model option A keeps |
| `src/SharedKernel/WM.SharedKernel/Security/WmPermissions.cs` | untouched. The 11 permissions become the group's rights at today's granularity |

**Identity module**

| File | Line | What is there now |
|---|---|---|
| `Domain/User.cs` | `:16` | `public List<UserRole> Roles { get; set; } = [];` |
| `Domain/User.cs` | `:22-29`, `:31-36`, `:38-42` | `Role`, `UserRole`, `RolePermission` — the three types that merge into `SecurityGroup` |
| `Domain/SecurityGroup.cs` | `:50` | `List<UserSecurityGroup> Members` — many-to-many |
| `Domain/SecurityGroup.cs` | `:10-13` | XML doc asserting roles and groups are "deliberately orthogonal" — **the sentence option A reverses**, and it carries two measured errors (below) |
| `Data/IdentityDbContext.cs` | `:9`, `:24`, `:28-45`, `:79-83` | `DbSet<Role>`, the `HasMany(x => x.Roles)`, and the `Role`/`UserRole`/`RolePermission`/`UserSecurityGroup` configurations |
| `Data/IdentitySeeder.cs` | `:40-96` | three built-in roles + one system group + admin |
| `Services/AuthService.cs` | `:23`, `:76`, `:97-98` | two `.Include(u => u.Roles).ThenInclude(r => r.Role).ThenInclude(r => r.Permissions)` loads and `PermissionsOf` |
| `Services/TokenService.cs` | `:43` | `claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.Name)));` |
| `Services/UserManagementService.cs` | `:13`, `:15`, `:19`, `:22`, `:47`, `:67-71`, `:94-96`, `:106`, `:147-149`, `:160-161`, `:192-197`, `:199-201` | `Roles`/`RoleIds` through the DTOs, the list query, `ListRolesAsync`, `ResolveRolesAsync` |
| `Services/DataScopeResolver.cs` | `:25-70` | the whole method — the resolution loop that becomes a lookup |
| `Services/SecurityGroupService.cs` | `:8-11`, `:143-167` | the list DTO, `GetUserGroupIdsAsync`, `SetUserGroupsAsync` |
| `Services/LegacyScopeMapping.cs` | whole file | survives as the migration's mapping; `FromLegacy` retires with the legacy columns |
| `Endpoints/UserEndpoints.cs` | `:21-22` | `GET /api/users/roles` |
| `Endpoints/SecurityGroupEndpoints.cs` | `:49-59`, `:79-86` | the membership pair, and `Explain` |

**API host**

| File | Line | What is there now |
|---|---|---|
| `src/Api/WM.Api/Realtime/AttendanceScopeGroups.cs` | `:44-53` | `ForScope(EffectiveDataScope)` — a `switch` over `DataScopeKind` |
| `src/Api/WM.Api/Realtime/AttendanceAudience.cs` | `:64`, `:126-147` | `ResolveAsync` returns `EffectiveDataScope` |
| `src/Api/WM.Api/Infrastructure/DemoUserSeeder.cs` | `:25-27`, `:37`, `:44`, `:53-56` | looks roles up by name and passes `roleId` into `CreateUserRequest` |
| `src/Api/WM.Api/Program.cs` | `:86-105` | the bootstrap block where a one-shot collapse step slots in, after `MigrateAsync()` |

**People module**

| File | Line | What is there now |
|---|---|---|
| `src/Modules/People/WM.Modules.People/Services/EmployeeScopeExtensions.cs` | `:15-25` | `WithinScope(this IQueryable<Employee>, EffectiveDataScope)` |
| `src/Modules/People/WM.Modules.People/PeopleModule.cs` | the `WithinScope` call sites | mechanical retype |

**Frontend**

| File | Line | What is there now |
|---|---|---|
| `frontend/portal/src/app/core/api/users.api.ts` | `:15`, `:18-23`, `:31`, `:39`, `:53-55` | `roles: string[]`, `RoleListItem`, `roleIds`, `roles()` |
| `frontend/portal/src/app/core/api/security-groups.api.ts` | `:6-21`, `:74-80` | the `DataScopeKind` mirror + `SCOPE_LABELS`, and the plural membership calls |
| `frontend/portal/src/app/pages/users/users.component.ts` | `:138-165`, `:238-241`, `:254-257`, `:267`, `:275-278`, `:289-301`, `:328-329` | two independent multi-selects — role checkboxes *and* group checkboxes — that become one required picker |
| `frontend/portal/src/app/pages/security-groups/security-groups.component.ts` | `:21` | the copy "…their roles — the two are separate on purpose", plus the editor gains rights |

**Tests that must move with it:** `WM.SharedKernel.Tests/Security/DataScopeTests.cs` (tests the
dying `EffectiveDataScope`), `ScopeRuleTests.cs`, `WM.Modules.Identity.Tests/Services/`
(`LegacyScopeMappingTests.cs`, `UserManagementScopeNotificationTests.cs`),
`WM.Api.Tests/Realtime/` (`ScopedPunchFeedTests.cs`, `ScopeRevocationTests.cs`,
`AttendanceScopeGroupTests.cs`).

**Total: 22 source files, 5 frontend files, 7 test files, 3 migrations.** This is a large plan and
saying otherwise would be dishonest. It is broken into six portions below, each of which builds,
tests and reviews on its own, and each of which leaves the app runnable.

### Two things measured that nobody has recorded

**1. `ClaimTypes.Role` is written and never read.** `TokenService.cs:43` mints one role claim per
role. A grep of `src/**` and `frontend/**` for `IsInRole`, `RequireRole` and `ClaimTypes.Role`
finds **no reader** — the only hits are that write and two unrelated `ClaimTypes.NameIdentifier`
references in `ICurrentUser.cs:21, 32`. Authorization runs entirely off `wm:perm`
(`IdentityModule.cs:66-70`, `HttpCurrentUser.Permissions` at `:103-104`). The users screen shows
role *names* from the REST list (`users.component.ts:254`), not from the token. So the JWT claim
shape question has a smaller answer than expected: **drop the role claim, keep `wm:perm`, add
nothing.** A group-id claim is deliberately *not* added — 004 already rules that screen rights are
resolved server-side per request and never trusted from a claim.

**2. Correction C2 is in FOUR code comments, not two.** The brief named `ScopeModel.cs:223` and
`SecurityGroupEndpoints.cs:61`. Grepping `src/**` for "diagnostic" finds two more:

- `src/Modules/Identity/WM.Modules.Identity/Domain/SecurityGroup.cs:10-13` — *"Legacy conflated the
  two across Role, SecurityGroup, FormAccess and SiteItemPermission, which is why it needed a
  diagnostics subsystem to explain itself."* Three errors in one sentence: C2 (the diagnostics
  claim), C5 (`FormAccess` is a static class and `SiteItemPermission` a DTO — neither is a table
  legacy could conflate anything *across*), and the orthogonality premise option A has now
  reversed.
- `src/Modules/People/WM.Modules.People/Services/EmployeeScopeExtensions.cs:9-11` — *"…which is how
  legacy ended up needing a diagnostics subsystem to find the gaps."* C2 again.

A fifth, milder instance is `DataScope.cs:5-6`, which calls `SiteItemPermission` an "authorizer
class"; that file dies in P4 regardless. `TLW-AUTHORIZATION-MODEL.md` §11 and `STATE.md` have been
corrected to say four. Each is folded into the *Touches* of the portion that opens that file.

### Corrections made to WM's records by this survey

| # | Where | Was | Now |
|---|---|---|---|
| 1 | `ARCHITECTURE.md:425` (§13 "One role per user") | ⚠️ "Decision pending" | ◐ decided 2026-08-05, option A, plan 005 |
| 2 | `ARCHITECTURE.md:424` (§13 built-in user types) | "`IsSelfOnly` and `CanModifySelf` not modelled" | both adopted — `SelfOnly` as `ScopeRuleKind.Self`, `CanEditOwnRecord` as a group flag; plan 005 P1 |
| 3 | `ARCHITECTURE.md:427` (§13 realtime feed) | ⚠️ "built but unscoped" | ✅ scoped — 003 P1 merged as `3389525` (#18) |
| 4 | `ARCHITECTURE.md:531` (§14 decision 5) | "WM allows several groups per user" | superseded by the 2026-08-05 ruling; one group per user |
| 5 | `TLW-AUTHORIZATION-MODEL.md` §11 C2, §14 q5 | "two code comments" | **four**, with lines |
| 6 | `STATE.md` shipped/in-flight tables | #18 open, #19 absent | both merged (`3389525`, `0613836`) |

---

## Legacy behaviour (what we are replacing)

Nothing in `E:\Tlw` is being *replaced* by this plan — WM's own multi-membership is. But two legacy
behaviours are being **adopted**, and their shape decides two portions:

1. **Assignment replaces, it does not accumulate.** `AddUserToRole(roleId, userId)` finds the
   existing row by `userId` alone, deletes it, and inserts the new one
   (`AuthorizationService.cs:1008-1041`). WM's `SetUserGroupsAsync`
   (`SecurityGroupService.cs:149-167`) does the same shape already — deletes all, inserts the
   posted set — so the endpoint narrows from `Guid[]` to `Guid` without changing its semantics.
   That is a smaller change than it looks.

2. **The Administrator object is read-only.** Legacy refuses to save rights or properties against
   the Administrator role in three places (`SetBulkPermissions:616-619`, `UpdateRole:860-863`,
   `GroupsController.cs:152-155, 256-263`) and `Role.IsReadonly => IsAdministrator`. WM's
   `SecurityGroupService.UpdateAsync:89` already freezes a system group's scope; once the group
   also carries **permissions**, freezing them is what stops an administrator editing themselves
   out of the product. Adopt it.

One legacy defect is fixed **by construction** rather than by code:
`dbo.UsersInRoles.IsActive` is checked by `IsUserActiveInRole` but ignored by `GetUserRole`,
`LoadUserInRoles` and `IsUserInRole` (`TLW-AUTHORIZATION-MODEL.md` §3), so a deactivated assignment
still grants scope. WM has no join row to deactivate — `User.IsActive` is the only switch, and both
paths already honour it (`AuthService.cs:26`, `DataScopeResolver.cs:33-34`). The inversion is free.

---

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| `ScopeConstraint` / `ScopeRule` / intersection-within-a-group (`ScopeModel.cs:78-172`) | **Keep, untouched** | Option A changes how many rules a user has, not what a rule is. 001 P1/P2 survive whole and under test. |
| Empty-dimension-matches-nobody, null-never-widens, unknown-fails-closed | **Keep** | The three inversions 001 shipped. Nothing here softens them. |
| `WmPermissions` as the right vocabulary | **Keep for now** | The brief's own framing: "a permission becomes a screen right at the granularity WM already uses". 004 refines the vocabulary; this plan must not invent a second one first. |
| `User.Roles` many-to-many (`User.cs:16`) | **Invert** | Not "improve": the cardinality is the decision. One non-null membership. |
| `Role` + `RolePermission` + `UserRole` (`User.cs:22-42`) | **Drop, merged into `SecurityGroup`** | Three tables expressing "a named bundle of permissions" that the group can carry as a child collection. Keeping both is the two-objects shape §4 rejected. |
| `UserSecurityGroup` join table (`SecurityGroup.cs:88-93`) | **Drop → `User.SecurityGroupId`** | A join table *is* the multi-membership. |
| `DataScope.Rules` union (`ScopeModel.cs:189, 225`) | **Drop** | One membership, one rule. The union is one `.Any(` and it is the whole of what option A deletes. |
| `DataScopeKind` / `EffectiveDataScope` (`DataScope.cs`) | **Drop** | 001 P3 was always going to delete these; it is this plan that does it. |
| `DataScopeResolver`'s widest-wins loop (`:55-69`) | **Drop → a lookup** | The loop exists only to reconcile several groups. |
| `ClaimTypes.Role` in the JWT (`TokenService.cs:43`) | **Drop** | Measured: written, never read. |
| `GET /api/users/roles` (`UserEndpoints.cs:21-22`) | **Drop** | Superseded by `GET /api/security-groups`. |
| Group assignment replaces rather than accumulates | **Keep** (legacy `AddUserToRole:1008-1041`) | Already WM's shape; only the arity changes. |
| Administrator object read-only | **Keep** (legacy `Role.IsReadonly`) | The only thing standing between an admin and locking themselves out once the group carries rights. |
| `Role.IsSelfOnly` → a group **mode** | **Keep, as `ScopeRuleKind.Self`** | Under exclusivity a mode *is* a rule kind. Adding a separate `Mode` column would be two fields with one meaning — the defect the survey criticised in legacy. See *Open questions* 1. |
| `Role.CanModifySelf` → `CanEditOwnRecord` | **Keep** | The user adopted it. Default `true` = today's WM behaviour, so the migration cannot narrow anyone. |
| `Role.IsDepartmentOnly` | **Drop** | Dead in legacy; settled (`TLW-AUTHORIZATION-MODEL.md` §6). Do not model it. |
| Deleting a group that still has members | **Invert** | Today `DeleteAsync:125-141` cascades the join rows away and the members silently fall back to `Self`/`None`. With a required membership that becomes a lost user. Refuse the delete while members remain, naming the count. |
| `dbo.UsersInRoles.IsActive` ignored on the hot path | **Invert — free** | No join row exists to deactivate; `User.IsActive` is the single switch and both paths honour it. |
| Legacy's site-id and `IncludeChildSites` leakage between groups (001 P3 deviations 2 and 3) | **Invert — by construction** | With one membership there is no other group to leak from. The migration *preserves* the leaked ids as data (nobody loses what they can see today), and the shape then makes it unrepresentable. |
| 001 P3 deviation 1 ("`Self` stops being outranked") | **Cancelled** | Under a union, a `Self` rule sitting beside a `Sites` rule would additively grant self. Under one membership a `Constrained` rule grants exactly its constraints, so a site-scoped manager still does not see their own record unless they fall inside it — which is also what legacy's TVF does. The sanctioned widening does not happen and must not be tested for. |

---

## Edge cases

Each needs a test that names it.

- **A user with no group.** Unrepresentable after P3 (`SecurityGroupId` non-null + FK). Assert the
  constraint, and assert `UserManagementService.CreateAsync` rejects a request without one —
  004's edge case "assert that it cannot be created, rather than deciding what it would mean".
- **An inactive user, at collapse time.** `DataScopeResolver.cs:33-34` returns `None` for an
  inactive user. If the collapse reads the resolver's output verbatim, **every inactive user
  collapses to a `None` group and loses their configuration on reactivation.** The collapse must
  compute the scope the user *would* have if active, and ignore `IsActive`. This is the single
  easiest way to get P2 quietly wrong.
- **A user in a Sites group and a Departments group.** Today's resolver takes the widest kind
  (`Sites` = 3 beats `Departments` = 2) and **discards the departments** — plan 001's D2. The
  collapse must reproduce that exactly: they get the sites, not the union. Losing access they can
  see today is a blocking defect; *gaining* the departments is also a defect, because it is a
  widening nobody authorised.
- **A group carrying site rows while its kind is `Departments`.** Today's resolver unions site ids
  from *every* group regardless of kind (`DataScopeResolver.cs:58-59`), so those stray rows widen a
  different group. `SecurityGroupService.CreateAsync:59-60` writes both lists regardless of kind,
  so the configuration is reachable. Preserve the effect; count the affected users in the report.
- **`IncludeChildSites` defaults to `true`** (`SecurityGroup.cs:30`) and is OR'd across all of a
  user's groups (`:60`), so in practice nearly every site-scoped user gets descendant expansion.
  The collapse must carry `true` onto the generated Site constraint whenever any contributing group
  had it. Assert on a real seeded site hierarchy that the post-collapse employee set is identical.
- **Two users with the same resolved access** share one generated group. Editing it moves both.
  Correct under the model, but it must be discoverable: the group's description names every
  (roles, groups) combination that folded into it.
- **A user with `All` scope but fewer than all permissions.** They must **not** land in the
  repurposed `Administrator` group — that would grant them everything. Only an exact
  permission-set match lands there; anyone else gets a generated `All`-scope group with their own
  permissions.
- **Deleting a group with members** — refused, with the member count in the message.
- **A group change mid-session.** Scope re-resolves per request and the open sockets re-group via
  `IScopeChangeNotifier` (already wired: `SecurityGroupService.cs:165`,
  `UserManagementService.cs:167-168`). **Permissions do not** — they are JWT claims, so a group
  change does not narrow a live token for up to `AccessTokenMinutes` (10). That is today's
  behaviour with roles and this plan neither improves nor worsens it; it is recorded in 003's
  *Deliberately not solved here* and stays there.
- **A generated group's name collides** with an existing group name (`SecurityGroups.Name` is
  unique, `IdentityDbContext.cs:55`). Deterministic suffixing, asserted.
- **The collapse runs twice.** Idempotent: guarded by the report table, re-running writes nothing
  and changes no assignment.

---

## The collapse rule

**This is the most important paragraph in the plan.** Today's users hold N roles and M groups.
Option A gives them one group. The rule is:

> **Collapse by resolved effect, not by stored configuration** — and materialise one group per
> distinct resolved effect.

It works, and it is provably lossless, because of a measured property of the code:

- **Permissions are already a set.** `AuthService.PermissionsOf` (`:97-98`) is
  `user.Roles.SelectMany(r => r.Role.Permissions).Select(p => p.Permission).ToHashSet()`. A union
  of sets is a set. One group can carry it exactly, whatever N is.
- **Scope is already collapsed to a single kind.** `DataScopeResolver.GetScopeForUserAsync`
  (`:50-69`) reduces every user, however many groups they hold, to one `DataScopeKind` plus one
  dimension's id set — and every consumer reads it that way
  (`EmployeeScopeExtensions.cs:15-25`, `AttendanceScopeGroups.cs:44-53`,
  `EffectiveDataScope.CanSee` at `DataScope.cs:47-54`). The union across groups **never
  materialises as an effect**; it only ever produces a wider `Kind` and one id set.

So the mapping is total:

| Today's resolved scope | Generated group |
|---|---|
| `All` | `RuleKind = All` |
| `Sites(ids, expand)` | `RuleKind = Constrained` + Site constraint, `IncludeDescendants = expand` |
| `Departments(ids)` | `RuleKind = Constrained` + Department constraint, `IncludeDescendants = false` |
| `Self` | `RuleKind = Self` (this *is* the `SelfOnly` mode) |
| `None` | `RuleKind = None` |

**There is therefore no user whose combination has no clean answer.** That is not luck; it is the
reason for choosing collapse-by-effect over collapse-by-configuration. A *configuration* union is
genuinely unrepresentable — group X constrained on Site S1 and group Y on Department D9 mean
"S1 **or** D9", and a single group intersects its constraints, so no single group can say it. The
*effect* is always representable, because the current resolver has already done the collapsing for
us. Collapse-by-configuration would have forced exactly the "refuse to migrate and ask an admin"
branch the brief asked about; collapse-by-effect removes the branch.

### The procedure

1. **Two built-in groups are established.**
   - **`Administrator`** — the existing `All employees` system group is **repurposed in place**
     (same row, same id, same `IsSystem`): renamed, and given the full `WmPermissions.All` set. Its
     permissions are read-only thereafter (legacy `Role.IsReadonly`). Doing it in place rather than
     creating a second `All`-scope group avoids leaving a permission-less system group behind that
     grants nothing and cannot be deleted.
   - **`Employee`** — new, `RuleKind = Self`, permissions `{selfservice.access}`, `IsSystem`. This
     is §4's floor.
2. **Each user's effect is computed** — permission set (union of their roles') and scope (today's
   resolver semantics, **with `IsActive` ignored**). Both are computed by **one pure function**,
   `MembershipCollapse`, and nothing else.
3. **Users whose effect exactly equals a built-in's** are assigned to that built-in. Exact means
   both halves: permission set *and* scope.
4. **Every other distinct effect gets one generated group.** Dedupe key: the sorted permission set
   plus the rule kind plus, per dimension, the sorted value ids and the descendants flag. Name:
   derived from the alphabetically-first contributing combination — `"Manager"` when the user had
   roles only, `"Manager · Ops North"` when they had a group too, suffixed ` (2)`, ` (3)` on
   collision. Description: `"Migrated <date> from: Manager + Ops North; Manager + Ops North (copy)"`
   — every contributing combination, so a shared group is discoverable.
5. **Every user is assigned**, and a report row is written per user.
6. **Nothing is deleted.** `UserRole`, `Role`, `RolePermission` and `UserSecurityGroup` rows are
   read and left exactly as they are.

### What is *not* preserved, stated plainly

A membership that was already doing nothing. A user in a Sites group *and* a Departments group
keeps every employee they can see today — the departments were discarded before this plan existed
(001's D2) — but the *record of the intent* disappears from the users screen. The old rows survive
until P6, and the report row survives forever, so an administrator can re-grant deliberately.
**Restoring access nobody actually had is a product decision, not a migration's job**, and this
migration will not make it silently.

### Why it is a C# runner and not raw SQL in the migration

`WM.Modules.Identity.Tests` uses `Microsoft.EntityFrameworkCore.InMemory`
(`WM.Modules.Identity.Tests.csproj:16`); there is no Postgres test harness anywhere in the repo.
That is why 001 P2's promised "migration up/down on a seeded DB" test was never written and the
review passed it anyway — the infrastructure to write it did not exist, and the plan did not say so.

This plan will not repeat that. The migration is **schema-only**. The collapse is a one-shot,
idempotent `MembershipCollapseRunner` invoked from the bootstrap block in `Program.cs:86-105`,
immediately after `MigrateAsync()` and **not** gated on `IsDevelopment`. Consequences:

- One implementation, not a C# copy and a SQL copy that can disagree — the lesson from legacy's
  four copies of the same filter.
- Testable today, with the existing InMemory provider and the existing test project.
- Idempotent, guarded by the report table, so a restart is safe.
- Its `Down` obligation is trivial: it only ever writes `User.SecurityGroupId`, new
  `SecurityGroup` rows and report rows.

---

## Reversibility — the honest answer

**P1–P5 are fully reversible. P6 is not, and no `Down` will pretend otherwise.**

The reason P1–P5 can be reversible is the same reason 001 P2 was: **they are purely additive.**
`AddComposedScopeConstraints` (`20260804104816_AddComposedScopeConstraints.cs:127-148`) reverses
exactly because it read `ScopeKind`, `IncludeChildSites`, `SecurityGroupSite` and
`SecurityGroupDepartment` and never modified them. This plan holds itself to the same rule:
`Role`, `RolePermission`, `UserRole` and `UserSecurityGroup` are **read and never written**
throughout P1–P5. Rolling back drops the new columns and tables and leaves the old model intact and
authoritative.

**P6 drops those four tables. That is one-way and it is the point of no return.** It is a separate
portion, sequenced last, precisely so that crossing the line is a decision somebody makes rather
than a side effect of shipping a feature. Its `Down` will recreate the *tables* and nothing else —
the rows are gone — and it will say so in a comment rather than implying a restore.

A practical consequence: **do not run P6 until the estate has run on the new model long enough to
trust it.** There is no technical coupling forcing P6 into this plan's cadence.

---

## Ordering against plan 003

| Portion | Before or after 005 | Why |
|---|---|---|
| **003 P2a** — fail closed by default | **Before.** Build it now, in parallel with this plan being written. | Model-independent: it registers a `FallbackPolicy` in `IdentityModule.RegisterServices`. 005 P3 also edits that method, but a different statement. Landing P2a first means 005's policy work starts from a default-deny baseline instead of racing it. |
| **003 P2b** — scope the employee writes | **Before.** | It calls `WithinScope(scope)`, whose parameter type 005 P4 changes. If P2b lands first, P4 retypes two call sites — mechanical. If it lands after, P2b is written against a type that is moving under it. It is also a live defect (create/edit-then-escape), which outranks a refactor. Its named `CanEditOwnRecord` seam is what 005 P4 wires to the real flag; **005 P4 must not invent that seam if P2b has not landed** — it wires an existing one or defers. |
| **003 P3** — scoped site/department lists | **Either.** Independent. | It adds `GET /api/departments` and fixes the employee-editor round-trip. If it lands before 005 P4, P4 retypes two more call sites; if after, nothing changes. Note it is also 001 P5's prerequisite. |
| **003 P4** — API-auth design note | **Either.** Documentation only. |
| **001 P4 / P5** | **After 005.** | P4 (employee-list dimension) and P5 (multi-dimension editor) both build on the resolver 005 P4 rewrites. P5 additionally owns the *closing* act this plan deliberately leaves undone: dropping `SecurityGroup.ScopeKind`, `IncludeChildSites`, `SecurityGroupSite` and `SecurityGroupDepartment`, which are still the group editor's contract until P5 replaces it. |
| **004** — screen rights | **After 005.** | Its P2 ("persist rights on the group") becomes an extension of a group that already carries permissions, rather than a new concept beside `Role`. |

**Recommended sequence:** 003 P2a → 003 P2b → **005 P1…P5** → 003 P3 → 001 P4 → 001 P5 → 005 P6 →
004.

---

## Target design in WM

```
SecurityGroup                                   one per user (ARCHITECTURE.md:157)
  Name, Description, IsSystem
  RuleKind        : None | Self | Constrained | All   -- Self IS the SelfOnly mode
  Constraints     : [ SecurityGroupConstraint ]       -- unchanged, 001 P2
  Permissions     : [ SecurityGroupPermission ]       -- NEW: the merged RolePermission
  CanEditOwnRecord: bool  (default true)              -- NEW: legacy Role.CanModifySelf

User.SecurityGroupId : Guid, required, FK restrict    -- replaces User.Roles + UserSecurityGroup
```

- **Contracts/events.** No new cross-module contract. `IDataScopeResolver` (`DataScope.cs:61-68`)
  keeps its two methods and changes its return type from `EffectiveDataScope` to the collapsed
  `DataScope`. `IScopeChangeNotifier` is unchanged and already invoked from both mutation paths.
- **Endpoints.**
  `GET/PUT /api/security-groups/{id}` gain `permissions` and `canEditOwnRecord`;
  `GET /api/users/{id}/security-groups` → `GET /api/users/{id}/security-group` (one id);
  `PUT` likewise takes one id;
  `POST/PUT /api/users` take `securityGroupId` and drop `roleIds`;
  `GET /api/users/roles` is deleted;
  `GET /api/access-diagnostics/{userId}` explains one group instead of a resolution.
- **Screens.** The security-group editor gains a permissions section and a "may edit own record"
  toggle. The users screen replaces two multi-selects with one required picker showing the group's
  name, its scope summary and its permission count.
- **Realtime.** The audience rule follows the composed rule; see P5, which settles how.
- Cites `ARCHITECTURE.md` §4 (`:157-160`), §5 (unlicensed ⇒ absent), CLAUDE.md invariants 1, 2, 5.

---

## Out of scope for this plan

- **The screen-rights catalogue and vocabulary** — plan 004. This plan carries `WmPermissions`
  across onto the group unchanged; it does not invent `none/read/edit`.
- **Multi-dimension constraint editing in the group editor** — 001 P5. Post-collapse every group is
  single-dimension, so nothing here needs it.
- **The explicit employee-list dimension** — 001 P4.
- **Dropping the legacy single-kind scope columns** (`ScopeKind`, `IncludeChildSites`,
  `SecurityGroupSite`, `SecurityGroupDepartment`) — they remain the group editor's contract until
  001 P5 replaces it. 005 P6 drops the *role* tables only.
- **Session and token revocation** — 003's *Deliberately not solved here*.
- **Navigation icons, dashboard categories, HR document rights** — later, per 004.
- **An `AccessRightsExclusions` replacement** — `TLW-AUTHORIZATION-MODEL.md` open question 4.
- **Anything under `E:\Tlw`.** Read-only.

---

## Portions

### [ ] P1 — A group carries permissions and `CanEditOwnRecord`
**Touches:** `src/Modules/Identity/WM.Modules.Identity/Domain/SecurityGroup.cs` (new
`SecurityGroupPermission`, new `CanEditOwnRecord`, **and the corrected XML doc at `:10-13` —
three errors: C2, C5, and the orthogonality premise §4 has reversed**),
`Data/IdentityDbContext.cs`, a new additive migration,
`Services/SecurityGroupService.cs` (DTOs + read/write of the permission set),
`Endpoints/SecurityGroupEndpoints.cs` (**and the C2 comment at `:61-62`**),
`frontend/portal/src/app/core/api/security-groups.api.ts`,
`frontend/portal/src/app/pages/security-groups/security-groups.component.ts` (**and the copy at
`:21`**), `WM.Modules.Identity.Tests`
**Done when:** a group persists a set of `WmPermissions` values and a `CanEditOwnRecord` flag
(default `true`); both round-trip through `GET`/`PUT /api/security-groups`; the editor shows a
permissions section and the toggle; a system group's permissions are refused on edit (legacy
`Role.IsReadonly`); an unknown permission string is rejected; **nothing reads either field yet**,
so no behaviour changes.
**Tests:** round-trip of the permission set and the flag; a permission not in `WmPermissions.All`
is rejected; a system group's permission edit is refused while its description still saves (mirrors
the existing scope rule at `SecurityGroupService.cs:89`); default `CanEditOwnRecord` is `true`;
`npm run test` for the editor; browser check via preview_start.
**Risk:** low — additive, no consumers.

### [ ] P2 — The collapse: built-in groups, generated groups, and the report
**Touches:** `Domain/User.cs` (`SecurityGroupId`, **nullable at this stage**),
`Data/IdentityDbContext.cs`, a new **schema-only** migration (column + FK + report table),
new `Services/MembershipCollapse.cs` (pure), new `Services/MembershipCollapseRunner.cs`,
`src/Api/WM.Api/Program.cs` (invoke the runner after `MigrateAsync()`, **not** dev-gated),
`Data/IdentitySeeder.cs` (seed the two built-in groups), `WM.Modules.Identity.Tests`
**Done when:** the runner assigns **every** existing user exactly one group whose resolved employee
set and permission set are **identical** to what they have today; `All employees` is repurposed in
place as `Administrator`; the `Employee` built-in exists; one `MembershipCollapseReport` row per
user records prior roles, prior groups, prior resolved scope, assigned group and the reason; the
runner is idempotent; **nothing reads `SecurityGroupId` yet.**
**Tests:** the *equivalence* suite is the point of this portion, and every case below needs its own
named test —
(a) each of the five resolved scopes maps to the stated rule;
(b) a Sites-group + Departments-group user gets the **sites only**, matching today's D2 behaviour —
neither narrower nor wider;
(c) an **inactive** user collapses to the scope they would have if active, not to `None`;
(d) stray site rows on a `Departments`-kind group still widen, exactly as `DataScopeResolver.cs:58-59`
does today, and the report counts the affected users;
(e) `IncludeChildSites = true` on any contributing group lands as `IncludeDescendants = true`, and
the post-collapse employee set over a seeded site hierarchy is identical;
(f) an `All`-scope user whose permissions are a strict subset of `WmPermissions.All` does **not**
land in `Administrator`;
(g) name collisions suffix deterministically;
(h) two users with the same effect share one group whose description names both combinations;
(i) running the runner twice changes nothing;
(j) the migration's `Down` drops the column and the report table and leaves `UserRole` /
`UserSecurityGroup` byte-identical.
Plus a **manual verification against a real seeded Postgres** via preview_start, with the report
table's contents pasted into the PR — the durable evidence 001 P2 never produced.
**Risk:** **high.** This is the portion that can silently widen or narrow access, and a reviewer
should treat any user whose post-collapse employee set differs from their pre-collapse set as a
blocking finding.

### [ ] P3 — The token and the permission set come from the group
**Touches:** `Services/AuthService.cs` (`:23`, `:76`, `:97-98`),
`Services/TokenService.cs` (`:43` — drop the dead role claim),
`Services/UserManagementService.cs` (DTOs, list query, delete `ListRolesAsync` + `ResolveRolesAsync`),
`Endpoints/UserEndpoints.cs` (`:21-22`), `Endpoints/SecurityGroupEndpoints.cs` (`:49-59` → singular),
`Services/SecurityGroupService.cs` (`SetUserGroupAsync`, and refuse deleting a group with members),
`Data/IdentitySeeder.cs`, `src/Api/WM.Api/Infrastructure/DemoUserSeeder.cs`,
a migration making `SecurityGroupId` **NOT NULL** (after a backfill sweep for any user created
between P2 and P3), `frontend/portal/src/app/core/api/users.api.ts`,
`frontend/portal/src/app/pages/users/users.component.ts`, `WM.Modules.Identity.Tests`
**Done when:** a login's permissions come from `user.SecurityGroup.Permissions`; the JWT no longer
carries `ClaimTypes.Role`; creating a user requires a `securityGroupId` and no longer accepts
`roleIds`; the users screen offers one required group picker; the demo seed puts the demo manager
in a **`Site managers`** group with a real site constraint; deleting a group with members is
refused with the count; `Role` rows still exist and are read by nothing.
**Tests:** permissions resolve from the group and match the pre-refactor set for every seeded user;
a token has no role claim and every existing authorization policy still passes; create without a
group is rejected; group change notifies `IScopeChangeNotifier` exactly as the role change used to
(`UserManagementScopeNotificationTests` extended); delete-with-members returns the refusal;
`npm run test`; **browser verification with two accounts side by side** via preview_start.
**Risk:** **high** — every sign-in goes through it, and a mistake is either a lockout or a silent
widening. Sequence the NOT NULL migration after the backfill sweep in the same migration.

**Re-measure the baseline here.** `STATE.md` has carried a wrong number for months: the seeded
manager belongs to **no** security group (`DemoUserSeeder.cs:37` passes a role id and nothing
else), so today they resolve to `Self` and see **1** employee, not 16. This portion is where that
becomes true, because the demo seed finally gives them a scope. Establish the real number from the
running stack and write it down; do not copy 16 forward.

### [ ] P4 — One group, one rule: the resolver and the query filter  ·  **supersedes 001 P3**
**Touches:** `src/SharedKernel/WM.SharedKernel/Security/ScopeModel.cs:174-226` (union → one rule,
**and the C2 comment at `:220-223`**),
delete `src/SharedKernel/WM.SharedKernel/Security/DataScope.cs`,
`Services/DataScopeResolver.cs:25-70` (loop → lookup),
`Endpoints/SecurityGroupEndpoints.cs:79-86` (`Explain` describes one group),
`src/Modules/People/WM.Modules.People/Services/EmployeeScopeExtensions.cs` (**and the C2 comment at
`:9-11`**), `src/Modules/People/WM.Modules.People/PeopleModule.cs` (`WithinScope` call sites),
`src/Api/WM.Api/Realtime/AttendanceScopeGroups.cs` + `AttendanceAudience.cs` (retype only),
`WM.SharedKernel.Tests`, `WM.Api.Tests`
**Done when:** `DataScopeResolver` reads one group and returns one `ScopeRule`; `WithinScope`
applies intersection-within-the-group; `DataScopeKind` and `EffectiveDataScope` are gone from the
tree; diagnostics explains a decision as *"group Ops North grants sites A, B (including
descendants)"*; the group editor and its API still work unchanged, because the legacy `ScopeKind`
column and its DTO field survive to 001 P5; and **if 003 P2b has landed**, its `CanEditOwnRecord`
predicate is wired to the group's flag.
**Interim, deliberately fail-closed:** `AttendanceScopeGroups.ForScope` cannot express an
*intersection* as SignalR group membership — see P5. Until P5, a `Constrained` rule with **more
than one** dimension returns **no groups** (the connection hears nothing) rather than leaking. This
arm is unreachable today: the collapse produces single-dimension rules only, and the editor cannot
author a second dimension until 001 P5. Assert the arm anyway.
**Tests:** the existing scope assertions still hold with the re-measured baseline from P3;
intersection-within-a-group works end to end; a group with a present-but-empty dimension matches
nobody; an employee with a null department is invisible to a department-constrained group; an
unknown rule kind returns nothing; a site-scoped manager does **not** see their own record unless
they fall inside the constraint (001 P3's deviation 1 is cancelled — assert the cancellation);
`AttendanceScopeGroupTests`' equivalence between `ForScope`/`ForSubject` and `CanSee` still holds;
the multi-dimension arm yields no groups.
**Risk:** **high** — largest surface, four projects, and the failure mode is silent.

### [ ] P5 — The realtime audience matches any rule
**Touches:** `src/Api/WM.Api/Realtime/AttendanceScopeGroups.cs`,
`AttendanceConnectionRegistry.cs`, `AttendanceAudience.cs`,
`src/Api/WM.Api/Infrastructure/EventStreamProducers.cs`, `WM.Api.Tests/Realtime/*`
**Done when:** a connection receives a punch **exactly when** its group's rule matches the punched
employee, for every rule shape including a multi-dimension intersection; no client ever receives
the same punch twice; a scope change still re-groups an open socket within the session (003
decision 2 is preserved).

**The problem this portion exists to solve, and the decision it takes.** SignalR group fan-out is
**union** semantics: a connection in `scope:site:S1` and `scope:dept:D9` receives anything addressed
to either. A `Constrained` rule is an **intersection**. So the moment a group constrains two
dimensions, the shipped mapping leaks — a punch for an employee at S1 in a *different* department
would be delivered. This is the mirror image of the duplicate-delivery warning 001 P3 left behind;
under option A it inverts from a cosmetic bug into a correctness leak, which is why it gets its own
portion rather than a footnote. Two workable answers were considered:

- **(a) Composite group keys** — one group name per *combination* of constrained values
  (`site:S1&dept:D9`), with the event addressed to every subset of the employee's axes. Correct,
  O(2^dimensions) keys per event — but the connection must join the **cross product** of its
  constraint value sets, so a manager over 50 sites × 20 departments joins 1,000 groups. Rejected
  on that.
- **(b) Evaluate the rule per open connection.** The registry already holds every connection and
  its user (`AttendanceConnectionRegistry.cs:61`); it additionally holds the resolved `ScopeRule`,
  and a punch is sent with `Clients.Clients(matching)`. Exactly correct for every rule shape, cost
  O(open sockets) of pure in-memory predicate evaluation per punch, and it **removes the
  two-mirrors-must-agree hazard** that `AttendanceScopeGroups.cs:8-18` documents at length.
  003 P1 rejected enumerating *users*; enumerating *connections* is a different and bounded
  population.

**Take (b).** `AttendanceScopeGroupTests.A_connection_never_receives_the_same_event_twice` becomes
trivially true (one send per connection) — rewrite it to assert that, do not delete it.
**Tests:** every existing realtime test still passes; a two-dimension intersection rule receives
only employees satisfying both; an employee with a null department is not delivered to a
department-constrained connection; a punch nobody can see reaches nobody; a scope change re-groups
mid-session; no duplicate delivery; measure and record the send cost at a realistic connection
count so the O(sockets) choice is on the record.
**Risk:** medium-high — it changes shipped, reviewed code (#18), and a mistake is silent.

### [ ] P6 — Delete the old model  ·  **irreversible**
**Touches:** `Domain/User.cs:22-42` (delete `Role`, `UserRole`, `RolePermission`),
`Domain/SecurityGroup.cs:88-93` (delete `UserSecurityGroup`), `Data/IdentityDbContext.cs`,
a migration dropping the four tables, `WM.Modules.Identity.Tests`
**Done when:** `identity."Roles"`, `"RolePermission"`, `"UserRole"` and `"UserSecurityGroup"` are
gone; the build has no reference to them; the `MembershipCollapseReport` survives as the record of
what was there.
**Not dropped here:** `SecurityGroup.ScopeKind`, `IncludeChildSites`, `SecurityGroupSite`,
`SecurityGroupDepartment`. They are still the group editor's contract until 001 P5 rewrites it;
their removal is that portion's closing act.
**Tests:** the full suite green with no reference to the dropped types; the report table still
answers "what did user X have before the collapse".
**Risk:** medium mechanically, but this is **the point of no return**. `Down` recreates the tables
empty and says so; it does not pretend to restore rows. Do not schedule it until the estate has run
on the new model long enough to trust it — nothing else in this plan waits on it.

---

## Open questions for the user

1. **Is `SelfOnly` a rule kind or its own column?** This plan models it as
   `ScopeRuleKind.Self` — under exclusivity a mode *is* a rule kind, and a separate `Mode` column
   would be two fields carrying one meaning, which is the shape the survey criticised in legacy.
   The one thing lost: legacy lets a role be `IsSelfOnly` **and** keep its managed lists dormant, so
   flipping the flag back restores them (`AuthorizationService.cs:220-224` short-circuits without
   wiping). Under `ScopeRuleKind`, switching a group to `Self` discards its constraints.
   **Recommendation: the rule kind.** Say if you want the dormant-constraints behaviour instead.

2. **Should a `Manager` built-in group be seeded?** Legacy has three built-in roles; this plan seeds
   two groups (`Administrator`, `Employee`) because a built-in `Manager` has no defensible default
   scope — it would either see nothing (confusing) or everything (dangerous). The demo seed instead
   creates a real `Site managers` group with a site constraint. **Recommendation: two built-ins.**

3. **`GET /api/access-diagnostics/{userId}` — keep it?** It was justified in three documents and two
   code comments by a claim about legacy that turned out to be false (C2 — legacy ships no such
   tool). Under one membership the answer is a lookup, and the endpoint reduces to "here is the
   user's group and what it grants". That may still be worth having as an admin affordance, but it
   should stand on its own merit now, not on the citation. **Recommendation: keep, renamed to
   something honest, and let 001 P5 decide how much it explains.**

4. **`ARCHITECTURE.md` §4A is now inconsistent with §4, and §4A is a design section this plan may
   not edit.** Concrete diff for you to accept or amend:
   - `:182` — *"Custom roles (bundles of fine-grained permissions) layer on top of these — an
     Administrator can define e.g. a 'Payroll Officer' role."* → **"Custom **groups** layer on top
     of these — an administrator can define e.g. a 'Payroll Officer' group carrying its own rights
     and scope. A user belongs to exactly one."**
   - `:197` — *"**Roles → permissions → policies** (already live in WM) for *what actions* a user
     may perform."* → **"**Group → permissions → policies** for *what actions* a user may
     perform."**
   - `:198` — *"…successor to TLW `RoleBasedEmployeeFilterService` + `SiteItemPermission` +
     `FormAccess`"* → **"…successor to TLW `RoleBasedEmployeeFilterService` and
     `dbo.EmployeeIdsManagedByRole`"**. `SiteItemPermission` is an in-memory DTO and `FormAccess` a
     static helper class (`TLW-AUTHORIZATION-MODEL.md` §1); neither is a scope mechanism, and
     naming them here is correction C5 in a third place.
   - `:193` — *"edit roles/active state/employee link"* → **"edit group / active state / employee
     link"**.

5. **The `CanEditOwnRecord` default is `true`** — today's WM behaviour, so the collapse cannot
   narrow anyone. Legacy's default for `Role.CanModifySelf` was not measured and this plan
   deliberately did not re-open `E:\Tlw` to find out. If you want new groups to default to `false`
   (see your own record, HR edits it), say so and P1 changes one line — but the *migration* default
   must stay `true` either way.
