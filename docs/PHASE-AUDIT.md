# Phase audit — everything shipped, re-checked against legacy and against WM's own documents

*2026-08-04. Retrospective audit of all merged work (PRs #3, #6, #7, #8, #14, #15), plan 001's two
completed portions (P1 merged as #12, P2 open as #16), and the seven planning documents. Measured
on branch `feat/001-P2-persist-composed-scope`.*

> **What this audit is for.** Two phases shipped before the surveyor/builder/reviewer cycle
> existed — no plan file, no survey, no review. This checks them the way a plan would have. Every
> claim below cites a file and a line on both sides. Where a document was right, it says so;
> where it was wrong, the correction is already applied and listed in §5.

---

## 0. What was measured

| Measurement | Result | How |
|---|---|---|
| Legacy tables | **578 distinct** (579 `TableAttribute` mappings — `dbo.PredefinedAbsences` is mapped twice) | `HorioDB.designer.cs`, 240,262 lines |
| Legacy columns | **8,173** | same |
| `dbo.Clockings` | **249** cols, `BadgeTime1..12`, `CPTN01..20`, six shifts, **75** `calc_*` columns | same |
| `dbo.Employees` | **153** cols — has `DepartmentId`, `EmployeeLocationId`, `CostCentreId`; **no `SiteId`, no `BuildingId`** | same |
| `dbo.Role` | **6** cols: `Id, Name, IsDepartmentOnly, IsSelfOnly, CanModifySelf, ManagementType` | same |
| WM HTTP endpoints | **29** across 5 files, + 1 SignalR hub + `/health` | `Map*` registrations |
| WM endpoints with a permission policy | **28 / 29** (`/api/auth/me` has bare `RequireAuthorization()`, correctly) | read every one |
| WM tests | 22 (`WM.SharedKernel.Tests`) + 11 (`WM.Modules.Identity.Tests`) = **33**, all passing | `dotnet test`, both csproj |
| WM entities | 8 aggregates / 15 classes | read every domain file |

Legacy source files opened, not summarised: `RoleBasedEmployeeFilterService.cs`,
`AuthorizationService.cs`, `IAuthorizationService.cs`, `Role.cs`, `FormAccess.cs`,
`Database\Versioning\80.V5.26.0.0.sql`.

---

## 1. Findings, ranked

Severity: **blocking** = a security or data defect in merged code · **material** = wrong or
missing in a way that will cost a later phase · **minor** = worth fixing, no consequence yet ·
**confirmed-correct** = checked, and it holds.

### A — Blocking

---

#### A1 · The realtime punch feed is broadcast to every authenticated client, unscoped
**Phase:** 1 (punch pipeline, PR #8) · **Severity: blocking**

`BroadcastingEventStreamProducer` sends every punch to every connected socket:

```csharp
// src/Api/WM.Api/Infrastructure/EventStreamProducers.cs:109
await hub.Clients.All.SendAsync("punchRecorded", @event, cts.Token);
```

The payload is `PunchRecorded(punchId, employeeId, employeeCode, employeeName, siteId, timestamp,
direction, source)` — name, badge number, site and movement. The hub carries `[Authorize]` with no
policy (`src/Api/WM.Api/Realtime/AttendanceHub.cs:7`), and is mapped without one
(`src/Api/WM.Api/Program.cs:65`), so **any authenticated user** — including a self-service-only
Employee-role user holding nothing but `selfservice.access` — receives the entire estate's punch
stream. The Angular client subscribes unconditionally
(`frontend/portal/src/app/core/realtime/realtime.service.ts:49-50`).

This is the same leak the HTTP path explicitly defends against, in a comment, twelve lines long:

```csharp
// src/Modules/TimeAttendance/.../PunchService.cs:98-99
// Restrict to employees the caller may see before touching punches — otherwise
// the feed would leak the existence and movements of out-of-scope staff.
```

`GET /api/punches/recent` does the right thing. The realtime leg beside it does not, and it is the
one that fires on every punch.

**Why it was missed:** the coverage matrix is organised by "what legacy had", and legacy has no
realtime push — so there was no row to mark. `COVERAGE-AUDIT.md` counted "29 endpoints" by
counting `Map{Get,Post,Put,Delete}`, which excludes `MapHub`. A transport nobody counted is a
transport nobody scoped.

**Fix:** plan **003 P1** (new). Server-side SignalR groups derived from resolved scope.

---

#### A2 · Employee writes are not scope-checked
**Phase:** 1 (employee CRUD, PR #8) · **Severity: blocking**

Two holes, both in `src/Modules/People/WM.Modules.People/PeopleModule.cs`:

1. **Create is entirely unscoped.** `MapPost("/")` at `:77-113` takes `EmployeesManage` and
   validates only that `request.SiteId` names an existing site (`:86-87`). A manager scoped to
   site A can create an employee at site B. There is no `IDataScopeResolver` in the handler's
   parameter list at all.
2. **Update checks the pre-image only.** `:122-125` correctly loads through `WithinScope` —
   "Edit is scoped like read: you cannot modify someone you cannot see" — and then at `:139`
   assigns `employee.SiteId = request.SiteId` with no check on the new value. A manager can move
   an employee out of their own scope, or into someone else's.

Legacy distinguishes these: `AuthorizationService.CanCurrentUserAccessEmployee(int employeeId,
AccessType accessType)` (`:266-289`) takes `Read` vs `Edit` as an argument. It does not check the
post-image either — so WM inverting this is an improvement, not a port.

> **Re-measured 2026-08-05 — the distinction is thinner than this reads.** The enum is
> `Read`/`Edit`, not `View`/`Edit` (`Enums.cs:9-13`); there are exactly three call sites
> (`ApiAuthorizer.cs:45`, `AuthorizingControllerBase.cs:76, 82`); and the argument changes the
> answer in exactly **one** case — your own record when `CanModifySelf = false`
> (`AuthorizationService.cs:281-286`). Otherwise `Read` and `Edit` resolve through the identical
> `dbo.EmployeeIdsManagedByRole` call. **Legacy's read scope and write scope are the same set.**
> WM therefore needs the post-image check (003 P2) but does **not** need a separate write-scope
> *model*. See `TLW-AUTHORIZATION-MODEL.md` §5.

**Fix:** plan **003 P2**.

---

### B — Material

---

#### B1 · There is no authorization fallback policy — unannotated endpoints are public
**Phase:** 0/1 · **Severity: material**

`src/Api/WM.Api/Program.cs:59-60` calls `UseAuthentication()`/`UseAuthorization()`, and
`IdentityModule.cs:65-69` registers one policy per permission — but never sets
`options.FallbackPolicy`. An endpoint mapped without `.RequireAuthorization(...)` is therefore
anonymous. Today 28 of 29 carry a policy and the 29th (`/api/auth/me`) carries a bare
`RequireAuthorization()`, so nothing is currently exposed — but the *default* is open.

That is structurally the same defect as legacy's `default:` branch returning an unfiltered query
(`RoleBasedEmployeeFilterService.cs:56-61`), which ARCHITECTURE.md §14 decision 5 commits WM to
inverting. CLAUDE.md invariant 5 ("no endpoint ships without an authorization policy") is
currently enforced by author discipline, and A1 is what author discipline produces at scale.

**Fix:** plan **003 P2**.

---

#### B2 · The department scope dimension has been unreachable since PR #7
**Phase:** 1b (access model, PR #7) · **Severity: material**

`DataScopeKind.Departments` exists in the enum (`DataScope.cs:16`), is honoured by the query
filter (`EmployeeScopeExtensions.cs:20-21`), is validated by the service
(`SecurityGroupService.cs:153-154`), was migrated by plan 001 P2, and is seeded as data
(`PeopleSeeder.cs:31-39`). It cannot be selected:

```html
<!-- frontend/portal/src/app/pages/security-groups/security-groups.component.ts:113-116 -->
<option [ngValue]="DataScopeKind.None">No employee data</option>
<option [ngValue]="DataScopeKind.Self">Own record only</option>
<option [ngValue]="DataScopeKind.Sites">Chosen sites</option>
<option [ngValue]="DataScopeKind.All">All employees</option>
```

`Departments` is absent from the dropdown, and there is **no `/api/departments` endpoint anywhere
in the API** to populate a picker even if it were added. Half of the shipped access model is
dead code from the user's point of view.

This is also a hard prerequisite for plan 001 **P5**, whose "Done when" assumes a multi-dimension
editor. It cannot be built without the endpoint.

**Fix:** plan 001 **P5** amended in place (prerequisite recorded); plan **003 P3** builds the
endpoint if it lands first.

---

#### B3 · Three of `dbo.Role`'s six columns are unmodelled, and one of them inverts WM's semantics
**Phase:** 1b · **Severity: material** (product decision, not a code defect)

`dbo.Role` has exactly six columns. Plan 001 surveyed one of them (`ManagementType`).

| Column | What it does | WM |
|---|---|---|
| `IsSelfOnly` | **Overrides everything**: `GetPermittedEmployeeIdsForUser` returns only the linked employee and never consults the managed lists (`AuthorizationService.cs:220-224`); same at `:236-240` and `:275-279` | no equivalent |
| `CanModifySelf` | May *see* your own record but not *edit* it (`AuthorizationService.cs:281-286`) | no equivalent |
| `IsDepartmentOnly` | ~~Persisted (`:866`), set through `UpdateRole`; no read path found in `Logic` — **unverified whether it is live**~~ **Settled 2026-08-05: dead code.** | do not model |

`IsSelfOnly` matters because it is a **narrowing** override, and WM's composed model is
union-only by design (`ScopeModel.cs:174-180`) — a union cannot express "and nothing else". If
customers use this configuration, WM has no way to represent it.

**Fix:** recorded in ARCHITECTURE.md §14 decision 5 addenda and in plan 001's *Still open*.
Needs a user ruling before Phase 1c.

> **Closed out 2026-08-05 by the authorization survey**
> ([`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md)):
>
> - **`IsDepartmentOnly` is dead.** Searched all of `E:\Tlw\Source` and `E:\Tlw\Database`. In
>   TLW's web product it is only ever *written* — the designer property
>   (`HorioDB.designer.cs:6287, 6394-6409`), the `UpdateRole` parameter
>   (`IAuthorizationService.cs:55`, `AuthorizationService.cs:828, 866`) and the editor checkbox
>   (`GroupsController.cs:135, 164`). No read in `Logic`, no read in `WebSite`, no SQL predicate;
>   `dbo.EmployeeIdsManagedByRole` never references it. The only reads in the repository are in
>   *other* products (`HealthWebSite`, `HorioMigration`). **WM must not model it.**
> - **`IsSelfOnly` and `CanModifySelf` are confirmed live**, with the call sites above verified,
>   plus `IsReadonlyForUser:544-570`, `UserCanModifySelf:539-542`,
>   `EmployeeUserCanModifySelf:528-537`, `UserHasLimitedBySelfPermissions:572-585` and the group
>   editor's own employee picker (`GroupsController.cs:616-619`).
> - **The framing "WM's composed model is union-only" is now the live question, not a footnote.**
>   A TLW user holds exactly one role, so `IsSelfOnly` never has to beat a union — there is
>   nothing to beat. WM's union exists because WM chose multi-membership, which the docs
>   attributed to TLW in error. B3's real content is therefore §4's model decision, which is
>   open for the user in `TLW-AUTHORIZATION-MODEL.md` §13–§14.

---

#### B4 · Plan 001 P4's Building dimension has no legacy basis
**Phase:** 1b (unbuilt) · **Severity: material**

P4 was approved to add Building as "a cheap real third dimension". Measured: `dbo.Employees` has
153 columns and **no `BuildingId`**. Every table carrying `BuildingId` is physical plant —
`Devices`, `GetDoorStatuses`, `ac_security_group`, `AnprEventsView`, `EposTills`, `LapiCameras`,
`FireMarshalMusterPoints`. All are dropped by invariant 3 except muster points (phase 6b).
`ManagedBuildingsByRole` exists as a role table, but there is nothing on an employee for it to
match against.

Building would be a WM invention wearing a legacy label. `CostCentre` is the honest alternative:
`Employees.CostCentreId` is real, `CostCentresManagedByRole` is real, and Phase 2 needs it.

**Fix:** plan 001 **P4 amended in place** — Building removed, employee list retained. Replacing it
with CostCentre is raised in *Still open* as a user decision.

---

#### B5 · The employee editor silently wipes department and phone on every edit
**Phase:** 1 (employee CRUD, PR #8) · **Severity: material**

`frontend/portal/src/app/pages/employees/employees.component.ts:300` hard-codes
`departmentId: null` in the save payload, and `:275` populates the form with `phone: ''` because
`EmployeeRow` never carried phone. `PUT /api/employees/{id}` is a full replace
(`PeopleModule.cs:137-140`), so **editing an employee's job title clears their department and
phone number**.

Department is a **scope dimension**. Wiping it changes who can see that person — a data-integrity
bug that becomes an access bug. It also means the department scope, once reachable (B2), would
degrade every time anyone edited an employee.

**Fix:** plan **003 P3**.

---

#### B6 · `GET /api/sites` is unscoped
**Phase:** 1 · **Severity: material**

`PeopleModule.cs:167-169` returns every site to any caller with `employees.view`. A manager scoped
to one site can enumerate the whole estate's structure — names, hierarchy, time zones. Minor on
its own; it is the picker that feeds the employee editor, so it also offers sites the caller
cannot legitimately assign to (which is how A2 becomes easy to trigger).

**Fix:** plan **003 P3**.

---

#### B7 · Plan 001 P2's stated test — "migration up/down on a seeded DB" — was not done
**Phase:** 001 P2 (PR #16, open) · **Severity: material**

P2's plan says: *"**Tests:** migration up/down on a seeded DB; a round-trip test asserting a
pre-migration group resolves to the identical employee set post-migration."*

What shipped is `LegacyScopeMappingTests.cs` — 11 tests, all passing, all exercising the **C#**
`LegacyScopeMapping.FromLegacy`. The **SQL** in
`20260804104816_AddComposedScopeConstraints.cs:76-124` is a second, independent implementation of
the same mapping, written as raw enum literals, and nothing tests it. I read both and they agree
(`ScopeKind 4→3, 1→1, 3→2, 2→2, else 0`; Site constraint carries `IncludeChildSites`; Department
constraint forces `false`), so this is a **process** finding, not a defect — but the portion's own
acceptance bar was not met and the review passed it anyway.

`Down()` is genuinely reversible, as claimed: the `Up` data migration only ever *reads*
`ScopeKind`, `IncludeChildSites`, `SecurityGroupSite` and `SecurityGroupDepartment` and only ever
*writes* the two new tables and the new column, all three of which `Down` drops (`:134-148`).
Verified by reading, not by running.

**Fix:** doc-only. Recorded here; the missing DB-level test should be folded into plan 001 **P3**,
which touches the same rows and needs a seeded-DB harness anyway.

---

### C — Minor

---

#### C1 · `ARCHITECTURE.md` §2 tech stack still says Angular 19
`docs/ARCHITECTURE.md:96` reads *"Frontend | Angular 19 (standalone, signals, zoneless)"*.
`frontend/portal/package.json` pins `@angular/core: ^22.1.0`, `@angular/cli: ^22.1.2`,
`typescript: ~6.0.3`. PR #14 did the upgrade; the table was not updated, and §2's own note twelve
lines below already says the Angular 19 ceiling is obsolete. **Not corrected here** — §2 is a
design section the surveyor may propose but not edit. One-line diff, in §5.

#### C2 · `CLAUDE.md` still says ".NET 9 + Angular"
`CLAUDE.md:3` reads *"**Workforce Management Platform** — .NET 9 + Angular rebuild"*.
`Directory.Build.props:3` is `net10.0` and every project inherits it. A builder reading CLAUDE.md
first could reasonably target `net9.0`. **Not corrected here** — CLAUDE.md is propose-only. Diff
in §5.

#### C3 · A fourth legacy fail-open of the same shape was never recorded
`if (managedEmployees.Any())` at `RoleBasedEmployeeFilterService.cs:50, 89, 128` — a `ByEmployees`
role with an empty list also sees everyone. WM's `ScopeRule.Constrained` already handles it; the
docs said "three fail-opens". **Corrected** in ARCHITECTURE.md §14 decision 5.

#### C4 · The rule has four implementations in legacy, not three
The plan counted the three C# marker-interface overloads. The authoritative path is the T-SQL
function `dbo.EmployeeIdsManagedByRole`
(`E:\Tlw\Database\Versioning\80.V5.26.0.0.sql:880-934`), called by `IsEmployeeManagedByRole`
(`AuthorizationService.cs:291-302`) and `GetPermittedEmployeeIdsForRole` (`:245-264`). **The two
disagree**: on an unknown `ManagementType` the C# returns the query unfiltered, the SQL returns an
empty table. WM's fail-closed choice therefore matches legacy's own SQL. **Corrected** in §14.

#### C5 · `SCREEN-TREE.md` §3 double-counted one dimension and included a non-existent one
It listed "departments · locations · sites · buildings · cost centres · work activities" as
`ByStructure`'s axes. Locations and sites are the same axis (`Employees.EmployeeLocationId`; there
is no `SiteId`), and buildings do not exist on an employee (B4). Four axes matter, not five.
**Corrected.**

#### C6 · `TLW-INVENTORY.md` said 579 tables, `COVERAGE-AUDIT.md` said 578
Both were arithmetically right: 579 `TableAttribute` mappings, 578 distinct names,
`dbo.PredefinedAbsences` mapped twice. The disagreement was unexplained across two documents that
cite each other. **Corrected in both**, with the reconciliation stated.

#### C7 · `ARCHITECTURE.md` §14's opening note still said "247 entities" and "3–5%"
Superseded by `COVERAGE-AUDIT.md` (578 tables, under 2%) and never updated at source — the exact
failure mode the surveyor charter names. **Corrected.**

#### C8 · Four §13 rows were stale, four were missing
"▢ planned (building now)" for user management, employee linking and the self-service portal —
all merged in PR #3. Missing entirely: the realtime feed, the API-auth surface, screen-level
rights, and the deny-list drop. **Corrected.**

#### C9 · Role and permission changes do not revoke live sessions
`UserManagementService.UpdateAsync` (`:122-149`) changes roles and `IsActive` without touching
refresh tokens; `ResetPasswordAsync` (`:151-168`) does revoke. Permissions are baked into the
access token (`AuthService.cs:100-110`), so a demoted user keeps their old permissions until the
token expires. `RefreshAsync` re-reads roles and checks `IsActive` (`:75-80`), so the window is
bounded by access-token lifetime. Standard, defensible, undocumented. No fix proposed.

#### C10 · `WmPermissions.SitesManage` is declared and never used
`WmPermissions.cs:14` declares it; no endpoint requires it, and there is no site CRUD at all.
Sites can only be created by the dev seeder. Not a defect today; a gap for 1c.

---

### D — Confirmed correct

Checked in full and found sound. Listing these is the point of an audit — silence would not
distinguish "verified" from "not looked at".

| # | Claim | Verification |
|---|---|---|
| D1 | Plan 001's three cited legacy fail-opens are real, at the cited lines | `RoleBasedEmployeeFilterService.cs:36-39` (empty list → no filter), `:44` (`EmployeeLocationId == null \|\|`), `:56-61` (`default:` logs Fatal, returns unfiltered, with the "avoid an error getting to the user" comment quoted verbatim) |
| D2 | Legacy exposes six managed dimensions plus an employee list | `IAuthorizationService.cs:22-23` (cost centres), `:37-42` (departments, locations, employees, buildings, work activities, caterers) |
| D3 | `ByDepartments` really is an intersection | `:34-46` — two successive `.Where()` calls |
| D4 | `ByEmployees` is real and WM had no equivalent | `:48-54`; `EmployeesManagedByRole` is a real table |
| D5 | P1's `ScopeModel.cs` delivers what the plan promised | intersection within `ScopeRule` (`:167`), union across `DataScope` (`:225`), `All`/`Self`/`None` preserved (`:116-129`), empty ids match nobody (`:98`, tested), null discriminator never widens (`:112-113`, tested), zero-constraint `Constrained` collapses to `None` (`:157-158`) — the fail-open that would otherwise be reintroduced by the *fix* |
| D6 | P1's constraint does not alias the caller's set | `:87` copies; test at `ScopeRuleTests.cs:80-92` mutates the caller's `HashSet` and asserts no widening. This is the kind of defect that is only ever found by writing the test |
| D7 | P2 maps **every** `DataScopeKind` including `None` and `Self` | `LegacyScopeMapping.cs:22-48`, five arms plus a fail-closed `default`; tested per kind at `LegacyScopeMappingTests.cs:55-102` |
| D8 | P2 carries `IncludeChildSites` onto every Site dimension with the group's existing value | `LegacyScopeMapping.cs:35`, migration `:96` (`SELECT "Id", 0, "IncludeChildSites"`); tested both ways at `LegacyScopeMappingTests.cs:104-116` |
| D9 | No row is widened or narrowed by P2's migration | the C# and SQL mappings agree arm for arm; equivalence is asserted against `EffectiveDataScope.CanSee` over a five-shape population including null departments (`LegacyScopeMappingTests.cs:28-53`). **Per-group. Cross-group resolution does change — see the P3 amendment** |
| D10 | P2's `Down()` genuinely restores prior state | `Up` never mutates `ScopeKind`, `IncludeChildSites`, `SecurityGroupSite` or `SecurityGroupDepartment`; `Down` drops exactly what `Up` created (`:134-148`) |
| D11 | The seeder and the service write both representations | `IdentitySeeder.cs:90-91`, `SecurityGroupService.cs:57-58` (create) and `:93-102` (update) |
| D12 | `TLW-CLOCKING-MODEL.md`'s headline numbers | measured: `dbo.Clockings` = 249 columns, `BadgeTime1..12` (with `…GeneratedBy`, `…Adjusted`, `…Location`, `…AdjustedShift1..6` variants), `CPTN01..20`, six shifts, **75** `calc_*` columns |
| D13 | `COVERAGE-AUDIT.md`'s 8,173 columns and its bucket total | 8,173 confirmed; buckets sum to 578 |
| D14 | `SCREEN-TREE.md` §3's "legacy only enforces two centrally" | `RoleBasedEmployeeFilterService` honours departments ∩ locations only; buildings/cost centres/activities are read elsewhere (`ManagedWorkActivitiesAuthorizer.cs:40`, `ManagedDepartmentsAuthorizer.cs:20,32`, `ExternalAccess/TLW.cs:586,779`) |
| D15 | Out-of-scope employee reads 404 rather than 403 | `PeopleModule.cs:69-75`, with the reasoning in a comment; the timesheet endpoint routes through the same check (`TimeAttendanceModule.cs:57-58`) |
| D16 | `POST /api/punches` cannot punch an out-of-scope employee | `PunchService.cs:32` goes through the scope-aware `FindByCodeAsync` (`PeopleModule.cs:197-201`) |
| D17 | Self-service takes the employee id from the token, never the request | `TimeAttendanceModule.cs:70, 76, 84`; `PeopleModule.cs:175` |
| D18 | Refresh-token rotation with reuse detection and family revocation | `AuthService.cs:57-87`; password reset revokes all sessions (`UserManagementService.cs:162-165`) |
| D19 | Containerisation (PR #6) | `deploy/docker-compose.yml`, `deploy/docker-compose.prod.yml`, three Dockerfiles present |
| D20 | .NET 10 upgrade (PR #14) is complete at the build level | `Directory.Build.props:3` = `net10.0`, inherited by all 11 projects; both test projects build and run on `net10.0` |
| D21 | Both test projects pass | `WM.SharedKernel.Tests` 22/22, `WM.Modules.Identity.Tests` 11/11 |

---

## 2. What this says about the process

Every **blocking** finding is in code merged **before** the plan/review cycle existed (PRs #3, #7,
#8). Every portion that went through the cycle (001 P1, 001 P2) checks out — P1 in full, P2 with
one process gap (B7) that is a missing test rather than a wrong behaviour. The cycle is working;
what it has not done is go back.

Two structural lessons:

1. **The coverage matrix cannot see WM-only surfaces.** It is organised by legacy capability, so a
   thing WM added that legacy never had gets no row and no scrutiny. That is exactly how A1
   survived. §13 now carries rows for WM additions.
2. **Counting routes is not counting attack surface.** "29 endpoints" was true and useless. The
   hub, the health check and the seeders are all reachable surface. `COVERAGE-AUDIT.md` §1 now
   says so.

---

## 3. Disposition

| Finding | Severity | Where it gets fixed |
|---|---|---|
| A1 realtime feed unscoped | blocking | **plan 003 P1** (new, draft) |
| A2 employee writes unscoped | blocking | **plan 003 P2** |
| B1 no authorization fallback policy | material | **plan 003 P2** |
| B2 department dimension unreachable | material | **plan 001 P5** (amended) / **003 P3** |
| B3 `IsSelfOnly` / `CanModifySelf` unmodelled | material | user decision — plan 001 *Still open*, §14 addenda. **`IsDepartmentOnly` half closed 2026-08-05: dead code, do not model.** The remainder is now §4's model decision — `TLW-AUTHORIZATION-MODEL.md` §13 |
| B4 P4's Building has no legacy basis | material | **plan 001 P4 amended in place** |
| B5 editor wipes department + phone | material | **plan 003 P3** |
| B6 `GET /api/sites` unscoped | material | **plan 003 P3** |
| B7 P2's DB-level migration test missing | material | fold into **plan 001 P3** |
| C1 §2 says Angular 19 | minor | **propose only** — diff in §5 |
| C2 CLAUDE.md says .NET 9 | minor | **propose only** — diff in §5 |
| C3–C8 doc errors | minor | **corrected in place**, §5 |
| C9 sessions survive role change | minor | no action; documented |
| C10 `SitesManage` unused | minor | Phase 1c |
| D1–D21 | confirmed-correct | no action |

---

## 4. Are the five unowned items of `COVERAGE-AUDIT.md` §3 still unowned?

Yes, four of five — but only one touches a shipped surface.

| Item | Cols | Still unowned? | Load-bearing for |
|---|---:|---|---|
| `ApiKeys` / `RsaKeys` / `SynergyAppAuthenticationTokens` | 5 / 4 / 6 | **now owned** — plan 003 P4 (design note) | **Identity, already shipped.** Legacy authenticates the mobile app as an *employee* with a per-device revocable token (`SynergyAppAuthenticationTokens: Id, EmployeeId, TokenHash, IssueDate, IsRevoked, MobileDeviceInfo`), not as a user with a password. WM's "Flutter uses the same JWT as the web" loses per-device revocation, which a phone-only punch product needs |
| `CalculationProcessingQueue` (+ `UserCalculationProcessingQueue`) | 5 / 3 | yes | **Phase 2, imminent.** Plan 002 should name it — the queue shape (`SerializedMessage, Status, PercentageComplete`) is what WM's replay design is reinventing |
| `FileVirusScanQueue` | 5 | yes | Phase 4, distant |
| `SalaryDeductions` (+ `SalaryDeductionTypes`) | 8 / 3 | yes | Phase 6, distant |
| `Currencies`, `Cultures` | 6 / 6 | yes | Phase 6/7, distant |

---

## 5. Corrections applied, and corrections proposed

### Applied by this audit

| File | Change |
|---|---|
| `docs/TLW-INVENTORY.md` | 579 → **578 distinct tables (579 mappings)**, with the `dbo.PredefinedAbsences` duplicate named and the two documents' disagreement reconciled |
| `docs/COVERAGE-AUDIT.md` §1 | table/mapping reconciliation; endpoint count qualified (hub + health were uncounted, and that is why A1 was missed); test projects 1→2, 22→33 |
| `docs/COVERAGE-AUDIT.md` §3 | column counts added per table, ownership column added, `ApiKeys`/`SynergyAppAuthenticationTokens` shapes measured |
| `docs/COVERAGE-AUDIT.md` §4 | "the access model is shipped under test" qualified — the model is, the enforcement is not |
| `docs/COVERAGE-AUDIT.md` §5 | queue re-ordered; plan 003 inserted; old 003/004 suggestions renumbered to 004/005 |
| `docs/ARCHITECTURE.md` §13 | 4 stale "building now" rows corrected against the code; 4 rows added (realtime feed ⚠️, API auth ▢, screen-level rights ▢, deny-lists ⏹); `⚠️` added to the legend; audit note added |
| `docs/ARCHITECTURE.md` §14 | opening note's "247 entities / 3–5%" replaced with the measured 578 / under 2%; Phase 1b now names plans 001 **and 003**; decision 5 extended with the fourth fail-open, the SQL fourth implementation and its disagreement with the C#, the three unmodelled `Role` columns, and the Buildings measurement; **new decision 7** records the deny-list drop |
| `docs/SCREEN-TREE.md` §3 | locations/sites de-duplicated, buildings removed with evidence, five→four dimensions, accessor naming corrected, status box added for both halves of the model |
| `docs/plans/001-compositional-data-scope.md` | **P3** amended (three sanctioned behaviour changes + the seeded-manager baseline warning); **P4** amended (Building removed, with the measurement); **P5** amended (`GET /api/departments` prerequisite); *Amendments* and two new *Still open* questions added |
| `docs/plans/003-enforcement-gaps.md` | **new**, status `draft`, 4 portions |
| `docs/plans/STATE.md` | audit recorded, queue updated |

### Proposed, not applied (outside the surveyor's edit scope)

**P1 — `docs/ARCHITECTURE.md:96` (§2 is a design section):**
```diff
-| Frontend | Angular 19 (standalone, signals, zoneless), Tailwind, "Control Room" design system |
+| Frontend | Angular 22 (standalone, signals, zoneless), Tailwind, "Control Room" design system |
```
Evidence: `frontend/portal/package.json` → `@angular/core: ^22.1.0`, `@angular/cli: ^22.1.2`,
`typescript: ~6.0.3`. §2's own note at `:108-109` already calls the Angular 19 ceiling obsolete.

**P2 — `CLAUDE.md:3`:**
```diff
-**Workforce Management Platform** — .NET 9 + Angular rebuild of the legacy TLW (Time & Labour Workforce) suite.
+**Workforce Management Platform** — .NET 10 (LTS) + Angular 22 rebuild of the legacy TLW (Time & Labour Workforce) suite.
```
Evidence: `Directory.Build.props:3` = `net10.0`, inherited by all 11 projects.

**P3 — `docs/ARCHITECTURE.md:157` (§4), the one that is a real contradiction, not staleness.**
§4 says groups carry per-screen rights *and* scope in one object, "mirroring TLW". The shipped
code says the opposite, deliberately and in writing: `SecurityGroup.cs:10-13` ("Deliberately
orthogonal to `Role`: roles say what a user may *do*, groups say which records they may *see*"),
and plan 001's *Out of scope* calls that orthogonality "settled and stays". Full replacement text
is in **plan 003, Open questions #4**. This one needs a user decision because it changes what
Phase 1b delivers.
