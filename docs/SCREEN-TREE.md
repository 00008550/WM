# WM — Screen Tree, Permissions & Licensable Modules

*v2, 2026-07-21. Agree this before building screens.*

Three jobs in one structure:

1. **Navigation** — how people find things.
2. **Permission unit** — a group grants none / read / edit per screen.
3. **Licensing unit** — a **branch is a licensable module**, so a customer can be
   shipped only what they need. This is a hard requirement, and it is why the tree
   follows business capability rather than convenience.

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

`◆` = licensable module. `[core]` = always shipped.

```
◆ Time & Attendance                                              [core]
   Daily browser · Presence panel · Clockings & corrections
   Manual timesheets · Exceptions (scores) · Transaction report
   Geolocation tracking · QR punching · Virtual terminal
   Period locking

◆ Personnel                                                      [core]
   Employees · Employee contracts · Contract assignment
   Positions & qualifications · Custom fields · Emergency contacts
   Employee groups · Population groups · Cost centres
   Personnel setup · Leavers · Anniversaries

◆ Work Rules                                                     [core]
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

◆ Insight                                                        [core]
   Assistant ⭐ · Core reports · Saved reports
   Report scheduling & delivery · Favourites
   (replaces 136 report controllers)

◆ Payroll Export                                                 [module]
   Export builder · Export runs & history · Format mappings
   (replaces 19 per-vendor screens + 44 plugins)

◆ Integrations                                                   [module]
   Connector settings · Sync history · Legacy TLW bridge

◆ Notification Setup                                             [core]
   Notification types · Templates · Recipients & manager roles
   Email settings · Attendance notifications · Report notifications

◆ System Setup                                                   [core]
   Options · Calculation settings · Localisation · Custom localisation
   Navigation & display settings · Data retention

◆ Security                                                       [core]
   Users · Groups · Access diagnostics · User action log
   Two-factor · SSO settings

◆ Maintenance                                                    [core]
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

**A branch is a licensable module.** A licence enables branches, and the navigation,
the API and the group editor all respect it — an unlicensed branch is absent, not
merely hidden.

```
Licence
 ├─ features:  ["timeattendance", "personnel", "workrules", "scheduling",
 │              "documents", "activities", "expenses", "insight.assistant", ...]
 └─ limits:    maxEmployees, maxSites, maxUsers
```

Consequences worth stating now, because they constrain how modules are built:

- **`[core]` branches ship with every licence** — a workforce product without time,
  people, work rules, reporting, security and setup is not a product.
- **Module boundaries must be real.** If Scheduling is licensable, nothing in a core
  branch may hard-depend on Scheduling types. This is already the module rule in
  `ARCHITECTURE.md`; licensing makes it enforceable rather than aspirational.
- **The assistant is separately licensable** (`insight.assistant`) because unlike every
  other feature it carries per-token cost.
- **Sub-features exist where the split is commercially real** — e.g. `documents.esign`
  separate from `documents`.
- **Group editors only offer licensed screens**, so an admin cannot grant rights to a
  module the customer has not bought.

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

## 4. Open questions

1. **Absence split.** I have put the *rules* (types, accruals, entitlements, holidays)
   in **Work Rules** and the *workflow* (requests, approvals) in **Absence Requests**,
   since they are configured by different people. TLW keeps absences under Work Rules
   throughout. Split, or keep together?
2. **HR as its own branch** or a section inside Personnel? Separate makes it licensable
   on its own, which may be worth money; combined is fewer clicks.
3. **Licensing granularity** — branch level only, or sub-features too (e.g.
   `documents.esign`, `activities.jobcosting`)? Finer sells better but is more to manage.
4. **What is genuinely core?** My `[core]` marking is a proposal. If customers buy
   time-and-attendance alone, then Scheduling, Documents and HR are all optional — but
   is *Insight* core, or would you sell reporting as an upsell?
