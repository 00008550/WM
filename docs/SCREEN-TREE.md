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
   Positions & qualifications · Custom fields · Emergency contacts
   Employee groups · Population groups · Cost centres
   Personnel setup · Leavers · Anniversaries

◆ Work Rules                                                     [module]
   ── Templates
      Daily templates · Periodic templates · Weekly models
      Master daily model assignment · Calendar day models · Day types
      Shift matching · Split shifts · Multi-shift
   ── Breaks & rounding
      Break rules · Rounding rules · Global schedule thresholds
   ── Pay
      Pay categories · Tariffs · Hourly rates · Cost centre allocation
      Pay periods
   ── Balances
      Counters · Weekly counters · Flexi balance · Flexi balance tracking
      Balance reset · Contract hours limits
   ── Absence & leave
      Absences · Predefined absences · Absence allocation
      Absence authorisation · Absence managers setup
      Accruals · Accrual adjustments · Length-of-service bonus
      Holidays · School holidays · Recaps
   ── Adjustments
      Time adjustments · Corrections · Exception setup
      Blocked exception rules

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

### Deliberately dropped

EPOS/catering (~48 controllers) · Student registration (~27) · Physical access control
(~17) · LAPI/ANPR vehicle (~7) · Device maintenance screens · Card printing.

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
timeattendance      requires: —
workrules           requires: timeattendance
scheduling          requires: workrules
absencerequests     requires: workrules.absence
payrollexport       requires: workrules
activities          requires: timeattendance
expenses            requires: —
documents           requires: —
insight             requires: —
```

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
 └─ Employee scope       None | AllEmployees | ByDepartments | ByEmployees
```

- A user may belong to **several groups**; access combines as a **union** — most
  permissive wins, like IAM group membership.
- **`read` opens a screen; `edit` allows mutation.** Enforced server-side; hiding a
  button is UX, not security.
- **Screen access and employee scope are independent** — "may edit, but only these
  people" is a normal configuration.
- **`My` is never group-controlled.** Any employee-linked user gets self-service.
- **No deny rules.** Narrow by removing membership. Deny lists interacting across
  several groups are what forced TLW to ship a diagnostics subsystem to explain itself.
- **Scope is explicit.** TLW fails open — an empty managed list applies no filter, so a
  half-configured group exposes the whole workforce. WM requires the intent to be stated.

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

## 5. Still open

- **Section boundaries inside Work Rules.** The six sections above (Templates, Breaks &
  rounding, Pay, Balances, Absence & leave, Adjustments) are my split. If customers
  actually buy along different lines, these should follow the money rather than my
  sense of tidiness.
- **`My` self-service and licensing.** Currently always present, but "My expenses" is
  meaningless without the Expenses module. Most likely each `My` sub-item inherits its
  module's licence, with "My time" and "My profile" always available.
