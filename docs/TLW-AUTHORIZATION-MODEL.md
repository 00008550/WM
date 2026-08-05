# TLW authorization model — what a person can see, and what they can edit

**Surveyed 2026-08-05.** This is the single authoritative answer to that question for the legacy
product. It replaces the partial and partly-wrong accounts scattered across `ARCHITECTURE.md` §4
and §13, `SCREEN-TREE.md` §3, `PHASE-AUDIT.md` B3 and plans 001/003. Where those documents
disagree with this one, **this one was measured and they were not** — see
[Corrections to WM's records](#corrections-to-wms-records).

Every claim below carries a `file:line`. Nothing here is inferred from another WM document.

---

## 1. The measurement

### Tables that participate in authorization

Measured from `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (the LINQ-to-SQL model,
578 tables / 8,173 columns overall). Column counts exclude navigation properties.

| Table | Cols | Columns | Line |
|---|---|---|---|
| `dbo.Role` | **6** | `Id, Name, IsDepartmentOnly, IsSelfOnly, CanModifySelf, ManagementType` | `:6277-6293` |
| `dbo.AccessControlEntry` | **6** | `Id, Allow, RoleId, ActionId, SecuredObjectTypeId, SecuredObjectId` | `:10330-10346` |
| `dbo.UsersInRoles` | 3 | `UserId, RoleId, IsActive` | `:6795` |
| `dbo.[User]` | 30 | (identity, password, 2FA, lockout) | — |
| `dbo.DepartmentsManagedByRole` | 3 | `Id, RoleId, DepartmentId` | `:43971` |
| `dbo.LocationsManagedByRole` | 3 | `Id, LocationId, RoleId` | `:183186` |
| `dbo.EmployeesManagedByRole` | 3 | `Id, RoleId, EmployeeId` | `:44163` |
| `dbo.CostCentresManagedByRole` | 3 | `Id, RoleId, CostCentreId` | — |
| `dbo.BuildingsManagedByRole` | 3 | `Id, RoleId, BuildingId` | — |
| `dbo.WorkActivitiesManagedByRole` | 3 | `Id, RoleId, WorkActivityId` | — |
| `dbo.EposCaterersManagedByRole` | 3 | `Id, RoleId, CatererId` | — |
| `dbo.AccessRightsExclusions` | 3 | `Id, RoleId, IsAllowedForSelf` | `:194910` |
| `dbo.AccessRightsExclusionEmployees` | 3 | `Id, EmployeeId, AccessRightsExclusionId` | `:195117` |
| `dbo.AccessRightsExclusionResources` | 3 | `Id, SecuredResourceName, AccessRightsExclusionId` | `:195309` |
| `dbo.NavigationIcons` | 4 | `Id, Code, Name, ParentId` | `:144669` |
| `dbo.RoleNavigationIcons` | 3 | `Id, RoleId, NavigationIconId` | `:144856` |
| `dbo.RoleDashboardCategories` | 3 | `Id, RoleId, DashboardCategory` | `:174770` |
| `dbo.RoleHrDocumentSecurity` | 4 | `Id, RoleId, HrDocumentType, AccessPermission` | `:103692` |

**18 tables, 59 columns.** That is the entire persisted authorization surface of a
578-table product. It is small because almost all of the expressive power lives in
`AccessControlEntry` rows, of which there are ~1,000 **per role**.

### Two names in WM's records are not authorization tables at all

- **`dbo.WebPages` is a localization table.** `WebPage(PageId, Name, Description)` with a child
  `LocalizationKeys` collection (`HorioDB.designer.cs:27642-27654`). It has no `RoleId`, no
  permission column, and nothing in `Logic/Security` reads it. §13 lists it as a screen-rights
  table; that is wrong.
- **`FormAccess` and `SiteItemPermission` are C# types, not tables.** `FormAccess` is a static
  helper class of extension methods (`Logic/Security/AccessControl/FormAccess.cs:7`);
  `SiteItemPermission` is an in-memory DTO carrying `(AccessAction, ObjectRef, bool IsAllow)`
  used only to pass a batch of edits into `SetBulkPermissions`
  (`Logic/Security/AccessControl/SiteItemPermission.cs:5-23`). The persisted form of both is
  `dbo.AccessControlEntry`.

### The secured-object tree

The thing rights are granted *on* is `SiteStructure` — the application's page tree, built in
`E:\Tlw\Source\WebSite\Misc\HorioSiteStructure.cs` (576 lines, one big object literal).

| | Measured |
|---|---|
| Branches (`Branch(`) | **49** |
| Forms (`Form(`) | **375** |
| Tabbed forms (`Form<TTabEnum>(`) | **7** |
| Form tabs (members of the 7 tab enums, `Logic/Security/AccessControl/Enums.cs:15-146`) | **75** |

Tab enums and their sizes: `PersonnelTab` 24, `SoftwareOptionsTab` 12, `GlobalSchedulerTab` 10,
`DocumentsTab` 9, `DepartmentsTab` 9, `VisitorSettingsTab` 9, `WebReportDesignerTab` 2.

Each branch yields 1–2 ACE rows, each form 2, each tab 2 (`GroupsController.cs:180-201`), so a
saved group writes **≈960–1,010 `AccessControlEntry` rows**. There is no inheritance and no
"unset" — every node is materialised for every role, every save
(`AuthorizationService.SetBulkPermissions:614-693`).

### Legacy files actually opened

```
Logic/Security/AccessControl/AuthorizationService.cs          (1,620 lines — read in full)
Logic/Security/AccessControl/FormAccess.cs                    (277)
Logic/Security/AccessControl/SiteItemPermission.cs            (25)
Logic/Security/AccessControl/AccessAction.cs                  (32)
Logic/Security/AccessControl/ObjectType.cs                    (72)
Logic/Security/AccessControl/ObjectRef.cs                     (38)
Logic/Security/AccessControl/BuiltinRoles.cs                  (63)
Logic/Security/AccessControl/Enums.cs                         (153)
Logic/Security/AccessControl/RoleBasedEmployeeFilterService.cs (143)
Logic/Security/HrDocument/RoleHrDocumentSecurityService.cs    (129)
Logic/Interfaces/IAuthorizationService.cs                     (108)
Logic/Entities/Role.cs                                        (partial class)
Logic/SiteStructure/SiteItem.cs, SiteFormTab.cs, SiteStructure.cs
Logic/DAL/DataContextTracking/DataAccessScopeDiagnostics.cs
SharedLogic/Enums/AccessPermission.cs
WebSite/Controllers/Security/GroupsController.cs              (845 — read in full)
WebSite/Controllers/AuthorizingControllerBase.cs              (721 — read in full)
WebSite/Controllers/DataAccessScopeDiagnosticsController.cs   (89)
WebSite/Models/Security/GroupsModel.cs, SiteItemAccess.cs
WebSite/Misc/HorioSiteStructure.cs
Database/Versioning/80.V5.26.0.0.sql:880-934   (dbo.EmployeeIdsManagedByRole)
```

---

## 2. The answer in one paragraph

A TLW user has **exactly one** `Role`. That single row *is* what the `/Groups` screen edits, and
it carries both the screen rights and the data scope. **Screen rights** decide which pages, and
whether GET-only or GET+POST; they are stored as ~1,000 allow/deny ACL rows per role keyed on the
page's path string, resolved **deny-overrides-allow, default deny**. **Data scope** decides which
employees; it is one SQL table-valued function over the role's managed-department ∩
managed-location lists (or an explicit employee list), resolved **fail-open on an empty list**.
The two are combined by *nothing* — they are checked at different places and never meet.
Read and write differ on the **screen** axis (`FormViewing` vs `FormEditing`) but are the **same
decision** on the **record** axis, with exactly one exception: your own record when
`CanModifySelf = false`.

---

## 3. Layer 0 — identity: one user, one role

This is the load-bearing fact that everything else depends on, and it is the one WM's records got
backwards.

```csharp
// AuthorizationService.cs:1265-1280
public Role GetUserRole(int userId)
    => _rolesCache.GetOrAdd(userId, id => (from ur in db.UsersInRoles
                                           where ur.UserId == userId
                                           select ur.Role).SingleOrDefault());
```

`SingleOrDefault` — more than one row **throws**. Reinforced everywhere:

- `LoadUserInRoles()` builds a `ConcurrentDictionary<int userId, int roleId>` —
  one role per user by construction (`:773-786`).
- `AddUserToRole(roleId, userId)` finds the existing row by **`userId` alone**, deletes it, and
  inserts the new one. Assigning a role *replaces* it (`:1008-1041`).
- `IsUserInRole(user, roleId)` is `_usersInRoleCache[user.Id] == roleId` (`:1236`).
- `GetManagedEmployeeIds` likewise `.SingleOrDefault()` on `UsersInRoles` (`:158-161`).
- The `/Groups` screen redirects an admin to *their own* role by
  `GetUserRole(GetCurrentUserId())?.Id` (`GroupsController.cs:70`).

**There are three built-in roles** — `Administrator` (Id 1), `Employee` (2), `Manager` (3)
(`BuiltinRoles.cs:33-38`) — and any number of custom ones. Administrator is hard-coded to bypass
every screen check (`FormAccess.cs:22-24, 94-97, 131, 147-150, 173-176`) and is read-only in the
editor (`Role.cs` `IsReadonly => IsAdministrator`; enforced at `SetBulkPermissions:616-619`,
`UpdateRole:860-863`, `GroupsController.cs:152-155, 256-263`).

**Pseudo-roles** `Everyone (-1)`, `Anonymous (-2)`, `Authenticated (-3)` exist in the resolution
code (`IsUserInRole:1222-1233`) but the declaration is commented **"pseudo roles - currently
unsupported"** (`BuiltinRoles.cs:9`) and `AddUserToRole` refuses them (`:998-1001`). No ACE can
be authored against them through the UI. They are the only way more than one `RoleId` could match
one user — which is why the deny-wins loop in §4 is, in practice, dead code.

> **Defect worth naming.** `dbo.UsersInRoles.IsActive` exists and is checked by
> `IsUserActiveInRole` (`:598-612`), which is what `IsCurrentUserAdministrator()` uses (`:113-116`).
> But `GetUserRole`, `LoadUserInRoles` and `IsUserInRole` **do not check it**. A user whose role
> assignment is deactivated therefore keeps their screen rights and their data scope, while
> `IsCurrentUserAdministrator()` starts returning false. The two halves of the system disagree
> about whether that person is in the role.

---

## 4. Layer 1 — screen rights ("what can I open, and can I POST to it")

### The vocabulary

Two secured-object **types** and three **actions** (`FormAccess.cs:9-18`, `AccessAction.cs:7-9`):

| ObjectType | Id | Actions | Action id |
|---|---|---|---|
| `Branch` | 1 | `BranchAccess` ("Access") | 1 |
| `Form` | 2 | `FormViewing` ("Viewing") | 1 |
| `Form` | 2 | `FormEditing` ("Editing") | 2 |

`BranchAccess` and `FormViewing` share action id **1**; they are disambiguated only by
`SecuredObjectTypeId`. Form **tabs** reuse `ObjectType.Form` — a tab is not its own type
(`SiteFormTab.cs:14-17`).

`SecuredObjectId` is a **path string**: `SiteItem.FullName`, i.e. the `/`-joined chain of node
names, e.g. `Personnel/Personnel/Salary` (`SiteItem.cs:41-52`). Renaming a menu node silently
orphans every right ever granted on it.

### What the UI offers per node

The editor is a radio per site item, posted as `acc_<FullName>` with one of
`allow | view | edit` (`GroupsController.cs:38-44, 180-201`):

```csharp
// GroupsController.cs:187-200 — the whole mapping
if (siteItem is SiteBranch branch)
    permissions.AddRange(FormAccess.GetBranchAccessPermissions(branch, allowAccess));
else if (siteItem is SiteForm siteForm) {
    permissions.Add(FormAccess.GetFormEditingPermission(siteForm, allowEdit));
    permissions.Add(FormAccess.GetFormViewingPermission(siteForm, allowEdit || allowView));
}
else if (siteItem is SiteFormTab siteFormTab) {
    permissions.Add(FormAccess.GetFormTabEditingPermission(siteFormTab, allowEdit));
    permissions.Add(FormAccess.GetFormTabViewingPermission(siteFormTab,
        allowEdit || allowView || siteFormTab.DisallowDenyAccess));
}
```

So: a branch is **access / no access**; a form or tab is **none / view / edit**, and
**edit implies view**. Anything not selected is written as an explicit `Allow = false` row —
there is no "unset".

`GetBranchAccessPermissions` also emits a `FormEditing` row for a synthesised form when the branch
itself has a URL (`FormAccess.cs:239-254`) — i.e. granting access to a branch grants **edit** on
the branch's own landing page.

`[DisallowDenyAccess]` on an enum member (`DisallowDenyAccessAttribute.cs`; applied to
`PersonnelTab.General`, `DocumentsTab.General`, `DepartmentsTab.General` in `Enums.cs:17, 60, 115`)
forces that tab to at least read-only and cannot be denied (`FormAccess.cs:76-79, 204-208`).

**A new group starts fully denied** — `GetRestrictedSiteItemAccess()` sets everything false
except `DisallowDenyAccess` tabs (`GroupsController.cs:695-723`). Screen rights fail **closed**.

### How a request is checked

`AuthorizingControllerBase.OnAuthorization` (`:547-591`) — this is the enforcement point for
every MVC screen:

```csharp
if (postAction)
    authorized = isAllowReadOnlyToPost
        ? _authService.CurrentUserCanEditForm(f) || _authService.CurrentUserCanViewForm(f)
        : _authService.CurrentUserCanEditForm(f);
else
    authorized = isAllowGetOnlyOnReadWrite
        ? _authService.CurrentUserCanEditForm(f)
        : _authService.CurrentUserCanViewForm(f);
```

**GET needs view, POST needs edit.** Unauthenticated → 401; authenticated but unauthorized →
403/AccessDenied. Opt-outs: `[AllowAnonymous]`, `[AllowAuthenticatedUser]`,
`[AllowReadOnlyToPost]`, `[AllowGetOnlyOnReadWrite]`.

The view layer then re-reads the same right to render read-only:
`ViewData[EditableKey] = CurrentUserCanEditForm(...)` (`:627-638`).

`CurrentUserCanViewForm` also walks **ancestors**: the form's own right *and*
`BranchAccess` on every branch above it must hold (`FormAccess.cs:37-43, 104-110, 157-163`).
A denied parent branch hides everything under it regardless of the children's rows.

### The conflict-resolution algorithm

This is the part the brief asked about, and it is **not a union**:

```csharp
// AuthorizationService.cs:695-718
private bool UserHasPermission(IAccessControlUser user, IEnumerable<AccessControlEntry> acl)
{
    var permissionFound = false;
    foreach (var ace in acl)
    {
        if (ace.Allow && permissionFound) continue;
        if (IsUserInRole(user, ace.RoleId))
        {
            if (ace.Allow == false) return false;   // deny wins, unconditionally
            permissionFound = true;
        }
    }
    return permissionFound;                          // no matching allow => denied
}
```

**Deny overrides allow, order-independent; absence of an allow is a deny.** `RoleCanPerformAction`
(`:77-99`) is the same rule for a single named role. The candidate ACL is selected by
`GetPermissions` (`:720-751`), which also matches rows with `SecuredObjectId == null` — a
wildcard-per-type grant that the `/Groups` UI cannot author but the resolver honours.

Because a user has exactly **one** role, the loop can only see multiple matching ACEs via
pseudo-roles or wildcard rows, neither of which the product creates. **In practice the algorithm
resolves a single row.** The deny-wins semantics are what the model *would* do under multi-role
membership, and they are the opposite of "most permissive wins".

### One licence flag disables the entire tab layer

`SiteStructure.IsFormTabsSupported` (`SiteStructure.cs:68`) is set from the licence package
property `"IsFormTabsSupported"` (`HorioSiteStructure.cs:554, 578`;
`LicensingConstants.cs:24`). When false, **every** tab check short-circuits to `true`:

```csharp
// FormAccess.cs:64, 131, 199, 231
if (authService.IsCurrentUserInRole(BuiltinRoles.Administrators) || !SiteStructure.IsFormTabsSupported)
    return true;
```

…and `SiteStructure` stops building tabs at all (`:204-219, 253-262`), so the form's own right
governs. A licence downgrade therefore *widens* per-tab access rather than removing the feature.

---

## 5. Layer 2 — record scope ("which employees")

One role-level enum decides the shape (`Enums.cs:3-7`):

```csharp
public enum RoleManagementType { ByDepartments = 1, ByEmployees = 2 }
```

**They are mutually exclusive, enforced on save** (`UpdateRole:872-883`):

```csharp
if (managementType == RoleManagementType.ByDepartments) managedEmployees   = new List<int>();
if (managementType == RoleManagementType.ByEmployees)  { managedDepartments = new List<int>();
                                                          managedLocations   = new List<int>(); }
```

A role cannot say "these departments **and also** these named people". WM's composed model can —
that is an improvement, not a port. (See the note on plan 001 P4 in §11.)

### The authoritative implementation is SQL

Every per-employee decision and every permitted-set query goes through one table-valued function
(`AuthorizationService.cs:245-264, 291-302`;
`E:\Tlw\Database\Versioning\80.V5.26.0.0.sql:880-934`):

```sql
IF @RoleManagementType = 1                      -- by departments
  SELECT e.Id FROM Employees e
    LEFT JOIN DepartmentsManagedByRole dr ON e.DepartmentId = dr.DepartmentId AND dr.RoleId = @roleId
    LEFT JOIN LocationsManagedByRole  lr ON e.EmployeeLocationId = lr.LocationId AND lr.RoleId = @roleId
  WHERE (@hasManagedDepartments = 0 OR dr.RoleId is not null)
    AND (@hasManagedLocations   = 0 OR (e.EmployeeLocationId IS NULL OR lr.RoleId IS NOT NULL));
ELSE IF @RoleManagementType = 2                 -- by employees
  ... WHERE @hasManagedEmployees = 0 OR er.RoleId is not null;
```

Three things this settles:

1. **Departments ∩ locations** — intersection, as plan 001 says.
2. **No hierarchy expansion.** `e.DepartmentId = dr.DepartmentId` is an exact match. Legacy does
   **not** walk the department tree when filtering employees. (`ManagedDepartmentsForTree`
   (`:392-435`) does walk it, but only to render the department *picker*, and only in the
   `ByEmployees` case.) WM's `IncludeChildSites`/`IncludeDescendants` has no legacy equivalent on
   this path.
3. **Unknown `ManagementType` → empty set** (no `INSERT` runs). The SQL fails **closed** where the
   C# `default:` branch fails open (`RoleBasedEmployeeFilterService.cs:56-61`).

A parallel C# implementation exists three times over for three marker interfaces
(`RoleBasedEmployeeFilterService.cs:28-65, 67-101, 103-140`) and a fourth time in the obsolete
`GetManagedEmployeeIds` (`AuthorizationService.cs:154-204`). The C# copies are used by
`WebsiteAccessFilter`; the SQL is used by the authoritative `CanCurrentUserAccessEmployee`.

### `AccessType` — every call site

`AccessType` is `{ Read, Edit }` (`Enums.cs:9-13`). **Not** `View`/`Edit`, as `PHASE-AUDIT.md` and
plan 003 state.

There are exactly **three** call sites in the entire source tree:

| Site | Value |
|---|---|
| `WebSite/API/Authorization/ApiAuthorizer.cs:45` | `AccessType.Read` |
| `WebSite/Controllers/AuthorizingControllerBase.cs:76` (`CanViewEmployee`) | `AccessType.Read` |
| `WebSite/Controllers/AuthorizingControllerBase.cs:82` (`CanModifyEmployee`) | `AccessType.Edit` |

And the parameter is consumed in exactly **one** place:

```csharp
// AuthorizationService.cs:266-289
public bool CanCurrentUserAccessEmployee(int employeeId, AccessType accessType)
{
    var userRole = GetUserRole(GetCurrentUser());
    var currentEmployeeId = GetEmployeeIdOfCurrentUser();

    if (userRole.IsSelfOnly)
        return employeeId == currentEmployeeId;              // accessType ignored

    if (!userRole.CanModifySelf
        && employeeId == currentEmployeeId
        && accessType == AccessType.Edit)
        return false;                                        // <-- the ONLY divergence

    return IsEmployeeManagedByRole(userRole.Id, employeeId); // identical for Read and Edit
}
```

**Settled: read scope and write scope are the same set, minus your own record when
`CanModifySelf = false`.** `AccessType` is not vestigial — it is load-bearing for exactly that
one rule. The batch equivalent is `FilterEmployeesForEdit`, which is
`FilterEmployeesForRead` minus self under the same condition
(`AuthorizingControllerBase.cs:86-98`).

Legacy does **not** check the post-image of a write. A manager can edit an employee they can see
and move them out of their own scope. WM's decision to check the post-image (003 decision 3) is an
inversion, not a port.

---

## 6. Layer 3 — the narrowing overrides

Legacy has three, and WM's union-only model can express none of them.

### `Role.IsSelfOnly` — hard narrowing, beats everything

Applied **before** the managed lists are consulted, and short-circuits them:

| Call site | Behaviour |
|---|---|
| `AuthorizationService.cs:220-224` | `GetPermittedEmployeeIdsForUser` returns `[selfEmployeeId]` or empty |
| `:236-240` | same in `GetFilteredPermittedEmployeeIdsForCurrentUser` |
| `:275-279` | `CanCurrentUserAccessEmployee` returns `employeeId == self`, ignoring `accessType` |
| `:549-552` | `IsReadonlyForUser` returns false — "can update in case of write access to the form" |
| `:572-585` | `UserHasLimitedBySelfPermissions()` — the flag other code branches on |
| `GroupsController.cs:616-619` | the employee picker in the group editor is narrowed to self |

Note `UserHasLimitedBySelfPermissions` swallows every exception and returns **false** —
commented `//no user - unlim access` (`:580-584`). An error resolving the role widens access.

`IsSelfOnly` and `CanModifySelf` are mutually exclusive by UI rule:
`canModifySelf = isSelfOnly ? false : canModifySelf` (`GroupsController.cs:158`).

### `Role.CanModifySelf` — see your own record, may not edit it

`AuthorizationService.cs:281-286` (above), plus `UserCanModifySelf():539-542`,
`EmployeeUserCanModifySelf(employeeId):528-537`, and `IsReadonlyForUser:544-570` which returns
true precisely when you are looking at yourself and the flag is off.

This is the **only genuine read/write asymmetry in the record layer**, and it is why
`AccessType` exists.

### `Role.IsDepartmentOnly` — dead. Settled.

Searched the entire `E:\Tlw\Source` tree and `E:\Tlw\Database`:

- **Writes only** in the TLW web product: the designer property
  (`HorioDB.designer.cs:6287, 6394-6409`), the `UpdateRole` parameter
  (`IAuthorizationService.cs:55`, `AuthorizationService.cs:828`), and the assignment
  `dbRole.IsDepartmentOnly = roleIsDepartmentOnly` (`AuthorizationService.cs:866`).
  Plus the editor's form field (`GroupsController.cs:135, 164, 221, 290, 323, 355, 388`).
- **No read anywhere** in `Logic`, `WebSite`, or in SQL. Every `E:\Tlw\Database` hit is DDL
  (`14.V2.0.10.sql:1056`) or an `INSERT` column list. `dbo.EmployeeIdsManagedByRole` does not
  reference it (`80.V5.26.0.0.sql:880-934`).
- The only *reads* in the repository are in other products: `HealthWebSite/Controllers/SecurityController.cs`
  (a separate app with its own 6-argument `UpdateRole`), `HorioMigration`, and a till-sync SQL string.

**Conclusion: `IsDepartmentOnly` is a persisted, editable, localized checkbox that changes
nothing in TLW.** WM must not model it. (This closes the "unverified" in `PHASE-AUDIT.md` B3.)

### `AccessRightsExclusions` — per-employee subtraction from a screen right

Three tables (§1) expressing: *for role R, employees {E…} are excluded from secured resources
{path…}*, with `IsAllowedForSelf` meaning "the exclusion lapses when you are looking at your own
record".

```csharp
// AuthorizationService.cs:1449-1465
var exclusions = _exclusionCache.Value.GetOrAdd(employeeId, GetAccessRightsExclusionsByEmployee);
if (employeeIdOfCurrentUser == employeeId)
    exclusions = exclusions.Where(e => !e.IsAllowedForSelf).ToList();
return exclusions.Any(e => IsUserInRole(currentUser, e.RoleId)
                        && e.Resources.Any(r => r.SecuredResourceName == securedResourceName));
```

Consumed at `FormAccess.CurrentUserCanViewEmployeeFormTab:85-90` (view only — there is no
`CanEditEmployeeFormTab`), `HRDocumentNotificationService.cs:257`, and
`ExternalAccessControllerHelper.cs:341`. The editor restricts the choosable resources to tabs of
the **Personnel** and **HR Documents** forms (`GroupsController.cs:463-466`).

**What it is actually for**, from that restriction: hiding `Salary`, `BankDetails`,
`Disciplinary`, `Remuneration` etc. for *named individuals* — typically directors — from managers
who otherwise legitimately hold the tab. WM has decided to drop deny rules (§14 decision 7); this
records the requirement that decision is declining, so it can be met another way.

---

## 7. Layer 4 — presentation allow-lists

Neither of these is a security boundary; both are per-role visibility lists edited on the same
`/Groups` screen.

- **Navigation icons.** `VisibleNavigationIconsByRole(roleId)` (`AuthorizationService.cs:511-526`)
  returns the role's icon codes **plus each icon's parent**. Empty list → no icons. Allow-list,
  no fail-open. Editor partial: `Views/Groups/_RoleManagesNavigationIcons.cshtml`.
- **Dashboard categories.** `GetDashboardCategoriesForRole(roleId)` (`:1423-1432`), a
  `DashboardCategory` enum per role, with two entries suppressed when the corresponding site item
  is absent from the licensed tree (`GroupsController.cs:824-844`).

## 8. Layer 5 — HR document type rights (a fourth vocabulary)

`dbo.RoleHrDocumentSecurity(Id, RoleId, HrDocumentType, AccessPermission)` with

```csharp
// SharedLogic/Enums/AccessPermission.cs
public enum AccessPermission { Deny = 1, ReadOnly, ReadWrite }
```

`GetPermissionForTheDocument(roleId, documentType)` returns `Deny` when no row exists **and** on
any exception (`RoleHrDocumentSecurityService.cs:64-87`) — fail-closed, correctly.
`UpdateHrDocumentSecurityForRoles` deletes and reinserts the whole set (`:93-123`).

This is a **third** rights vocabulary in the same product — after branch access/view/edit and
`AccessType.Read/Edit` — on a third kind of object (document *category*, not page, not record).
It is edited on the Documents settings screen, not on `/Groups`.

---

## 9. Composition: the actual algorithm, and a worked example

### The algorithm

```
resolve(user):
  role := UsersInRoles WHERE UserId = user            -- exactly one; IsActive NOT checked
  if role is null: user is not a manager, most checks throw or fail closed

may_open(page, method):                               -- Layer 1
  if role is Administrator: true
  right := (method == POST) ? FormEditing : FormViewing
  if no ACE(role, right, page.FullName) with Allow=true: false
  if any ACE(role, right, page.FullName) with Allow=false: false      -- deny wins
  for each ancestor branch b: require ACE(role, BranchAccess, b.FullName).Allow

may_see_tab(tab, employee):                           -- Layers 1 + 3
  if not IsFormTabsSupported: true                     -- licence flag opens the layer
  if tab.DisallowDenyAccess: true
  may_open(tab.parentForm, GET) AND ace_allows(role, FormViewing|FormEditing, tab.FullName)
  AND NOT exclusion(role, employee, tab.FullName)      -- unless IsAllowedForSelf and employee == self

may_see(employee):                                    -- Layer 2 + IsSelfOnly
  if role.IsSelfOnly: employee == self
  employee IN dbo.EmployeeIdsManagedByRole(role.Id)

may_edit(employee):                                   -- Layer 2 + CanModifySelf
  if role.IsSelfOnly: employee == self
  if not role.CanModifySelf and employee == self: false
  employee IN dbo.EmployeeIdsManagedByRole(role.Id)    -- identical to may_see otherwise
```

Note the two axes never intersect. `may_open` never consults an employee; `may_see` never
consults a page. The **only** place they meet is `may_see_tab`, via the exclusion list.

### Worked example

**Setup.** Role 7 "North Warehouse Supervisor": `ManagementType = ByDepartments`,
`IsSelfOnly = false`, `CanModifySelf = false`, `IsDepartmentOnly = true` (ignored).
`DepartmentsManagedByRole` = {Warehouse 3, Packing 4}; `LocationsManagedByRole` = {Site North 1}.
ACEs: `Personnel` → BranchAccess allow; `Personnel/Personnel` → FormViewing allow, FormEditing
allow; `Personnel/Personnel/Salary` → FormViewing allow, FormEditing **deny**.
One `AccessRightsExclusion(RoleId=7, IsAllowedForSelf=false)` over employees {42} and resources
{`Personnel/Personnel/Salary`}. User `jsmith` is in role 7 and linked to employee **42**
(Warehouse, Site North).

| # | Question | Answer | Why |
|---|---|---|---|
| 1 | See employee 99 (Packing, Site North)? | **yes** | dept 4 ∈ {3,4} ∧ loc 1 ∈ {1} |
| 2 | See employee 100 (Packing, **location NULL**)? | **yes** | `EmployeeLocationId IS NULL OR …` — fail-open #2 |
| 3 | See employee 101 (Finance, Site North)? | **no** | dept not in list |
| 4 | Edit employee 99? | **yes** | same TVF; `accessType` irrelevant here |
| 5 | See employee 42 (self)? | **yes** | in the managed set |
| 6 | **Edit** employee 42 (self)? | **no** | `!CanModifySelf ∧ self ∧ Edit` (`:281-286`) |
| 7 | Open `/Personnel` (GET)? | **yes** | FormViewing allow + BranchAccess on `Personnel` |
| 8 | POST to `/Personnel`? | **yes** | FormEditing allow |
| 9 | See the **Salary** tab for employee 99? | **yes, read-only** | FormViewing allow; FormEditing deny |
| 10 | See the **Salary** tab for employee 42 (self)? | **no** | exclusion covers 42, `IsAllowedForSelf = false` |
| 11 | If `LocationsManagedByRole` is emptied? | **sees every Warehouse/Packing employee at every site** | `@hasManagedLocations = 0` — fail-open #1 |
| 12 | If **both** lists are emptied? | **sees the entire estate** | both fail-opens fire |
| 13 | If the licence drops `IsFormTabsSupported`? | **Salary tab becomes editable** | `FormAccess.cs:64, 131` short-circuit to true |
| 14 | If `UsersInRoles.IsActive` is set false for jsmith? | **nothing changes** except `IsCurrentUserAdministrator()` | §3 defect |

Row 12 is the configuration that motivates WM's fail-closed inversion; rows 6, 10 and 13 are the
behaviours WM currently cannot express at all.

---

## 10. The fail-open inventory

Plan 001 records three (later four). Measured, in the in-scope surface alone:

| # | Where | Shape |
|---|---|---|
| 1 | `RoleBasedEmployeeFilterService.cs:36, 75, 111` + TVF `@hasManagedDepartments = 0` | empty department list → no filter |
| 2 | `:44, 83, 121` + TVF `e.EmployeeLocationId IS NULL OR` | null location → visible to everyone |
| 3 | `:56-61, 95-98, 134-137` | unknown `ManagementType` → unfiltered (SQL disagrees: empty) |
| 4 | `:50, 89, 128` + TVF `@hasManagedEmployees = 0` | empty employee list → no filter |
| 5 | `AuthorizingControllerBase.FilterDepartments:101-105` | empty list → every department |
| 6 | `FilterLocations:107-114` | empty list → every location |
| 7 | `FilterBuildings:216-221` | empty list → every building |
| 8 | `FilterClockingActivities:280-300` | empty list → every activity; `ActivityId == null` widens |
| 9 | `FilterClockingScheduledActivities:302-322` | same |
| 10 | `CanViewDailyPeriodicTemplate:444-454` | comment: `//no departments to manage - allow all` |
| 11 | `UserHasLimitedBySelfPermissions:572-585` | any exception → `false` (`//no user - unlim access`) |
| 12 | `FormAccess.cs:64, 131, 199, 231` | licence flag off → every tab allowed |

A further ten of the same shape sit in dropped verticals (`FilterDevices`, `FilterLogs`,
`FilterDoors`, `FilterReaders`, `FilterTimeZones`, `FilterSecurityGroups`, `FilterAnprRecords`,
`FilterTills`, `FilterCaterers`, `CanViewSecurityGroups`, `CanViewTimezone`) — recorded so nobody
re-derives them later, not because WM will build them.

**The pattern is uniform: "no configuration" means "no restriction".** WM inverts it. That
inversion is correct and is the single most important behavioural difference between the two
products.

---

## 11. Corrections to WM's records

Made directly, this survey. Each was a claim carried forward from a document rather than measured.

| # | Claim | Where it was | Measured truth |
|---|---|---|---|
| C1 | "A user may belong to several groups; access combines as a union — **mirroring TLW**" | `ARCHITECTURE.md:157`, `SCREEN-TREE.md:314-315` | **TLW has one role per user** (`GetUserRole:1277` `SingleOrDefault`; `AddUserToRole:1010-1034` replaces). The union is a WM invention attributed to TLW. |
| C2 | "legacy needed `DataAccessScopeDiagnostics` to explain its own answers/decisions" | `SCREEN-TREE.md:310, 322`, `001:35`, `003:62`, `ARCHITECTURE.md:553-555`, and in code at **four** sites (see below) | **False.** `DataAccessScopeDiagnostics` counts LINQ-to-SQL `DataContext` create/dispose to find connection leaks (`DataAccessScopeDiagnostics.cs:10-55`; `DataAccessScopeDiagnosticsController.cs:70-87` exposes `TotalScopesCreated`, `ActiveScopes`, `TotalDisposalErrors`, `AutoDisposalRate`). It has nothing to do with authorization. **Legacy ships no tool that explains an access decision.** |
| C3 | Screen rights combine as a union / most-permissive-wins | implied by `ARCHITECTURE.md:157` | **Deny overrides allow; no allow means denied** (`UserHasPermission:695-718`). |
| C4 | `WebPages` is a screen-rights table | `ARCHITECTURE.md:426` | It is localization: `WebPage(PageId, Name, Description)` + `LocalizationKeys` (`HorioDB.designer.cs:27642-27654`). The rights table is `dbo.AccessControlEntry`. |
| C5 | `FormAccess` / `SiteItemPermission` are tables | `ARCHITECTURE.md:196, 426` | C# helper class and DTO. Persisted as `AccessControlEntry`. |
| C6 | `AccessType` is `View`/`Edit` | `PHASE-AUDIT.md:97`, `003:45, 71` | It is `Read`/`Edit` (`Enums.cs:9-13`). |
| C7 | `Role.IsDepartmentOnly` — "no read path found; unverified whether it is live" | `PHASE-AUDIT.md:163` | **Settled: dead.** Written and localized, never read, in `Logic`, `WebSite` or SQL. |
| C8 | Legacy's `ByEmployees` list intersects the other dimensions | implied by `001` P4 "intersecting correctly with the other dimensions" | **They are mutually exclusive** — `UpdateRole:872-883` wipes the other lists on save. WM's intersection is an improvement with no legacy precedent. |
| C9 | Legacy expands the department/site tree when filtering employees | implied by `IncludeChildSites` "preserving current behaviour" | The TVF matches `e.DepartmentId = dr.DepartmentId` exactly (`80.V5.26.0.0.sql:911`). No expansion on the employee path. |

C2 matters beyond bookkeeping: it was the stated evidence for two of WM's design decisions
(orthogonal roles/groups, no deny rules). One of those decisions — orthogonality — the user
reversed on 2026-08-05. The other may still be right, but it now has to stand on its own
reasoning, because the evidence cited for it does not exist.

> **C2 undercount, corrected 2026-08-05 while planning 005.** This survey said the claim survived
> in **two** code comments. Grepping `src/**` for "diagnostic" finds **four**:
>
> | Site | What it says |
> |---|---|
> | `SharedKernel/Security/ScopeModel.cs:220-223` | "the failure legacy's `DataAccessScopeDiagnostics` existed to chase" |
> | `Identity/Endpoints/SecurityGroupEndpoints.cs:61-62` | "legacy shipped `DataAccessScopeDiagnostics` because 'why can't this user see this employee?' is otherwise unanswerable" |
> | `Identity/Domain/SecurityGroup.cs:10-13` | "Legacy conflated the two across Role, SecurityGroup, **FormAccess and SiteItemPermission**, which is why it needed a diagnostics subsystem to explain itself" — **three errors in one sentence**: C2, C5 (neither is a table), and the roles-vs-groups orthogonality the 2026-08-05 ruling reverses |
> | `People/Services/EmployeeScopeExtensions.cs:9-11` | "which is how legacy ended up needing a diagnostics subsystem to find the gaps" |
>
> A fifth, milder instance is `SharedKernel/Security/DataScope.cs:5-6`, which calls
> `SiteItemPermission` an "authorizer class" (C5); that file is deleted by 005 P4 regardless.
> Each site is folded into the *Touches* of the 005 portion that opens that file.
>
> The lesson is the one this document already carries: a wrong claim propagates as far as somebody
> finds it useful, and "I corrected the two I happened to see" is not a sweep.

---

## 12. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| One editable object carrying screen rights **and** scope (`/Groups` → `Role`) | **Keep** | Measured correct, and it is what makes the model explainable: one screen, one answer. `GroupsModel { Role, Roles, SiteItems }` (`GroupsModel.cs:6-12`). |
| One role per user | **Keep — but it is a decision, not a default** | It is *why* the one-object shape works. See §13. |
| Tri-state right per node (none / view / edit), edit ⇒ view | **Keep** | Clean, and it maps to HTTP method cleanly (`OnAuthorization:565-575`). |
| Ancestor branch access required | **Keep** | Prevents "granted a leaf, orphaned from the tree" (`FormAccess.cs:37-43`). |
| GET needs read, POST needs edit | **Keep** | The right shape for API-first WM: read policy on `GET`, write policy on `POST/PUT/DELETE`. |
| New group starts fully denied | **Keep** | `GetRestrictedSiteItemAccess:695-723`. Already WM's instinct; now it has a precedent. |
| Deny-overrides-allow resolution | **Improve** | Correct under multi-membership, unnecessary under exclusivity. Keep the *rule* only if WM keeps multi-membership; otherwise it is unreachable complexity. |
| `SecuredObjectId` = page path string | **Improve** | Rename a menu node → silent orphaning of every right. WM should use a stable screen id, with the path as display only. |
| ~1,000 ACE rows written per role per save | **Improve** | No inherit/unset. WM should store only deviations from a role template. |
| `AccessType.Read/Edit` on the record axis | **Improve** | Real, but carries exactly one rule (`CanModifySelf`). WM needs the axis *and* the post-image check legacy lacks. |
| `Role.CanModifySelf` | **Keep** | A genuine requirement — "see your own record, HR edits it". Cheap to model. |
| `Role.IsSelfOnly` | **Keep, as a mode not a flag** | The self-service configuration. See §13 — it is why plan 001 P3's union is wrong. |
| Empty managed list ⇒ no filter (×12) | **Invert** | Already WM's decision; §10 gives the full count. |
| Null discriminator widens (`EmployeeLocationId IS NULL OR`) | **Invert** | Already WM's decision. |
| `default:` → unfiltered query | **Invert** | Already WM's decision; legacy's own SQL agrees with WM. |
| Exception → `return false` from `UserHasLimitedBySelfPermissions` | **Invert** | An error must never widen. |
| Licence flag opening the tab layer | **Invert** | An unlicensed feature must be **absent**, never **permitted** — §5 already says this for modules. |
| `UsersInRoles.IsActive` ignored on the hot path | **Invert** | An inactive assignment must deny. |
| `Role.IsDepartmentOnly` | **Drop** | Dead code (§6). |
| `ByDepartments` / `ByEmployees` mutual exclusion | **Drop** | Arbitrary. WM's composed constraints subsume it. |
| `AccessRightsExclusions` (3 tables) | **Drop, with a replacement owed** | Decision stands (§14 decision 7), but the requirement — hide named individuals' sensitive tabs from managers who hold the tab — is real and currently unmet. |
| Pseudo-roles `Everyone`/`Anonymous`/`Authenticated` | **Drop** | Declared "currently unsupported" (`BuiltinRoles.cs:9`), unauthorable, but still honoured by the resolver — a latent hole. |
| `dbo.WebPages` | **Drop (not authorization)** | Localization. |
| `RoleHrDocumentSecurity` (`Deny/ReadOnly/ReadWrite`) | **Improve** | A third vocabulary for the same idea. WM should have **one** right vocabulary applied to more object kinds, not three vocabularies. |
| Navigation icons / dashboard categories per role | **Improve** | Keep as *presentation* preferences on the group; never as a security boundary. |
| EPOS caterers dimension | **Drop** | Dropped vertical. |
| Buildings dimension over employees | **Drop** | No `Employees.BuildingId` (already recorded, `PHASE-AUDIT.md` B4). |

---

## 13. Proposed target model for WM

> **Everything in this section is a recommendation, not a measurement.** The user's standing
> direction is *"probably best to adapt strategy from old TLW."* This section says what that
> implies concretely, and where I think it should **not** be adapted, with the measured reason.
>
> ### ✅ **Ruled on 2026-08-05: option (A).** The recommendation below was accepted.
> `ARCHITECTURE.md` §4 (`:157-160`) is rewritten and normative; `CanEditOwnRecord` is adopted. The
> refactor — every cost item listed under (A) below, sized against WM's own tree — is planned as
> [`plans/005-one-membership.md`](./plans/005-one-membership.md), 6 portions, `draft`.
> **Option (B) below is retained as the record of the path not taken.**

### The one thing that has to be decided first

Legacy gets away with putting rights and scope in one object **because a user has exactly one of
them**. There is then never a question to answer: no union, no precedence, no diagnostics screen.

WM today has the opposite on both axes — `User.Roles` is a list (`User.cs:16`) and
`SecurityGroup.Members` is many-to-many (`SecurityGroup.cs:50`).

**Conflating the two objects while keeping multi-membership is the worst of the three options.**
It forces WM to answer questions legacy never poses — "the user is in *Ops North* (view Personnel,
northern employees) and *Payroll Officer* (edit Payroll, all employees); may they edit a northern
employee's Personnel record?" — and every answer to that question is a rule somebody has to
remember. That is the position WM is drifting into by default, and it is a fair description of
what the user meant by *"we have only a vague idea about groups"*.

So the real choice is between:

**(A) Adopt TLW's strategy fully — one object, one per user.**
- One entity, `SecurityGroup`, carrying: name, screen rights, data-scope constraints, mode
  (`Normal` / `SelfOnly`), `CanEditOwnRecord`.
- `User.SecurityGroupId` — exactly one, non-null (the "Employee" group is the floor).
- WM's `Role`/`RolePermission` merge into it: a permission becomes a screen right at the
  granularity WM already uses.
- **Every question in §9 becomes a lookup with no resolution step.** `IsSelfOnly` is a mode, not
  a flag that must beat a union. The diagnostics endpoint has one group to explain.
- Cost: real. `User.Roles` → one; JWT claim shape; `IdentitySeeder`/`DemoUserSeeder`;
  `DataScopeResolver` collapses to a lookup; the users screen; plan 001 P3's union premise is
  deleted rather than amended; 003 P1's `AttendanceAudience` simplifies.

**(B) Keep multi-membership, and adopt TLW's *resolution* rules explicitly.**
- Grants union across groups (WM's current instinct).
- **Screen rights: deny-overrides-allow** — legacy's actual rule (`UserHasPermission:708-711`),
  which WM's docs currently invert.
- **Narrowing modes win over the union**: any group with `SelfOnly` collapses the user to self,
  regardless of what other groups grant. This is the only way to express `IsSelfOnly` under a
  union, and it must be written down or it will be re-litigated.
- Cost: lower now, higher forever. Three precedence rules a human must hold to answer §9's
  questions, plus a real need for the "explain this decision" endpoint WM already has.

**My recommendation: (A).** The user's complaint is that the model is not legible, and (A) is the
option that makes it legible. It is also the honest reading of *"adapt strategy from old TLW"* —
the one-object shape and the one-group-per-user rule are the same design decision, and taking one
without the other is where the incoherence comes from. If exclusivity later proves too rigid, a
group can be *composed* from other groups at edit time (copy-on-write, as `DuplicateGroup`
already does at `GroupsController.cs:318-435`) without reintroducing runtime resolution.

**What I recommend *not* adapting**, with the measured reason:

| Do not adapt | Because |
|---|---|
| Path-string ACL keys | `SecuredObjectId = SiteItem.FullName` (`SiteItem.cs:41-52`); renaming a node orphans rights silently |
| ~1,000 materialised ACE rows per role | No inherit, no unset (`SetBulkPermissions:614-693`) |
| Fail-open dimensions (×12, §10) | Already inverted; do not soften it to "match legacy" |
| Licence flag that *grants* (`IsFormTabsSupported`) | An unlicensed feature must be absent |
| Three rights vocabularies | branch/view/edit, `Read/Edit`, `Deny/ReadOnly/ReadWrite` — one is enough |
| `AccessRightsExclusions` deny rows | Decision already taken; but owe a replacement (§6) |
| `IsDepartmentOnly` | Dead (§6) |
| Read scope == write scope | Legacy never checks the post-image; WM's 003 decision 3 is better |

### Proposed §4 replacement text

> **Propose only — §4 is a design section this survey may not edit.** This is the concrete diff
> for the user to accept, amend or reject. It is written for option (A); the (B) variant is in
> *Open questions*.

Replace `ARCHITECTURE.md:157` (the `**Authorization**` bullet) with:

```markdown
- **Authorization**: a **security group** is the single unit of access. It carries **per-screen
  rights** (none / read / edit, where edit implies read and a parent branch must be readable for
  its children to be) **and** the **employee scope** (composed dimension constraints, intersected
  within the group) in one object — mirroring TLW, where `/Groups` edits one `dbo.Role` holding
  both (`WebSite/Controllers/Security/GroupsController.cs:76-108`, `Models/Security/GroupsModel.cs:6-12`).
  **A user belongs to exactly one group**, as in TLW (`AuthorizationService.cs:1265-1280`,
  `:1008-1041`) — there is therefore no union, no precedence rule, and no case in which two
  grants must be reconciled. A group may be created by duplicating another.
  Three inversions of TLW, all deliberate: **an unset constraint denies** rather than permitting
  (TLW fails open in twelve measured places, `TLW-AUTHORIZATION-MODEL.md` §10); **an unlicensed
  feature is absent, never permitted** (TLW's `IsFormTabsSupported` grants when the licence lacks
  it, `FormAccess.cs:64`); and **a write is checked on its post-image**, which TLW never does.
  A group may be `SelfOnly` (the self-service mode, TLW `Role.IsSelfOnly`) and may withhold edit
  on the holder's own record (TLW `Role.CanModifySelf`). Scope is applied as query filters, never
  per endpoint. See [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md) for the measured
  legacy model and [`SCREEN-TREE.md`](./SCREEN-TREE.md) for the tree.
  **Multi-tenancy-ready** (tenant id + filter).
  > Naming note: TLW's `SecurityGroup` is a *different, physical* concept — which employees may
  > open which door readers. Out of scope for WM; the name is deliberately not reused. Likewise
  > TLW's `SiteStructure` is the application's page tree, not the site/department hierarchy.
  > TLW's `dbo.WebPages` is a **localization** table, not an access-control one.
```

### Shape sketch (for whoever builds it)

```
SecurityGroup                       one per user
  Name, Description, IsSystem
  Mode            : Normal | SelfOnly              -- TLW Role.IsSelfOnly
  CanEditOwnRecord: bool                           -- TLW Role.CanModifySelf
  ScreenRights    : [ (ScreenId, Right) ]          -- Right = None | Read | Edit; deviations only
  Constraints     : [ ScopeConstraint ]            -- existing composed model, intersected
User.SecurityGroupId : Guid (required)

ScreenId is a stable identifier, not a path.
Absent ScreenRight  => None (deny). Absent Constraint dimension => unconstrained.
Constraint present with zero values => matches nothing.
```

Endpoints: `GET/PUT /api/security-groups/{id}` gains a `screenRights` collection;
`GET /api/screens` enumerates the licensed screen tree for the editor;
`GET /api/me/screen-rights` feeds the SPA's route guards (server re-checks regardless).
Enforcement: a `RequireScreen("<id>", Read|Edit)` policy alongside the existing permission
policies, plus the fallback policy from 003 P2.

---

## 14. Open questions for the user

> **Questions 1–3 and 5 were answered on 2026-08-05.** Kept with their answers rather than deleted,
> so the reasoning that produced the ruling stays attached to it.

1. ~~**(A) or (B)?**~~ **Answered: (A)** — one object, one membership. `ARCHITECTURE.md` §4
   (`:157-160`) rewritten to match. The refactor is planned as
   [`plans/005-one-membership.md`](./plans/005-one-membership.md); its measured cost is 22 source
   files, 5 frontend files, 7 test files and 3 migrations, in 6 portions.
2. ~~**If (B):** confirm the two precedence rules.~~ **Moot.** (B) was not taken, so neither
   precedence rule is needed. That is the point of (A).
3. ~~**`CanEditOwnRecord`.**~~ **Answered: adopted.** Built in 005 P1 as a group flag defaulting to
   `true` (today's WM behaviour, so the migration cannot narrow anyone); wired into the write check
   by 005 P4, through the seam 003 P2b leaves.
4. **The exclusion-list requirement.** *Still open.* Deny rules stay dropped, but "hide the directors' Salary
   tab from the regional manager who legitimately holds it" is a real customer need
   (`GroupsController.cs:463-466` restricts exclusions to exactly the Personnel and HR Documents
   tabs). Options: a sensitivity flag on the employee that requires a distinct right; a separate
   group for sensitive personnel with the tab withheld; or accept the gap. Not urgent — no screen
   rights exist yet — but it should not be discovered during Phase 1b's second half.
5. ~~**`ScopeModel.cs:223` and `SecurityGroupEndpoints.cs:61` carry correction C2 in code
   comments.**~~ **Re-measured 2026-08-05: there are four, not two** — see the boxed table in §11.
   Each is assigned to the 005 portion that opens that file (P1 for `SecurityGroup.cs` and
   `SecurityGroupEndpoints.cs`, P4 for `ScopeModel.cs` and `EmployeeScopeExtensions.cs`);
   `DataScope.cs` is deleted by P4 outright.

---

## 15. One-line notes for future surveys (outside this scope)

- `dbo.[User]` has **30** columns — 2FA, lockout, password history, email preferences — against
  WM's 10. A user-lifecycle survey is owed before Phase 3 (SSO/2FA).
- `dbo.GuardScreenWithPersonGroupsRecords` (37 cols) and `dbo.UnifiedAccessControlTransactionsReportView`
  (59) are the largest "access" tables in the schema and are entirely physical access control —
  confirmed out of scope by invariant 3, recorded so nobody re-measures them.
