# 004 — Screen-level rights (the second half of Phase 1b)

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 1b — Access model. Plan 001 is the *scope* half, plan 003 the
*enforcement* half; this is the *screen rights* half, which both explicitly excluded.

> ### ✅ The blocking decision has been taken — **option A**, user, 2026-08-05.
> One object, one membership: a security group carries screen rights, data scope, a mode
> (`Normal` / `SelfOnly`) and `CanEditOwnRecord`, and a user belongs to exactly one.
> `ARCHITECTURE.md` §4 has been rewritten to match. The portions below were already written for
> option A and stand as drafted; the §"If the user picks B" section is retained only as a record
> of the path not taken and should be deleted when this plan is next revised.
>
> **Still `draft`, and still not executable**, for a different reason: option A requires the
> one-membership refactor (`User.Roles` → `User.SecurityGroupId`, the JWT claim shape, both
> seeders, `DataScopeResolver`) which is *not* in this plan. **That work is now planned as
> [`005-one-membership.md`](./005-one-membership.md)** (6 portions, `draft`) and must land first.
>
> **What 005 changes for this plan.** 005 merges `Role`/`RolePermission` into `SecurityGroup`, so
> the group already carries a set of `WmPermissions` values by the time this plan starts. P2 below
> therefore becomes *"extend the rights a group already holds"* rather than *"introduce rights
> beside roles"* — a materially smaller change — and open question 2 ("do `WmPermissions`
> survive?") is half-answered: they survive as the group's rights at today's granularity, and this
> plan decides whether to refine them into a `None/Read/Edit` catalogue. 005 deliberately does
> **not** invent a second vocabulary first.

Legacy sources surveyed (all read in full unless noted):
- `E:\Tlw\Source\WebSite\Controllers\Security\GroupsController.cs` (845 lines)
- `E:\Tlw\Source\WebSite\Controllers\AuthorizingControllerBase.cs` (721)
- `E:\Tlw\Source\Logic\Security\AccessControl\AuthorizationService.cs` (1,620)
- `E:\Tlw\Source\Logic\Security\AccessControl\FormAccess.cs`, `AccessAction.cs`, `ObjectType.cs`,
  `ObjectRef.cs`, `SiteItemPermission.cs`, `Enums.cs`, `BuiltinRoles.cs`
- `E:\Tlw\Source\Logic\SiteStructure\SiteItem.cs`, `SiteFormTab.cs`, `SiteStructure.cs`
- `E:\Tlw\Source\WebSite\Misc\HorioSiteStructure.cs` (the tree itself)
- `E:\Tlw\Source\WebSite\Models\Security\GroupsModel.cs`, `SiteItemAccess.cs`
- `E:\Tlw\Source\Logic\Security\HrDocument\RoleHrDocumentSecurityService.cs`
- Schema: `HorioDB.designer.cs` — `dbo.AccessControlEntry` **6** cols (`:10330-10346`),
  `dbo.Role` **6** (`:6277`), `dbo.RoleNavigationIcons` 3, `dbo.RoleDashboardCategories` 3,
  `dbo.RoleHrDocumentSecurity` 4, `dbo.AccessRightsExclusions` + 2 children 3+3+3.
  **`dbo.WebPages` (3 cols) is a localization table, not an authorization one** (`:27642-27654`).

## Ground truth

Measured 2026-08-05. Full detail and citations in
[`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md); the numbers this plan is sized
against:

| | Measured |
|---|---|
| Secured-object store | **`dbo.AccessControlEntry(Id, Allow, RoleId, ActionId, SecuredObjectTypeId, SecuredObjectId)`** |
| Secured objects | 49 branches + 382 forms (7 of them tabbed) + 75 tabs |
| Rows written per group, per save | **≈960–1,010** — every node, every time, no inherit, no unset |
| Right vocabulary | branch: access / none · form & tab: none / view / edit, **edit ⇒ view** |
| Resolution | **deny-overrides-allow; no matching allow = denied** (`AuthorizationService.cs:695-718`) |
| Roles per user | **exactly one** (`:1265-1280`, `:773-786`, `:1008-1041`) |
| Enforcement point | `AuthorizingControllerBase.OnAuthorization:547-591` — GET needs view, POST needs edit |
| Key | `SecuredObjectId` = the node's `/`-joined **path string** (`SiteItem.cs:41-52`) |
| Admin | hard bypass at every check (`FormAccess.cs:22-24, 94-97, 131, 147-150, 173-176`) |

WM today: **nothing.** No screen-right entity, no policy, no endpoint. The Angular routes guard on
coarse permission names client-side (`frontend/portal/src/app/app.routes.ts:17-40`), and
`WmPermissions` has **11** permissions (`src/SharedKernel/WM.SharedKernel/Security/WmPermissions.cs`)
against legacy's ~500 secured nodes.

**Corrections this survey already made** to `ARCHITECTURE.md` §13/§14, `SCREEN-TREE.md` §3 and
`PHASE-AUDIT.md` B3 are listed in `TLW-AUTHORIZATION-MODEL.md` §11.

## Legacy behaviour (what we are replacing)

See `TLW-AUTHORIZATION-MODEL.md` §4 for the full account. The parts that shape this plan:

1. **The tree is licence-filtered first, rights-filtered second.** `SiteStructure` is rebuilt per
   install from the licence package: a branch with no allowed children is dropped entirely
   (`SiteStructure.cs:196, 226-240`). Rights are then granted only over what survives. WM's §5
   already says an unlicensed branch is *absent*; this is the same order and it is right.
2. **Ancestor branches gate their children.** A form's view right is insufficient if any ancestor
   branch is denied (`FormAccess.cs:37-43, 104-110, 157-163`).
3. **A new group starts fully denied** (`GroupsController.cs:695-723`).
4. **Some tabs cannot be denied.** `[DisallowDenyAccess]` on `PersonnelTab.General`,
   `DocumentsTab.General`, `DepartmentsTab.General` forces at least read-only
   (`Enums.cs:17, 60, 115`; `FormAccess.cs:76-79, 204-208`).
5. **Granting a branch grants *edit* on the branch's own landing page** — a quiet widening
   (`FormAccess.cs:245-253`).
6. **The tab layer is a licence flag that grants when absent.** `!IsFormTabsSupported` makes every
   tab viewable *and* editable (`FormAccess.cs:64, 131, 199, 231`).
7. **The right is keyed on a path string**, so renaming a menu node orphans every grant.
8. **Nav icons and dashboard widgets are per-role allow-lists** on the same screen
   (`AuthorizationService.cs:511-526, 1423-1432`) — presentation, not security.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Rights and scope on one editable object | **Keep** | Measured; it is what makes the model explainable, and it is the user's §4 direction. |
| Tri-state none / read / edit; edit ⇒ view | **Keep** | Clean, and maps to HTTP method directly. |
| Ancestor branch gates children | **Keep** | Prevents orphaned leaf grants. |
| New group denies everything | **Keep** | Fail-closed default; already WM's instinct. |
| GET needs read, POST/PUT/DELETE need edit | **Keep** | The API-first form of `OnAuthorization:565-575`. |
| Admin bypass | **Keep, but make it a grant not a branch** | A hard-coded `if (isAdmin) return true` is untestable and unauditable. Model the admin group as *holding every right*, so the same code path answers. |
| Path-string keys | **Improve** | Stable `ScreenId` (e.g. `people.employees`); path is display metadata only. |
| ~1,000 rows per group per save | **Improve** | Store **deviations from `None`** only. A fully-denied group is zero rows. |
| Deny rows | **Drop under option A** | With one group per user, `Allow = false` is only ever "the absence of a grant", which zero rows already says. §14 decision 7 stands. |
| `[DisallowDenyAccess]` tabs | **Improve** | Legacy's way of saying "this screen has a floor". Model as a screen-catalogue property (`MinimumRight`), not a special case in the resolver. |
| Branch access implying edit on its landing page | **Invert** | Silent widening. A branch grant grants navigation, nothing more. |
| Licence flag that *grants* the tab layer | **Invert** | Unlicensed ⇒ absent from the catalogue, never permitted. |
| Nav icons / dashboard categories | **Keep, as presentation** | On the group, explicitly not a security boundary. Ships after the rights themselves. |
| `RoleHrDocumentSecurity` (`Deny/ReadOnly/ReadWrite`) | **Improve** | A third vocabulary for the same idea. Fold into the one right vocabulary when Documents is built; do not port the enum. |
| Tab-level rights | **Defer** | `SCREEN-TREE.md` already defers them. Screens first. The catalogue must not make them impossible to add. |

## Edge cases

Each of these needs a test that names it.

- **A screen is renamed.** Under legacy, every grant on it is silently lost
  (`SecuredObjectId = FullName`). Under WM's stable id, nothing changes. Assert it.
- **A screen is unlicensed.** It must be absent from the catalogue, absent from the editor, and
  the endpoint must 404 — not "present and denied", and emphatically not legacy's
  "unlicensed ⇒ permitted" (`FormAccess.cs:64`).
- **A parent branch is denied but a child screen is granted.** The child must be unreachable, and
  the editor should show why.
- **A screen with a floor** (`MinimumRight = Read`, legacy `[DisallowDenyAccess]`) is set to
  `None`. It must persist and resolve as `Read`, not `None`.
- **`Edit` granted, `Read` not.** Impossible by construction (edit ⇒ read), but the *stored* form
  must not permit expressing it — assert the invariant, do not rely on the UI.
- **A user with no group.** Under option A this is unrepresentable (the link is required). Assert
  that it cannot be created, rather than deciding what it would mean.
- **A right revoked mid-session.** Permission claims live in the JWT
  (`IdentityModule.cs:66-70`), so today a removed right survives up to `AccessTokenMinutes`
  (10) and, on the SignalR hub, for the life of the connection — recorded in 003's *Deliberately
  not solved here*. This plan must not make that worse: screen rights are resolved **server-side
  per request**, never trusted from a claim.
- **The SPA hides a screen it may not open.** Hiding is UX; the server re-checks. Assert the
  server 403s even when the route guard is bypassed.

## Target design in WM

- **Screen catalogue** — a declared, licence-filtered tree of `Screen { Id, ParentId, Kind
  (Branch|Screen), MinimumRight, RequiredLicenseFeature }` in SharedKernel. Static and
  code-declared, like legacy's, so it is reviewable in a diff. Cite: `ARCHITECTURE.md` §5
  ("a navigation branch is a licensable module") and `SCREEN-TREE.md`.
- **`ScreenRight { None, Read, Edit }`**, edit ⇒ read, on the group. Stored as deviations only.
- **Resolution** — `IScreenRightResolver.Resolve(user, screenId) → ScreenRight`, honouring the
  ancestor rule and the catalogue floor. One implementation, one call path (the lesson from
  legacy's four copies of the scope filter).
- **Enforcement** — a `RequireScreen("<id>", Read|Edit)` authorization policy alongside the
  existing `WmPermissions` policies, applied per endpoint, plus the fallback policy from 003 P2a.
  Cite `ARCHITECTURE.md` §4 and CLAUDE.md invariant 5.
- **`GET /api/screens`** (licensed catalogue, for the editor) and **`GET /api/me/screen-rights`**
  (resolved rights, for the SPA's guards). Both API-first per invariant 2.
- **Editor** — the screen-rights tree inside the existing security-group editor, so a group is
  edited as one object, as `/Groups` does.

## Out of scope for this plan

- **The §4 model decision itself.** This plan consumes it; it does not make it.
- Tab-level rights (deferred by `SCREEN-TREE.md`; the catalogue must merely allow them later).
- `RoleHrDocumentSecurity` — arrives with the Documents module.
- Nav icons / dashboard categories — presentation, after the rights.
- Deny rules and `AccessRightsExclusions` — dropped (§14 decision 7). The unmet requirement
  behind them is open question 4 in `TLW-AUTHORIZATION-MODEL.md`; it is not solved here.
- Token/session revocation of rights — owned by whichever phase takes session lifetime
  (003 *Deliberately not solved here*).
- Anything in plan 001 or 003.

## Portions

### [ ] P1 — Screen catalogue + right model in SharedKernel
**Touches:** `src/SharedKernel/WM.SharedKernel/Security/ScreenCatalogue.cs` (new),
`ScreenRight.cs` (new), `WM.SharedKernel.Tests`
**Done when:** the catalogue declares WM's current screens with parents, floors and licence
features; `ScreenRight` enforces edit ⇒ read; a pure `Resolve(rights, screenId)` honours the
ancestor rule and the floor. No persistence, no endpoints, nothing else compiles against it.
**Tests:** ancestor-denied hides a granted child; floor screen set to `None` resolves `Read`;
`Edit` implies `Read`; an unknown screen id resolves `None` (never a permissive default);
a renamed *display path* does not change resolution.
**Risk:** low — no callers.

### [ ] P2 — Persist rights on the group
**Touches:** `Identity/Domain/SecurityGroup.cs`, `IdentityDbContext`, new migration,
`SecurityGroupService`, `IdentitySeeder`, `WM.Modules.Identity.Tests`
**Done when:** a group persists a sparse set of screen rights; absent = `None`; the system
admin group resolves `Edit` on every screen **as stored data, not as an `if`**; migration is
reversible and adds no rights to any existing group.
**Tests:** round-trip; absent screen resolves `None`; admin group resolves `Edit` everywhere;
migration up/down on a seeded DB **actually written this time** (the gap `PHASE-AUDIT.md` found
in 001 P2).
**Risk:** medium — it is a schema change on a shipped table.

### [ ] P3 — Enforce it, and expose it
**Touches:** `IdentityModule.cs` (policy provider), `src/Api/WM.Api/Program.cs`,
`Identity/Endpoints/SecurityGroupEndpoints.cs`, endpoints across People/TimeAttendance,
`WM.Api.Tests`
**Done when:** `RequireScreen("<id>", Read|Edit)` exists and every non-`/api/me/*` endpoint
carries one; `GET /api/screens` returns the licensed catalogue; `GET /api/me/screen-rights`
returns the caller's resolved rights; a user without the right gets **403**, and an unlicensed
screen gets **404**.
**Tests:** read-only user 403s on POST and 200s on GET of the same screen; unlicensed screen
404s for everyone including admin; rights are resolved server-side even when the JWT is stale;
the 003 P2a fallback policy still holds.
**Risk:** **high** — it touches every endpoint, and a mistake is a lockout or a hole.
Sequence it so the policy defaults to the *existing* permission check until each endpoint is
migrated deliberately.

### [ ] P4 — The group editor shows one object
**Touches:** `frontend/portal/src/app/pages/security-groups/security-groups.component.ts`,
`security-groups.api.ts`, `core/api/workforce.api.ts`, route guards
**Done when:** an admin edits screen rights **and** data scope on one screen, as `/Groups` does;
the tree renders branch → screen with none/read/edit per row and disables rows below a denied
branch; the SPA's route guards read `/api/me/screen-rights`. Verified in the browser via
preview_start with two accounts side by side.
**Tests:** `npm run test` for the editor; browser verification with screenshots; a guard bypass
still 403s server-side.
**Risk:** medium.

## If the user picks option B (multi-membership) instead

Nothing is thrown away, but three things change and the plan must be re-drafted before approval:

- **P1** gains a cross-group resolution step. It must be **deny-over-allow**, matching legacy
  (`AuthorizationService.cs:695-718`) — *not* most-permissive-wins, which is what
  `ARCHITECTURE.md` §4 and `SCREEN-TREE.md` §3 currently imply.
- **P2** must then store explicit **deny** as distinct from **absent**, because under a union
  those differ. That reintroduces the deny rows §14 decision 7 dropped, and the decision needs
  revisiting rather than quietly contradicting.
- **P4** must explain a resolved right ("denied by group *Contractors*, granted by *Ops North*"),
  which is the feature legacy never had — and, contrary to what four WM documents said until
  2026-08-05, never shipped.

## Open questions for the user

1. **Option A or B** (`TLW-AUTHORIZATION-MODEL.md` §13). Blocks approval of this plan.
2. **Do `WmPermissions` survive?** Under A, a screen right subsumes most of the 11 permissions
   (`employees.view` ≈ `Read` on `people.employees`). Either fold them in — cleaner, one concept —
   or keep both and accept two overlapping systems. Recommendation: fold, with `selfservice.access`
   surviving as the one non-screen concept.
3. **Static catalogue or database table?** Legacy declares its tree in code
   (`HorioSiteStructure.cs`, 576 lines) and it is reviewable in a diff. A DB table would let a
   customer add a screen, which nothing in WM's plugin design currently needs.
   Recommendation: code, with plugin-contributed screens appended at startup from the manifest.
4. **How fine?** Legacy has ~500 nodes. WM has ~15 screens today and will not reach 500. Confirm
   screen-level is the right grain and tabs stay deferred.
