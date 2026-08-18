# WM — Screen Tree, Permissions & Licensable Modules

*v2, 2026-07-21. Agree this before building screens.*

Three jobs in one structure:

1. **Navigation** — how people find things.
2. **Permission unit** — a group grants none / read / edit per screen.
3. **Licensing unit** — a customer is shipped **selected branches and sections**, so
   the product can be tailored to what they actually need. This is a hard
   requirement, and it is why the tree follows business capability, not convenience.

---

## 0. Source of truth

The branch names below are **TLW's own**, taken from documented menu paths in
`E:\Tlw\Documentation` (e.g. *"Work Rules → Daily Templates"*, *"System Setup →
Options"*, *"Time and Attendance → Presence Panel"*), cross-referenced against the
~230 folders in `WebSite/Views` and the controller areas.

An earlier draft of this document invented its own grouping and undercounted the
surface. That is corrected here.

### Corrections carried forward

| Earlier claim | Reality in TLW |
|---|---|
| "Security groups control which employees a user can see" | `SecurityGroup` is **physical door access** — employees to readers, dated, synced to Suprema hardware. Out of scope; name not reused. |
| "`SiteStructure` = site/department hierarchy" | It is the **application page tree** (`SiteBranch`/`SiteForm`/`SiteFormTab`). |
| "Roles and groups are separate" | **A Group *is* a Role** — one object carries per-form read/edit **and** managed departments/locations/employees. |
| "Time rules" as a branch | Not a TLW concept. The real branch is **Work Rules**, and it already contains absences and accruals alongside templates. |
| "Day types" and "Calendar day models" are Work Rules screens | They are **Access Control** — `Controllers/AccessControl/DayTypeController.cs` → `dbo.ac_day_type` (5 cols), `AccessControlCalendarController.cs` → `dbo.ac_calendar` (5). Out of scope with the rest of `ac_*`. Corrected 2026-08-18; see §5 note. |

---

## 1. The tree

`◆` = licensable module, sold whole or by section. `[foundation]` = not sellable,
because the application cannot run without it.

**There is no "core product".** Every capability is sellable, including Time &
Attendance. What is not sellable is the platform: you cannot have a workforce system
without people to record, accounts to sign in with, and rights to control them.

```
◆ Time & Attendance                                            [module]
   Daily browser · Presence panel · Clockings & corrections
   Manual timesheets · Exceptions (scores) · Transaction report
   Geolocation tracking · QR punching · Virtual terminal
   Period locking

  Personnel                                                      [foundation]
   Employees · Employee contracts · Contract assignment
   Positions & qualifications · Perks · Custom fields · Emergency contacts
   Employee groups · Population groups
   ── Org structure
      Sites (= TLW Locations) · Departments · Cost centres · Buildings
      (TLW's Locations screen sits under Personnel Setup, not access control;
       Buildings is reused by muster points, so it outlives the AC drop)
   Bank details · Salary · Personal contact & address · Emergency contacts
   Personnel setup · Leavers · Anniversaries

◆ Import / Export                                                [module]
   Bulk import: personnel (simple & advanced) · clockings ·
   clockings with absences · absences · activities ·
   employee custom field values · clients & client sites/contacts
   Export settings · Import history & error reporting

  Work Rules — base                            [required by Time & Attendance]
      Daily templates                     (dbo.DailyModels, 124 cols)
      Master daily model assignment       (dbo.EmployeeMasterDailyModels, 5)
      Shift matching · Split shifts · Multi-shift
                                          (DailyModelShiftMatchingRules 8 /
                                           DailyModelSplitShifts 7 /
                                           DailyModelMultiShifts 4)
      Break rules · Rounding rules · Global schedule thresholds
                                          (DailyModelBreaks 34 /
                                           RoundingRules 21 /
                                           GlobalScheduleThresholds 7)
      Pay categories · Pay periods        (dbo.Counters 7 / dbo.Periods 6)
      Corrections & time adjustments      (dbo.Corrections, 13)
      Exception setup · Blocked exception rules · Muted exceptions
                                          (ScoresAbnormalitiesSetup 7;
                                           blocked-exception rule is 3 global
                                           modes, not a screen; muting is
                                           Clockings.ShouldHideExceptions)

      Measured 2026-08-18: 22 tables, 341 columns. Full anatomy in
      `TLW-WORK-RULES.md`.

      Required, but less so than this document claimed. Corrected below.

◆ Work Rules — Periodic & weekly                                 [module]
      Periodic templates · Weekly models
      Weekly/periodic band & hour counters

◆ Work Rules — Balances                                          [module]
      Weekly counters · Flexi balance · Flexi balance tracking
      Balance reset · Contract hours limits · Debit/credit rules
      (~~Counters~~ moved to the base 2026-08-18: `CountersController`
       over `dbo.Counters` **is** the Pay categories screen, listed twice)

◆ Work Rules — Absence & leave                                   [module]
      Absences · Predefined absences · Absence allocation
      Absence authorisation · Absence managers setup
      Accruals · Accrual adjustments · Length-of-service bonus
      Holidays · School holidays · Recaps

◆ Work Rules — Costing                                           [module]
      Tariffs · Hourly rates · Cost centre allocation

◆ Scheduling                                                     [module]
   Planning board · Planning control · Auto-planning
   Roster · Roster calendar · Roster notification templates
   Employee schedule requests · Timetables · Timetable creator

◆ Absence Requests                                               [module]
   Requests · Approvals · Team calendar · Blocked dates
   (the employee-facing workflow; the *rules* live in Work Rules)

◆ Documents                                                      [module]
   Company documents · Document categories · Employee documents
   Onboarding documents · Document tags · Expiry tracking
   E-signature

◆ HR                                                             [module]
   Appraisals · Disciplinaries · Objectives · Remunerations
   Certificates · Probation & fixed-term tracking

◆ Activities / Job Costing                                       [module]
   Work activities · Activity hierarchy · Activity machines
   Scheduled activities · Clients · Job sheets
   Activity documents & notes · Global activity exceptions
   Global activity rounding rules · Default activity assignment

◆ Expenses                                                       [module]
   Claims · Mileage · Expense dashboard
   Types & groups · Rates · Payment categories · Vehicle types

◆ Emergency & Safety                                             [module]
   Emergency control · Live roll call · Muster points
   Fire marshals · Incident archive · Guard screen

◆ Visitors                                                       [module]
   Visitor activities · Pre-registration & invitations
   Check-in/out · Deliveries · Visitor settings

◆ Insight                                                        [module]
   Assistant ⭐ · Core reports · Saved reports
   Report scheduling & delivery · Favourites
   (replaces 136 report controllers)

◆ Payroll Export                                                 [module]
   Export builder · Export runs & history · Format mappings
   (replaces 19 per-vendor screens + 44 plugins)

◆ Integrations                                                   [module]
   Connector settings · Sync history · Legacy TLW bridge

  Notification Setup                                             [foundation]
   Notification types · Templates · Recipients & manager roles
   Email settings · Attendance notifications · Report notifications

  System Setup                                                   [foundation]
   Options · Calculation settings · Localisation · Custom localisation
   Navigation & display settings · Data retention

  Security                                                       [foundation]
   Users · Groups · Access diagnostics · User action log
   Two-factor · SSO settings

  Maintenance                                                    [foundation]
   System health · Version & rollout · Backup/restore
   Licence status & features · Plugins · Audit browser

   My                                                            [always]
   My time · My absence · My documents · My expenses · My profile
```

**~120 screens** across the retained scope — not the ~40 an earlier draft claimed.
Against legacy's ~230 the reduction comes almost entirely from three places:
reports (136 controllers → the assistant), payroll (19 vendor screens → one builder),
and the dropped verticals.

◇ Student Registration                          [module — deferred, schools vertical]
   Lesson registration (+ bulk, retro) · AM/PM registration
   Registration marks · School calendar · Timetables
   Student dashboard & search · Mentors
   Consecutive & percentage absence alerts
   (MIS connectors — SIMS, iSAMS, Tribal EBS — already sit in Integrations)

   Kept in the tree, not built. Cheap to retain because TLW models students and
   teachers as **Employee records** distinguished by employee group —
   `IStudentRegistrationService.GetStudents()` and `GetTeachers()` both return
   `IQueryable<Employee>`. Nothing in the People model needs to change to allow
   it later, provided the person-type question below is settled early.

### Deliberately dropped

EPOS/catering (~48 controllers) · Physical access control (~17) ·
LAPI/ANPR vehicle (~7) · Device maintenance screens · Card printing.

---

## 2. Licensing

**Two levels, matching TLW: pick a branch, then pick the sections within it.** A
customer buying Work Rules need not take every section of it, so the licence is a set
of selected branches and sections rather than a flat product tier.

```
Licence
 ├─ modules:
 │    timeattendance                       whole branch
 │    workrules.templates                  one section
 │    workrules.balances
 │    workrules.absence
 │    documents.*                          branch plus every section
 │    insight.assistant                    metered — real per-token cost
 └─ limits:  maxEmployees · maxSites · maxUsers
```

An unlicensed branch or section is **absent, not hidden**: missing from navigation,
refused by the API, and **not offered in the group editor**, so an admin cannot grant
rights to something the customer has not bought.

Foundation branches — Personnel, Notification Setup, System Setup, Security, Maintenance — are always
present and carry no licence flag. They are not a "free tier"; they are the platform,
and there is no coherent product without employees to record, accounts to sign in with,
and rights to control them. **Everything else is sellable, including Time & Attendance.**

### Dependencies are declared, not forbidden

Some modules genuinely need others — Scheduling is meaningless without Work Rules, and
Payroll Export needs calculated time. Pricing handles that: sell them together.

What pricing does **not** handle is *accidental* coupling — a module reaching into
another's types when nobody intended them bundled. That is only discovered when someone
tries to sell them apart, which is the expensive moment to find out.

So each module **declares what it requires**, and the declaration does three jobs:

```
timeattendance         requires: workrules.base      always together
workrules.base         requires: —                   the calculation core
workrules.periodic     requires: workrules.base
workrules.balances     requires: workrules.base
workrules.absence      requires: workrules.base
workrules.costing      requires: workrules.base
scheduling             requires: workrules.base
absencerequests        requires: workrules.absence
payrollexport          requires: workrules.base
activities             requires: timeattendance
hr                     requires: —
expenses               requires: —
documents              requires: —
visitors               requires: —
emergency              requires: timeattendance      roll call reads presence
insight                requires: —
```

**`workrules.base` and `timeattendance` are effectively one purchase.** They are kept
as separate ids because the *screens* live in different branches and groups grant
rights per screen — not because either is useful alone.

1. **Licence validation** refuses an incoherent combination (Scheduling without Work
   Rules) at issue time rather than at the customer's site.
2. **Code may depend along a declared edge.** No ceremony between modules that always
   ship together — that would be wasted effort.
3. **Anything undeclared is a bug**, caught in review rather than by a customer.

The point is not to prevent dependencies. It is to make them **visible and deliberate**,
so bundling is a decision rather than an accident.

- **Sections must be separable too** where they are separately sold:
  `workrules.balances` being optional means `workrules.templates` cannot assume flexi
  balances exist.
- **The assistant is metered**, unlike every other feature, because it carries genuine
  marginal cost per customer.

---

## 3. Permission model

```
Group
 ├─ Screen permissions   screen → none | read | edit
 └─ Employee scope       None
                       | AllEmployees
                       | ByEmployees        an explicit list
                       | ByStructure        any combination of:
                             departments · locations (= WM "sites")
                             cost centres · work activities
```

**`ByStructure` carries more dimensions than an earlier draft allowed.** `IAuthorizationService`
exposes **six** managed-dimension accessors — departments, locations, buildings, cost centres,
working activities and caterers — not the two (departments, sites) previously modelled. Caterers
belong to the dropped EPOS vertical. They combine as an intersection within a group: "departments
A and B, but only at location C" is a real configuration and a common one, since a manager
typically owns a function at a place rather than everywhere.

> **Two corrections from the phase audit (2026-08-04), both measured:**
>
> 1. **"locations" and "sites" are the same axis, not two.** `dbo.Employees` (153 columns) has
>    `DepartmentId`, `EmployeeLocationId` and `CostCentreId` — there is no `SiteId`. WM's `Site`
>    *is* legacy's `Location`. The earlier tree listed both and so overcounted.
> 2. **Buildings are not an employee attribute, so buildings are not four-of-six — they are
>    zero.** `BuildingId` appears on `Devices`, `GetDoorStatuses`, `ac_security_group`,
>    `AnprEventsView`, `EposTills`, `LapiCameras` and `FireMarshalMusterPoints` — physical plant,
>    all of it dropped except muster points (Safety, phase 6b). `ManagedBuildingsByRole` exists,
>    but there is nothing on an employee for it to match against. **Four dimensions matter to WM,
>    not five:** departments, locations/sites, cost centres, work activities — plus the explicit
>    employee list.
>
> Precision note: five of the six are named `Managed*ByRole`; cost centres is
> `ManagedCostCentres(int roleId)` (`IAuthorizationService.cs:22-23`). Same thing, different name.

> **A third correction, 2026-08-06 (People/HR survey) — the tree above still listed *both* "Sites"
> and "Locations" under Org structure**, contradicting correction 1 in the same file. Fixed; the
> axis is now named once. The survey also measured which dimensions actually nest, which turns out
> to be the opposite of what WM built ([`TLW-PEOPLE-MODEL.md`](./TLW-PEOPLE-MODEL.md) §5.1):
>
> | Legacy table | Cols | Nests? | WM | Nests in WM? |
> |---|---:|---|---|---|
> | `dbo.Locations` (`HorioDB.designer.cs:182940`) | 4 | **No** — `Id, Code, DisplayName, IsActive` | `Site` | **Yes** (`ParentId`, `SiteHierarchy`) |
> | `dbo.Departments` (`:55085`) | 10 | **Yes** — `ParentId` at `:55267` | `Department` | **No** |
> | `dbo.CostCentres` (`:91377`) | 15 | Yes | — | not modelled |
> | `dbo.Buildings` (`:113785`) | 5 | Yes | — | dropped (correction 2) |
>
> So **WM's site tree has no legacy precedent on that axis** (it is a WM improvement, which is
> fine — but it should be labelled one, not a port), and **the axis legacy does nest is flat in
> WM**. Legacy expands the department tree in exactly one place — absence-request scope,
> `Planning/AbsenceRequests.cs:3039-3043` via `dbo.DepartmentHeirarchyView`
> (`Database/Versioning/81.V5.27.0.0.sql:594-601`) — which is a *different* scope mechanism from
> the role TVF and does **not** overturn `TLW-AUTHORIZATION-MODEL.md` C9. Affects **001 P4/P5**.

**Legacy only enforces two of them centrally.** `RoleBasedEmployeeFilterService` honours
departments ∩ locations and nothing else; buildings, cost centres and working activities are
read at scattered call sites. WM's single `WithinScope()` filter exists to prevent that scatter.
See `docs/plans/001-compositional-data-scope.md`.

> **Correction (2026-08-05).** This paragraph previously ended *"that scatter is why legacy
> needed `DataAccessScopeDiagnostics` to explain its own decisions"*. **It isn't.**
> `DataAccessScopeDiagnostics` is `DataContext` lifetime tracking for connection leaks
> (`Logic/DAL/DataContextTracking/DataAccessScopeDiagnostics.cs:10-55`). Legacy has no tool that
> explains an access decision. See [`TLW-AUTHORIZATION-MODEL.md`](./TLW-AUTHORIZATION-MODEL.md) §11.

- ~~A user may belong to **several groups**; access combines as a **union** — most permissive
  wins, like IAM group membership.~~ **Wrong about TLW, measured 2026-08-05.** A TLW user holds
  **exactly one** role/group: `GetUserRole` uses `SingleOrDefault`
  (`AuthorizationService.cs:1265-1280`), the cache is `Dictionary<userId, roleId>` (`:773-786`),
  and `AddUserToRole` **deletes the existing assignment before inserting** (`:1008-1041`). There
  is no union in TLW and no precedence rule, because there is never more than one grant.
  **WM's model here is undecided** — see `TLW-AUTHORIZATION-MODEL.md` §13 and §4's open question.
- **`read` opens a screen; `edit` allows mutation.** Enforced server-side; hiding a
  button is UX, not security. *(Matches TLW: GET requires `FormViewing`, POST requires
  `FormEditing` — `AuthorizingControllerBase.cs:565-575`. Edit implies view, and every ancestor
  branch must also be accessible — `FormAccess.cs:31-43`.)*
- **Screen access and employee scope are independent** — "may edit, but only these
  people" is a normal configuration. *(Matches TLW: the two axes never intersect, except in
  `CurrentUserCanViewEmployeeFormTab`, which is the exclusion list — `FormAccess.cs:85-90`.)*
- **`My` is never group-controlled.** Any employee-linked user gets self-service.
- **No deny rules.** Narrow by removing membership.
  *(The legacy feature being declined is `AccessRightsExclusions` + `AccessRightsExclusionEmployees`
  + `AccessRightsExclusionResources`, wired through `AuthorizationService.SaveExclusionListForRole`.
  Note that TLW's own screen-right resolution is **deny-overrides-allow, default deny**
  (`AuthorizationService.cs:695-718`) — the opposite of "most permissive wins". It is only
  unreachable in practice because a user has one role. The requirement the exclusion list met —
  hiding named individuals' Salary/Bank/Disciplinary tabs from a manager who legitimately holds
  the tab (`GroupsController.cs:463-466`) — is currently unmet in WM.)*
- **Scope is explicit.** TLW fails open in **twelve** measured places in the in-scope surface,
  not the three or four previously recorded — an empty managed list applies no filter, so a
  half-configured group exposes the whole workforce. WM requires the intent to be stated.
  Inventory: `TLW-AUTHORIZATION-MODEL.md` §10.

> **Status check (2026-08-04, screen-permissions half re-measured 2026-08-05).** Neither half of
> this model is finished, and the doc previously implied both were closer than they are:
> - **Screen permissions: not started.** No equivalent of legacy's `dbo.AccessControlEntry`
>   exists in WM. The Angular routes guard on coarse permission names (`app.routes.ts:17-40`),
>   which is not the same thing and is client-side. *(The legacy table was previously named here
>   as `WebPage`/`FormAccess`. `dbo.WebPages` is a **localization** table
>   (`HorioDB.designer.cs:27642-27654`) and `FormAccess` is a static C# helper class
>   (`FormAccess.cs:7`) — neither is the store. The store is
>   `AccessControlEntry(Id, Allow, RoleId, ActionId, SecuredObjectTypeId, SecuredObjectId)`,
>   `HorioDB.designer.cs:10330-10346`, ≈960–1,010 rows per role over 49 branches / 382 forms /
>   75 tabs.)*
> - **Employee scope: shipped but partly unreachable.** The `Departments` scope kind exists in the
>   API and the database, but the group editor's dropdown offers only None / Self / Sites / All
>   (`security-groups.component.ts:113-116`) and **there is no `/api/departments` endpoint** to
>   populate a picker. Plan 001 P5 cannot be built until that endpoint exists.
>
> See [`PHASE-AUDIT.md`](./PHASE-AUDIT.md) A2 and C5.

### Deferred: tab-level permissions

TLW goes finer, with per-tab rights inside Personnel (`PersonnelTab`). Screens first;
tabs can be added later without changing the group shape.

---

## 4. Decisions taken (2026-07-21)

1. **Licensing is two-level** — branch, then sections within it. Matches TLW.
2. **Absence rules and absence workflow are split** across Work Rules and Absence
   Requests. Safe to do, because sections are licensed independently, so the split
   changes navigation without changing what can be shipped.
3. **HR is its own branch**, as in TLW.
4. **There is no core product.** Every capability is sellable, Time & Attendance
   included. Only the platform — Personnel, System Setup, Security, Maintenance — is
   always present, because nothing functions without it.

5. **Notification Setup is foundation.** Every module raises notifications, so it is
   platform rather than product.
6. **Dependencies are declared, not avoided.** Modules that need each other are sold
   together; the declaration exists so bundling is deliberate and licence validation
   can refuse an incoherent combination.

7. **Work Rules has a required base.** Daily templates, pay categories, breaks,
   rounding, corrections/adjustments and exceptions ship whenever Time & Attendance
   does — verified in the code, not assumed. Periodic/weekly, Balances, Absence & leave
   and Costing are the genuinely optional sections.
   *Qualified 2026-08-18: required to run the product, but the Daily Browser **renders**
   without most of it — see the correction at the end of §5.*
8. **`My` sub-items inherit their module's licence.** "My expenses" appears only with
   Expenses; "My time" and "My profile" are always present.

## 5. Gap analysis (2026-07-21)

Every one of the 236 view folders was mapped against this tree. 82 are dropped
verticals or plumbing; the remaining 154 all map, **after** adding three things the
tree was missing:

| Missing | Where it actually lives in TLW | Now |
|---|---|---|
| **Locations** | `PersonnelSetupController/LocationsController` — personnel setup, *not* access control | Personnel → Org structure |
| **Buildings** | `AccessControl/BuildingsController`, but reused by muster points | Personnel → Org structure (outlives the AC drop) |
| **Import / Export** | `ImportExportController` — bulk import of personnel, clockings, absences, activities, custom fields, clients | Its own module |

And one correction to the **permission model**, which matters more than the tree: group
scoping has **five** structural dimensions, not two. See §3.

### Correction, 2026-08-14 — two leaver-vocabulary setup screens, one of them unrecorded

Surveying the employment/leaver area for plan 007 found that the *Personnel setup* branch owns
**two** customer-maintained leaver lookups, each with a list + add + edit screen. Only the first
was implied by this tree, and neither was named:

| Screen set | Controller | Views | Table |
|---|---|---|---|
| **Leave reasons** | `PersonnelSetupController/LeaveReasonsController` | menu key `Menu_Personnel_LeaveReasons` | `dbo.LeaveReasons` (3 cols, `HorioDB.designer.cs:53169-53209`) |
| **Leave notice periods** | `PersonnelSetupController/LeaveNoticePeriodsController.cs` (+ `Logic/Settings/LeaveNoticePeriodService.cs`, `ILeaveNoticePeriodService.cs`) | `Views/PersonnelSetup/LeaveNoticePeriods.cshtml`, `AddLeaveNoticePeriod.cshtml`, `EditLeaveNoticePeriod.cshtml`, tab in `_Tabs.cshtml` | `dbo.LeaveNoticePeriods` (2 cols, `:182854-182874`) |

Both feed dropdowns on the employee **Leaver** tab
(`Views/Personnel/Controls/_Leaver.cshtml:7-11`, `:17-20`). WM plan 007 P1 builds the first and has
no owner for the second. Detail in `TLW-PEOPLE-MODEL.md` §4.1b.

### Correction, 2026-08-18 — the Work Rules base, measured (plan 010)

This document's Work Rules entry was wrong in three ways and overstated the Daily Browser's
dependencies in a fourth. Full anatomy in [`TLW-WORK-RULES.md`](./TLW-WORK-RULES.md).

**1. "Day types" and "Calendar day models" are Access Control.** `DayTypeController.cs` and
`AccessControlCalendarController.cs` both live in `WebSite/Controllers/**AccessControl**/`, over
`dbo.ac_day_type` and `dbo.ac_calendar` (5 columns each) — part of the 12-table `ac_*` door-access
schema alongside `ac_security_group` and `ac_timezone`. There is no Work Rules "day type": the
nearest concept is `DailyModels.ModelType` (11 variants,
`SharedLogic\Enums\ShiftType.cs:4-17`). **This is the third instance of the same misattribution** —
after `SecurityGroup` (corrected at the top of this document) and `ac_timezone` (corrected at
`008-a-day-has-a-place.md:155`). Both tree lines are now fixed.

**2. "Pay categories" and "pay periods" have no tables of those names, and "Counters" was listed
twice.** A pay category is a row in `dbo.Counters` (7 cols); its value for a day is
`Clockings.CPTN01..20` — twenty fixed columns, so twenty categories, maximum. A pay period is
`dbo.Periods` (6 cols, `PeriodsController.cs`), which is **not** `dbo.ac_period`.

`CountersController` over `dbo.Counters` **is** the Pay categories screen, so this tree listed the
same screen in *both* Work Rules — base and Work Rules — Balances. The duplicate is removed from
Balances, which keeps `WeeklyCounters` (a different controller and table).

The 20-category ceiling is harder than "a schema limit": `dbo.Counters` is created and seeded with
**exactly 20 rows** in one block (`Database\Versioning\27.V2.1.12.sql:390-501`), and
`CountersController` exposes **only `Index` and `EditCounter`** — no Create, no Delete. Twenty
renameable slots, permanently.

**3. "Blocked exception rules" and "Muted exceptions" are not screens.** The blocked-exception rule
is three global modes (`Documentation\Blocked Exception Rules.md:11-14`). Muting is a single
day-level flag, `Clockings.ShouldHideExceptions`, with two grid filters
(`DailyBrowserController.cs:178-179`). *Authorising* an exception is a genuinely separate,
per-exception action with a user and timestamp (`dbo.ScoresAbnormalitiesAuthorized`, 8 cols) and was
missing from this tree entirely.

**4. "Daily Browser cannot render without these" — half right.** Counting call sites in
`DailyBrowserController.cs` (113 KB, 26 injected services):

| Service | Uses | Path |
|---|---|---|
| `IScoreService` | 15 | **read — essential.** It is the grid's row repository (`:1059`) |
| `IDailyModelService` | 4 | read, but **degrades**: a missing template yields `new DailyModel()` (`DailyBrowserDetailsViewModel.cs:61`) |
| `IHoursCalculationService` | **2** | **write only** (`:1257`, `:1270`) — reached solely from `Save` |
| `IClockingPausesService` | **1** | **write only** (`:1725`) |
| `ClockingService` | **0** | injected, never used |

So the Daily Browser renders from stored `calc_*` columns **without a calculation engine**. And it
renders 14 columns by default, not 126: the selectable set is 48
(`Logic\Scores\DailyBrowserColumn.cs`) and the shipped default is
`"2,3,4,5,6,9,10,11,12,20,22,24,28,29,30"` (`Core\Constants.cs:48`). That is what makes a read-only
first version possible — plan **010**.

Confirmed dropped after checking: `Events` (AC event types), `Timezone` (door-access
timezones, unrelated to site time zones), `BulkRegistration` (student lesson
registration), `Inventory`/`PaymentType`/`ReceiptStatus`/`TipManagement` and the
ParentPay/Squid/WisePay settings (all EPOS). Added 2026-08-18: `DayType`,
`AccessControlCalendar` and `AccessControlPeriod` are AC, not Work Rules.

## 6. Person type — decide before People is finished

Keeping the schools vertical open raises a modelling question worth settling now,
because it is cheap now and expensive later.

TLW puts **students, teachers and staff all in the `Employee` table**, separated by
employee group. It works, but it means contracts, timesheets, accruals and payroll
export all technically apply to a fourteen-year-old, and nothing in the model says
otherwise. The same strain shows up outside schools: agency and contractor staff are
tracked but not paid through the system.

Recommendation: a **`PersonType`** discriminator on the people record —
`Employee · Contractor · Student · Teacher` — with modules declaring which types they
apply to. One table, no new joins, but payroll can refuse a student and attendance can
still include one. Adding it now costs a column; adding it after Work Rules and Payroll
are built means revisiting both.

9. **`PersonType` is adopted** — `Employee · Contractor · Student · Teacher` on the
   people record, with modules declaring which types they apply to. Goes in before
   People is finished, so payroll can refuse a student while attendance includes one.
10. **Periodic/weekly and Costing stay optional sections.** Only the Work Rules base
    ships with Time & Attendance.

## 7. Still open

- **Screen count per section**, which drives the build estimate. Not urgent: the Work
  Rules base is the first thing to build regardless, and it is ~15 screens.
