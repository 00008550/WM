# 009 — What the running app actually does

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->

> ### ✅ Verification pass, 2026-08-18 — every citation opened, six corrections applied
>
> This plan was written by a surveyor that died mid-report, so nothing in it had been checked. It
> has now been audited claim by claim against the tree at `ead3bfc` (the commit it measured) and
> against `E:\Tlw`. **Every `file:line` in it resolves to what it says it does**, with the
> exceptions corrected in place below and listed here:
>
> | # | Where | What was wrong |
> |---|---|---|
> | 1 | *Open questions* 2, P8's ⚠️ block | **The readiness endpoint IS anonymous.** The 401 that prompted the earlier "correction" came from `/health/ready` — the wrong path. Corrected there, with four independent proofs. |
> | 2 | *Ground truth*, "Test projects that execute SQL: **0**" | `WM.Api.Tests` executes SQL against in-process SQLite. What is zero is *Postgres*, and *migrations*. Corrected — and it strengthens P4. |
> | 3 | Legacy behaviour → leaver lookups; *Open questions* 1 | `Web.sitemap` carries **two** LeaveReasons nodes, not one. The Personnel-area one is dead. Conclusion unchanged and better evidenced. |
> | 4 | P3 / *Target design* | The mechanical rule flags **two** things today, not one. The second is the interesting one. |
> | 5 | P3 / *Out of scope* | **003 P3 already owns the `phone` fix by name**, not only `departmentId`. Stated rather than silently overlapped. |
> | 6 | P4, edge case 19, *Open questions* 3 | `ryuk` is not pulled; `LeaverRecordEndpointTests.cs:443-474` is helpers not tests; the §13 row is `ARCHITECTURE.md:444`, not `:446`. |
>
> **Reproduced exactly, so read them as measured rather than asserted:** the schema numbers (578
> tables / 8,173 columns, `dbo.Employees` rank 6 at 153 cols); the four full-replace `PUT`s and the
> fact that the membership one round-trips; `Phone` as the *only* upsert field missing from the list
> projection; `FindByCodeAsync`'s `e.Code == code` with no collation configured anywhere in `src/`;
> `openEdit`'s unconditional `phone: ''`; both leaver lookups shipping empty with one read endpoint
> and no writes; `WmHealthChecks` never inspecting a column; and every legacy citation — the
> `ToLower()`/`Code ==` pair, the padded uniqueness SQL, `LeaveReasonService` line for line, the
> `EntitySet<Employee>` and the two unordered `FirstOrDefault()`s, `SetEmployeesActive`, the
> mandatory triple, and the `77.V5.23.0.0.sql` collation reading.
>
> Left `draft`, nothing ticked. — audit, 2026-08-18

Roadmap: ARCHITECTURE.md §14 — **no new roadmap capability.** This is corrective work on code
already shipped under phases 1, 1b and 1c, plus one piece of test infrastructure the repository has
never had. Its §13 rows are the ones [#62](https://github.com/00008550/WM/pull/62) adds; this plan
references them rather than restating them.

**Legacy sources surveyed: deliberately almost none, and that is the honest answer.** Seven of the
findings below are defects in WM's own code, measured against WM's own tree and the running stack.
TLW has an opinion on exactly **two** of them, and only those two were measured:

| Question | Legacy read |
|---|---|
| Does TLW normalise a badge code on lookup, and is its collation case-insensitive? (P2) | `Logic/Extensions/EmployeeExtensions.cs:129-152`; `Logic/Personnel/PersonnelService.cs:2697-2724`; `Database/Versioning/77.V5.23.0.0.sql:1414-1471` |
| Do the leaver lookups have a maintenance surface, and where does it live? (P6) | **Read in full:** `Logic/Settings/LeaveReasonService.cs` (115 lines), `WebSite/Controllers/PersonnelSetupController/LeaveReasonsController.cs` (150 lines). **Read in part:** `WebSite/Web.sitemap:53-55, 60-71, 87-89`; `Logic/ExternalAccess/TLW.cs:199-219`. **Enumerated, not opened** *(and labelled so, because a file listing is not a reading)*: `Logic/Settings/LeaveNoticePeriodService.cs`, `…/Interfaces/ILeave*Service.cs`, `WebSite/Controllers/PersonnelSetupController/LeaveNoticePeriodsController.cs`, the six `Views/PersonnelSetup/{,Add,Edit}Leave{Reasons,NoticePeriods}.cshtml`. |
| *(unplanned, found while checking P5)* What is TLW's user↔employee cardinality? | `Logic/Entities/HorioDB.designer.cs:7834-7845, 30451`; `Logic/Security/AccessControl/AuthorizationService.cs:788-802, 1091-1115` |

Plus the schema measurement below, re-run rather than quoted. Two further legacy citations —
`PersonnelModels.cs:1354-1368` (the mandatory leaver triple) and
`PersonnelService.SetEmployeesActive:151-173` (un-leaving) — were carried over from
`TLW-PEOPLE-MODEL.md` §4.1b and **re-opened and re-verified here** rather than trusted, because
both are load-bearing for what P6 does and does not adopt. Both are exactly as recorded.

Nothing else in this plan was derived from `E:\Tlw`, and nothing in it should be. P1, P3, P4 and P7
have no legacy analogue at all — TLW has no Angular SPA, no Postgres, no Testcontainers and no
full-replace JSON `PUT`. P5 turned out to have one after all, and it says the opposite of what WM's
records claim; that is the third row above.

---

## Ground truth

### The schema measurement

Re-run against `HorioDB.designer.cs`, not copied from a previous survey:

```
TOTAL TABLES: 578   TOTAL COLUMNS: 8173
   268  dbo.Devices                     (line 60538)   ⏹ dropped
   249  dbo.Clockings                   (line 20151)   plan 002
   227  dbo.SoftwareMainOptions         (line 69348)   deferred by name
   177  dbo.FireMarshalMusterPoints     (line 236848)  phase 6b
   167  dbo.UnifiedTimesheetReportView  (line 212331)
   153  dbo.Employees                   (line 28594)   ← rank 6, this plan's area
```

The three legacy tables this plan touches, with their rank in the estate:

| Table | Rank | Cols | Line |
|---|---:|---:|---:|
| `dbo.Employees` | **6** / 578 | 153 | `:28594` |
| `dbo.LeaveReasons` | 459 | 3 | `:53137` |
| `dbo.LeaveNoticePeriods` | 560 | 2 | `:182826` |

The two lookups are among the smallest tables in the product — and **each still has its own
service, its own controller and three screens.** That is the point of P6: a two-column table is not
too small to need administering, and legacy did not think so either.

> ✅ **Re-measured 2026-08-18 (audit): 578 / 8,173 and rank 6 / 153 cols / `:28594` all reproduce
> exactly.** One caveat so nobody "corrects" the other two later: **129 tables have exactly 3 columns
> and 29 have exactly 2**, so `LeaveReasons` can legitimately print as any rank in **421–549** and
> `LeaveNoticePeriods` as any rank in **550–578**, depending on how the sort breaks ties
> (PowerShell's `Sort-Object` is not stable). 459 and 560 are inside their bands. The load-bearing
> fact is tie-free: **549 of 578 tables are wider than `LeaveNoticePeriods`, and 420 are wider than
> `LeaveReasons`.**

### The WM surface, measured at `ead3bfc`

| | Measured |
|---|---|
| Test projects in `WM.sln` | **4** — `WM.SharedKernel.Tests`, `WM.Modules.Identity.Tests`, `WM.Api.Tests`, `WM.Modules.People.Tests` |
| Test projects that execute SQL | **1, and it is not the one that matters** — see the correction below. `WM.Modules.People.Tests.csproj:16-19` and `WM.Modules.Identity.Tests.csproj:13-16` both take `Microsoft.EntityFrameworkCore.InMemory`; `PeopleEndpointHost.cs:72` calls `UseInMemoryDatabase`. The in-memory provider **does not run migrations at all.** |
| Test projects that run a **migration** | **0.** This is P4's actual gap. |
| Unique indexes on `identity."Users"` | **2** — `Email`, `UserName` (`IdentityDbContextModelSnapshot.cs:259-263`). **`EmployeeId` has none** (`:230-231`). |
| Full-replace `PUT` transports | **4**, per `EndpointAuthorizationInventoryTests.cs:85, 89, 93, 101` — `PUT /api/users/{id}`, `PUT /api/users/{id}/security-groups`, `PUT /api/security-groups/{id}`, `PUT /api/employees/{id}`. The membership one is easy to overlook and belongs in the rule: it replaces a whole collection from a body the SPA builds out of a `GET` (`users.component.ts:267` → `:301`). |
| Fields on `EmployeeUpsertRequest` absent from the list projection the SPA reads | **1 — `Phone`.** `PeopleModule.cs:384-397` accepts it; `PeopleModule.cs:75-88` does not return it. |
| Maintenance endpoints for `LeavingReasons` / `LeaveNoticePeriods` | **1 read, 0 writes** — `GET /api/leaving-reasons` (`PeopleModule.cs:326-336`). `LeaveNoticePeriods` has no endpoint at all. |

> ⚠️ **Correction, measured 2026-08-18 (audit).** The row above originally read *"Test projects that
> execute SQL: **0**"*. **`WM.Api.Tests` executes SQL.** `ApiTestHost.UseSqlite` opens an in-process
> SQLite database per module (`ApiTestHost.cs:275, 287`), `SeedIdentity` calls
> `EnsureCreated()` (`:321`), and `MarkAsMigrated` runs `ExecuteSqlRaw` against the history
> repository (`:308-310`). Nine tests use it (`ReadinessEndpointTests`, the sign-in path).
>
> **This makes P4's case stronger, not weaker.** `MarkAsMigrated`'s own doc comment
> (`ApiTestHost.cs:290-300`) says why: *"The migrations themselves cannot be replayed here:
> `AddComposedScopeConstraints` and friends carry schema-qualified Postgres SQL, which SQLite has no
> notion of. Stamping the history is the honest substitute."* So the repository has a SQL harness
> that is **structurally incapable of executing a migration**, and a readiness check whose only
> question — "is anything pending?" — is answered by a table the harness *writes by hand*. Both
> readiness tests pass against a database that was never migrated.
>
> The same inaccuracy is in the product tree at `EmploymentMigrationTests.cs:11-12` (*"every test
> project uses the EF in-memory provider"*), which is where this plan inherited it. That file is
> `src/`, so it is not corrected here — it is P4's to fix, and it is listed under P4's *Touches*.

### Corrections made to WM's records by this survey

**One, and it is a real one.** `docs/TLW-PEOPLE-MODEL.md` §4.3 — a new measured block. §4.3
established the *direction* of legacy's user↔employee link and never measured its **cardinality**.
It is **one-to-many**: `HorioDB.designer.cs:7834-7845` maps `User_Employee` over
`EntitySet<Employee> Employees` — plural — and legacy resolves the many-link with two *separate,
unordered* `FirstOrDefault()` calls (`AuthorizationService.cs:1091-1104` and `:1106-1115`) that can
return different rows. So `ARCHITECTURE.md:172`'s *"The link is optional and one-to-one"* and §13's
*"one-to-one enforced"* are both **WM inventions credited to TLW** — the same class of error as
`WebPages`, `DataAccessScopeDiagnostics` and the one-role-per-user claim. WM's rule is *better*;
it just is not a port. Both §13 and §1–§12 are out of my hands here — §13 is being edited by
[#62](https://github.com/00008550/WM/pull/62) and §1–§12 are design sections — so the two diffs are
**proposed, not applied**, in *Open questions* 3.

**Nothing to correct in `TLW-INVENTORY.md`.** Grepped for `LeaveReason`, `Notice Period`,
`user management` and `Users screen`: no matches, so it makes no claim this survey contradicts.
Recorded so that "not mentioned" does not later read as "not checked".

**`SCREEN-TREE.md` deliberately untouched** — a second surveyor is in it for the Work Rules /
Daily Browser survey. P6 adds ~~two administration screens~~ **one** administration screen that will
need a row there; that is a note for whoever lands next, not an edit made across another agent's
working set. *(⚠️ **"two" corrected to "one" 2026-08-18** — P6's Target design builds one screen, and
this plan's own* Out of scope *explicitly defers the `LeaveNoticePeriods` surface. Also verified:
`SCREEN-TREE.md:439-440` already carries rows for both **legacy** screens, correctly attributed to
`PersonnelSetupController`, so the file needs an addition on the WM side only, not a correction on
the TLW side.)*

**One imprecise citation flagged, not edited.** Plan 007's *Legacy behaviour → Identity* section
offers `77.V5.23.0.0.sql:1425` as where SQL Server's `SQL_Latin1_General_CP1_CI_AS` is *"visible"*.
Opened: `:1414-1471` is the `Employees_Update_Trigger` audit unpivot, and every `COLLATE` there is
an explicit **coercion on a temp table**, sitting under the comment `--got strange collation issue,
therefore COLLATE` (`:1432`). It evidences that the estate's collation *is* `CP1_CI_AS`, but it is
**not** a declaration of `Employees.Code`'s collation, and no `CREATE DATABASE` for `HorioDB`
exists anywhere in `E:\Tlw\Database`. The stronger evidence is behavioural and is set out under
P2 below. 007 P3 owns that paragraph; churning it while 007 has open portions costs more than the
imprecision does.

---

## Legacy behaviour (what we are replacing)

Only two areas. Everything else in this plan replaces **WM**.

### Badge-code lookup: legacy normalises the input *and* leans on the collation

`Logic/Extensions/EmployeeExtensions.cs:129-152`:

```csharp
public static IQueryable<Employee> FilterByCriteria(this IQueryable<Employee> query,
                                                    PersonnelFilterType? filterType, string filterCriteria)
{
    var filter = filterCriteria.Trim().ToLower();          // :133  — input normalised
    …
        case PersonnelFilterType.EmployeeId:
            if (isOk)
                return query.Where(e => e.Code == filter);  // :143  — against Code AS STORED
```

Read those two lines together. The input is lower-cased; the stored `Code` is **not** — legacy
stores whatever was typed, and `TLW.cs:78, 149` compare it raw. If the collation were
case-sensitive, `:143` would return **zero rows for every employee whose code contains an upper-case
character**, which is most of them. The comparison therefore *cannot* be case-sensitive in the
shipping product, and the CI collation is load-bearing rather than incidental. That is the
behavioural proof; the `COLLATE SQL_Latin1_General_CP1_CI_AS` coercions at
`77.V5.23.0.0.sql:1414-1471` are corroboration.

Two further facts about how legacy treats the code, both relevant:

- **Legacy never rewrites the stored casing.** No `ToUpper()` on any write path. The code prints on
  reports and exports, so the customer's chosen form survives.
- **Uniqueness is left-zero-padded to 10 and case-blind** — `PersonnelService.cs:2697-2709`,
  `right('0000000000' + @code, 10) in (select right('0000000000' + Code, 10) …)` — under the same
  CI collation. The padding half is already **Dropped** by 007 decision 1; the case half is 007 P3's.

### The leaver lookups are first-class administered vocabulary, in the *setup* area

`dbo.LeaveReasons` (3 columns) and `dbo.LeaveNoticePeriods` (2 columns) each have:

| | `LeaveReasons` |
|---|---|
| Service | `Logic/Settings/LeaveReasonService.cs` — `Add` `:26`, `Update` `:33`, `Delete` `:62`, `GetAllLeaveReasons` `:103`, **`GetActiveLeaveReasons` `:108-111`** |
| Interface | `Logic/Settings/Interfaces/ILeaveReasonService.cs` |
| Controller | `WebSite/Controllers/PersonnelSetupController/LeaveReasonsController.cs` — `Index`, `AddLeaveReason` ×2, `EditLeaveReason` ×2, `DeleteLeaveReason` |
| Screens | `Views/PersonnelSetup/LeaveReasons.cshtml`, `AddLeaveReason.cshtml`, `EditLeaveReason.cshtml` |
| Menu | `Menu_PersonnelGroup_PersonnelSetup_LeaveReasons` (+ `_AddLeaveReason`, `_EditLeaveReason`) |
| Sitemap node | `Web.sitemap:69-71`, nested under `PersonnelSetup` (`:60`) — **not** under Personnel; see the correction below |

`LeaveNoticePeriods` is identical in shape (`Web.sitemap:87-89`).

> ⚠️ **Correction, measured 2026-08-18 (audit). There are TWO `LeaveReasons` sitemap nodes, and the
> row above named only one.** The plan's own header table cites `Web.sitemap:53-55` as "read in
> part" and then concludes *"not under Personnel"* — a reader opening `:53-55` would find the
> opposite and stop trusting the section. What is actually there:
>
> | Node | Controller / action | Status |
> |---|---|---|
> | `Web.sitemap:53-55` (inside the **Personnel** group, closes `:58`) | `controller="Personnel"` `action="LeaveReasons" / "AddLeaveReason" / "EditLeaveReason"` | **Dead.** No such action exists on any controller (`grep 'ActionResult LeaveReasons'` finds only `PersonnelSetupController/LeaveReasonsController.cs`), `Views/Personnel/` has no leave views, and `WebSite/Misc/localization1.generated.cs:3163-3165` carries only the `PersonnelSetup` menu keys. |
> | `Web.sitemap:69-71` (inside **PersonnelSetup**, `:60`) | `controller="LeaveReasons"` | **Live.** Controller, service, three views, three menu keys. |
>
> So the screens *moved* from Personnel to PersonnelSetup and the old sitemap entry was never
> deleted. **The plan's conclusion holds and is stronger than it claimed** — legacy's *shipping*
> information architecture puts the vocabulary in Setup, and the Personnel-area node is a stale
> pointer to nothing. `Menu_Personnel_LeaveReasons` still exists, but only in `HealthWebSite`
> (`HealthWebSite/Misc/localization1.generated.cs:6108`), a different site in the estate.

**A fifth thing, found by the audit and worth having: legacy already exposes the lookup over REST,
read-only.** `WebSite/Controllers/API/V1/EmployeeLeaveReasonApiController.cs` and
`API/V2/EmployeeLeaveReasonApiController.cs` both wrap `ILeaveReasonService`, and **both map `GET`
and nothing else** (V2 `:31, :46`). Maintenance is reachable only from an MVC screen. That makes
P6's `POST`/`PUT`/`DELETE` an **Improve** against legacy rather than a port, and it is the
invariant-2 argument in legacy's own evidence: the vocabulary was already considered API-worthy to
read, and the write half was simply never exposed.

Four behaviours worth naming:

1. **`GetActiveLeaveReasons()` is a separate query from `GetAllLeaveReasons()`** (`:103-111`).
   "Everything" and "what is offered" are two questions. WM's `?includeRetired=` already mirrors
   this (`PeopleModule.cs:326-336`).
2. **Delete exists, and is guarded by usage, not by the flag.** `Delete:62-90` loads
   `LoadWith<LeaveReason>(l => l.Employees)` and returns `DeletionResult.AlreadyUsed` if any
   employee references it. So legacy has **both** a retire flag and a delete-if-unused — they are
   not alternatives.
3. **Name uniqueness is enforced in the controller, not the database** —
   `ValidateUniqueLeaveReasonName:139-148`, `x.Name == model.Name && x.Id != model.Id`, again
   case-insensitive only by collation.
4. **A second fill path exists, and it is defective.** The HR import auto-creates a reason by name
   (`TLW.cs:205-219`) — but it looks up `l.Name == … && l.IsActive`, so an import naming a
   **retired** reason inserts a *duplicate row with the same name*, which the screen's own validator
   would have refused. A rule the database does not hold is not a rule; this is that, demonstrated.

### The user↔employee link is one-to-many, and legacy resolves it arbitrarily

Measured while checking P5, unplanned, and it changes what §13 may claim. Full table in
`TLW-PEOPLE-MODEL.md` §4.3's 2026-08-18 block. Short form: `EntitySet<Employee>` at
`HorioDB.designer.cs:7834-7845`; `GetEmployeeIdOfUser:1091-1104` and
`DepartmentOfCurrentUser:1106-1115` are two unordered `FirstOrDefault()`s over the same set, so
*"who am I?"* and *"which department am I in?"* can answer from different rows.

> ✅ **Independently re-measured 2026-08-18 (audit): every element of this is exact.**
> `AssociationAttribute(Name="User_Employee", Storage="_Employees", ThisKey="Id", OtherKey="UserId")`
> over `EntitySet<Employee> Employees` at `:7834-7845`, plural and with no `IsUnique`;
> `Employees.UserId` nullable `Int` at `:30451`; and both `FirstOrDefault()`s are unordered, verbatim
> as quoted. **The plan's headline legacy claim is right: TLW is one-to-many and resolves it
> arbitrarily, so WM's one-to-one is an invention, not a port.**
>
> **One thing to be precise about, because it decides what P5's migration can find.** The two models
> put the *many* on **opposite sides**:
>
> | | Column that holds the link | What is unconstrained | The bad shape it permits |
> |---|---|---|---|
> | TLW | `Employees.UserId` | many **employees** → one user | one login answers as several people; `GetEmployeeIdOfUser` picks one |
> | WM | `Users.EmployeeId` | many **users** → one employee | several logins each self-service the same person |
>
> Each direction is single-valued in the other model's schema, so these are mirror images rather
> than the same defect. Two consequences, neither of which changes P5's design — **a partial unique
> index on `Users.EmployeeId` genuinely yields one-to-one in WM, because the reverse multiplicity has
> no column to live in** — but both worth having written down:
>
> 1. **Edge case 15's duplicate pair cannot arrive from legacy data.** It is right to say "planted":
>    legacy has no `Users.EmployeeId` to import a duplicate *from*. The only source is WM's own race
>    (edge case 14). The migration must still report and refuse, for the reason given; the *scenario*
>    is a WM-native one.
> 2. **A future TLW importer inherits the harder half of this**, and it is nobody's today: legacy's
>    several-employees-on-one-login has no WM representation at all. Whoever writes the importer must
>    choose — mint a user per employee, or keep one and drop the rest — and legacy's arbitrary
>    `FirstOrDefault()` gives no basis for choosing. Named here so it is found rather than
>    rediscovered; **not P5's, and not this plan's.**

---

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Badge-code comparison is **case-insensitive** | **Keep** | Legacy is right and it is what users expect. `E1030` and `e1030` are one badge. |
| **Where** the case-folding happens — the storage layer vs. the client | **Invert** | Legacy got it for free from the collation, which is fine when there is one client. WM's Postgres default collation is deterministic — ✅ *audit 2026-08-18: `grep -i collat\|citext src/Modules` returns nothing, so `Employee.Code` takes the database default and no `citext` is in play* — and the only case-folding **on the lookup path** is `dashboard.component.ts:260`. (⚠️ *"WM's only case-folding" corrected 2026-08-18*: `PeopleModule.cs:126` and `:196` also fold, with `.ToLower()` on both sides — but those are the **uniqueness** check, which is 007 P3's, and they are the reason a lower-case code is refused on create yet unfindable on lookup.) A rule enforced in a browser is not a rule (invariant 2). |
| Storing the code **as typed**, not upper-cased | **Keep** | Legacy never rewrites it, and the code prints on reports and payroll exports. Fold at comparison, never at rest. Also makes P2 migration-free. |
| Zero-padding-insensitive uniqueness | **Drop** | Already dropped by 007 decision 1. Restated so P2 does not reintroduce it while "matching legacy". |
| Leaver vocabularies as **customer-maintained, administered lookups** | **Keep — and build the surface** | Legacy gave two-column tables a service, a controller and three screens each. WM ships them read-only and fillable only by SQL. |
| Retiring via `IsActive` rather than deleting | **Keep** | Already built (`PeopleModule.cs:276-295`, FK `RESTRICT`). |
| Legacy's **delete-if-unused** alongside `IsActive` | **Improve** | Keep the capability — an admin who mistypes a reason nobody used should be able to remove it — but let the FK be the guard and map `23503` to a readable 409, instead of `LoadWith` pulling every referencing employee into memory to count them. |
| Lookup **name** uniqueness enforced in a controller | **Invert** | Same defect as legacy's employee code, and WM already half-inherited it: `IX_LeavingReasons_Name` (`PeopleDbContext.cs:33`) is a plain unique index, so WM today permits `Redundancy` **and** `redundancy` where legacy permits one. |
| Auto-creating vocabulary from an import (`TLW.cs:205-219`) | **Drop** | It is how legacy's lists filled up with near-duplicates. WM has no HR import yet; do not build this one when it arrives. |
| Screen location: leaver vocabularies under **Setup**, not under Personnel | **Keep** | Legacy's own information architecture, and it is the argument for the permission in *Open questions* 1. |
| **One user ↔ one employee** | **Invert** | Legacy allows many and resolves it by unordered `FirstOrDefault()`. WM's rule is better — but it is currently held in two `if` statements and by nothing else. |
| Full-replace `PUT` | **Keep the verb, Improve the contract** | `PUT` *should* be a full replace; that is what `PUT` means. What must change is that WM has no mechanism ensuring a client can *reconstruct* the full body from what the API told it. See P3. |
| Stored derived values (`calc_*`) | — | Not in scope; no instance in this plan's surface. Named so its absence is deliberate. |

**Two ceilings found while measuring, both the "fixed numbered slot" smell in modern dress:**

- `users.component.ts:318` requests `list('', 1, 100)` and the screen renders **no paging control
  and no search box**, while the header at `:22` prints the true `total()`. At 400 accounts the
  page says *"400 accounts"* and shows 100, with no way to reach the other 300. **Improve** — P1.
- `users.component.ts:224` loads the linked-employee picker with `employees('', 1, 200)`, and the
  server clamps `pageSize` to 200 (`PeopleModule.cs:50`). The 201st employee **cannot be linked to
  a user at all**, silently. **Improve** — P5.

---

## Edge cases

No Given/When/Then examples exist in `E:\Tlw\Documentation` for any of this — checked; the vault's
worked examples are all swipe-allocation and absence. These are derived from the code and the
running stack instead, and each is a required test.

**Badge-code lookup (P2)**

1. `FindByCodeAsync("e1030")` where the stored code is `E1030` → **resolves.** Today returns null,
   and the punch is rejected as *"Unknown employee code"* (`PunchService.cs:32-34`).
2. `FindByCodeAsync("E1030")` where the stored code is `E1030` → resolves. Unchanged.
3. `FindByCodeAsync(" E1030 ")` → resolves. The server must trim; today only
   `dashboard.component.ts:260` does.
4. `FindByCodeAsync("")` / whitespace → **no match**, not "the first employee". A `Trim()` that
   yields empty must not become an unanchored predicate. ✅ *Audit 2026-08-18: legacy already agrees
   and the plan did not know it — `EmployeeExtensions.cs:142-144` guards with
   `if (isOk)` and otherwise returns `Enumerable.Empty<Employee>().AsQueryable()`, and the `switch`'s
   `default:` (`:149-150`) does the same. Both are **fail-closed**, so this case is a Keep, not a
   WM invention. Cite legacy for it.*
5. `FindByCodeAsync("0042")` where the stored code is `42` → **no match.** Deliberate divergence
   from legacy's padding rule (007 decision 1); asserted so nobody "fixes" it later.
6. The resolved employee is **out of the caller's data scope** → still no match. `FindByCodeAsync`
   goes through `Scoped(...)` (`PeopleModule.cs:401-405`) and case-folding must not widen it. This
   is the fail-open trap in this change; assert it explicitly.
7. A punch recorded via a lower-case code stores `Punch.EmployeeCode = employee.Code` — the
   **canonical stored casing**, not what was typed (`PunchService.cs:43`). Assert it, so the feed
   and the timesheet cannot show two spellings of one person.
8. Two employees whose codes differ only by case → cannot both exist *via the API*
   (`PeopleModule.cs:126`) but **can** exist in the database (007 P3). If they do, `FindByCodeAsync`
   must not silently pick one: it must behave deterministically and the test must say which. 007 P3
   removes the possibility; until then P2 owns the behaviour.

**Full-replace `PUT` (P3)**

9. `GET /api/employees` → build the `PUT` body from **that projection alone** → `PUT` → the stored
   row is byte-identical in every field. This is the test shape that would have caught all three
   instances and did not exist.
10. Same, for `PUT /api/users/{id}` built from `GET /api/users`.
11. Same, for `PUT /api/security-groups/{id}` built from `GET /api/security-groups`, and for
    `PUT /api/users/{id}/security-groups` built from `GET /api/users/{id}/security-groups`. That
    fourth one **already round-trips correctly** (`users.component.ts:267` → `:301`) — it is the
    second worked example after 007 P1, and a rule that cannot show a passing case as well as a
    failing one is not calibrated.
12. A field that is **deliberately** write-only or read-only (a password, a computed status) must be
    *named* as such, not silently missing. The registry has to distinguish "excluded on purpose"
    from "forgotten" or it becomes a rubber stamp — the `DeliberatelyAnonymous` lesson. Live
    example: `CreateUserRequest` carries `UserName` and `Password`, `UpdateUserRequest` carries
    neither, and `UserListItem` returns `UserName` and never a password
    (`UserManagementService.cs:13, 19, 22`). All three exclusions are right, and all three must be
    stated rather than inferred.

**One user, one employee (P5)**

13. Link employee X to user A, then to user B **sequentially** → refused. Works today
    (`UserManagementService.cs:144`).
14. Link employee X to user A and user B **concurrently** → exactly one succeeds. Today **both
    succeed**: there is no unique index, so the pre-check races with nothing behind it.
15. Two users already linked to one employee (planted, since nothing prevents it) → the migration
    must **report** them and refuse rather than silently unlink one. Which one it dropped would be
    unrecoverable, and legacy's own arbitrary `FirstOrDefault()` is the reason not to imitate it.
16. `EmployeeId = null` on many users → allowed. A partial unique index, not a plain one.
17. The picker must not offer an employee already linked to a *different* user, and **must** still
    offer the one linked to the user being edited — otherwise opening the drawer clears the link.
18. The 201st employee is linkable (ceiling above).

**Leaver vocabulary and the gate (P6)**

19. Create a reason, use it on a leaver, then **retire** it → the leaver still renders it; it is no
    longer offered for new leavers. Already true and tested — ⚠️ **citation corrected 2026-08-18:**
    `:443-474` is the helper block (`Leaving`, `Retire`, `Row`, `Reasons`, `Seed`), not the tests.
    The tests are `LeaverRecordEndpointTests.cs:93-146` —
    `Retiring_a_reason_leaves_the_records_that_used_it_readable` (`:94`),
    `A_retired_reason_is_no_longer_offered` (`:117`) and
    `A_retired_reason_cannot_be_given_to_a_new_leaver` (`:131`). All three must keep passing.
20. **Delete** a reason nobody uses → succeeds. **Delete** one in use → 409 with a readable message,
    from the FK, not a 500. Legacy's `DeletionResult.AlreadyUsed` (`LeaveReasonService.cs:75-78`).
21. Create `Redundancy`, then create `redundancy` → refused. Today **accepted**
    (`PeopleDbContext.cs:33`), where legacy refuses.
22. Rename a reason in use → the change is visible on every leaver already filed under it. That is
    the intended behaviour of a lookup, and it is worth pinning so nobody "improves" it into a copy.
23. **The gate:** with the vocabulary **empty**, setting `EmployedUntil` → the API must not answer
    *"a leaving reason is required"* against a list the user cannot fill. The gate ships **in the
    same portion** as the surface, after it, and a test asserts that an admin holding the
    administration permission can go from an empty database to a recorded leaver **without SQL**.
24. Un-leave (clear `EmployedUntil`) under the gate → still clears reason and comments
    (`PeopleModule.cs:263-274`), and is **not** blocked by the gate. Legacy's `SetEmployeesActive`
    does exactly this.

**The diagnostics panel (P7)**

25. Open the drawer for a self-scoped user, tick a group granting site scope, do **not** save → the
    panel must not read *"Sees only their own employee record"* as though it were current.
26. Save, then reopen → the panel reflects the saved state. Today it does, which is why the defect
    reads as confirmation rather than as an error.
27. Tick **Administrator** (a *role*) → the visibility panel must not change, because data scope
    comes from security groups and never from roles (`DataScopeResolver.cs:25-69`). The panel is
    labelled *"Effective visibility"*, which invites exactly that expectation; the label is part of
    the fix.

---

## Target design in WM

**P2 — normalisation is server-side.** `EmployeeDirectory.FindByCodeAsync`
(`PeopleModule.cs:401-405`) trims and folds case in the query. `.ToLower()` on both sides, or
`EF.Functions.ILike` with the pattern escaped — whichever the builder can show translating to SQL
via `ToQueryString()`, as `EmploymentPredicateTests.cs:171-181` already does for the employment
predicate. `Employee.Code` stays exactly as typed at rest, so **there is no migration and no data
change.** `dashboard.component.ts:260`'s `.toUpperCase()` becomes redundant; remove it, so the
next reader cannot conclude the server needs it. Contract (`Contracts/EmployeeDirectory.cs:11`) is
unchanged — this is a behaviour fix behind an existing signature (invariant 1 intact).

> **Seam with 007 P3, stated so whichever lands second does not clobber the first.**
> **009 P2 owns the *lookup*** — `FindByCodeAsync` and nothing else.
> **007 P3 owns *uniqueness*** — the pre-check at `PeopleModule.cs:126`, the `23505` handler at
> `:153-157` and `:222`, the false comment at `:155`, and the index. When 007 P3 lands its
> `lower(code)` index, `FindByCodeAsync` becomes sargable for free **if** P2 wrote the predicate as
> `lower(Code) = lower(@code)`; write it that way. Until then it is a sequential scan on a
> `varchar(32)` column, which is correct and, at any plausible employee count, fast. Say the number
> in the PR rather than asserting "fast".

**P3 — a rule for full-replace `PUT`s, in three layers.** The durable half of finding 3, and the
reason it is one portion rather than a one-line fix:

1. **Mechanical, in `WM.Api.Tests`.** A new inventory test in the shape of
   `EndpointAuthorizationInventoryTests` (`:33-57` is the pattern): every writable property of a
   full-replace request record must be **readable from the list projection of the same resource**,
   or be named in an `IntentionallyWriteOnly` table with a reason. A new `PUT` fails until it is
   named — anonymity's lesson applied to round-tripping.

   > ⚠️ **Correction, measured 2026-08-18 (audit).** This said *"Applied today this flags exactly one
   > thing: `EmployeeUpsertRequest.Phone`."* **It flags two, and the second decides how strict the
   > rule is.** All four request/projection pairs, compared property by property:
   >
   > | Full-replace `PUT` | Request | Projection | Verdict |
   > |---|---|---|---|
   > | `/api/employees/{id}` | `EmployeeUpsertRequest` (`PeopleModule.cs:384-397`) | `PeopleModule.cs:75-88` | **`Phone` absent.** The known defect. |
   > | `/api/users/{id}` | `UpdateUserRequest` (`UserManagementService.cs:21-22`) — `RoleIds: Guid[]` | `UserListItem` (`:11-13`) — `Roles: string[]` | **Flagged.** Different name *and* different type. It does round-trip today, but only because the SPA maps names back to ids against a **second** endpoint (`users.component.ts:254` over `GET /api/users/roles`). |
   > | `/api/security-groups/{id}` | `SecurityGroupUpsertRequest` (`SecurityGroupService.cs:13-15`) | `SecurityGroupListItem` (`:8-11`) | Clean — every writable property present, name for name and type for type. |
   > | `/api/users/{id}/security-groups` | `SetUserGroupsRequest` (`SecurityGroupEndpoints.cs:9`) | `GET` returns `Guid[]` (`:49-50`) | Clean. The plan's worked passing case; confirmed. |
   >
   > `RoleIds` is the case that keeps the rule honest, and it needs a **third** category beside
   > "readable" and `IntentionallyWriteOnly`: *reconstructible from a named companion endpoint*.
   > Without it the builder either files a false positive under `IntentionallyWriteOnly` — which is
   > how a registry becomes the rubber stamp edge case 12 warns about — or weakens the match until
   > `Phone` stops being caught. **Decide it in the PR and say which.** 005 collapses `RoleIds` to a
   > single group, so the entry is temporary either way; the *category* is not.
2. **Behavioural, per endpoint.** One test per full-replace `PUT` that constructs the body from the
   endpoint's *own* `GET` response and asserts the stored row is unchanged — edge cases 9–11. This
   is the layer that catches "the client omits a field the projection does return", which is what
   `departmentId` is.
3. **Frontend, in `employees.component.spec.ts`.** The modal's payload must carry back every field
   `openEdit` received. `openEdit` at `:287-306` already carries the leaver fields **with a comment
   explaining why** (`:296-299`) — 007 P1's worked example. `phone: ''` at `:291` is the same bug
   the same comment describes, three lines above it.

Then the actual fix: add `e.Phone` to the list projection (`PeopleModule.cs:75-88`) and change
`:291` to `phone: e.phone ?? ''`.

`departmentId: null` at `:329` is **003 P3's**, not this plan's. Do not touch it; P3 here only has
to make sure the new tests describe it truthfully rather than encoding it as correct.

> ⚠️ **Correction, measured 2026-08-18 (audit). `phone` is 003 P3's too, and this plan did not say
> so.** The paragraph above cedes `departmentId` and quietly keeps `phone`. But 003 P3's Done-when
> names both, twice:
>
> - `003-enforcement-gaps.md:253-254` — *"the employee editor carries `departmentId` **and `phone`**
>   through an edit instead of blanking them."*
> - `:255-256` — *"editing an employee preserves department **and phone** (the current UI wipes
>   both — assert the round-trip, not the request body)."*
> - `:279-280` — *"**`phone` is a data-loss fix; `departmentId` is an availability fix.** If P3 is
>   ever descoped or split, the department half is the half that cannot be dropped."*
>
> **003 is `in-progress` and user-approved.** 007 decision 3 — which this plan invokes two sections
> down to keep P8 out of 006 — is the rule that an unapproved plan does not silently reshape an
> approved one, and quietly discharging half of an approved portion's Done-when is the same move in
> the opposite direction. It is not a veto: `phone` is one line of a defect this plan found
> independently, and 003 P3 is blocked behind its own ⛔ (`003:260-283`, and 007 P4 must land with or
> before it), so waiting for it means shipping the round-trip rule with its only live finding
> already fixed elsewhere. **But it must be a decision, not an accident.** Two options, and the
> builder must state which in the PR:
>
> 1. **009 P3 takes `phone`** and the PR edits `003:252-256` and `:279-280` to strike it, leaving
>    003 P3 owning `departmentId` and the scoped-caller round-trip alone. Cheapest, and it is a
>    plan-file edit to an approved plan's *scope*, so it wants a line in the PR body.
> 2. **009 P3 ships the rule only** — the three layers, with `Phone` as its first *failing* entry —
>    and 003 P3 makes it pass. The rule is the durable half anyway; a mechanical test that ships red
>    against a named, owned defect is a legitimate outcome and arguably the better demonstration.
>
> Either way, **009 P3 does not discharge 003 P3.** 003 P3's round-trip test must run *as a
> `Departments`-scoped caller* (`003:273-278`) because `All` short-circuits the scope check; nothing
> in edge cases 9–12 says anything about the caller's scope, and for `phone` it does not matter.

**P4 — a Postgres migration harness.** `src/TestSupport/WM.TestSupport.Postgres` (or a shared
fixture inside an existing project — the builder chooses and justifies): a Testcontainers-backed
`PostgreSqlContainer`, an xUnit collection fixture so one container serves the assembly, and a
helper that runs `Up` to a named migration, seeds, runs `Up` to head, asserts, then runs `Down`.
The 2026-08-17 transcript in 007 P1 is the **specification** — it lists the four properties worth
asserting (one transaction per direction; backfill strictly before `DROP COLUMN`; `Down` restores
the schema *including the absence of a default*; unrecognised values fail closed) and the two
choices that made it meaningful (timezone on the **database**, not the session; `log_statement`).
Port it. Two rules, both non-negotiable and both from that note:

- **Never skip when no Docker is present.** A suite that goes green because it silently skipped is
  worse than no suite. Fail, loudly, with the reason.
- **It must run in CI** (`.github/workflows/ci.yml`), or it is a second hand-run.

> ⚠️ **Correction, measured 2026-08-18 (audit).** This paragraph ended *"`ryuk` has been pulled on
> this machine and Docker is working, so the container availability question is already answered."*
> **Half of that is true.** `docker version` → server **27.1.1**, and **`postgres:17-alpine` is
> already local** (it is also what `deploy/docker-compose.yml:27` runs, so the harness and the demo
> agree on a version). But **`testcontainers/ryuk` is not present** — 13 local images, none matching
> `ryuk` or `testcontainers`. Testcontainers starts the reaper alongside every container unless
> `TESTCONTAINERS_RYUK_DISABLED=true`, so **the first `dotnet test` after this lands will pull an
> image over the network**, on the dev box and on every CI runner. That is exactly the first-run
> surprise that makes a new gate look flaky. Decide it explicitly in the PR — pre-pull, pin the ryuk
> tag, or disable the reaper and dispose containers in the fixture — and say which. It does not
> change the risk rating; it is why the rating is what it is.

**The in-repo statement of this gap is `EmploymentMigrationTests.cs:8-37`, and P4 owns rewriting
it.** That doc comment already argues P4's case in P4's own words — *"the alternative — a test that
silently skips when no database answers — is how a suite comes to report green while proving
nothing"*, and *"a flaky container blocks every later PR"* — and it names the four things not
covered (`:25-30`). It also contains the inaccuracy corrected under *Ground truth* (`:11-12`,
"every test project uses the EF in-memory provider"). When the harness lands, that comment stops
being true and must be rewritten in the same commit, or the repository's most careful piece of
self-documentation becomes its most misleading one.

**P5 — one user per employee, held by the database.** A **partial** unique index —
`CREATE UNIQUE INDEX … ON identity."Users" ("EmployeeId") WHERE "EmployeeId" IS NOT NULL` — because
most users have none. `SaveGuardingUniquenessAsync` (`UserManagementService.cs:117-131`) currently
maps any non-`Email` constraint name to *"username"*, so without a third branch a duplicate link
would report *"That username is already in use."*; it needs to read `ConstraintName` properly —
the same remedy `PeopleModule.cs:310-313` already has written down for `23503`. The picker
(`users.component.ts:128-134`) filters out employees linked to *another* user, keeping the current
one; the server stays the enforcement point and the screen stops inviting a save it will reject.

**P6 — the leaver vocabulary gets an administration surface, then the gate.**
`POST`/`PUT`/`DELETE /api/leaving-reasons`, beside the existing `GET` (`PeopleModule.cs:326-336`),
plus one Angular administration screen. `DELETE` maps the FK's `23503` to a **409** with legacy's
meaning (*"this reason is in use — retire it instead"*), not a 500. The `Name` index becomes
case-insensitive so WM matches legacy's effective rule (edge case 21) — that is a migration, which
is why this portion sits behind P4. **Then**, in the same portion and only after, the gate:
`EmployedUntil` requires `LeavingReasonId`. Permission is **Open question 1** and this portion is
blocked on it.

**P7 — the panel stops presenting pre-edit state as current.** Two halves, and the split is
deliberate:

- **Now, and independent of 005:** re-fetch diagnostics after a successful save, and label the panel
  for what it is (*"Effective visibility — as saved"*), with a hint that unsaved group changes are
  not reflected. Zero new API surface; survives 005 untouched.
- **After 005 P4, not before:** a real preview —
  `POST /api/access-diagnostics/preview { userId, groupIds }` resolving a scope from a *candidate*
  membership without persisting, which is what the comment at `users.component.ts:265-266` promises
  and is API-first (invariant 2: the Flutter app can ask the same question). Building it now means
  writing a resolver call whose argument shape (`Guid[] groupIds`) becomes a single `groupId` under
  one-membership, and 005 P4 rewrites `DataScopeResolver.cs:25-70` wholesale. That is fighting 005.
  Listed in *Out of scope*, assigned, not lost.

---

## Out of scope for this plan

- **`/api/health/ready` reports "migrated" while the schema is wrong.** *(⚠️ path corrected
  2026-08-18 — it is `/api/health/ready`, `WmHealthChecks.cs:36`. Writing it without the prefix is
  what produced the withdrawn anonymity claim in* Open questions *2.)*
  `src/Api/WM.Api/Infrastructure/WmHealthChecks.cs:129-142` — verified at this commit; the
  `DatabaseReadinessCheck<TContext>` reads `GetAppliedMigrationsAsync().Count()` and
  `GetPendingMigrationsAsync().Count()` into the response body, branches on `pending == 0` alone
  (⚠️ *not* a comparison of the two counts — corrected 2026-08-18) and never touches a column, so it
  answered `Healthy` (`:140-141`) while every employee-list request 500'd with `42703`. That last
  observation is an anecdote from one running instance and the audit could not re-stage it; it is
  not load-bearing — the defect is visible in `:129-142` without it. **This belongs to 006, not to
  009** (reviewer's ruling): P4's harness would *detect* that class of failure in CI, which is not
  the same as a running host telling the truth about itself. **Not added to 006 by this plan** —
  006 is `in-progress, approved by the user 2026-08-06`, and 007 decision 3 settled that an
  unapproved portion is not folded into an approved plan. Proposed text is in *Open questions* 2.
- **Employee-code *uniqueness*.** 007 P3. Both halves confirmed live: the app check refuses `e1030`
  as a duplicate of `E1030` (`PeopleModule.cs:126`) while `IX_Employees_Code` (`PeopleDbContext.cs:47`)
  would accept both, and the comment at `:155` calling the index *"the real guarantee"* is false.
  Referenced, not adopted.
- **`departmentId: null` in the employee modal** (`employees.component.ts:329`). 003 P3, with the
  ⛔ blocker P2b's review added. P3 here must describe it, never fix it. ⚠️ **2026-08-18 (audit):**
  003 P3's Done-when claims **`phone` as well** — see the correction under *Target design* P3. The
  ownership of the phone half is an open decision, not a settled exclusion.
- **`FinalEmploymentDate`, `ResignationDate`, `LeaveNoticePeriodId`.** Schema exists, nothing writes
  them (007 decision 5). P6 deliberately does **not** adopt legacy's mandatory reason + discharge +
  final-date **triple** (`PersonnelModels.cs:1354-1368`): `FinalEmploymentDate` has no editor, so
  mandating it would make the leaver record unreachable for precisely the reason the reviewer ruled
  the gate must not ship before the lookup. Same trap, different field. See *Open questions* 4.
- **A maintenance surface for `LeaveNoticePeriods`.** Its lookup would administer a field nothing
  can set — `EmployeeUpsertRequest` (`PeopleModule.cs:384-397`) has no `LeaveNoticePeriodId`.
  Building the admin screen first would ship a screen with no effect. It goes with whichever portion
  makes the field writable.
- **Field-group write rights.** Plan 004. P6's permission question is a *screen* permission, not the
  per-field model.
- **Everything in plans 001–008.**
- **`SCREEN-TREE.md` rows for P6's two screens.** Another agent is in that file today.

---

## Portions

Ordered cheap-and-independent first. **Independence, stated plainly:**

| Portion | Depends on 005? | Depends on 007? | Depends on anything in this plan? |
|---|---|---|---|
| P1 users list | no | no | no |
| P2 punch code | no | **no** — but shares one file with 007 P3; seam above | no |
| P3 `PUT` rule | no | no — 007 P1 is its worked *example*, not a prerequisite | no |
| P4 harness | no | no | no |
| P5 one user per employee | no | no | **P4** (its migration test) |
| P6 leaver vocabulary | no | **007 P1 only, already merged** | **P4** (its migration test) |
| P7 diagnostics panel | **first half no; second half after 005 P4** | no | no |

P1–P4 can be built in any order or in parallel. Nothing in this plan blocks lane A, lane B or
lane C, and nothing in those lanes blocks P1–P4.

> ✅ **Independence checked line by line, 2026-08-18 (audit). The table above is right about
> *logical* dependency and silent about *textual* overlap, which is the thing that actually costs a
> rebase.** 005 `:88` publishes the exact `users.component.ts` ranges it will rewrite —
> `:138-165`, `:238-241`, `:254-257`, `:267`, `:275-278`, `:289-301`, `:328-329` — so this is
> checkable rather than a guess:
>
> | Portion | Regions it edits | Overlap with 005's published ranges | Verdict |
> |---|---|---|---|
> | **P1** | `users.component.ts` header ~`:19-29`, `:22`, `load()` `:317-323` | **none** | Genuinely independent of 005 and of 007. Build it first. |
> | **P2** | `PeopleModule.cs:401-405`; `dashboard.component.ts:260` | n/a | Independent of 005. Independent of 007 P3 *logically* (different method, same file — the seam note covers it). One ordering note the plan misses: **007 P2 rewrites `PunchService.RecordAsync`** and requires that *"the rejection message does not differ between 'unknown employee' and 'not employed'"* (`007:729-731`). Edge case 1 asserts today's `"Unknown employee code"` text — if 007 P2 lands first, assert the *behaviour* (null → rejected) rather than the string. |
> | **P3** | `PeopleModule.cs:75-88`; `employees.component.ts:291`; new tests | n/a for 005 | Independent of 005 and 007. **Overlaps 003 P3 on `phone`** — see the correction under *Target design*. Also touches `LeaverRecordEndpointTests.cs:410-438`, whose `PortalEditBody` hard-codes `"phone": null` with a comment calling it pre-existing; that helper must change with the modal or the round-trip test pins the bug. |
> | **P4** | new project, `ci.yml`, `EmploymentMigrationTests.cs` | none | Fully independent. |
> | **P5** | `users.component.ts:128-134`, `:224`; `IdentityDbContext`; `UserManagementService.cs:91, 117-131, 144` | frontend **none** (`:138-165` is adjacent, not overlapping); backend: 005 P3 also edits `UserManagementService` | Logically independent — 005 touches roles/groups, P5 touches `EmployeeId`, and the partial index is on a column 005 never reads. Expect a merge conflict in `UserManagementService`, not a design conflict. |
> | **P6** | `PeopleModule.cs`, `PeopleDbContext.cs:33`, new screen | none | Independent of 005. 007 P1 already merged. Note it also *discharges* 007 decision 5's *"**This needs a portion**, alongside the `LeavingReason`/`LeaveNoticePeriods` maintenance surface"* (`007:922-924`) — this is that portion, so 007 should be told when it lands. |
> | **P7** | `users.component.ts:168-173`, `:262-268`, `:281-305` | **`:267` and `:289-301` — direct hits** | Logically independent of 005 (no new API, no resolver change) but it edits **two of the seven regions 005 names**. Whichever lands second rebases by hand. If sequencing matters, P7 is cheap and small — land it before 005 P1, or accept the conflict. |
>
> Short answer to "which are genuinely independent of 005 and 007": **P1, P2, P3 and P4 are, on both
> axes.** P5 and P6 are logically independent with a merge-overlap in one backend file each. **P7 is
> the only one with real textual overlap with 005**, and the plan's *"independent of 005 and can
> ship any time"* should be read as *"needs no 005 decision"*, not *"will not conflict"*.

### [ ] P1 — The users list shows more than the first hundred
**Touches:** `frontend/portal/src/app/pages/users/users.component.ts` (search input + paging
control + `load()` at `:317-323`), `users.component.spec.ts` (new).
**Done when:** the screen has a debounced search box and a paging control bound to the `search` and
`page`/`pageSize` parameters `UserEndpoints.cs:18` and `UserManagementService.cs:42-65` already
accept; the header's `total()` (`:22`) and the rows on screen can no longer disagree about how many
accounts exist. The employees screen is the model — `employees.component.ts:25-28, 122-127,
240-276, 391` is the same pattern, already shipped and reviewed; lift it rather than inventing a
second one.
**Tests:** `ng test` — typing filters and debounces (one request, not one per keystroke); paging
requests the right page; clearing the box restores the unfiltered list. No server change, so no
xUnit test; **say so in the PR** rather than letting a green `dotnet test` imply coverage.
**Risk:** low. Frontend only, one file, a sibling component to copy from.
**✅ Verified 2026-08-18 (audit), and it is better news than the plan assumed.** The repository has
**zero `*.spec.ts` files**, so whichever of P1/P3/P7 lands first writes the portal's first spec —
but the runner is already wired (`angular.json:79-86`: `@angular/build:karma`, `zone.js/testing`,
`tsconfig.spec.json`; karma + jasmine in `package.json:32-40`), **and CI already runs it the moment
a spec appears**: `.github/workflows/ci.yml:105-116` detects `*.spec.ts` and runs
`npm run test -- --watch=false --browsers=ChromeHeadless`, otherwise emits a `::warning`. So there is
no harness to build and no CI step to add — and the first portion to land a spec should delete that
guard's `else` branch, since its comment says *"Remove this guard once specs are routine."*

### [ ] P2 — A punch code is normalised by the server, not the browser
**Touches:** `src/Modules/People/WM.Modules.People/PeopleModule.cs:401-405` (`FindByCodeAsync`
only — see the seam note above), `frontend/portal/src/app/pages/dashboard/dashboard.component.ts:260`,
`src/Modules/People/WM.Modules.People.Tests/`.
**Done when:** `FindByCodeAsync` trims and folds case in the query, so
`POST /api/punches { employeeCode: "e1030" }` resolves employee `E1030`; `Employee.Code` is
unchanged at rest and **no migration is added**; the SPA's `.toUpperCase()` is gone and the punch
still works from the dashboard; the predicate is written `lower(Code) = lower(@code)` so 007 P3's
index makes it sargable without a rewrite.
**Tests:** edge cases 1–8, each named. **6 is the one that matters** — a scope-restricted caller
must still not find an out-of-scope employee by lower-casing the code; write it as a mutation the
reviewer can run (remove `Scoped(...)` and watch it fail). Plus a `ToQueryString()` assertion that
the fold translates to SQL rather than falling back to client evaluation, in the shape of
`EmploymentPredicateTests.cs:171-181`.
**Risk:** low. One method, no schema, and the case that could go wrong (widening scope) has a test
whose failure mode is loud.

### [ ] P3 — Phone survives an edit, and full-replace `PUT`s get a rule
**Touches:** `PeopleModule.cs:75-88` (add `e.Phone` to the list projection),
`frontend/portal/src/app/pages/employees/employees.component.ts:291`,
`employees.component.spec.ts` (new), `src/Api/WM.Api.Tests/Security/` (new inventory test),
`src/Modules/People/WM.Modules.People.Tests/` — **specifically
`Endpoints/LeaverRecordEndpointTests.cs:410-438`**, whose `PortalEditBody` helper hard-codes
`"phone": null` and documents it as pre-existing (⚠️ *named by the audit 2026-08-18; the plan said
only "the tests project"*). It mirrors the SPA payload key for key on purpose, so it must change in
the same commit as `:291` or the round-trip test goes on pinning the defect —
`src/Modules/Identity/WM.Modules.Identity.Tests/`.
**Done when:** editing an employee no longer nulls their phone; **and** the three-layer rule in
*Target design* exists, so the next full-replace `PUT` cannot ship without a round-trip test.
The inventory test must name all **four** current full-replace `PUT`s
(`EndpointAuthorizationInventoryTests.cs:85, 89, 93, 101`) and fail by name when a fifth appears,
exactly as `DeliberatelyAnonymous` does.
**Tests:** edge cases 9–12. The mutation that proves the rule works: revert `e.Phone` out of the
projection and confirm the **mechanical** test fails; separately, revert `openEdit`'s
`phone: e.phone ?? ''` back to `phone: ''` and confirm the **frontend** test fails. If only one of
the two mutations is caught, the rule is half-built and the PR must say which half.
**Risk:** medium — not the fix, which is two lines, but the inventory test. It is new machinery
with a real failure mode: a registry that lists endpoints without asserting anything about them
becomes a rubber stamp. Edge case 12 is what keeps it honest.
**Note:** 007 P1's leaver round-trip is the worked example — `employees.component.ts:296-301` fixed
exactly this bug for `leavingReasonId`/`leaverComments` and left a comment saying why. Cite it in
the PR; the comment three lines above the defect is the strongest argument that a rule is needed
rather than another careful reviewer.

### [ ] P4 — A Postgres test harness, so a migration is tested by something that re-runs
**Touches:** a new test-support project (or shared fixture) wired into `WM.sln`;
`WM.Modules.People.Tests` (its first Postgres test — ⚠️ *not* its first executed-SQL test in the
repository; see the *Ground truth* correction); `EmploymentMigrationTests.cs:8-37` (the doc comment
this portion falsifies); `.github/workflows/ci.yml`.
**Done when:** a migration can be executed `Up` and `Down` against a real `postgres:17-alpine` from
`dotnet test`, on the dev box and on GitHub's runners; the harness **fails rather than skips** when
no container engine answers; and 007 P1's `20260814080315_EmploymentWindowAndLeaverRecord` is the
first migration re-run by it, asserting the four properties its hand-run transcript demonstrates —
one transaction each direction, backfill strictly before `DROP COLUMN "Status"`, `Down` restoring
the schema *including the absence of a `DEFAULT` on `Status`*, and unrecognised status values
failing closed to suspended. The hostile timezone is set on the **database**, not the session; a
session-level `SET` never reaches EF's migrator connection and the test would pass with or without
the `AT TIME ZONE 'UTC'` cast. Prove that by falsification, as the transcript did.
**Tests:** the harness *is* the test infrastructure; what must exist is the re-run above plus a
self-test that the fixture fails loudly with no Docker.
**Risk:** medium-high, and it is CI risk, not code risk. **A flaky container blocks every later
PR** — that is exactly why 007 P1 declined to smuggle it in. Mitigations to state in the PR: one
container per assembly via a collection fixture, an explicit startup timeout, and a measured
before/after CI wall-clock number. If it cannot be made reliable, saying so and reverting is a
successful outcome for this portion; shipping a flaky gate is not.
**Note:** two reviewers have now asked for this to carry a number. It has one.

### [ ] P5 — One user per employee, held by the database
**Touches:** `src/Modules/Identity/WM.Modules.Identity/Data/IdentityDbContext.cs`, a new migration +
snapshot, `Services/UserManagementService.cs:91, 117-131, 144`,
`frontend/portal/src/app/pages/users/users.component.ts:128-134, 224`,
`WM.Modules.Identity.Tests`, and the P4 harness.
**Done when:** a **partial** unique index on `Users.EmployeeId` (where not null) exists and holds
the rule the two `if`s currently only assert; `SaveGuardingUniquenessAsync` reads
`PostgresException.ConstraintName` and names the right field instead of defaulting to *"username"*;
the picker no longer offers an employee linked to another user while still offering the current
one; and the picker no longer stops at 200 employees.
**Tests:** edge cases 13–18. **14 needs two concurrent inserts** — a sequential pair passes today
and proves nothing, the same trap 007 P3's tests carry. **15 runs on the harness**: plant two users
on one employee at the pre-migration schema, run `Up`, and assert it reports and refuses rather
than unlinking one. A frontend test for 17, including the "editing the linked user" case, which is
the one a naive filter breaks.
**Risk:** medium — the migration can fail on real data by design, which is correct and must be
called out in the PR, exactly as 007 P3's is.
**Note:** §13's *"one-to-one enforced"* is what this portion makes true. It is also **not** a TLW
port: legacy is one-to-many and resolves it arbitrarily (`TLW-PEOPLE-MODEL.md` §4.3, 2026-08-18).
The §13 wording correction is *Open questions* 3.

### [ ] P6 — The leaver vocabulary can be filled, and only then does the reason gate the record
**⛔ Blocked on *Open questions* 1 (the permission). Do not start without an answer.**
**Touches:** `PeopleModule.cs:326-336` (grow the lookup endpoints), `Data/PeopleDbContext.cs:33`
(case-insensitive name index) + a migration and snapshot, `PeopleModule.cs:236-245` (the gate in
`Validate`), a new Angular administration screen + route,
`frontend/portal/src/app/pages/employees/employees.component.ts` (a reason selector and a comments
field — the modal has **neither** today), `src/Api/WM.Api.Tests/Security/EndpointAuthorizationInventoryTests.cs`
(new transports fail by name until listed), `WM.Modules.People.Tests`, and the P4 harness.
**Done when:** an administrator can go from an **empty database** to a recorded leaver — reason
created through the UI, selected in the employee modal, comments entered — **without touching
SQL**; and, in that order and in this portion, `EmployedUntil` requires `LeavingReasonId`.
Retiring keeps historical leavers readable; deleting an unused reason succeeds; deleting one in use
is a 409 from the foreign key, not a 500.
**Tests:** edge cases 19–24. **23 is the acceptance test for the whole portion** and must be
written as an end-to-end sequence over the endpoints, from an empty lookup table, because that is
the failure the reviewer's ordering ruling exists to prevent. 21 runs on the harness (it is an
index change). 24 must assert the gate does **not** block un-leaving — legacy's
`SetEmployeesActive:151-173` clears reason and date together, and a gate that fires on the clear
path traps every re-hire.
**Risk:** medium — the largest surface here (endpoints + screen + a validation rule + a migration),
and the one that changes an existing write path. If it grows past ~8 files, split the gate out and
sequence it immediately after; **never before.**

### [ ] P7 — The effective-visibility panel stops showing pre-edit state
**Touches:** `frontend/portal/src/app/pages/users/users.component.ts:168-173, 262-268, 281-305`,
`users.component.spec.ts`.
**Done when:** the panel is labelled for what it shows (*as saved*), says so when the drawer holds
unsaved group changes, and re-fetches after a successful save so reopening never shows stale text;
and the label no longer invites the reading that ticking a **role** changes data scope, which it
never does (`DataScopeResolver.cs:25-69`).
> ⚠️ **Correction, measured 2026-08-18 (audit): one third of that Done-when is already true, and
> saying so keeps the portion honest.** *"Re-fetches after a successful save so reopening never
> shows stale text"* describes a defect that does not exist: `save()` closes the drawer on success
> (`users.component.ts:284`), and `openEdit` clears the panel (`:262`) and re-fetches (`:268`) every
> time. **Reopening already shows saved state — which is edge case 26, correctly recorded as passing
> today.** The live defect is edge case 25 only: within an open drawer, ticking a group changes
> nothing the panel says, while the comment at `:265-266` promises *"an admin can see the effect of a
> change without leaving the drawer."* So this portion is **the label plus the unsaved-changes
> hint**, and a post-save re-fetch is optional belt-and-braces, not a fix. Scoping it as a fix would
> produce a test that passes before the change. `DataScopeResolver.cs:25-69` confirmed for edge
> case 27: scope comes from `UserSecurityGroup` and `EmployeeId`, never from a role.
**Tests:** edge cases 25–27, in `ng test`. 27 is the cheap one that documents a real
misunderstanding: Administrator is a role and roles carry no scope.
**Risk:** low.
**Sequencing:** this half is independent of 005 and can ship any time. **The preview endpoint that
would actually satisfy the comment at `:265-266` is deliberately not here** — it needs
`DataScopeResolver`, which 005 P4 rewrites, and its request shape (`Guid[] groupIds`) collapses to
a single group under one-membership. It belongs to 005 P5 or a follow-on immediately after 005 P4;
raised as *Open questions* 5 so it is assigned rather than forgotten.

---

## Open questions for the user

**1. ⛔ P6's permission — `employees.manage`, or an administration permission of its own?**
CLAUDE.md says propose and stop, so this is proposed and stopped. **P6 cannot start without it.**

*Measured:* legacy puts both lookups under **PersonnelSetup**, not Personnel
(`Web.sitemap:60-71, 87-89`; menu key `Menu_PersonnelGroup_PersonnelSetup_LeaveReasons`) — a
separate screen in a separate area, and under legacy's model that means a separate screen right
(`TLW-AUTHORIZATION-MODEL.md`: rights live per screen in `dbo.AccessControlEntry`).

> ✅ **Re-measured 2026-08-18 (audit): this holds, and the one thing that looked like a
> counter-example is not one.** `Web.sitemap:53-55` *does* carry a `LeaveReasons` node inside the
> **Personnel** group — see the correction in *Legacy behaviour* — but it points at
> `controller="Personnel" action="LeaveReasons"`, and **no such action, view or menu key exists in
> `WebSite`.** It is a stale pointer left behind when the screens moved to Setup. So legacy's
> shipping IA is unambiguous, and the argument for a permission of its own stands on it. Two facts
> the audit adds, both pulling the same way: the reason vocabulary is exposed over REST **read-only**
> in two API versions (`API/V1` and `API/V2/EmployeeLeaveReasonApiController.cs`, `GET` only), and
> the Setup area holds nine sibling vocabularies of the same shape (`Web.sitemap:61-90` —
> custom fields, departments, employment types, job roles, salary change reasons, deduction types,
> entitlement types, currencies, notice periods). Whatever is decided here is the precedent for all
> nine, which is an argument for the named permission rather than against it.

*My recommendation:* **a new `people.lookups.manage`.** Editing the vocabulary the whole estate's
HR reporting is grouped by is an administration act, not an employee edit — and `employees.manage`
is already the permission plan 004 exists to break up, so adding a ninth meaning to it makes 004's
job harder. The cost is one more permission to seed and one more row in
`EndpointAuthorizationInventoryTests`.

*The case against:* on a small install there is no one who holds one and not the other, so it is a
distinction without a difference until 004 lands. If you prefer to defer, say `employees.manage` and
P6 proceeds unchanged apart from the policy name.

**2. Add the readiness finding to 006 as a new portion?** Not done by this plan, because 006 is
approved and 007 decision 3 settled that folding an unapproved portion into an approved plan changes
the shape of something you already signed off. Proposed text, ready to paste:

```markdown
### [ ] P8 — Readiness inspects the schema, not the migration count
**Touches:** `src/Api/WM.Api/Infrastructure/WmHealthChecks.cs:119-149`, `src/Api/WM.Api.Tests/Security/ReadinessEndpointTests.cs`.
**Done when:** `/api/health/ready` fails when the database's schema disagrees with the model, not
merely when a migration row is missing. `DatabaseReadinessCheck` reads the applied and pending
migration counts into the body and then branches on `pending == 0` alone (`:132-142`); it **never
inspects a column**, so it answered `Healthy("migrated")` while every employee-list request 500'd
with Postgres `42703` (undefined_column) — observed 2026-08-18. The check that would have caught
that is to **execute the model's own no-row query per aggregate** (`SELECT … WHERE false`, one per
`DbContext`): it touches every mapped column and costs one round trip. Note what will *not* work,
because it looks like it should: comparing applied migrations against the assembly's migration ids
is what `GetPendingMigrationsAsync()` already does internally, so it is the same check by another
name.
**Tests:** a database at head with a column dropped out of band answers Unhealthy; the message
still leaks no Npgsql text (`ReadinessEndpointTests.cs:60-69` already asserts that and must keep
passing); the endpoint stays inside the rate-limit bucket 006 P3 gave it.

> ⚠️ **Correction WITHDRAWN — it was itself wrong. Re-measured 2026-08-18 (audit).**
>
> A correction was added to this portion on 2026-08-18 claiming the endpoint *"is not anonymous
> today — `GET /health/ready` answers **401**"*, and inferring a second, higher-priority defect from
> it. **The 401 is real but the endpoint is not the cause: the path is wrong.** The readiness
> endpoint is `/api/health/ready`, with the `/api/` prefix (`WmHealthChecks.cs:36`), because that is
> the only prefix the portal's nginx proxies. `GET /health/ready` — no prefix — matches **no route
> at all**, and the repository has a test that says exactly what happens then:
>
> > `FallbackPolicyTests.cs:118-131` — `A_path_that_matches_no_endpoint_challenges_instead_of_answering_404`
> > *"the authorization middleware applies it to unmatched requests too, so an anonymous caller can
> > no longer probe which routes exist."* Anonymous → **401**; authenticated → 404.
>
> So a 401 from `/health/ready` is 003 P2a's default-deny working correctly on a typo, not the
> readiness endpoint refusing a probe. **`/api/health/ready` is anonymous**, on four independent
> pieces of evidence:
>
> 1. `PlatformEndpoints.cs:54-62` maps it with `.AllowAnonymous()` and a comment explaining why.
> 2. `ReadinessEndpointTests.cs:114-124` —
>    `Readiness_answers_an_anonymous_caller_rather_than_challenging_one` asserts no
>    `WWW-Authenticate` and `NotEqual(401)`. **Run 2026-08-18: passes** (13/13 in that file plus
>    `EndpointAuthorizationInventoryTests`).
> 3. `EndpointAuthorizationInventoryTests.cs:39-57` names it as the **fifth** deliberately-anonymous
>    transport, *"Grown by one on 2026-08-06 (plan 006 P2), deliberately"*, and
>    `The_anonymous_surface_is_exactly_the_one_we_decided_on` pins the set at five. So the withdrawn
>    text's claim that 003 P2a *"fixed the anonymous surface at exactly four transports … and never
>    added `/health/ready`"* is contradicted by the very test it appealed to.
> 4. `src/Api/WM.Api/Dockerfile:43-44` — `HEALTHCHECK … CMD wget -qO- http://127.0.0.1:8080/api/health/ready`,
>    plain `wget`, no credentials. Were the endpoint challenged, every container would report
>    unhealthy.
>
> **Nothing about this portion changes except that it gets simpler.** The endpoint *is* reachable by
> the things meant to consult it; the only defect here is the one the portion was written for — it
> reports `Healthy("migrated")` while the schema disagrees. The original "stays anonymous" wording
> was right. Restored below.
>
> ⚠️ **Second, smaller correction to this portion's own text, same audit.** The Done-when offers two
> remedies and **the second is a no-op**: *"compare `GetAppliedMigrations()` against the assembly's
> migration *ids* rather than their count"* is precisely what `GetPendingMigrationsAsync()` already
> computes — it returns the assembly's migrations minus the history table's rows, so `pending == 0`
> **is** the id comparison. (Note also that `:132-142` does not "compare applied against pending":
> it reads both counts for the response body and branches on `pending == 0` alone.) The real remedy
> is the first one only: **execute the model's own no-row query per aggregate**, which is the sole
> check that would have caught the `42703`. Delete the second option before building.

**Authorization:** unchanged — `/api/health/ready` **stays anonymous**, the fifth entry in
`EndpointAuthorizationInventoryTests.DeliberatelyAnonymous` (`:50-57`). No inventory-test edit, no
new decision to take.
**Risk:** low for the schema check itself — but the endpoint is anonymous, uncached and public
(006 P3 put it in the sign-in rate-limit bucket for exactly that reason), so a per-request schema
probe must stay cheap. One no-row `SELECT` per `DbContext`, not a catalogue crawl.
```

**3. Two `ARCHITECTURE.md` corrections, propose-only.** Both follow from `TLW-PEOPLE-MODEL.md`
§4.3's 2026-08-18 measurement. §1–§12 are design sections; §13 is being edited by
[#62](https://github.com/00008550/WM/pull/62) right now, so neither was applied.

```diff
 ARCHITECTURE.md:172
-- **Not every employee has a login** … The link is optional and one-to-one.
+- **Not every employee has a login** … The link is optional and one-to-one. **One-to-one is a WM
+  decision, not a TLW port** — legacy's `User_Employee` association is an `EntitySet<Employee>`
+  (`HorioDB.designer.cs:7834-7845`) and it resolves the many-link with two separate unordered
+  `FirstOrDefault()` calls that can disagree (`AuthorizationService.cs:1091-1104`, `:1106-1115`).
```

```diff
 ARCHITECTURE.md:444  (§13 — coordinate with #62; ⚠️ was cited as :446, corrected 2026-08-18 —
                       :446 is the "User types Administrator/Employee/Manager" row)
-| User ↔ employee linking … | ✅ built — `User.EmployeeId`, one-to-one enforced, `/api/me/employee` |
+| User ↔ employee linking … | ⚠️ built — `User.EmployeeId`, `/api/me/employee`. One-to-one is
+  checked in `UserManagementService.cs:91, 144` and **held by no index**; the picker offers
+  already-linked employees. A WM improvement (TLW is one-to-many), closed by 009 P5. |
```

**4. Does P6 also adopt legacy's mandatory reason + discharge + final-date triple?** My
recommendation is **no, not yet**, and the reason is the reviewer's own ordering rule applied to a
different field: `FinalEmploymentDate` is a stored column with **no writer and no editor** (007
decision 5), so mandating it would make the leaver record unreachable exactly as gating on an empty
lookup would. The triple belongs with the portion that makes `FinalEmploymentDate` writable. Say
otherwise and P6 grows an editor for it.

**5. Where does the diagnostics *preview* endpoint land — 005 P5, or a follow-on right after
005 P4?** P7 ships the honest-labelling half now. The preview is what the comment at
`users.component.ts:265-266` actually promises and what invariant 2 wants (a Flutter admin screen
should be able to ask "what would this grant?"), but its request shape depends on whether membership
is a list or a single value, which 005 decides. Assigning it to 005 P5 costs 005 a small growth;
assigning it to a follow-on costs one more numbered thing to track. Your call.
