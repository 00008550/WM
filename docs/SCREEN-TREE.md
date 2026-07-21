# WM — Screen Tree & Permission Model

*Proposed 2026-07-21. Agree this before building screens.*

This is the navigation tree and the unit of permission. It is derived from TLW's
actual structure (evidence below), not invented — but reorganised, because the goal
is a better product, not a reproduction.

---

## 0. Corrections to earlier documentation

Reading TLW's access code properly turned up three things the earlier docs got wrong.
They are corrected here and in `ARCHITECTURE.md`.

| Earlier claim | Reality in TLW |
|---|---|
| "Security groups control which employees a user can see" | **Wrong.** `SecurityGroup` is *physical access control* — which employees may open which door readers, with dated assignments, synced to Suprema hardware (`Logic/AccessControl/SecurityGroupService.cs`, `SupremaControllerService/SyncSecurityGroups.cs`). With devices dropped, the concept is **out of scope for WM**. |
| "`SiteStructure` = the site/department hierarchy" | **Wrong.** It is the **application's page tree** — `SiteBranch` / `SiteForm` / `SiteFormTab`. "Site" means the *website*. The physical hierarchy is Sites/Departments in People. |
| "Roles bundle permissions; groups are separate" | **Wrong.** In TLW a **Group *is* a Role** (`/Groups` → `Controllers/Security/GroupsController.cs`, `SoftwareAccessGroupDto`). One object carries per-form Read/Edit **and** managed departments/locations/employees. |

Two further details worth recording:

- TLW permissions are **tri-state per form**: none / `Read` / `Edit` (`AccessType`), and go down to **tab** level within a form (`PersonnelTab`, with a `[DisallowDenyAccess]` marker for tabs that can never be hidden).
- TLW **fails open**: `if (managedDepartments.Any())` means an *empty* managed list applies no filter, so a half-configured group silently exposes the whole workforce. **WM does not copy this** — scope type is explicit (decision 2026-07-21).

---

## 1. Evidence: TLW's real functional areas

Controllers per area (`WebSite/Controllers`, ~500 total):

| Area | Controllers | WM disposition |
|---|---|---|
| **Reports** | **136** | ⏹ replaced by the **AI assistant** + ~8 core reports |
| API | 72 | — (transport, not a screen area) |
| root-level | 52 | spread across the areas below |
| EPOS (accounts, products, reports, sales, settings, tills) | 48 | ⏹ dropped |
| Student registration (+ admin, reports, settings) | 27 | ⏹ dropped |
| Maintenance | 24 | ◐ device-related parts dropped; system health kept |
| Payroll | 19 | ◐ 19 vendor screens → **one export builder** |
| Access control | 17 | ⏹ dropped (device-dependent) |
| Expenses | 9 | ✅ keep |
| Notifications | 8 | ✅ keep |
| LAPI / vehicle | 7 | ⏹ dropped |
| Emergency | 7 | ✅ keep — **software-only, a differentiator** |
| Work activities | 7 | ✅ keep (job costing) |
| Scheduling | 6 | ✅ keep |
| Security | 6 | ✅ keep (users, groups) |
| Personnel setup | 11 | ✅ keep |
| Scores (exceptions) | 4 | ✅ keep — folds into Attendance |
| Company documents | 4 | ✅ keep |
| MIS / payment integrations | 8 | ◐ connectors, config only |
| Accruals | 3 | ✅ keep |
| Planning | 3 | ✅ keep |
| Visitors | 2 | ▢ later |

**The single biggest fact: reporting is 27% of the application.** Replacing it with an
assistant plus a handful of fixed reports is the highest-leverage decision in the plan.

---

## 2. Proposed WM tree

Grouped by what a person is *doing*, not by which service owns it. Each **leaf is a
screen** and a screen is the unit of permission (none / read / edit).

```
Operations
  ├─ Dashboard                 live presence, punch feed, quick punch
  ├─ Attendance                clockings, corrections, exceptions, manual timesheets
  ├─ Timesheets                calculated results per employee/period
  └─ Emergency                 trigger, live roll call, muster points, incident history

People
  ├─ Employees                 the record: identity, contract, custom fields
  ├─ Org structure             sites, departments, positions, population groups
  ├─ HR                        appraisals, disciplinaries, objectives, qualifications
  └─ Documents                 company + employee documents, e-signature

Time rules            ← the engine; deepest part of the product
  ├─ Daily templates           shifts, breaks, core hours, rounding, exceptions
  ├─ Weekly & periodic         weekly models, periodic templates, pay periods
  ├─ Counters & balances       counters, flexi balances, accrual rules
  └─ Pay categories            categories, tariffs, rounding rules, cost centres

Scheduling
  ├─ Planning board            rotas, drag & drop, coverage
  ├─ Roster                    roster calendar, schedule requests
  └─ Auto-planning             rules-driven generation

Absence
  ├─ Requests                  submit, approve, reject, cancel
  ├─ Entitlements              allocations, accrual balances
  └─ Calendars                 holidays, school holidays, team calendar

Activities                     ← field service / job costing
  ├─ Work activities           activity hierarchy, machines
  ├─ Scheduled activities      assignment, support member, travel
  ├─ Clients                   sites visited
  └─ Job sheets                completion, attachments

Expenses
  ├─ Claims                    expenses + mileage, approval workflow
  └─ Expense setup             types, rates, vehicle types, payment categories

Insight
  ├─ Assistant                 ask questions, author reports        ⭐ replaces 136 controllers
  ├─ Reports                   saved + core reports, scheduled delivery
  └─ Exports                   payroll export builder, run history

Administration
  ├─ Users                     accounts, employee link, group membership
  ├─ Groups                    screen permissions + employee scope
  ├─ Settings                  options, localisation, notification setup
  ├─ Integrations              connector configuration
  ├─ Licensing                 licence status, features, limits
  ├─ Plugins                   installed plugins
  └─ Audit                     user action log, access diagnostics

My                             ← self-service; visible to any employee-linked user
  ├─ My time                   own status, timesheet, punches
  ├─ My absence                own requests and balances
  ├─ My documents              view and e-sign
  └─ My expenses               own claims
```

**~40 screens** versus TLW's ~230 — because reports collapse into the assistant,
per-vendor payroll screens collapse into one builder, and EPOS/schools/devices are gone.

---

## 3. Permission model

Each screen carries **none / read / edit** per group. A user may belong to **several
groups**; access combines as a union (most permissive wins), like IAM group membership.

```
Group
 ├─ Screen permissions      screen → none | read | edit
 └─ Employee scope          None | AllEmployees | ByDepartments | ByEmployees
```

Rules:

- **`read` opens the screen; `edit` allows mutation.** Endpoints enforce this — a read
  user hitting a write endpoint gets 403 regardless of what the UI shows.
- **Screen access and employee scope are independent.** `edit` on Employees plus a
  department scope means "may edit, but only these people".
- **The `My` branch is not group-controlled.** Any employee-linked user gets it, so
  self-service never depends on an admin remembering.
- **No deny rules.** Narrowing is done by removing group membership. Deny lists that
  interact across several groups are exactly what made TLW's model hard to reason about
  (it needed a `DataAccessScopeDiagnostics` subsystem to explain itself).

### Deferred: tab-level permissions

TLW goes finer than screens — `PersonnelTab` gives per-tab rights inside Personnel.
**Not in the first pass.** The `Employees` screen will have tabs (contract, HR, documents)
and those are candidates later, but screen-level first: it covers the real cases, and
tab-level can be added without changing the group shape.

---

## 4. Open questions before building

1. **Grouping** — does the eight-branch top level match how you think about the product?
   The one I am least sure of is **Time rules** as its own branch rather than sitting
   under a Setup/Configuration branch with Settings and Integrations.
2. **Timesheets vs Attendance** — separate screens, or one screen with two views?
   TLW separates them; they overlap heavily.
3. **Insight** as a name for the assistant + reports branch, or something plainer
   ("Reports", with the assistant inside it)?
4. **Naming**: TLW says *Groups*; AWS-style would be *Groups* too, so this seems safe.
   Confirm we are not reusing "roles" anywhere in the UI.
