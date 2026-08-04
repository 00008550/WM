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
      Sites · Departments · Locations · Buildings · Cost centres
      (Locations sits under Personnel Setup in TLW, not access control;
       Buildings is reused by muster points, so it outlives the AC drop)
   Personnel setup · Leavers · Anniversaries

◆ Import / Export                                                [module]
   Bulk import: personnel (simple & advanced) · clockings ·
   clockings with absences · absences · activities ·
   employee custom field values · clients & client sites/contacts
   Export settings · Import history & error reporting

  Work Rules — base                            [required by Time & Attendance]
      Daily templates · Day types · Calendar day models
      Master daily model assignment
      Shift matching · Split shifts · Multi-shift
      Break rules · Rounding rules · Global schedule thresholds
      Pay categories · Pay periods
      Corrections & time adjustments
      Exception setup · Blocked exception rules · Muted exceptions

      Not optional: Daily Browser cannot render without these. Its controller
      pulls in HoursCalculation, ClockingPauses and Scores, and
      DailyBrowserClocking directly carries ClockingCorrection,
      ClockingShiftCorrection, ClockingPause and ScoresAbnormality. A punch
      with no template to match, no pay category to classify into and no way
      to correct it is not a usable attendance product.

◆ Work Rules — Periodic & weekly                                 [module]
      Periodic templates · Weekly models
      Weekly/periodic band & hour counters

◆ Work Rules — Balances                                          [module]
      Counters · Weekly counters · Flexi balance · Flexi balance tracking
      Balance reset · Contract hours limits · Debit/credit rules

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

**Legacy only enforces two of them centrally.** `RoleBasedEmployeeFilterService` honours
departments ∩ locations and nothing else; buildings, cost centres and working activities are
read at scattered call sites. That scatter is why legacy needed `DataAccessScopeDiagnostics`
to explain its own decisions, and it is the thing WM's single `WithinScope()` filter exists
to prevent. See `docs/plans/001-compositional-data-scope.md`.

- A user may belong to **several groups**; access combines as a **union** — most
  permissive wins, like IAM group membership.
- **`read` opens a screen; `edit` allows mutation.** Enforced server-side; hiding a
  button is UX, not security.
- **Screen access and employee scope are independent** — "may edit, but only these
  people" is a normal configuration.
- **`My` is never group-controlled.** Any employee-linked user gets self-service.
- **No deny rules.** Narrow by removing membership. Deny lists interacting across
  several groups are what forced TLW to ship a diagnostics subsystem to explain itself.
  *(The legacy feature being declined is `AccessRightsExclusions` + `AccessRightsExclusionEmployees`
  + `AccessRightsExclusionResources`, wired through `AuthorizationService.SaveExclusionListForRole`.)*
- **Scope is explicit.** TLW fails open — an empty managed list applies no filter, so a
  half-configured group exposes the whole workforce. WM requires the intent to be stated.

> **Status check (2026-08-04).** Neither half of this model is finished, and the doc previously
> implied both were closer than they are:
> - **Screen permissions: not started.** No `WebPage`/`FormAccess` equivalent exists in WM. The
>   Angular routes guard on coarse permission names (`app.routes.ts:17-40`), which is not the
>   same thing and is client-side.
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

Confirmed dropped after checking: `Events` (AC event types), `Timezone` (door-access
timezones, unrelated to site time zones), `BulkRegistration` (student lesson
registration), `Inventory`/`PaymentType`/`ReceiptStatus`/`TipManagement` and the
ParentPay/Squid/WisePay settings (all EPOS).

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
