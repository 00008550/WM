# Plan state

The handoff bus for the wm-surveyor → wm-builder → wm-reviewer cycle.
`wm-surveyor` writes plans. `wm-builder` executes one portion. `wm-reviewer` judges and flips status.

## Phase audit — 2026-08-04

A retrospective audit of **everything merged so far** ran before the next portion was started.
Findings: [`../PHASE-AUDIT.md`](../PHASE-AUDIT.md). Headline:

- **Two blocking findings, both in code merged before this cycle existed.** The realtime punch
  feed broadcasts every punch to every authenticated client with no data scope and no permission
  check (`EventStreamProducers.cs:109`), and employee create/update are not scope-checked
  (`PeopleModule.cs:77-113`, `:139`).
- **Both portions that went through the cycle check out.** 001 P1 fully; 001 P2 with one process
  gap — its stated "migration up/down on a seeded DB" test was never written, and the review
  passed it anyway. Behaviour is correct; the SQL half of the migration is untested.
- **Plan 001's claims about legacy held up** on every point checked, and gained four corrections:
  a fourth fail-open, a fourth (SQL) implementation that disagrees with the C# on the fail-open
  case, three unmodelled `Role` columns, and the finding that **Building is not an employee
  attribute in legacy at all** — P4 was amended in place to drop it.
- **21 claims were verified correct** and are listed, so "not mentioned" never again reads as
  "not checked".

## Authorization survey — 2026-08-05

The user's read of the cycle: *"we don't get correct groups and we have only a vague idea about
groups and what an employee can see and edit."* A focused survey of legacy's **entire**
authorization surface answered that, and produced
[`../TLW-AUTHORIZATION-MODEL.md`](../TLW-AUTHORIZATION-MODEL.md) — now the single place that
question is answered, replacing five partial accounts.

Headline, all measured with `file:line`:

- **`/Groups` edits exactly one `dbo.Role`**, carrying screen rights *and* all seven managed
  dimensions *and* nav icons, dashboard widgets and exclusions. §4's "one object, mirroring TLW"
  is **correct**.
- **A legacy user belongs to exactly one of them.** §4's "several groups, union, mirroring TLW"
  is **wrong and was never TLW** (`AuthorizationService.cs:1265-1280`, `:773-786`, `:1008-1041`).
  WM's multi-membership is a WM choice nobody made deliberately.
- **Screen rights resolve deny-over-allow, default deny** (`:695-718`) — the opposite of the
  "most permissive wins" WM's docs promise.
- **`AccessType.Read` vs `.Edit` changes the answer in exactly one case** — your own record under
  `CanModifySelf = false`. Legacy's read scope *is* its write scope. `AccessType` is not
  vestigial, but it is nearly so.
- **`Role.IsDepartmentOnly` is dead** — written, localized, never read. Settled; do not model it.
- **`dbo.WebPages` is a localization table** and `FormAccess`/`SiteItemPermission` are C# types.
  The screen-rights store is `dbo.AccessControlEntry`. §13 named the wrong things for three
  revisions.
- **Twelve fail-opens in the in-scope surface, not four.**
- **`DataAccessScopeDiagnostics` has nothing to do with authorization** — it counts
  `DataContext` disposals to find connection leaks. It was cited as the evidence for two design
  decisions in three docs and — **corrected 2026-08-05 while planning 005** — **four** code
  comments, not two: `ScopeModel.cs:220-223`, `SecurityGroupEndpoints.cs:61-62`,
  `SecurityGroup.cs:10-13` (which also repeats C5 and asserts the orthogonality option A has now
  reversed) and `EmployeeScopeExtensions.cs:9-11`. A fifth, milder instance sits in
  `DataScope.cs:5-6`, in a file 005 P4 deletes. Each is folded into the *Touches* of the 005
  portion that opens that file.

### Decisions taken (user, 2026-08-05)

1. **Option A — one object, one membership.** A security group carries screen rights, data scope,
   a mode (`Normal` / `SelfOnly`) and `CanEditOwnRecord`; a user belongs to exactly one, non-null.
   `ARCHITECTURE.md` §4 **has been rewritten** to match, including *why* exclusivity is part of the
   design rather than an accident of it. Plan 004's ⛔ block is cleared.
   *(005 models the mode as `ScopeRuleKind.Self` rather than a separate column — under exclusivity
   a mode is a rule kind, and two fields for one meaning is the shape the survey criticised. Raised
   as 005 open question 1 in case you want the column.)*
2. **Adopt `CanEditOwnRecord`** (legacy `CanModifySelf`): a user may see their own record without
   being able to edit it. 003 P2b already carries the named seam for it.

**What A costs, and what it does not.** 001 P1/P2 survive intact — intersection-within-a-group is
still the right shape and is shipped under test. What dies is the *union across groups*: D2, and
with it 001 P3's premise. `003 P1`'s `AttendanceAudience` simplifies but is not wrong.

### The refactor is now planned — 005, drafted 2026-08-05

[`005-one-membership.md`](./005-one-membership.md) — `draft`, 6 portions. It **supersedes 001 P3**
(the resolver rewrite and the union deletion are one edit to one method; splitting them would mean
writing a one-membership resolver that still switches on `ScopeKind`, then rewriting it).

Measured blast radius: **22 source files, 5 frontend files, 7 test files, 3 migrations.** It is a
large plan and the plan says so.

Four things it settles rather than defers:

- **The collapse rule: collapse by resolved *effect*, not by stored configuration.** A
  *configuration* union across groups is genuinely unrepresentable in one group (X on Site S1 and Y
  on Department D9 mean "S1 **or** D9"; one group intersects its constraints). The *effect* always
  is — because `DataScopeResolver.cs:50-69` **already** reduces every user to a single
  `DataScopeKind` plus one dimension's id set, and every consumer reads it that way. So the mapping
  is total and **no user's combination lacks a clean answer.** One group is materialised per
  distinct (permission set, scope) pair, deduped, named from its contributing combinations, with a
  report row per user. The one thing *not* preserved is a membership that was already doing
  nothing — the old rows survive to P6 and the report survives forever, so an admin can re-grant
  deliberately. Restoring access nobody had is a product decision, not a migration's job.
- **`EffectiveDataScope`/`DataScopeKind` die; `ScopeConstraint`/`ScopeRule`/intersection survive
  untouched.** `DataScope` collapses from a list of rules to one. 001 P3's three sanctioned
  deviations: #1 is **cancelled**, #2 and #3 are **resolved by construction**.
- **Ordering:** 003 P2a and P2b both land **before** 005 (P2b is a live defect and carries the
  `CanEditOwnRecord` seam 005 P4 wires up); 003 P3 either side; 001 P4/P5 and 004 after.
- **Reversibility:** P1–P5 are genuinely reversible because they are purely additive — the four
  old tables are read and never written, exactly as 001 P2 was reversible. **P6 drops them and is
  one-way**, which is why it is a separate portion sequenced last. Its `Down` recreates empty
  tables and says so rather than implying a restore.

Two process facts it also records: **`ClaimTypes.Role` is minted at `TokenService.cs:43` and read
nowhere** in `src/**` or `frontend/**`, so the JWT change is a deletion rather than a redesign; and
there is **no Postgres test harness in the repo** (`WM.Modules.Identity.Tests` is EF InMemory),
which is why 001 P2's promised migration test was never written — so 005 does the collapse in a
tested C# runner rather than untestable SQL.

## People/HR survey — 2026-08-06

The user's question was *"see if we missed something from old TLW within our development"*, pointed
at the largest bucket nobody had ever measured: `COVERAGE-AUDIT.md` §2 had People/HR at 95 tables /
1,074 columns and marked it *"◐ People partly; HR barely"* — while WM has already **shipped** a
People module on it. Result: [`../TLW-PEOPLE-MODEL.md`](../TLW-PEOPLE-MODEL.md), and plan
[`007-the-person-record.md`](./007-the-person-record.md) (`draft`, 5 portions).

**The answer is yes, we missed something — and it is the same shape as the Clocking finding.**
`dbo.Employees` is the **6th-largest table in the schema at 153 columns**
(`HorioDB.designer.cs:28594`). WM models **11 of them** with the same meaning. 44 are dropped by
an existing decision and 74 belong to a named later phase — both fine. **33 have no owner
anywhere**: not in `Employee.cs`, not in a migration, not in a plan, not a §13 row, not on a
screen. Names (`KnownAs`, `MiddleName`, `Title`), HR identity (DOB, gender, nationality, NI
number), personal contact and address, **all six bank-detail columns**, the nine
employment-lifecycle dates, `WTDOptOut`, `ExternalId`.

### Three of them are defects in running code, not backlog

None appears in `PHASE-AUDIT.md` (checked A1–A4, B1–B7, D1–D17).

- **A terminated employee can still punch.** `FindByIdAsync`/`FindByCodeAsync` apply the caller's
  data scope and **no status filter** (`PeopleModule.cs:197-207`); neither punch path checks
  afterwards (`PunchService.cs:32, 69`); `AuthService` checks only `User.IsActive`
  (`AuthService.cs:76, 117, 131`) and nothing deactivates a user when their employee leaves. The
  inconsistency is visible inside one class — `GetRecentAsync:100` builds its visible set from
  `ListActiveAsync`, so **WM accepts a punch it will then never display.** Legacy gates every
  channel on `ActiveEmployeesView` / `.ActiveNotFired()` and pushes a device **delete** the moment
  someone is deactivated (`77.V5.23.0.0.sql:1523-1528`).
- **The employee-code uniqueness rule is not enforced by anything.** The check is case-insensitive
  (`PeopleModule.cs:84`) and `IX_Employees_Code` is a plain unique index on `varchar(32)`
  (`20260720080022_Initial.cs:85-90`) — case-**sensitive**. The comment at `:109` — *"The unique
  index is the real guarantee"* — is false. Two concurrent posts of `E1030` and `e1030` both
  insert, and every later `PUT` on either then 409s against the other, making both uneditable.
  `FindByCodeAsync:199` is case-sensitive too, so a punch for `e1030` against `E1030` is rejected
  as *"Unknown employee code"*.
- **`DepartmentId` is validated nowhere** — no FK, no index, no existence check, no site-agreement
  check (`20260720080022_Initial.cs:44`; `PeopleModule.cs:86-87, 98, 140`). Department is a **scope
  dimension**, so a cross-site department makes one person visible to two disjoint scopes. Legacy
  cannot have this bug — its department and location are independent columns with no relation to
  break. **WM created the relation and left it unenforced.**

### The one that changes a plan already written

**Employment is a date in legacy and an enum in WM.** `dbo.IsActiveEmployment(@dischargeDate,
@referenceDate)` (`76.V5.22.0.0.sql:25-38`) is evaluated at 20+ sites in that one script.
WM has `EmployeeStatus { Active, OnLeave, Terminated }` with no date (`Employee.cs:20-25`). So a
future-dated leaver cannot be recorded, and *"who was employed on 3 March?"* is unanswerable —
which **plan 002's clocking replay needs and does not currently ask for**. `OnLeave` has no legacy
counterpart at all; legacy's "on leave" is an *absence*, a separate dated subsystem.
*(Measured 2026-08-06 by a narrow dependency check before P1 was built — `TLW-PEOPLE-MODEL.md` §4.1a,
scope in `COVERAGE-AUDIT.md` §2a. `OnLeave`'s drop is confirmed; Absence itself remains unsurveyed.)*

### Ranked effect on plans

| Plan | Effect |
|---|---|
| **002** (`draft`) | **Amendment needed before approval** — the replay cannot determine who was employed on the day replayed. Take 007 P1 as a prerequisite, or accept that replays include leavers. |
| **001 P4/P5** (paused) | `Departments` nests in legacy (`ParentId`, `:55267`) and is flat in WM; `Locations` is flat in legacy and nested in WM. **WM put the hierarchy on the wrong axis.** Either add `Department.ParentId` in P4 or record the flatness as deliberate. Does **not** overturn `TLW-AUTHORIZATION-MODEL.md` C9 — the role TVF still does no expansion; the one expansion legacy has is on department, for absence approvals (`AbsenceRequests.cs:3039-3043`). |
| **003 P3** (approved, queued) | **Ordering constraint.** 007 P4 must land with or before it — 003 P3 fixes B5's hard-coded `departmentId: null`, which makes the unvalidated field live. |
| **005** (`draft`) | Not contradicted, but its evidence is weaker than stated: **six live permissions sit on `dbo.[User]`**, outside `Role` and `AccessControlEntry` — including a period-lock bypass (`DataLockChecker.cs:22-35`) and a second absence-approval scope. Legacy did not actually keep all access in one object. Conclusion unchanged; caveat owed. |
| **004** (`draft`) | Legacy has **24 per-tab *write* permissions** on the employee (`UpdateEmployeePermissions.cs:5-28`). WM has one `employees.manage` and a full-replace `PUT`. Once salary/bank land that is a grant of "rewrite everyone's bank account" — which is why 007 defers them to 004. |
| **006** | None. It surveyed no legacy and correctly says so. |

### Corrections applied in place

`COVERAGE-AUDIT.md` §2 (mark + a measured note + the queue — Absence is now the largest unsurveyed
bucket, and its priority rose); `ARCHITECTURE.md` §13 (one vague People row → **sixteen** checkable
rows; swipe capture and job role moved to `⚠️`); `TLW-AUTHORIZATION-MODEL.md` §1 (**19 tables / 66
columns, not 18 / 59** — plus a new §1A on the seven `[User]` permission booleans, and the fact
that 2FA / password history / email preferences are *separate tables*, not `[User]` columns), §10
(**fourteen** fail-opens, not twelve — `PersonnelService.CanViewEmployee:3151` and
`CanEditEmployee:3177` are a fifth copy of the filter, and #14 is the only one that fails open on a
**write**), §15 (closed); `SCREEN-TREE.md` (it listed Sites *and* Locations as separate org
dimensions 240 lines after stating they are one axis).

**Propose-only, not applied:** `ARCHITECTURE.md:171` says *"(TLW: `User.EmployeeId`, …)"*.
`dbo.[User]` has **no `EmployeeId` column** — legacy's link is `dbo.Employees.UserId`
(`AuthorizationService.GetUserByEmployeeId:788-800`). WM's direction is deliberate and better, but
§4A is a design section. Diff in `TLW-PEOPLE-MODEL.md` §10.

## Active plan

**003 — Enforcement gaps** (`003-enforcement-gaps.md`) — `in-progress`, approved 2026-08-04, and
**ordered ahead of the model work by the user**. A1 and A2 are defects in running code; the model
work corrects something no endpoint consults yet. Fix what is bleeding first. *(The user's 2026-08-04
ordering named "001 P3"; that portion is now 005 P4 and the ordering carries over unchanged.)*

**P1 passed review and merged 2026-08-05 as [#18](https://github.com/00008550/WM/pull/18)
(`3389525`)** — A1 is closed: the punch feed addresses scope groups instead of broadcasting, the
hub requires `attendance.view`, and a scope change re-groups the user's open sockets within the
session. §13's `⚠️ built but unscoped` row for the realtime feed is corrected accordingly.

**P2 was split into P2a + P2b on 2026-08-05 at the user's direction** — same scope, two branches.
The plan now has 5 portions; the approval as given still covers it.

**P2a passed review and merged 2026-08-05 as [#21](https://github.com/00008550/WM/pull/21)
(`e948481`)** — A2 is closed: an endpoint mapped without `RequireAuthorization` now answers 401, the
anonymous surface is exactly four transports (`/api/auth/login`, `/refresh`, `/logout`, `/health`) and
a test asserts that list against the composed host, so adding a fifth is a decision with a reviewer
attached. *(Plan 006 P2 adds `/health/ready` and will trip that test on purpose.)*

**CI landed 2026-08-05 as [#22](https://github.com/00008550/WM/pull/22) (`18ca773`)** —
`.github/workflows/ci.yml` builds and tests the solution and builds the portal on every push and PR
to `master`. **Branch protection is not enabled**, so nothing enforces it yet; raised as 006 open
question 5.

**P2b passed review 2026-08-11 and merged as [#57](https://github.com/00008550/WM/pull/57) (`1462c0f`)** — A2 is
now fully closed: `POST /api/employees` refuses a destination outside the caller's scope and
`PUT /api/employees/{id}` checks the post-image as well as the pre-image, so a manager can no longer
file someone where they will not then be able to see them. It reuses `EffectiveDataScope.CanSee`
rather than inventing a write-scope model, as the measurement required, and carries the named
`CanEditOwnRecord` seam for 005 P4. First People test project; the reviewer confirmed by mutation
that deleting both call sites fails 4 of its 15 tests.

**Both of that review's low findings were then closed on the same branch, and re-reviewed
2026-08-11 (round 2).** They are recorded here as *closed*, not deferred, because a later portion
would otherwise inherit them. The second commit is **test-only — no product code changed** — and
each half was verified by re-running the exact mutation that went undetected the first time:

- **The `POST` ordering is now pinned.** `A_create_outside_the_callers_scope_is_refused_before_the_code_is_probed`
  posts an already-taken badge number at an out-of-scope site, where both refusals are available;
  demoting the scope check below the code probe now answers `Conflict` where the test demands
  `Forbidden`. Previously that mutation failed nothing.
- **The permission on every endpoint is now pinned, not merely its presence.**
  `EndpointAuthorizationInventoryTests.Every_authorized_transport_requires_the_permission_we_chose_for_it`
  compares all **28** non-anonymous transports against a hardcoded table, keyed by method so `GET`
  and `PUT` on one path stay two decisions. Downgrading `POST`/`PUT /api/employees` to
  `employees.view` now fails it **by name** (`POST /api/employees -> employees.manage` expected,
  `employees.view` actual) instead of surfacing as a phantom scope defect in the People project;
  moving `GET /api/sites` off `employees.view` — which previously failed nothing anywhere — fails
  it too, and it is the only test that catches that. The table is complete: a new authorized
  endpoint fails until it is named, like `DeliberatelyAnonymous` above it.

The **409 enumeration oracle stays open** and both the test and the plan say so: the uniqueness
probe is itself unscoped, so any `employees.manage` holder can still ask about any badge number in
the estate by naming a site they *can* write to. Scoping the probe is plan 007's. Suite is **166**,
up from 164 by exactly these two tests.

**Next portion:** **003 P3 — scoped site and department lists** (lane A, and see its ⛔ blocker
below), or **006 P1 — demo data leaves the product** (lane B, but constrained: it must land after
005 P3). The user pulled 006 P2 and P3 ahead of 003 P2b (006 decision 1) because they fix defects in
shipped code; both have since merged, so lane B's security work is done and the argument for staying
in it has run out.

**Then 003 P2b — scope the employee writes.** It was to wait on a measurement of whether legacy's
read and write scope are distinct decisions. **They are not** — same TVF, one carve-out — so P2b
is cleared: reuse `WithinScope`, do not invent a write-scope model, and leave a named
`CanEditOwnRecord` seam for legacy's `CanModifySelf`. **The user has since adopted
`CanEditOwnRecord`** (2026-08-05), so that seam is no longer speculative: 005 P1 adds the group
flag and 005 P4 wires it to this predicate. P2b must therefore leave a real, named seam rather
than hard-coding today's behaviour, and should land **before** 005 P4.

**001 — Compositional data scope** (`001-compositional-data-scope.md`) — `in-progress`, paused
after P2. Approved by user 2026-08-03, all 5 portions. **P3 is now ⏹ superseded by 005 P4** — the
union it implemented cannot exist under one membership, and "resolve one group" and "honour the
composed rule" are the same edit to `DataScopeResolver.cs:25-70`. P3 was amended in place on
2026-08-05 with a table of where each of its parts went. **P4 and P5 stand, and move behind 005**
in the queue. P4 was also amended earlier — legacy's `ByDepartments` and `ByEmployees` are mutually
exclusive on save (`UpdateRole:872-883`), so P4's "intersecting with the other dimensions" is a WM
improvement with no legacy precedent, which the builder needs to know before hunting for one. P5
additionally inherits the drop of the legacy single-kind scope columns, which 005 deliberately
leaves alone because they are still the group editor's contract.

**⚠️ The baseline in this file was wrong, and 005 P3 is where it gets fixed.** It said *"admin sees
40 employees, manager sees 16"*. The seeded manager belongs to **no security group** —
`DemoUserSeeder.cs:37` assigns a role and never a group — so today they resolve to `Self` and see
**1**, not 16. Admin sees 40 via the "All employees" system group (`IdentitySeeder.cs:82-92`).
005 P3 gives the demo manager a real `Site managers` group and **re-measures the number against
the running stack**; do not copy 16 forward into any test.

## The demo deployment is planned — 006, drafted 2026-08-05

[`006-public-demo-deployment.md`](./006-public-demo-deployment.md) — `in-progress`, 7 portions,
**approved by the user 2026-08-06**. **P2 and P3 both passed review 2026-08-06 and are open as
[#40](https://github.com/00008550/WM/pull/40) and [#47](https://github.com/00008550/WM/pull/47)**,
which closes **all four** of the security facts below: the image no longer ships a signing key or
an administrator password and refuses to boot outside Development on one this repository
publishes (P2); `/health` is real liveness with a separate readiness check (P2); forwarded headers
and rate limiting both exist, as one mechanism (P3); and lockout is configuration with the shipped
values as defaults (P3). A publicly reachable demo on
Oracle Cloud Always Free (arm64), kept current by CD on every merge to `master`. **No legacy was surveyed and none should be** — TLW has no analogue; the plan says so in
its *Legacy sources surveyed* line. Everything in it was measured against WM's own tree.

Four things it found while measuring, all in shipped code:

- **The image ships a working JWT signing key and an admin password.** `appsettings.json:17,25`
  are baked into `wm-api` (`.dockerignore:12` excludes only the *Development* file), and nothing
  refuses to boot on them. `docker-compose.prod.yml:99,101` guards this for people who use that
  compose file; the image is fail-open. This also makes §13A's *"no hard-coded administrator
  password in a production build"* false — **corrected in this pass**.
- **`/health` cannot fail** — `Program.cs:46` registers no checks, so it answers `Healthy` with the
  database down, and the container `HEALTHCHECK` believes it. It is also unreachable from outside
  (`nginx.conf:32,42` proxy only `/api/` and `/hubs/`), so no smoke check can use it as-is.
- **No rate limiting and no `UseForwardedHeaders`** anywhere in `src/**`. The second makes the
  first a trap rather than an omission: without it every request looks like it came from the nginx
  container, so an IP-keyed limiter would bucket the whole internet together.
  **Closed by P3 ([#47](https://github.com/00008550/WM/pull/47))** — both ship as one mechanism,
  and the review measured that an *empty* trust list makes the framework skip its trust check
  altogether, so the host now refuses to compose on one.
- **Published credentials plus a five-strike lockout** (`AuthService.cs:17-18,36-41`, `const`, not
  configuration) means the demo locks itself out of itself on day one.
  **Closed by P3** — `AccountLockoutOptions`, defaults unchanged at 5 / 15 min, validated at
  composition, with no value that disables lockout.

Its three code portions (demo data out of the product; refuse to boot on the shipped dev secrets;
the public edge) are worth building **whether or not a demo ever exists**, and are independent of
the access model.

## Queue
<!-- OVERWRITE-ONLY: plan rows track current status; correct them in place, do not append. -->

| Plan | Title | Portions | Status |
|---|---|---|---|
| 003 | Enforcement gaps found by the phase audit | 5 (P1, P2a, **P2b done — [#57](https://github.com/00008550/WM/pull/57)**; **P3 next**, and it now carries a ⛔ blocker) | **in-progress — active** |
| 005 | One object, one membership (the Identity refactor option A requires) | 6 | **draft — awaiting approval** |
| 006 | A public demo, kept current by CI/CD | 7 (**P2 [#40](https://github.com/00008550/WM/pull/40) and P3 [#47](https://github.com/00008550/WM/pull/47) both merged** — `5406575`, `1240dbc`; P1 next in lane B, after 005 P3) | **in-progress — approved 2026-08-06** |
| 001 | Compositional data scope (the model half of Phase 1b) | 5 (P1–P2 done, **P3 ⏹ superseded by 005 P4**) | in-progress, paused after P2 |
| **007** | **The person record: employment as a date, and three defects under it** | 5 (**P1 merged [#61](https://github.com/00008550/WM/pull/61)** `ead3bfc`; **P2 next** — the punch boundary, whose defect was confirmed live 2026-08-18) | **in-progress — approved by the user 2026-08-06, all 5 portions** |
| 004 | Screen-level rights (the second half of Phase 1b) | 4 | draft — §4 now decided; **needs 005 to land first**; **007 adds field-group write rights to its scope** |
| 002 | The Clocking daily aggregate (Phase 2 prerequisite) | 5 | **draft — blocked on a design decision only.** The 007 P1 half cleared when it merged (`ead3bfc`); a replay can now ask who was employed on the day replayed. **010 P1 is its other prerequisite** and needs no approval from this plan. P3 was also stale: the swipe→day rule is **five branches plus a master override**, not three (`010`, and `008:253-256` already said five) |
| 008 | A day has a place — per-site time zones | 5 | **draft — awaiting approval.** Direction confirmed by the user 2026-08-18 (per-site with inheritance). P3 sequences after 007 P1, which has merged |
| **009** | **What the running app does — the defects the 2026-08-18 audit found** | **8** | **draft — awaiting approval.** Claims independently verified; P1–P4 independent of 005 and 007. Open decision: **003 P3 already owns `phone` by name** |
| **010** | **The Daily Browser** | **5** | **draft — awaiting approval.** **P1 is the five-column daily-template subset** — deliverable before 002 is approved, and 002 P3's only prerequisite. P3–P5 would ship a read-only browser (open question 3) |
| **011** | **Production readiness: the machinery this repository claims and does not have** | **8** (**P2 merged [#67](https://github.com/00008550/WM/pull/67); P5 merged [#68](https://github.com/00008550/WM/pull/68)**; six remain: P1 P3 P4 P6 P7 P8) | **in-progress — approved by the user 2026-08-29, all 8 portions.** Lane D. Surveys **WM, not legacy**. Build order **P2 → P5 → P1 → P3 → P6 → P4 → P7 → P8**. P7 needs ADR 0001 first; P8 after 007 P2 |
| **012** | **The rules behind the settings** (`SoftwareMainOptions`) | 5 | **draft — awaiting approval.** 47 of 227 global settings are rules-as-config; found a 2nd settings singleton `dbo.Calculations`. Open Q: effective-dating |
| **013** | **The day-calculation core** (the hours engine) | 5 | **draft — awaiting approval.** First read of the 866 KB engine. Phase 2 is a **~3× underestimate**; **MVC = 8 stages**, P1-sized. Feeds 002 |
| **014** | **Payroll plugins / the generic export builder** | *(plan not landed — see note)* | **survey merged [#71]; plan file was not committed.** The builder hypothesis **holds** (44 short files, median 216 lines). Plan needs re-generating |
| **015** | **Absence & entitlement** | *(plan not landed — see note)* | **survey merged [#72]; the plan file it references (`015-absence-and-entitlement.md`) was not committed.** Needs re-generating. Absence sits on the Clocking; two balance engines |
| **016** | **Activities & cost centres** | 7 | **draft — awaiting approval.** Cost-centre allocation is a **second counter run** on the Clocking → constrains 002. Phase 7 |
| **017** | **Scheduling foundation** | 4 | **draft — awaiting approval.** Foundation only; auto-planning/open-shift-eligibility/WTD are WM-new, deferred. Produces the planned shift 013 consumes |
| **018** | **The audit trail becomes real** | 4 | **draft — awaiting approval.** `ClockingsLog` is a full-row snapshot; legacy audit is a 4-mechanism patchwork. Depends on 011 P7 (outbox) + 009 P4 (harness) |
| **019** | **Documents & e-signature** | 6 | **draft — awaiting approval.** Every byte is SQL `VarBinary(MAX)` → object storage is the Improve. E-sign is real; onboarding-checklist is WM-new |
| **020** | **Expenses & mileage** | 6 | **draft — awaiting approval.** Banded mileage rates are data-driven. Approval shape **differs from absence** — shared-engine decision (open Q1) |
| **021** | **Visitors** | 6 | **draft — awaiting approval.** Survives no-devices. **Feeds the fire muster roll** → a fragment is a phase-6b dependency, not phase-8-isolated |

**Ordering, decided by the user 2026-08-04:** 003 P1 and P2 run before 001 P3. They are defects in
running code rather than missing capability, and they are independent of 001 — 003 touches the API
host and the People endpoints, 001 touches the resolver and the filter.
**Reinforced 2026-08-05:** 003 P2a and P2b are correct under either access model, so they can
proceed while the §4 question is open. 001 P3 cannot — and it no longer exists (005 P4).

**Full sequence, updated 2026-08-05 when 006 was drafted. It is now two lanes, not one line** —
006 is a platform plan and only one of its portions touches anything the model work touches.

**Lane A — the access model (unchanged):**
**003 P2b → 005 P1…P5 → 003 P3 → 001 P4 → 001 P5 → 005 P6 → 004.**
003 P2b lands before 005 P4 because it is a live defect and because it carries the
`CanEditOwnRecord` seam that 005 P4 wires to the real flag — built afterwards it would be written
against a type that is moving under it. **005 P1–P5 stay contiguous:** between P3 and P4 the system
is half-refactored (the group carries permissions, the resolver still walks the old union), which
is not a state to park in. **005 P6 is the only irreversible portion and is deliberately detached.**

**Lane B — the demo platform:**
**006 P2 → 006 P3 → 006 P1 → 006 P4 → 006 P5 → 006 P6 → 006 P7.**

**006 runs alongside lane A, not ahead of it** — 003 P2b is a live defect and a demo is a nicety;
fix what is bleeding first. But the lanes barely touch:

- **One hard constraint between them: 006 P1 must land after 005 P3**, because both rewrite
  `DemoUserSeeder.cs` — 005 P3 gives the demo manager a real `Site managers` group, 006 P1 moves
  the file out of the shipping assembly. In this order the seeder moves once, with its final
  content. The reverse also works and ships the demo weeks sooner, at the cost of re-targeting
  005 P3's *Touches* line; that is the user's call.
- **006 P2 and P3 may be pulled forward at any time** — including ahead of 003 P2b. They are
  security fixes to shipped code (a published signing key that boots, no rate limiting, a health
  check that cannot fail) and touch no part of the access model. The only file they share with 005
  is `IdentityModule.RegisterServices` — a guard at `:41-51` versus `AddAuthorization` at `:67-78`.
- **006 P4–P7 touch `src/**` only in the four Dockerfiles.** They are the portions with no unit
  test and a smoke check instead, and they can proceed whenever lane A is waiting on a decision.

**Lane C — the person record (new, 2026-08-06, `draft`):**
**007 P1 → 007 P2**, and independently **007 P3**, **007 P4**, **007 P5**.

Lane C is nearly orthogonal to A and B — it touches `Domain/Employee.cs`, `PeopleDbContext`, People
migrations and `PunchService`, none of which lane A or B opens. Three notes:

- **007 P4 must land with or before 003 P3** (lane A). Both touch the employee write path, and
  003 P3 makes the unvalidated `DepartmentId` reachable from the SPA.
- **007 P1 and P2 are the defect fixes** — a terminated employee can punch today. By the standing
  rule ("fix what is bleeding first") they outrank everything in lane B and sit alongside 003 P2b.
- **007 P1 unblocks 002.** If plan 002 is approved before 007 P1 lands, its replay has no way to
  know who was employed on the day it replays.

**Lane D — the platform's production machinery (new, 2026-08-29, `approved`):**
[`011-production-readiness.md`](./011-production-readiness.md) — 8 portions, from an external
senior-engineer audit the user commissioned (idempotency, transactions, jobs, caching,
observability, testability, migrations). **Every finding was re-measured before being planned:** two
were materially wrong, one was already an approved portion, one was understated.

It is the most orthogonal lane yet — six of eight portions touch `README.md`, `ci.yml`,
`deploy/*.yml`, `Program.cs`, `SharedKernel` and a new architecture-test project, which no other
lane opens.

### Approved 2026-08-29, all 8 portions. Build order: **P2 → P5 → P1 → P3 → P6 → P4 → P7 → P8**

**P5 is the only live defect in the plan** — two managers editing one employee, last write wins
silently — and it is the one place WM is measurably **worse than the product it replaces**. It is
second rather than first, for one concrete reason: **P5 adds a `version` field that must round-trip
through the SPA**, which is the exact class of bug 009 P3 exists to fix (`phone`, silently nulled by
a full-replace `PUT`). If the SPA drops `version`, either every save 409s or — worse — the check is
**silently bypassed and P5 ships a token that never fires**. A green backend suite would prove
nothing. So P2 (the spec harness) goes first. **Escape hatch:** if P2 cannot be made reliable in a
timebox, revert it and build P5 anyway with a *labelled manual* SPA check, as 007 P1 did for its
migration.

**Constraints, all recorded in the plan's portions:**

- **011 P2 → before 009 P3.** 009 P3 writes `employees.component.spec.ts`, and there are **zero**
  specs today, so `ci.yml:105-116` flips from `::warning` to a headless-Chrome `ng test` that has
  **never run in this repository** — inside a PR whose subject is a phone field. Debugging karma
  belongs in its own portion.
- **011 P5 now runs *before* 009 P3, not after** — changed by the approval. Gating the only live
  defect on approving a `draft` plan was the wrong trade. **P5 therefore carries 009 P3's
  `PortalEditBody` amendment** (`LeaverRecordEndpointTests.cs:410-438`) and re-verifies 009 P3's
  *Touches* citations in its own commit, since it moves `employees.component.ts:291` and
  `PeopleModule.cs:75-88`.
- **011 P8 → after 007 P2.** Both rewrite `PunchService.RecordAsync`, and 007 P2 is an approved
  bleeding-defect fix. **They are complementary, not alternatives:** 007 P2's seconds-wide window
  suppresses double-click noise; P8's client key answers the offline queue flushed hours later,
  which no window can catch and which invariant 3 makes the *normal* case.
- **011 P7 → after 009 P4** (the Postgres harness), **and after ADR 0001**. An outbox is a claim
  about transaction boundaries and EF's in-memory provider has none; unlike P5, a manual run is
  **not** an acceptable substitute here.

### Three rulings on 2026-08-29 that changed the plan

- **D1 — the outbox is a small one in `SharedKernel`, not MassTransit in the API.** **P7 unblocked**;
  `docs/adr/0001-outbox-in-sharedkernel.md` is written before any code. Consequence: **§2:93 is now
  wrong** (it promises the outbox via MassTransit); the diff is proposed in the plan's open
  question 1, not applied, because §2 is a design section.
- **D2 — Redis gets the SignalR backplane job now**, overruling the survey's "premature, one
  container per customer" recommendation. **P4 was rewritten, not edited**: it was a *defect fix*
  and is now *added capability*, because the ruling **voids the defect** — a hard `depends_on` on
  Redis is correct once the API connects to it. P4 now also owns two consequences the original did
  not contain: what a Redis outage does to a hub that depends on it (answer: degrade the feed, never
  fail a connection or a request), and the fact that **a backplane silently breaks live scope
  revocation** — `UserScopeChangedAsync:115` scans an in-process registry, so a scope edit on one
  instance re-groups only that instance's sockets, downgrading 003 P1's ✅. **The code predicted this
  in a comment** (`AttendanceConnectionRegistry.cs:19-21`). The fix is to distribute the
  *notification*, not the registry.
- **D4 — P5's 409 carries a message, not the current record.** *"Someone else changed this record —
  reload and try again"*; no current-record payload, so **no client-side diff and no "keep mine /
  keep theirs"**. It closes the defect at the lowest cost — the bleeding thing is *silence*, not
  friction — and the richer version stays **purely additive** (a field in the body; the token and
  the refusal do not change). Recorded as a deliberate omission so it reads later as a first step
  rather than an oversight. **This still puts WM ahead of legacy**, which detects the conflict
  (`UpdateCheck.Always` on 148/153 `dbo.Employees` columns) and then throws a
  `ChangeConflictException` that **nothing in `Logic/` catches**. Detection without handling is a
  stack trace, not a feature.
- **D3 — rollback becomes real tooling**, and it is **not** part of 011. Registered below by name.
  The ruling is recorded against `ARCHITECTURE.md` §13A:739-740 so the contradiction stops being
  live.

**One thing measured rather than assumed, because it decided the order.** Does P5 need the Postgres
harness? **No.** EF Core 10's InMemory provider *does* enforce an explicit concurrency token
(`DbUpdateConcurrencyException`, A's write survives), and the mutation with the token removed
reproduces WM's current bug exactly — *"B silently overwrote A"*. **But only for an explicit mapped
column:** Npgsql's `xmin` is never populated in memory, so an `xmin` token would make every
concurrency test pass **vacuously**. That is now a design constraint on P5 rather than a trap for
the builder. The migration's SQL still wants 009 P4.

**What it found that the records did not have.** Legacy runs full-column optimistic concurrency on
**~99.5%** of its schema (356 `UpdateCheck.Never` of 8,173 columns; `dbo.Employees` checked on
148/153) and handles `ChangeConflictException` **nowhere** — so TLW *detects* a concurrent edit and
crashes, while WM does not detect it at all and silently loses the loser's write. And
`dbo.Clockings` is **249/249 `Never`**: legacy deliberately exempts the wholesale-recalculated
aggregate, which is a free argument for plan **002**'s replay design. Six §13 rows added; the
CLAUDE.md test-project count corrected (**four**, not two).

**Note on the Postgres harness.** The task brief asked whether the standing gap belongs in 011. It
does **not** — **009 P4 already owns it**, written and numbered. 011 depends on it and does not
duplicate it.

**Next portion:** **003 P3 — scoped site and department lists** (lane A; P2b merged 2026-08-11 as
[#57](https://github.com/00008550/WM/pull/57), `1462c0f`), with **006 P1** the lane B alternative
once 005 P3 has landed. In lane C, **007 P1 merged 2026-08-17 as
[#61](https://github.com/00008550/WM/pull/61) (`ead3bfc`)**; **007 P2 — the punch boundary fails
closed** is the next candidate there, and a manual test on 2026-08-18 confirmed the defect it owns
is live (a seven-week-old leaver was accepted into the punch feed, then hidden from presence).
006 P2 and P3 have merged (`5406575`, `1240dbc`).

> **Audit of the running app against these records, 2026-08-18.** Every ✅/⚠️ claim reachable
> through the UI was exercised. The records held on the big things — the 007 P1 behaviour, the
> terminated-punch ⚠️, user↔employee linking — and were wrong in four places, now corrected in
> `ARCHITECTURE.md` §13: user management claimed a **search** the screen does not have; the
> **effective-visibility panel is stale** and contradicts its own comment; **punch-code
> normalisation lives in the SPA**, not the server (invariant 2); and this file still described
> #57 and #61 as open. A fifth is an environment fact, not a defect: **this dev database has
> drifted from the seeded baseline** — `Production Plant managers` exists in it but in no seeder —
> so no headcount measured on that box is reproducible, which is a second reason not to copy any
> number from it into a test.

**007 P1 — employment is a date, not an enum — review passed 2026-08-17 (round 3), open as
[#61](https://github.com/00008550/WM/pull/61) against `master`. Squash-merge it:** `d132c65` is a
work-in-progress checkpoint whose "do not merge" message is stale and vanishes under a squash.
`Employee` carries
`EmployedFrom` / `EmployedUntil` (last day **inclusive**) / `IsSuspended`; `EmployeeStatus` is
derived at a reference date and **never stored**, so it cannot disagree with the dates the way
`dbo.ActiveEmployeesView` does. The leaver record lands with it — a `LeavingReason` lookup (**seeded
empty**; customer vocabulary), `LeavingReasonId` and `LeaverComments(500)` — and clearing
`EmployedUntil` clears both, as `SetEmployeesActive:151-173` does. `IEmployeeDirectory` carries the
window and an `IsEmployedOn`, so `ListActiveAsync` becomes `ListEmployedOnAsync(date)` and no consumer
reads People's tables. **002's blocker is cleared**: "who was employed on 3 March?" now has an answer,
pinned by `LeaverRecordEndpointTests.The_list_derives_the_status_at_the_date_it_is_asked_about` —
in memory, over the endpoint. No running stack was involved.

Two things about it worth carrying forward:

- **The migration was executed against real Postgres, and there is still no harness.** 001 P2's
  identical promise went unwritten because none exists, so P1 did both halves and labels them
  differently: seven xUnit tests read the migration's *operations* (round-trip, backfill-before-drop,
  `Down` drops everything `Up` adds) and execute **no SQL**; a **one-off manual** up/down run on
  `postgres:17-alpine` covered the SQL itself, with `Pacific/Kiritimati` set on the **database** —
  not the session — so that EF's own migrator connection inherited UTC+14 and the
  `AT TIME ZONE 'UTC'` cast was exercised where it actually runs. **A session-level `SET` proves
  nothing here**: it never reaches that connection, so it passes with or without the cast. Shown by
  falsification — guard removed *with* the database setting, the backfill landed a day out; guard
  removed *without* it, the dates were right. Nothing re-runs the manual half. **A real Postgres test
  harness is now the clearest gap in this repository's testing** — Docker exists on the dev box and on
  GitHub's runners, so it is buildable; it wants a portion, not a smuggled-in dependency.
- **`LeavingReason` has no maintenance surface.** Read-only endpoint, no create/rename/retire, no
  screen — so the table can only be filled by SQL today. Deliberate (see the plan's As-built note),
  and it needs a permission decision before someone improvises one.

**⛔ 003 P3 now carries a blocker, added by the P2b review.** P2b turned the SPA's unconditional
`departmentId: null` (`employees.component.ts:300`) from silent data loss into a **403 on every save
for a department-scoped caller**. Not reachable on a default install — the only seeded group is the
`All` one — and strictly better than the wipe it replaces, which is why P2b shipped first. But P3
owns the fix, and **its round-trip test must run as a `Departments`-scoped caller**: an
admin-scoped round-trip passes over the live defect, because `All` short-circuits the scope check.
Full note in `003-enforcement-gaps.md` under P3. This stacks with the existing 007 P4 constraint
below rather than replacing it.

**006 P2's carry-over to P3 is closed.** The readiness endpoint P2 added is anonymous, uncached and
costs roughly three database round-trips per module per request; P3 chose the limiter over a cache,
so `/api/health/ready` now shares the anonymous bucket with the three sign-in transports. A cache
would have needed invalidating on migration state and would have made the smoke check's build
identity stale, while the real callers — the container `HEALTHCHECK` at 4/min from loopback and the
6-hourly smoke run — sit an order of magnitude inside the 30/min default.

Suggested but not yet written, from the schema sweep (`docs/TLW-SCHEMA-SWEEP.md`) and the People/HR
survey (`docs/TLW-PEOPLE-MODEL.md`) — **names, not numbers**, per the convention below:
**"Tariffs and the calculation core"** — tariffs, employee contracts and their **two disagreeing
resolution mechanisms** (date-blind `Employees.ContractId` vs dated `EmployeeAssignedContracts`,
`CalculatedEmployeesContractsForPeriod.cs:37-43`), calculation settings, the counter formula
language;
**"Per-install configuration"** — `SoftwareMainOptions` (227 cols);
**"HR records and documents"** — the 8 near-identical document-category tables (16 tables / 140
columns expressing one idea eight times), bank details and salary. **Blocked on 004** — legacy
guards these behind 24 per-tab write permissions and WM has one `employees.manage`;
**"Rollback that actually rolls back"** — *added 2026-08-29 by user ruling D3, and the only one of
these names that comes from a **measured contradiction in a shipped promise** rather than from the
schema.* `ARCHITECTURE.md` §13A:739 promises *"instant rollback — repin `WM_VERSION`"*; §13A:740
promises *"schema upgrades itself"* via migrate-on-start (`Program.cs:96-98`, every environment).
**Repinning does not un-migrate.** Its concrete test case already exists and is merged: 007 P1's
`20260814080315_EmploymentWindowAndLeaverRecord` does `DROP COLUMN "Status"`, so repinning to the
previous image leaves the old build querying a column that is gone. The user chose the **tooling**
answer over narrowing the promise, so the plan owes an expand/contract discipline with a mechanical
check, a rollback path that includes the database, and its relationship to the per-customer
backup/restore §13A:765 already lists as a deliverable. **Plan 011 measured this and deliberately
does not absorb it** — see 011's *Out of scope*. It is the strongest candidate of the four for being
written next, because unlike the others it is a live promise to customers rather than unbuilt
surface.
*(These have twice been renumbered while carrying reserved numbers they had not earned — 004/005,
then 006/007. Both of those numbers are now real written files. They keep names until someone
writes them.)*

## Shipped
<!-- APPEND-ONLY: one row per merged PR, never edited once written. -->

| Plan | Portion | PR | Status |
|---|---|---|---|
| — | workflow + doc corrections | [#10](https://github.com/00008550/WM/pull/10) | ✅ merged to master |
| 001 | P1 — compositional scope model | [#11](https://github.com/00008550/WM/pull/11) | ⚠️ merged to the wrong branch — superseded by #12 |
| 001 | P1 — compositional scope model | [#12](https://github.com/00008550/WM/pull/12) | ✅ merged to master |
| — | clocking survey + schema sweep + plan 002 | [#13](https://github.com/00008550/WM/pull/13) | ✅ merged to master |
| — | .NET 10 LTS, EF Core 10, Angular 22 | [#14](https://github.com/00008550/WM/pull/14) | ✅ merged to master |
| — | coverage audit | [#15](https://github.com/00008550/WM/pull/15) | ✅ merged to master |
| 001 | P2 — persist composed scope + migrate | [#16](https://github.com/00008550/WM/pull/16) | ✅ merged to master (`a9fdcc3`) |
| — | phase audit + plan 003 + doc corrections | [#17](https://github.com/00008550/WM/pull/17) | ✅ merged to master (`2557921`) |
| 003 | P1 — scope the realtime punch feed | [#18](https://github.com/00008550/WM/pull/18) | ✅ merged to master (`3389525`) |
| — | authorization survey + §4 rewrite + the one-membership ruling | [#19](https://github.com/00008550/WM/pull/19) | ✅ merged to master (`0613836`) |
| — | plan 005 + 001 P3 superseded + §4A corrections | [#20](https://github.com/00008550/WM/pull/20) | ✅ merged to master (`3f05071`) |
| 003 | P2a — fail closed by default (`FallbackPolicy`) | [#21](https://github.com/00008550/WM/pull/21) | ✅ merged to master (`e948481`) |
| — | CI: build + test gate on `master` | [#22](https://github.com/00008550/WM/pull/22) | ✅ merged to master (`18ca773`) |
| — | 006 measured against the tree; arm64 and librdkafka answered | [#39](https://github.com/00008550/WM/pull/39) | ✅ merged to master (`1a7e292`) |
| 006 | P2 — a host that can be verified from outside | [#40](https://github.com/00008550/WM/pull/40) | ✅ merged to master (`5406575`) |
| 006 | P3 — the public edge: real client IPs, rate limits, configurable lockout | [#47](https://github.com/00008550/WM/pull/47) | ✅ merged to master (`1240dbc`) |
| — | the person record measured: 153 columns, three defects, plan 007 | [#48](https://github.com/00008550/WM/pull/48) | ✅ merged to master (`9cf8925`) |
| — | survey just-in-time; the `OnLeave` assumption measured | [#49](https://github.com/00008550/WM/pull/49) | ✅ merged to master (`8b005c2`) |
| — | the WM presence mark, used as the favicon | [#55](https://github.com/00008550/WM/pull/55) | ✅ merged to master (`b3f91d2`) |
| 003 | P2b — scope the employee writes | [#57](https://github.com/00008550/WM/pull/57) | ✅ merged to master (`1462c0f`) |
| 007 | P1 — employment is a date, not an enum | [#61](https://github.com/00008550/WM/pull/61) | ✅ merged to master (`ead3bfc`) — review passed round 3 (F1 data-loss fix + three `STATE.md` claims), squash-merged |
| — | provenance audit of 007 P1 against legacy | [#59](https://github.com/00008550/WM/pull/59) | ✅ merged to master (`6eb52a1`) |
| — | legacy time survey + plan 008 | [#60](https://github.com/00008550/WM/pull/60) | ✅ merged to master (`211e628`) |
| 011 | P2 — the first frontend spec, and CI stops warning | [#67](https://github.com/00008550/WM/pull/67) | ✅ merged to master — 13 specs, both mutations red **by name**, CI green (`Angular build` 28 s → 51 s) |

## In flight
<!-- OVERWRITE-ONLY. This section is live state: it must list ONLY PRs that are open right now.
     When a PR merges, delete its line here and let the Shipped table (append-only) carry it.
     Verified by scripts/check-plan-state.sh. Do NOT append merged PRs to the lead below. -->

**Nothing is in flight.** The full legacy survey programme (plans 012–021) and the re-audits of
the built areas (authorization, People) are **merged**, and the consolidated documentation pass
applying their findings to `ARCHITECTURE.md`, `COVERAGE-AUDIT.md` and `TLW-SCHEMA-SWEEP.md` landed
2026-09-01. Building is still paused for the survey programme; **011 is the only approved build
plan with portions left** (P1, P3, P4, P6, P7, P8). Run `scripts/check-plan-state.sh` before
trusting this line — if it names an open PR that has merged, it is stale.

### Merged — review notes kept because they still bind later portions
<!-- APPEND-ONLY, and deliberately exempt from the in-flight check: these PRs ARE merged; their
     review notes are retained here because 006 P5 and later portions still depend on them. -->

*(Both of these were listed here as "open against `master`" long after they merged; corrected
2026-08-11. They are in the Shipped table with their merge commits. The notes stay because 006 P5
and the IPv6 caveat are still owed.)*

**[#47](https://github.com/00008550/WM/pull/47) — 006 P3, merged to `master` (`1240dbc`).** Review passed
2026-08-06 with no findings. It closes the last two of the four security facts 006 measured in
shipped code: there was **no rate limiting and no `UseForwardedHeaders`** anywhere in `src/**`, and
the second made the first a trap rather than an omission — an IP-keyed limiter added on its own
would have bucketed the entire internet together. Both now ship as one mechanism in
`PublicEdge.cs`. Lockout thresholds become `AccountLockoutOptions` with the shipped 5 / 15 min as
defaults, so nothing changes for existing installs.

**The subtlest thing in it, and it is right:** `ForwardedHeadersMiddleware` **skips its
chain-of-trust check entirely when both known lists are empty**, so an empty trust list means
"trust everybody", not "trust nobody". Measured against the framework, not reasoned about. The host
therefore refuses to compose on an empty list. The trust boundary is enforced rather than merely
configured — adding `0.0.0.0/0` to the list fails two tests, including
`An_untrusted_caller_cannot_choose_its_own_address`.

Limits land on `/api/auth/login`, `/refresh`, `/logout` **per endpoint rather than on the group**
(`/me` shares it), plus `/api/health/ready` — which closes P2's recorded carry-over below.
`/health`, the hub and authenticated traffic stay unlimited, asserted by a second inventory test in
the shape of `EndpointAuthorizationInventoryTests`. **The anonymous surface stays at five and that
test needed no edit.**

**Carried forward from this review:** IPv6 is bucketed per address rather than per /64 (acceptable
until an IPv6-reachable edge exists); **P5 should narrow the `172.16.0.0/12` default** to the demo
box's real bridge subnet; and the container pipeline is still unexercised because Docker and
Postgres were both down.

**[#40](https://github.com/00008550/WM/pull/40) — 006 P2, merged to `master` (`5406575`).** Review passed
2026-08-06 with no findings. It closes the worst defect the repo was carrying: `appsettings.json`
shipped a working `Jwt:SigningKey` and a `Bootstrap:AdminPassword` inside the `wm-api` image and
nothing refused to boot on them, so anyone holding this repo or an image could mint administrator
tokens against an install that had not overridden the key. Both values now live in
`appsettings.Development.json` (not in the image), and outside Development the host refuses to
start on a missing, under-32-byte or repository-published key — verified by mutation, and against
the real host rather than only the test host. `/health` stays liveness; `/api/health/ready` is new
and is the **fifth** anonymous transport, added deliberately through
`EndpointAuthorizationInventoryTests`.

`master` is at `b3f91d2`.

`docs/scope-model-corrections` is fully contained in `master` and can be deleted. See the
stacking note in `CLAUDE.md` → Git for why #11 went astray.

## Conventions

- **Status:** `draft` → `approved` (user signed off) → `in-progress` → `in-review` → `merged`.
- A plan is only executable when its status is `approved`. `wm-builder` refuses `draft`.
- **A plan number is claimed when the plan *file* is written — never when a plan is merely
  suggested. Suggestions get names, not numbers.** Two schema-sweep ideas held reserved numbers
  they had not earned and were renumbered twice as real plans overtook them (004/005 → 006/007 →
  names). Every renumbering is an opportunity for a stale cross-reference in another document, and
  a number that points at nothing is worse than no number. If it is worth a number, write the file.
- One portion = one branch = one PR. Never batch portions into a single PR.
- **Survey a module *just before* planning it, not long before — and only the part something
  depends on now.** (User, 2026-08-06.) Breadth-first surveying was outrunning delivery: by that
  date the repo held **30 unbuilt portions across 7 plans**, 15 of them in `draft`, against 3
  portions built in the same stretch. A plan is a depreciating asset — 005's measured blast radius
  of 22 source files was taken at a commit several merges stale, and every later merge means
  someone re-verifies it before building. So the default is **build**, and a survey earns its place
  by one of two tests:
  1. **A live dependency.** Something already approved or shipped rests on an unmeasured
     assumption. This is the only case that justifies interrupting delivery — and the scope is the
     *question*, not the bucket. Example: 007 P1 deletes `EmployeeStatus.OnLeave` on the claim that
     Absence owns that concept, and Absence had never been measured.
  2. **The module is next.** Then survey it fully, immediately before writing its plan.
  Everything else waits. `SoftwareMainOptions` (227 columns, third-largest table in the product) is
  the standing example of a genuinely important area that is still correctly deferred: nothing
  shipped depends on it, and it can be added later without unpicking anything.
- **A subagent's report is a claim, not a result. Verify the load-bearing ones.** Roughly **three
  in ten** dispatches so far have carried something wrong or materially incomplete, and none were
  caught by the agent that wrote them:
  | Claim | What it actually was | Cost of not checking |
  |---|---|---|
  | `DataAccessScopeDiagnostics` explains access decisions | It counts `DataContext` create/dispose to find connection leaks | Propagated through **3 docs and 4 code comments** as the evidence for two design decisions |
  | `OnLeave` has no legacy counterpart, leaving is only a date | Missed `LeaveReasonId`, `AdditionalLeaverComments` and the whole `dbo.LeaveReasons` table — in a survey that claimed to classify all 153 columns | Caught by the **user**, not the process |
  | Attendance endpoints have no scope filter | False — the filter is on the employee lookup feeding the query | Would have spawned a plan for a defect that does not exist |
  What catches these is the orchestrator re-running the specific claim: a targeted `Grep` on the
  cited `file:line`, or a mutation the reviewer runs itself rather than reading about. That is
  cheaper than a re-dispatch and it is why the reviewer's mutation tests earn their place — they
  measure whether the suite *detects* the defect, not whether the tests look thorough.
- `wm-reviewer` updates the Shipped table when it opens a PR, and again when you merge.
- **The review pass is not optional and the builder never opens its own PR.** P2 was built,
  self-assessed and published without one; the review was run afterwards, retroactively. On P1
  that same step caught a real defect, so it earns its place. If a portion reaches a PR without a
  review recorded here, that is a process failure worth treating as a finding.
- **A portion is not done until the tests its own plan named exist.** The 2026-08-04 audit found
  001 P2 shipped without the "migration up/down on a seeded DB" test its plan required, and the
  review passed it. The behaviour turned out to be correct, so nothing broke — this time. Check
  the portion's **Tests:** line against the test files, item by item.
- **Count transports, not routes.** "29 endpoints" excluded the SignalR hub, and the hub was the
  one thing with no authorization scoping at all. Anything a client can reach is surface.
- **A WM-only capability has no row in the coverage matrix, so nothing prompts anyone to check
  it.** The realtime feed is better than legacy — legacy has no push at all — which is exactly
  why it escaped scrutiny for three phases. §13 now carries rows for WM additions.
- **"Mirroring TLW" is a claim, and it needs a `file:line` like any other.** §4 asserted that
  TLW users belong to several groups whose access unions. TLW does the opposite, and the claim
  survived into two plans, the screen tree, the coverage matrix and two code comments before
  anyone opened `GetUserRole`. Ditto `DataAccessScopeDiagnostics`, cited five times as evidence
  for two design decisions, which turned out to be a connection-leak counter. **If a doc says
  legacy does X, either it cites the line or it is a hypothesis.**
- **Check what a legacy identifier actually names before planning against it.** Three of the four
  things §13 listed as the screen-rights schema were not tables: `WebPages` is localization,
  `FormAccess` is a static class, `SiteItemPermission` is a DTO. One `Glob` for the file name
  would have caught it in any of three earlier passes.
