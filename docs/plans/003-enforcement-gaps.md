# 003 — Close the enforcement gaps in the shipped phases

Status: in-progress      <!-- draft → approved → in-progress → in-review → merged -->
Approved by user 2026-08-04, all 4 portions, and **ordered ahead of 001 P3** — A1 and A2 are
defects in running code, 001 P3 corrects a model no endpoint consults yet.

> **P2 was split into P2a + P2b on 2026-08-05, at the user's direction.** Same scope, same
> "Done when" conditions, two branches instead of one — nothing was added or removed, so the
> approval as given still covers it. The plan now has **5 portions**.
Roadmap: ARCHITECTURE.md §14 Phase 1b — Access model (this is the enforcement half; plan 001 is
the model half). Also repairs defects merged in Phase 1 (PRs #3, #7, #8).
Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Security\AccessControl\RoleBasedEmployeeFilterService.cs`
- `E:\Tlw\Source\Logic\Security\AccessControl\AuthorizationService.cs`
- `E:\Tlw\Source\Logic\Interfaces\IAuthorizationService.cs`
- `E:\Tlw\Database\Versioning\80.V5.26.0.0.sql` (`dbo.EmployeeIdsManagedByRole`)
- Schema measurement: `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — 578 distinct tables /
  8,173 columns; `dbo.Employees` 153 cols; `dbo.Role` 6 cols; `ApiKeys` 5; `RsaKeys` 4;
  `SynergyAppAuthenticationTokens` 6.

## Why this plan exists

Plan 001 fixes the *shape* of the scope rule. This plan fixes the places the rule is never
consulted at all. Both matter, but a correct rule behind an unscoped transport buys nothing, and
the gaps below are in code that is merged, running, and would ship to a customer today.

They were missed because the phases that introduced them (PR #3 users/self-service, PR #7 access
model, PR #8 punch + employee CRUD) predate the surveyor/builder/reviewer cycle: no plan file, no
survey, no review. The full evidence is in [`../PHASE-AUDIT.md`](../PHASE-AUDIT.md).

## Ground truth

Measured 2026-08-04 against the working tree at `feat/001-P2-persist-composed-scope`.

| | Measured |
|---|---|
| HTTP endpoints | 29 (`Map{Get,Post,Put,Delete}`) across 5 files |
| Non-HTTP transports | 1 SignalR hub (`/hubs/attendance`) + `/health` — **neither counted before** |
| Endpoints with a permission policy | 28 of 29 |
| Endpoints with `AllowAnonymous` | 3 (`/api/auth/login`, `/refresh`, `/logout` — correct) |
| Endpoints reading employee data **without** `WithinScope` | `GET /api/sites` |
| Endpoints **writing** employee data without a scope check | `POST /api/employees` |
| Authorization fallback policy | **none** — an unannotated endpoint is anonymous |
| Scope applied on the realtime leg | **none** |

Legacy comparison points actually opened:
- `AuthorizationService.cs:291-302` — legacy's authoritative per-employee check is the SQL TVF
  `dbo.EmployeeIdsManagedByRole`, i.e. one filter used by every path, not a per-call-site choice.
- `AuthorizationService.cs:266-289` `CanCurrentUserAccessEmployee(employeeId, AccessType)` — legacy
  takes an **access type** (`Read` / `Edit`). WM reuses the read filter for writes where it checks
  at all.
  > **Re-measured 2026-08-05 — this line overstated the split.** The enum is `Read`/`Edit`
  > (`Enums.cs:9-13`), it has three call sites (`ApiAuthorizer.cs:45`,
  > `AuthorizingControllerBase.cs:76, 82`), and it changes the answer in exactly **one** case:
  > your own record when `CanModifySelf = false` (`:281-286`). Otherwise both resolve through the
  > same `dbo.EmployeeIdsManagedByRole` call. **Legacy's read scope and write scope are the same
  > set.** So WM needs no separate write-scope *model* — only the post-image check P2 already
  > specifies. See `../TLW-AUTHORIZATION-MODEL.md` §5.
- `AuthorizationService.cs:245-264` — legacy's permitted-employee set is additionally filtered by
  `IsActive` and `dbo.IsActiveEmployment(DischargeDate, getdate())`. WM's `WithinScope` has no
  status filter; that is a defensible improvement but it is undocumented.

## Legacy behaviour (what we are replacing)

Legacy has **no realtime leg at all** — there is nothing to compare A1 against, because live
push is a WM addition. That is precisely why it slipped: the coverage matrix is organised by
"what legacy had", so a WM-only surface has no row and gets no scrutiny. The matrix now carries
a row for it.

For the rest, legacy's shape is a warning rather than a model: enforcement scattered across
`RoleBasedEmployeeFilterService` (three overloads), the SQL TVF, `WebsiteAccessFilter` and
per-controller checks. WM's single `WithinScope()` is the right answer — it just has to be
reached from every path, including the ones that are not queries.

> **Correction 2026-08-05.** This paragraph used to end *"…which is why it needed
> `DataAccessScopeDiagnostics` to explain its own answers."* **False.**
> `DataAccessScopeDiagnostics` counts `DataContext` create/dispose to find connection leaks
> (`Logic/DAL/DataContextTracking/DataAccessScopeDiagnostics.cs:10-55`). Legacy has no tool that
> explains an access decision. See `../TLW-AUTHORIZATION-MODEL.md` §11 C2.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| One central employee filter (`WithinScope`) | **Keep** | Right idea; legacy's scatter is the counter-example. |
| Realtime push of punches | **Improve** | Genuinely better than legacy (which has none) — but it must be a *scoped* fan-out, not a broadcast. Per-user groups keyed on resolved scope. |
| Read scope reused as write scope | **Keep the set, add the post-image check** *(reclassified 2026-08-05)* | Measured: legacy's `AccessType.Read` and `AccessType.Edit` resolve through the **same** `dbo.EmployeeIdsManagedByRole` call and differ in exactly one case — your own record under `CanModifySelf = false` (`AuthorizationService.cs:281-286`). So reusing the read set for writes is *correct*, not a defect. What legacy genuinely lacks is any check on the **post-image**: a legacy manager can move an employee out of their own scope. That check is WM's addition (003 decision 3), and it is the whole of P2b. |
| Endpoint-by-endpoint opt-in to authorization | **Invert** | An unannotated endpoint being anonymous is a fail-open of exactly the kind §14 decision 5 rejects. A `FallbackPolicy` makes the default deny. |
| Site list readable by anyone with `employees.view` | **Improve** | Legacy has `Locations` behind personnel setup. A scoped user should see the sites their scope reaches, not the estate. |
| `AccessRightsExclusions` deny-lists | **Drop** | Already decided; now recorded in §14 decision 7 rather than only in a code comment. |
| `ApiKeys` / `RsaKeys` / `SynergyAppAuthenticationTokens` | **Improve** | Legacy authenticates the mobile app as an **employee** with a per-device revocable token. WM's "same JWT as the web" is simpler but loses per-device revocation, which is a real requirement for a phone-only punch product. Design now, build with Flutter (phase 9). |

## Edge cases

- **A user connected to the hub when their scope changes.** Group membership is edited; the open
  socket must be re-grouped or dropped, or they keep receiving the old scope's punches until they
  reconnect. Legacy has no analogue; decide it explicitly.
- **A punch for an employee visible to nobody** (no groups grant them). It must reach the hub for
  zero clients, not for all of them.
- **A self-service-only user (Employee role) with an open socket.** They hold `selfservice.access`
  and nothing else; today they receive the entire estate's punch stream.
- **Create-then-escape.** A manager scoped to site A creates an employee at site B: allowed today
  (`PeopleModule.cs:77-113` validates only that the site exists). The employee then does not
  appear in the creator's own list, which is also how you notice.
- **Edit-then-escape.** A manager scoped to site A edits an employee they can see and sets
  `SiteId` to site B (`PeopleModule.cs:139`). The pre-image is in scope, the post-image is not.
- **A full-replace PUT with a partial form.** `employees.component.ts:300` sends
  `departmentId: null` unconditionally and `:275` sends `phone: ''`, so any edit through the UI
  wipes both. Department is a **scope dimension** — wiping it silently changes who can see that
  person.
- **An endpoint added without `RequireAuthorization`.** Today: public. Must be: 401.

## Target design in WM

- **Scoped fan-out.** `BroadcastingEventStreamProducer` stops calling `hub.Clients.All`. The hub
  places each connection into SignalR groups derived from the connected user's resolved scope
  (site groups, department groups, `self:{employeeId}`, `all`), and a punch is sent to the groups
  that the punched employee belongs to. This keeps the fan-out O(groups) and never enumerates
  users. ARCHITECTURE.md §2 (SignalR) and §4 (scope as filters, never per endpoint) — the hub is
  the one place "filter at the query" cannot apply, so it needs its own stated rule.
- **`FallbackPolicy = RequireAuthenticatedUser`** in `IdentityModule.RegisterServices`, so the
  default is deny and `AllowAnonymous` becomes the deliberate exception it already reads as.
- **Write scope**: a `WithinScope` check on the *post-image* of employee create and update, plus
  the existing pre-image check on update.
- **Scoped `GET /api/sites`** and a new scoped `GET /api/departments` (the latter is plan 001 P5's
  prerequisite; build it here if 003 lands first, and delete the duplicate from P5).

## Out of scope for this plan

- Screen-level permissions (legacy `dbo.AccessControlEntry`) — the other half of Phase 1b, now
  surveyed and drafted as **plan 004**. *(This line previously named `WebPage`/`FormAccess`;
  neither is the store — `dbo.WebPages` is localization and `FormAccess` is a C# helper class.
  See `../TLW-AUTHORIZATION-MODEL.md` §1.)*
- Building/implementing the API-key auth surface. P4 produces a **design note only**; the
  implementation belongs with Flutter in phase 9.
- Anything in plan 001's P3–P5. This plan must not touch `ScopeModel.cs`, `DataScopeResolver.cs`
  or `EmployeeScopeExtensions.cs` beyond calling them.
- Employee status/leaver filtering semantics — noted in the audit, decided with 1c.

## Portions

### [x] P1 — Scope the realtime punch feed  ·  reviewed 2026-08-05, [#18](https://github.com/00008550/WM/pull/18)
**Touches:** `src/Api/WM.Api/Realtime/AttendanceHub.cs`,
`src/Api/WM.Api/Infrastructure/EventStreamProducers.cs`, `src/Api/WM.Api/Program.cs`,
new `src/Api/WM.Api.Tests/`
**Done when:** a punch is delivered only to connections whose resolved scope contains the punched
employee; the hub requires `attendance.view` (not merely authentication); a self-service-only user
receives nothing on the hub. Verified in the browser with two sessions side by side.
**Tests:** first API-host test project — wire into `WM.sln`. Cover: admin receives, in-scope
manager receives, out-of-scope manager does not, employee-role user does not, punch for an
employee nobody can see reaches nobody.
**Risk:** **high** — it is the live data leak, and it is the only portion where a mistake is
silent (nothing errors; the wrong people just keep seeing things).

### ~~[ ] P2 — Fail closed by default, and scope the writes~~ — **split 2026-08-05 into P2a and P2b**

> **Why it was split.** The user directed that the `FallbackPolicy` half proceed while the
> write-scoping half waited on a measurement of whether legacy's read scope and write scope are
> genuinely different decisions. **That measurement is now done and it clears both halves** — but
> they are still better as two portions, because they touch different modules (Identity vs People)
> and have different risk profiles, which is exactly the "split if it crosses two modules" rule.

### [ ] P2a — Fail closed by default
**Touches:** `src/Modules/Identity/WM.Modules.Identity/IdentityModule.cs`,
`WM.Modules.Identity.Tests`
**Done when:** `FallbackPolicy` requires an authenticated user, the three `AllowAnonymous` auth
endpoints still work, `/health` still answers unauthenticated.
**Tests:** an endpoint mapped without `RequireAuthorization` returns 401; `/health` still 200
anonymously; the three auth endpoints still 200 anonymously.
**Risk:** medium — the fallback policy can break an endpoint that was quietly relying on being
anonymous. There is only one candidate (`/health`) and it is asserted.
**Independent of the §4 decision.** Correct under every model.

### [ ] P2b — Scope the employee writes
**Touches:** `src/Modules/People/WM.Modules.People/PeopleModule.cs`, People tests
**Done when:** `POST /api/employees` refuses a site the caller's scope does not contain;
`PUT /api/employees/{id}` refuses a *post-image* the caller's scope does not contain, in addition
to today's pre-image check.
**Tests:** create-out-of-scope returns 403; edit-into-out-of-scope returns 403; edit within scope
still succeeds; the pre-image check still 404s an invisible employee (not 403).
**Risk:** medium.

**Cleared to proceed — measurement 2026-08-05** ([`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md) §5):

- **Legacy's write scope *is* its read scope.** `CanCurrentUserAccessEmployee` resolves both
  through the same `dbo.EmployeeIdsManagedByRole(roleId)` call; the `AccessType` argument alters
  the result in exactly one case (`AuthorizationService.cs:281-286`). WM does **not** need a
  second scope model for writes, and P2b must not invent one — reuse `WithinScope`.
- **Legacy never checks the post-image at all.** A legacy manager can edit someone they can see
  and move them out of their own scope. P2b's post-image check is a WM improvement (003 decision
  3), correct under every §4 option, and has no legacy behaviour to preserve.
- **One deferred addition, not a blocker.** Legacy's `Role.CanModifySelf` says "may see your own
  record, may not edit it" — the only genuine read/write asymmetry in the record layer. WM has no
  equivalent. If the §4 decision adopts it (`TLW-AUTHORIZATION-MODEL.md` open question 3), P2b's
  write check gains a self carve-out. Additive; do not block P2b on it. **Leave a named seam**
  (a single `CanEditOwnRecord` predicate, defaulting to `true`) rather than hard-coding the
  current behaviour, so the carve-out is a one-line change later.

### [ ] P3 — Scoped site and department lists
**Touches:** `src/Modules/People/WM.Modules.People/PeopleModule.cs`,
`frontend/portal/src/app/core/api/workforce.api.ts`,
`frontend/portal/src/app/pages/employees/employees.component.ts`
**Done when:** `GET /api/sites` returns only sites the caller's scope reaches; `GET /api/departments`
exists with the same rule; the employee editor carries `departmentId` and `phone` through an edit
instead of blanking them.
**Tests:** admin sees all sites, a site-scoped manager sees theirs; editing an employee preserves
department and phone (the current UI wipes both — assert the round-trip, not the request body).
**Risk:** low. **Note:** this portion is plan 001 P5's prerequisite. Whichever plan runs first
builds it; the other deletes its copy.

### [ ] P4 — API/integration auth: design note, no code
**Touches:** `docs/TLW-API-AUTH.md` (new), `docs/ARCHITECTURE.md` §4 (proposal only)
**Done when:** a short reference document records what legacy's three key tables actually do —
`ApiKeys(Id, Name, ApiKey, IsActive, ApiType)`, `RsaKeys(Id, PrivateKeyXml, PublicKeyXml, CreatedAt)`,
`SynergyAppAuthenticationTokens(Id, EmployeeId, TokenHash, IssueDate, IsRevoked, MobileDeviceInfo)` —
what `ApiType` discriminates, which callers use which, and what WM's equivalent should be. Ends
with a recommendation for phase 9, not an implementation.
**Tests:** none — documentation portion.
**Risk:** low. Closes the last of `COVERAGE-AUDIT.md` §3's five unowned items that touches a
shipped surface.

## Decisions (user, 2026-08-04)

1. **The hub fans out by scope, server-side.** Client-side filtering was rejected: it is not
   security, because the payload still crosses the wire to a client that should never have
   received it. P1 builds SignalR grouping keyed on the connection's resolved scope.
2. **An open socket is re-grouped when the user's groups change** — option (b): push a
   "re-authorize" message and move the connection between groups, rather than waiting for
   reconnect or dropping the socket. Revocation must bite within the session, not at next login.
3. **A manager may not create an employee outside their own scope.** Legacy does not enforce this
   either way, so it is WM's choice: a create you cannot then see is indistinguishable from a
   create that failed. P2 enforces it on the post-image of both create and update.

## Deliberately not solved here — session and token revocation

Decision 2 revokes a user's **realtime audience** within the session. It does **not** revoke their
session, and the difference is easy to miss precisely because the visible half looks complete: a
socket that stops delivering punches reads like "access removed", while the same account is still
calling `GET /api/employees` with the same bearer token.

What P1 gives you, concretely:

- A scope change (group edited or deleted, membership changed, user deactivated, employee link
  changed) re-groups every open socket that user holds, immediately.
- Nothing else. The access token they already hold keeps working on every HTTP endpoint until it
  expires — `AccessTokenMinutes` is 10 by default, so **a deactivated user keeps HTTP access for
  up to ten more minutes.** They cannot extend it: `AuthService.RefreshAsync` re-checks `IsActive`
  and refuses, so the exposure is bounded by the access-token lifetime rather than the 14-day
  refresh window.
- Permissions are worse than scope, because they are claims. `WmPermissions` policies are
  `RequireClaim` against the JWT (`IdentityModule.cs:66-70`), so removing a role does not narrow a
  live token, and — unlike scope — **it does not close a socket the user already holds either**:
  the hub authorizes `attendance.view` at connect and never again. That one is not bounded by ten
  minutes; it lasts as long as the connection does.

None of this is a P1 defect: revoking a session is a token-lifetime decision (short-lived access
tokens plus a revocation list, or per-request re-validation) that belongs to `TokenService` /
`AuthService`, not to the scope model or the transport. It is recorded here so the next person to
read `AttendanceAudience` does not conclude revocation is solved. Owner: the phase that takes
session lifetime — see also P4, which designs per-device revocable tokens for the Flutter app.

## Still open — §4, and it does not block this plan

**User direction, 2026-08-04: "probably best to adapt strategy from old TLW."**

§4 currently says *"**Groups** carry per-screen rights (none / read / edit) **and** employee
scope, in one object — mirroring TLW"*, while the shipped code does the opposite: roles carry
permissions, groups carry scope (`SecurityGroup.cs:10-13`), which plan 001 records as settled.
The user has directed that WM follow legacy's model rather than the doc being rewritten to match
the code.

**This is recorded, not yet actioned, and deliberately so.** Two reasons:

- **Nobody has measured legacy's `/Groups` object.** "Adapt TLW's strategy" needs the actual
  shape — what a per-screen right is, how `WebPage`/`FormAccess` rows hang off a group, and how
  legacy resolves a user in several groups with conflicting rights — before anyone writes the §4
  text or changes an entity. That is a survey, and it should precede the decision, not follow it.
- **It changes Phase 1b's *second* half only.** Screen-level rights are out of scope for both
  plan 001 and this plan. P1 (scope the hub) and P2 (fail closed, scope the writes) are unaffected
  by whether a group also carries screen rights — they are about transports and write paths that
  must consult *whatever* the scope rule turns out to be.

**Next step when this plan's P1/P2 land:** survey `E:\Tlw` for the `/Groups` editor and its
backing tables, then bring back a concrete §4 amendment plus an honest cost for reconciling it
with the shipped `SecurityGroup`. Until then §13/§14 carry the contradiction as `⚠️`, and plan
001's orthogonality claim stands as *provisional*, not settled.

> ### ✅ **That survey ran early, on 2026-08-05.** [`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md)
>
> It did not settle §4 — it changed what the question is.
>
> - **§4's "one object" claim is right.** `/Groups` edits exactly one `dbo.Role`
>   (`GroupsModel { Role, Roles, SiteItems }`, `Models/Security/GroupsModel.cs:6-12`;
>   `GroupsController.cs:76-108`), carrying screen rights **and** all seven managed-dimension
>   lists **and** navigation icons, dashboard widgets and exclusions, saved by one
>   `UpdateRole(...16 args...)` + `SetBulkPermissions` pair.
> - **§4's "several groups, union" claim is wrong and was never TLW.** A legacy user holds
>   exactly **one** role (`AuthorizationService.cs:1265-1280`, `:773-786`, `:1008-1041`).
> - **Screen rights resolve deny-over-allow, default deny** (`:695-718`) — the opposite of
>   most-permissive-wins, though unreachable in practice for the same reason.
>
> So "adapt TLW's strategy" resolves to a real fork, and it is the user's call:
> **(A)** one object *and* one group per user, which is what makes legacy's model answerable
> without any resolution rules; or **(B)** keep WM's multi-membership and write down two
> precedence rules legacy never needed. The survey recommends **(A)**, gives the concrete §4
> replacement text for it, and lists the eight parts of legacy's model that should **not** be
> adapted — see §12–§14 there.
>
> **This plan is unaffected.** P1 shipped, P2a and P2b are correct under either option.
> Plan 001 P3 is not: it implements the union, and it is on hold pending the ruling.
