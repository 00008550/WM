# 011 — Production readiness: the machinery this repository claims and does not have

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 *"Running workstream (not a phase): on-prem rollout tooling"*, plus the
rows of §2's tech-stack table that are **promised and unbuilt** (`Observability | OpenTelemetry`,
`Cache / realtime backplane | Redis`, `RabbitMQ … outbox`).

**Legacy sources surveyed:** deliberately almost none, and the plan says so. This survey's ground
truth is **this repository**, because the findings are about WM's own production machinery, which
TLW has no analogue for. Legacy was opened for exactly one question — *does TLW do anything about
two people editing the same record?* — and the answer changed this plan. Files opened:

- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — measured for `UpdateCheck` and `IsVersion`
  across all 579 `TableAttribute` mappings / 8,173 `ColumnAttribute`s (script and results in
  *Legacy behaviour* below)
- `E:\Tlw\Source\Logic\**` — searched for `ChangeConflictException` / `ConflictMode` /
  `RefreshMode` / `ResolveAll`. **Zero hits.**

Everything else below is measured against `master`-as-of-`b3f91d2` plus the merged 007 P1
(`ead3bfc`), on branch `docs/queue-catchup`.

---

## Why this plan exists

The user had WM audited against an external senior-engineer checklist — idempotency, transactions
and concurrent access, background jobs and queues, caching, observability, testability, schema
migrations and backward compatibility. The audit rated the access-model and modularity work strong
and produced eight findings. **Each was re-measured by this survey before being planned**, per the
standing rule that a claim without `file:line` is a hypothesis. Two were materially wrong, one was
already owned by an approved portion, and one turned out to be a live defect the audit understated.

---

## Ground truth

### The repository, measured

| Measure | Value | How |
|---|---|---|
| Tracked C# files in `src/**` | **99** (13,525 lines) | `git ls-files 'src/**/*.cs'` |
| Of those, test files | **26** | `git ls-files 'src/**/*Tests*.cs'` |
| Test projects | **4** — `WM.SharedKernel.Tests`, `WM.Modules.Identity.Tests`, `WM.Modules.People.Tests`, `WM.Api.Tests` | `git ls-files 'src/**/*.csproj'` |
| Modules with **no** test project | **1 of 3** — `WM.Modules.TimeAttendance` | ditto |
| Frontend TypeScript files | **22** | `git ls-files 'frontend/portal/src/**'` |
| Frontend `*.spec.ts` files | **0** | ditto |
| Occurrences of `Idempot\|ConcurrencyToken\|RowVersion\|BeginTransaction\|FromSql\|Dapper\|IDistributedCache\|IMemoryCache\|xmin` in `src/**` | **0** | `Grep`, gitignore-aware |
| Module→module `ProjectReference`s | **1** — TimeAttendance → People | `grep ProjectReference` across all `.csproj` |
| Mechanical checks enforcing invariant 1 | **0** | — |

> **Correction to CLAUDE.md, applied.** The *Commands* section says *"`src/SharedKernel/WM.SharedKernel.Tests/`
> and `src/Modules/Identity/WM.Modules.Identity.Tests/` (xUnit) are currently the only test
> projects."* There are **four**; `WM.Api.Tests` landed with 003 P2a and `WM.Modules.People.Tests`
> with 003 P2b. The warning the paragraph exists to give is still exactly right — TimeAttendance,
> the module that owns the punch, still has none — so the sentence is corrected rather than
> deleted. *(CLAUDE.md is an invariants file; this is a factual count inside an advisory paragraph,
> not an invariant. Flagged in* Open questions *in case the user wants it reverted.)*

### What ARCHITECTURE.md already rules

This matters more than any individual finding, because three of the eight were handed to me as
*"raise a design proposal"* and turned out to be **already decided in WM's favour**. I do not need
to propose them; the design already promises them and the code does not deliver them. That reclassifies
them from *design question* to *defect against the stated design*.

| Concern | ARCHITECTURE.md says | Line | Consequence |
|---|---|---|---|
| Observability | `Observability \| OpenTelemetry, Serilog` | `:100` | **Not an open question.** OTel is promised. Its absence is a gap against §2, and F6 needs no proposal. |
| Transactional outbox | `RabbitMQ via MassTransit (retry, outbox, sagas)` | `:93` | Promised for the **RabbitMQ** leg. |
| | *"MassTransit gives all of that plus a **transactional outbox**, so a database commit and its message can never diverge."* | `:264` | The **intent** is unambiguous. |
| | Cross-cutting in SharedKernel: *"audit event emission, multi-tenancy, **outbox**, event stream contracts"* | `:797` | It has a designated home. |
| Redis | `Cache / realtime backplane \| Redis`; in the diagram as *"(cache, …)"*; in the default ship list | `:92`, `:67-68`, `:690` | **Removing it contradicts the design.** It has a designed job. It does not have a caller. |
| Multi-tenancy | *"~~Multi-tenancy~~ — **decided: database per customer, deployed on the customer's own server**"* | `:914` | **Confirmed out of scope.** Recorded here so the question stops being asked. |
| Graceful degradation | *"publish failures never fail a user request (already implemented: `KafkaEventStreamProducer` logs and continues)"* | `:703` | The fire-and-forget Kafka producer is **deliberate and ratified**. Not to be reversed. |
| Bounded Kafka retention | *"must ship with v1"* | `:701` | ✅ **Already shipped** — `docker-compose.prod.yml:73-77` sets `KAFKA_LOG_RETENTION_HOURS` and `_BYTES` with documented defaults. A sub-claim I expected to confirm and instead disproved. |
| Consumer lag on the health endpoint | *"must ship with v1"* | `:702` | ▢ Not built. Folded into P6. |

### Where 011 sits in the lane structure

`STATE.md` describes three lanes: **A** the access model (003/005/001/004), **B** the demo platform
(006), **C** the person record (007). 011 is **lane D — the platform's production machinery**.

It is the most orthogonal lane yet: six of its eight portions touch no file any other lane opens
(`README.md`, `.github/workflows/ci.yml`, `deploy/*.yml`, `Program.cs`, `SharedKernel`, a new
architecture-test project, the frontend spec harness). The two that are not orthogonal are P5 and
P8, and both are sequenced below rather than left to collide.

---

## Legacy behaviour (what we are replacing)

Only one question was asked of legacy, and it was the right one to ask.

### TLW has optimistic concurrency on almost every table, and handles the conflict nowhere

LINQ-to-SQL's `ColumnAttribute.UpdateCheck` defaults to **`UpdateCheck.Always`**: every mapped
column is compared in the `WHERE` clause of an `UPDATE`, so a row changed since it was read fails to
update and `SubmitChanges` throws `ChangeConflictException`. Measured across
`HorioDB.designer.cs`:

```
UpdateCheck.Never       356 columns   (46 tables carry at least one)
UpdateCheck.WhenChanged  10 columns
IsVersion=true            0 columns   (no rowversion anywhere in the schema)
```

Against 8,173 columns, that is **~99.5% of the schema running full-column optimistic
concurrency**. Per table:

| Table | `Never` / total | Reading |
|---|---|---|
| `dbo.Clockings` | **249 / 249** | The daily aggregate is **completely exempt**, deliberately. |
| `dbo.ManualTimesheets` | 39 / 41 | Same shape — a recalculated/overwritten record. |
| `dbo.Employees` | **5 / 153** | The employee record **is** concurrency-checked. |
| `dbo.Role` (the security group) | **0 / 6** | Fully checked. |

And then: `grep -rn "ChangeConflictException\|ConflictMode\|RefreshMode\|ResolveAll" Logic/`
returns **nothing**. Every call site is a bare `db.SubmitChanges()` (79+ of them across
`Logic/AccessControl/`, `Logic/Activities/`, `Logic/Clockings/` alone).

**So legacy detects the conflict and then crashes on it.** The losing manager gets an unhandled
exception, not a message. That is the baseline WM owes something better than — and note that WM is
currently *worse*: it does not detect the conflict at all.

The `Clockings` exemption is the second half of the finding and it is a gift to plan **002**: the
one table WM's replay design is modelled on is the one table legacy explicitly exempts from
concurrency checking, because a recalculated aggregate is overwritten wholesale and a column-by-column
check would conflict against itself on every recalculation. That reasoning transfers directly.

---

## The eight findings, verified

Stated as the audit measured them; **confirmed**, **corrected** or **disproved** by this survey.

### F1 — the README misdescribes the system · **CONFIRMED, with one sub-claim disproved**

`README.md` measured against the tree:

| README says | Line | Truth | Source |
|---|---|---|---|
| ".NET 9 modular monolith" | `:3` | **.NET 10** | `Directory.Build.props:3` = `net10.0` |
| "ASP.NET Core 9 minimal APIs" | `:7` | ASP.NET Core 10 | ditto |
| "EF Core 9 + PostgreSQL 17" | `:9` | **EF Core 10.0.10** | every `.csproj`, e.g. `WM.Modules.People.csproj:3` |
| "Angular 19 standalone + signals" | `:14` | **Angular 22.1** | `frontend/portal/package.json` `@angular/core: ^22.1.0` |
| `../TlwNext/docs/ARCHITECTURE.md` | `:3` | **A path outside this repository.** The file is `docs/ARCHITECTURE.md`. | — |
| "see architecture doc §11" for phases | `:43` | §11 is *"Frontend — Control Room design system"*. The roadmap is **§14**. | `ARCHITECTURE.md:365, 519` |
| "Next: … **device gateway**, Flutter app" | `:43` | **Contradicts CLAUDE.md invariant 3** and §14 decision 3 — devices are out of scope by decision 2026-07-20. | `CLAUDE.md:33` |
| "Dev login: `admin` / `Admin!234567` (seeded, Development only)" | `:32` | ✅ **Still true.** | `appsettings.Development.json` `Bootstrap:AdminPassword`; `.dockerignore:12` keeps it out of the image |

Seven errors in a 43-line file, in the first file anyone opens. The version drift dates from
[#14](https://github.com/00008550/WM/pull/14), which moved the repo to .NET 10 / Angular 22 and did
not touch the README.

### F2 — nothing in `src/**` is idempotent · **CORRECTED — half of it is already an approved portion**

The grep result is **stronger** than the audit stated: `Idempot|ConcurrencyToken|RowVersion|
BeginTransaction|FromSql|Dapper|IDistributedCache|IMemoryCache|xmin` returns **zero** matches in
`src/**`, not one. (The audit's "one hit in a test host" was the word *idempotent* in a comment at
`Realtime/AttendanceAudience.cs:54`, describing an in-memory SignalR group registry — not a domain
write.)

But the two stated consequences both need correcting:

**F2a — the punch. Already owned by 007 P2, which is approved.** `PunchService.RecordAsync`
(`PunchService.cs:30-62`) does build and save a `Punch` with no dedup key, and
`TimeAttendanceDbContext.cs:18-19` declares two **non-unique** indexes and no unique constraint, so
a retry does insert a second row. That is real. It is **not unowned**: plan 007 P2 explicitly says
*"P2 lands the **dedupe guard** — a same-direction punch within a configured window is accepted
idempotently and returns the existing punch rather than creating a second one"*
(`007-the-person-record.md:817-819`). 011 must not re-plan it.

  **What 007 P2 does not cover, and 011 P8 does.** A time-window guard on (employee, direction) is a
  *heuristic sized for a double-click* — 007 P2's own table sizes it in **seconds**
  (`:804`). It cannot cover the case CLAUDE.md invariant 3 makes normal: **an offline queue flushed
  hours later.** A phone that captured an IN at 08:00, lost signal, and replays at 14:00 sends a
  punch whose `Timestamp` is 08:00 but whose arrival is outside any sane seconds-wide window, and
  whose *correct* behaviour is "you already have this one" — not "here is a second one" and not "a
  duplicate, discarded". Only a client-supplied key answers that. The two mechanisms are
  complementary and P8 says so.

**F2b — the payroll consumer. Confirmed, but latent, not live.** `RunPayrollExportConsumer` does
receive `command.RequestId` and never dedups on it. Two corrections to the severity:

- `RequestId` *is* used — as the output directory name (`RunPayrollExportConsumer.cs:33`) and as the
  correlation id on `PayrollExportCompleted` (`:21, :48`). Just not for dedup.
- **Nothing in `src/**` ever sends a `RunPayrollExport`.** A `Grep` for
  `RunPayrollExport|IPublishEndpoint|ISendEndpoint|IBus` across `src/` finds the consumer, its
  registration (`Program.cs:22`) and the contract — **and no producer anywhere**. The API cannot
  dispatch this job. So the consumer is unreachable scaffolding today.

  It is still worth fixing, and the reason is `Program.cs:30-31`:
  ```csharp
  cfg.UseMessageRetry(r => r.Intervals(
      TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)));
  ```
  **Retry is configured; dedup is not.** The retry policy actively guarantees the double-run the
  moment a producer exists. And the side effect is not the file write (`Directory.CreateDirectory`
  is idempotent, `File.WriteAllBytesAsync` overwrites) — it is `plugin.ExportAsync` at `:29`,
  arbitrary third-party code from a business model of *~60 per-customer payroll plugins*
  (CLAUDE.md invariant 6), several of which will inevitably push to an SFTP or a payroll API.
  Re-running that is not idempotent by any assumption we get to make.

**One thing the audit's blanket statement flattens.** Insert-race handling *does* exist, correctly,
in two places, and they are the pattern to copy rather than invent:
`UserManagementService.SaveGuardingUniquenessAsync` (`:117-131`) and `PeopleModule.cs:149-160` both
catch `PostgresException { SqlState: "23505" }` and convert it to a 409. The comment at
`UserManagementService.cs:112-115` states the principle exactly: *"the checks above race with
concurrent requests, so the database constraint is the real guarantee."* **Idempotency** is absent
everywhere; **write-race handling** is not.

### F3 — punch persistence and event publication are a dual write · **CONFIRMED, and worse than stated**

`PunchService.cs:54-59` — `SaveChangesAsync` then `eventStream.PublishAsync`, no transaction, no
outbox. `KafkaEventStreamProducer.PublishAsync` (`EventStreamProducers.cs:38-61`) is fire-and-forget
with a swallowed error callback. All confirmed. Four things the audit did not measure, which
together change what the fix has to be:

1. **`Acks = Acks.Leader`** (`:32`), not `Acks.All`. A *successfully* produced message is lost on a
   leader failover, not only on an outage.
2. **The error callback only logs a warning** (`:52-53`). No dead-letter, no local spool, no counter.
3. **`Dispose` flushes for 2 seconds** (`:65`). Anything still queued on shutdown is dropped silently.
4. **The worst shape is the config one.** If `Kafka:BootstrapServers` is unset or misspelt, the
   constructor logs **one warning at startup** and sets `_producer = null` (`:22-27`); every
   subsequent publish returns `Task.CompletedTask`. An on-prem install with a typo runs forever with
   no event stream and **no ongoing signal at all**.

**The gap is not that events can be dropped — §13A:703 ratifies that deliberately. The gap is that
a dropped event is neither replayable nor countable.** Those are two different fixes and this plan
separates them: the **count** is P6 (cheap, no design question, and it makes the outage visible);
the **replay** is P7.

**On the accepted-risk-vs-outbox choice the audit offered:** ARCHITECTURE.md has already ruled
(`:93, :264, :797`), so this is not mine to re-open. But the *stated rationale* for fire-and-forget
— *"a broker outage must never fail the user's request"* — **does not argue against an outbox**. It
argues against a *synchronous produce*. An outbox writes a row inside the same transaction as the
punch and never touches the broker on the request path, which serves that goal strictly better than
today's code does. The genuine open question is narrower and is listed at the end: §2's outbox
promise is worded around **MassTransit/RabbitMQ**, and the punch's dual write is **Kafka**.

### F4 — Redis is deployed and unused · **CONFIRMED, and it is a live availability defect, not hygiene**

No `.csproj` references any Redis client and `Grep` for `StackExchange|IDistributedCache|
IMemoryCache|Redis` over `src/**` returns nothing but compiled binaries. Confirmed.

The audit called this "a false claim about the system". It is more than that:

```yaml
# deploy/docker-compose.prod.yml:92-94
    depends_on:
      postgres: { condition: service_healthy }
      redis:    { condition: service_healthy }
```

**`wm-api` will not start until Redis reports healthy** — for a service that never opens a
connection to it. On a customer's unattended box (§13A), a Redis container that fails its
healthcheck after a reboot takes the whole product down for no benefit. The same file passes
`Redis__ConnectionString: redis:6379` (`:103`) to a host that reads no such key, and then, three
lines below, comments *"Brokers are not a hard dependency: the API starts and serves even if Kafka
or RabbitMQ are still coming up"* (`:106-107`). **The file contradicts itself about the one
component with no consumer.**

Removing Redis outright contradicts §2:92 and §13A:690, so P4 does not propose that unilaterally —
see *Open questions* 2. It does, unconditionally and regardless of that answer, remove the
`depends_on` and the dead environment key, because a hard dependency on an unused service is wrong
under every option.

**There is a real job waiting for it,** which is why "delete it" is not obviously right:
`AttendanceConnectionRegistry` (`:27`) and `AttendanceAudience` (`:50`) hold SignalR connection
state in in-process `ConcurrentDictionary`s. A second API replica silently breaks the scoped punch
feed — 003 P1's whole guarantee. Today the deployment is one container per customer (§13A), so it
is not wrong yet; it is *unmarked*.

### F5 — the frontend has no tests at all · **CONFIRMED verbatim, plus one thing that changes its priority**

Zero `*.spec.ts` under `frontend/portal/src` (22 `.ts` files, none a spec). `ci.yml:105-116` detects
this and emits `::warning title=No frontend tests`, with a comment saying the first spec harness is
queued work. All exactly as stated.

Two measurements the audit did not make:

- **The harness is configured and has never been executed.** `angular.json` declares the
  `@angular/build:karma` builder with `tsConfig: tsconfig.spec.json`; `tsconfig.spec.json` exists and
  includes `src/**/*.spec.ts`; karma, jasmine and `karma-chrome-launcher` are all in
  `devDependencies`. So the work is not "build a harness" — it is "prove the configured one runs,
  headless, on a GitHub runner". Smaller than it sounds, and with a known failure mode nobody has hit
  yet.
- **⚠️ 009 P3 lands a spec into that unexercised harness.** Its *Touches* names
  `employees.component.spec.ts (new)` and its *Tests* line requires that reverting a frontend change
  makes "the **frontend** test fail". The moment that file exists, `ci.yml`'s guard flips from
  `::warning` to `npm run test -- --watch=false --browsers=ChromeHeadless` — a command that has
  never run in CI — **inside the PR that is trying to fix a phone field**. That is a merge blocked on
  debugging Chrome in a container, attributed to the wrong portion. **011 P2 must land before
  009 P3.** This is the single most useful sequencing fact in the survey.

### F6 — observability stops at Serilog console plus health checks · **CONFIRMED; and it is a defect against §2, not a design proposal**

`Program.cs:18-21` — `UseSerilog` with `.WriteTo.Console()` and nothing else. No OpenTelemetry
package in any `.csproj`, no meter, no activity source, no exporter. Health checks are real
(`AddWmHealthChecks()`, `:48`, shipped by 006 P2).

The audit asked me to check ARCHITECTURE.md before treating this as a gap. **§2:100 promises
`Observability | OpenTelemetry, Serilog`.** So this is the "promised but unbuilt" category, and F6
needs no design proposal from me. §13A:702 additionally makes *"consumer lag exported to health
endpoint"* a **v1 requirement**.

One consequence worth stating because it is specific to WM's deployment model: console-only logging
on an on-prem box means logs live and die in the container's stdout, on a server §13A says *"we
cannot reach"*, while §13A:764 lists *"version + health reporting back to the vendor"* as a
deliverable. The gap is not aesthetic.

### F7 — the modular-monolith boundary is enforced by review only · **CONFIRMED, and there is already an open door**

No architecture test exists. And the reference graph has one module→module edge:

```
src/Modules/TimeAttendance/WM.Modules.TimeAttendance.csproj
  → ..\..\People\WM.Modules.People\WM.Modules.People.csproj
```

**The good news, and it is genuinely good:** the *code* is clean today. Every cross-module `using`
in a module is `WM.Modules.People.Contracts` — three of them, in `PunchService.cs:2`,
`TimeAttendanceModule.cs:7` and `PunchSeeder.cs:3`. Nobody has abused the reference. But nothing
would fail if they did: `using WM.Modules.People.Data;` compiles, and `PeopleDbContext` is `public`.
The invariant CLAUDE.md calls *"the most common violation"* is held up by author discipline alone.

**And the test fails on day one, for a reason worth fixing.** `Contracts/EmployeeDirectory.cs:1`
opens with `using WM.Modules.People.Domain;`, and the contract's public surface returns domain
types:

```csharp
public EmployeeStatus StatusOn(DateOnly on) =>            // EmployeeStatus is in ...People.Domain
    Employment.StatusOn(IsSuspended, EmployedFrom, EmployedUntil, on);   // so is Employment
```

`EmployeeStatus` (`Domain/Employee.cs:83`) and `Employment` (`:114`) are an enum and a static
function — neither is an *entity*, so this is not the "shared entity type" CLAUDE.md forbids. But
they sit in `Domain`, so the only rule simple enough to be enforceable — *"a module may reference
another module's types only from its `.Contracts` namespace"* — has an exception on the day it is
written. P3 removes the exception by moving both types to `Contracts`, which is where a type that is
part of a published contract belongs anyway.

### F8 — no optimistic concurrency on any entity · **CONFIRMED, and it is the one thing here that is actually bleeding**

`PeopleModule.cs` `MapPut("/{id:guid}")` (`:164` onward) loads the employee, mutates it, and saves.
No version column, no `WHERE` predicate on a token, no 409. Two managers editing the same employee:
last write wins, silently, and the loser is never told. No entity in `src/**` carries a concurrency
token — `Entity` is `{ Guid Id }` and `AuditableEntity` adds four audit columns
(`SharedKernel/Domain/Entity.cs`), neither a version.

**Legacy's answer changes the framing** (measured above): TLW runs full-column `UpdateCheck.Always`
on 148 of `dbo.Employees`' 153 columns, so it **detects** this conflict — and handles it nowhere, so
it surfaces as an unhandled `ChangeConflictException`. WM is currently *behind* legacy on detection
and ahead of it on not-crashing. The target is to be ahead on both.

**Why a single row version, and not legacy's per-column check.** Full-column comparison lets two
managers editing *different* fields both succeed. That would be the better trade-off — except WM's
API shape forecloses it: all four employee/user mutation endpoints are **full-replace `PUT`s**
(`009-what-the-running-app-does.md:718-719` names them at
`EndpointAuthorizationInventoryTests.cs:85, 89, 93, 101`). A full-replace `PUT` from a stale form
does not edit one field; it writes *every* field, so a "disjoint edit" cannot exist and a stale form
silently reverts the other manager's work. That is 009 P3's `phone` bug generalised, and a row
version is exactly the right shape for it.

---

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Fire-and-forget Kafka publish off the request path | **Keep** | §13A:703 ratifies it, and it is right: a broker outage must not fail a punch. An outbox *strengthens* this, it does not reverse it. |
| A dropped event being silent and unreplayable | **Improve** | Right goal, no recovery and no counter. → P6 (count) + P7 (replay). |
| `Acks = Acks.Leader` | **Improve** | Acknowledges before replication. With an outbox in front, `Acks.All` costs the request nothing. → P7. |
| `_producer = null` on missing config, one warning, silent forever | **Invert** | A misconfigured event stream must be **loud and ongoing**, not a startup whisper. Fail the readiness check outside Development, or count every skipped publish. → P6. |
| Legacy's full-column `UpdateCheck.Always` on `dbo.Employees` | **Improve** | The *detection* is right and WM lacks it. The *granularity* is wrong for WM's full-replace `PUT`s, and the *handling* (crash) is wrong outright. → P5. |
| Legacy exempting `dbo.Clockings` (249/249 `Never`) from concurrency checks | **Keep** | Correct for a wholesale-recalculated aggregate. Carry the reasoning into plan **002** rather than re-deriving it. |
| Redis as a hard `depends_on` for a service that never connects | **Invert** | An unused component must never be able to prevent startup. Unconditional in P4. |
| Redis in the design as cache + SignalR backplane | **Keep** (the design) | §2:92. It has a real future job the moment there are two replicas. |
| Redis shipped in every compose file with no consumer | **Improve or Drop** | User's call — *Open questions* 2. |
| `UseMessageRetry` configured with no dedup | **Improve** | Retry without idempotency is a guarantee of double-execution, not resilience. → P8. |
| `Contracts` returning `Domain` types | **Improve** | Makes the only enforceable module rule unenforceable. → P3. |
| Invariant 1 enforced by reviewer instruction | **Improve** | The reviewer prompt saying *"every single time; it is the most common violation"* is an admission. → P3. |
| CI emitting `::warning` instead of running `ng test` | **Improve** | Correct behaviour for an empty suite; wrong to leave standing. → P2. |
| Migrate-on-start (`Program.cs:96-98`) | **Keep** *(with a caveat)* | §13A:740 makes it the on-prem upgrade mechanism, and it is the right call. But see *Open questions* 4 — it collides with §13A:739's rollback promise, and nothing in the repo records or enforces the discipline that reconciles them. |
| Multi-tenancy | **Drop** | §14 decision, `:914`. Recorded here so it stops being asked. |
| A React/frontend-framework migration | **Drop** | Out of scope by the user's direction. The SPA stays Angular 22. |
| Anything device-related | **Drop** | CLAUDE.md invariant 3. |

---

## Edge cases

The behaviour each portion has to get right, and which are testable today.

1. **Retry across the offline boundary.** A phone captures IN at 08:00, loses signal, replays at
   14:00. 007 P2's seconds-wide window does not fire. The correct answer is *"already recorded"*
   with the original punch returned — not a duplicate, and not a rejection. (P8.)
2. **Two different requests, same idempotency key.** A client reuses a key with a different payload.
   Must be a `422`/`409`, never a silent replay of the first answer. (P8.)
3. **Same key, concurrent arrival.** Two in-flight requests with one key must not both insert. The
   unique index is the guarantee, not the pre-check — the pattern already correct at
   `UserManagementService.cs:117-131`. (P8.)
4. **Key retention.** Keys cannot accumulate forever on an unattended on-prem box (§13A). A retention
   window must be stated and enforced, and the behaviour *after* expiry defined (a very late replay
   creates a second punch — is that acceptable?). Named in *Open questions* 5.
5. **Concurrent edit, disjoint fields.** Manager A edits `phone`, Manager B edits `jobTitle`, both
   from the same load. Under a row version the second gets a **409**. Confirm that is wanted before
   building it — under full-replace `PUT`s the alternative is silent loss, not a merge. (P5.)
6. **Concurrent edit, same field, same value.** Two managers set the same phone number. A row version
   still 409s the second. Acceptable, but the message must not read as data loss.
7. **The 409 must say what changed.** Legacy crashes; a bare 409 is only marginally better. The
   response should carry enough for the SPA to say *"someone changed this while you were editing"*.
8. **A version token must not become a scope oracle.** A 409 on a record the caller cannot see would
   confirm its existence — the same class of leak as the still-open 409 enumeration oracle recorded
   in `STATE.md`. The scope check must precede the version check, exactly as 003 P2b's
   `A_create_outside_the_callers_scope_is_refused_before_the_code_is_probed` pins the ordering for
   `POST`. (P5.)
9. **Kafka unconfigured vs Kafka down.** These are different failures with the same silence today.
   Unconfigured is a deployment error; down is expected and transient. They must be
   distinguishable in metrics. (P6.)
10. **Broker down for longer than the outbox drain window.** The outbox table grows. It needs a
    bound, a lag metric and a documented behaviour at the bound — the same discipline §13A:701
    already applies to Kafka retention. (P7.)
11. **Outbox replay ordering.** Punches for one employee must not be delivered out of order after an
    outage. Kafka partitions by key (`employee.Code`, `PunchService.cs:57`), so the drain must
    preserve per-key order. (P7.)
12. **Outbox at-least-once meets consumers.** The outbox guarantees delivery, not uniqueness.
    Consumers must tolerate a repeat — which makes `PunchRecorded` carrying `punch.Id` (`:58`) the
    dedup handle downstream, and worth stating rather than leaving implicit. (P7.)
13. **The architecture test must not fire on the composition root.** `WM.Api` legitimately references
    every module's `.Data` namespace — `Program.cs:7,9,11` for migrations, `WmHealthChecks.cs:5-7`
    for readiness. The rule is "module → module", not "anything → anything". (P3.)
14. **The architecture test must not fire on tests.** `WM.Api.Tests/Security/ApiTestHost.cs:20-27`
    composes the whole host. Test projects are exempt; the exemption must be explicit, not incidental. (P3.)
15. **Karma on a headless runner with no display.** The failure mode P2 exists to find before 009 P3
    does. It must fail loudly rather than skip — the same principle 009 P4 applies to Docker.
16. **A frontend test must be able to fail.** A spec that asserts nothing passes forever. Each first
    spec must be proven by mutation, per the repo's standing practice.

---

## Target design in WM

**P1** — documentation only.

**P2** — `frontend/portal/src/app/core/auth/*.spec.ts` first (pure, security-relevant, no DOM), then
remove the `ci.yml:110-116` guard so `ng test` is unconditional. Cite `ARCHITECTURE.md §11`.

**P3** — a new `src/ArchitectureTests/WM.ArchitectureTests` project, wired into `WM.sln`, reflecting
over the built module assemblies. Rules: (a) no type in `WM.Modules.X` references a type in
`WM.Modules.Y` outside `WM.Modules.Y.Contracts`; (b) no `DbContext` is referenced across a module
boundary; (c) the `WM.Api*` assemblies are exempt and **named**, so adding a third exemption is a
decision with a reviewer attached — the `EndpointAuthorizationInventoryTests` /
`DeliberatelyAnonymous` pattern, which this repo has twice found to be the difference between a
registry and a rubber stamp. Enforces CLAUDE.md invariant 1 and `ARCHITECTURE.md §3`.

**P4** — `deploy/docker-compose.yml`, `deploy/docker-compose.prod.yml`. Per `ARCHITECTURE.md §2:92`
and §13A:690.

**P5** — a `Version` concurrency token on `AuditableEntity` (`SharedKernel/Domain/Entity.cs`), a
migration per module that has one, `409 Conflict` from the four full-replace `PUT`s, and the token
on the read DTO so the SPA can round-trip it. Scope check strictly before version check
(§4, and 003 P2b's ordering precedent).

**P6** — OpenTelemetry in `src/Api` and `src/Worker`: traces (ASP.NET Core, HttpClient, EF Core,
Npgsql), metrics, OTLP exporter, all off by default and enabled by configuration so an on-prem
install with no collector is unaffected. Plus the counters F3 needs — `wm.eventstream.published`,
`wm.eventstream.dropped` tagged by reason (`unconfigured` / `queue_full` / `delivery_failed`) — and
Kafka consumer lag on `/api/health/ready` per §13A:702. `ARCHITECTURE.md §2:100`, §13A:702.

**P7** — an outbox table in `SharedKernel` (§2:797 designates it there), written in the same
`SaveChangesAsync` as the punch, drained by a background dispatcher, ordered per key.
`ARCHITECTURE.md §7A:264`, §2:93.

**P8** — an `IdempotencyKey` store, applied to `POST /api/punches` (composing with, not replacing,
007 P2's window) and to `RunPayrollExportConsumer` via `command.RequestId`. `CLAUDE.md` invariants
3 and 6.

---

## Out of scope for this plan

- **A React or any other frontend-framework migration.** The SPA stays Angular 22. (User direction.)
- **Multi-tenancy.** Decided against — `ARCHITECTURE.md:914`, database per customer, on the
  customer's own server. Recorded so the question stops recurring.
- **Anything device-related.** CLAUDE.md invariant 3.
- **The Postgres test harness.** ⚠️ **Already owned by 009 P4**, which is written, numbered and
  detailed (`009-what-the-running-app-does.md:732-754`) and carries the falsification discipline
  007 P1's manual run established. 011 **depends on it and does not duplicate it.** The task brief
  asked me to decide whether that portion belongs in 011; it does not, because it already belongs to
  009, and moving it would be the renumbering churn `STATE.md`'s conventions warn about.
- **The punch double-click dedupe window.** Owned by 007 P2 (approved). P8 composes with it.
- **The `phone` full-replace bug and the full-replace `PUT` rule.** Owned by 009 P3. P5 builds on it.
- **Readiness inspecting the schema.** Owned by 009 P8.
- **One user per employee (partial unique index).** Owned by 009 P5.
- **Case-insensitive employee-code uniqueness.** Owned by 007 P3.
- **Reversing the fire-and-forget publish** so a broker outage fails the user's request. §13A:703
  ratifies the current behaviour; P7 preserves it.
- **Distributed caching as a feature.** P4 decides Redis's *fate*, not a caching strategy. Nothing in
  WM has a measured cache need; adding one without a measurement would be the "infrastructure
  nothing consumes" mistake in a new costume.
- **Branch protection on `master`.** Still not enabled; already raised as 006 open question 5.

---

## Portions

Ordered by the standing rule, **fix what is bleeding first** — with the honest caveat that **most of
this plan is not bleeding.** Separating the two, as the task required:

| | Portion | Category |
|---|---|---|
| **Defects in shipped code** | P4 (Redis `depends_on` blocks startup), P5 (silent data loss on concurrent edit) | 2 of 8 |
| **Documentation that is factually wrong** | P1 | 1 of 8 |
| **Missing capability promised by ARCHITECTURE.md** | P6 (§2:100), P7 (§2:93), P4's Redis job (§2:92) | — |
| **Missing capability, no promise — hygiene** | P2, P3, P8 | 3 of 8 |

Only **P5** is both reachable today and losing data, and it is the one portion with a hard
prerequisite outside this plan. P4's defect is real but low-probability (it needs a Redis
healthcheck failure). That the audit of a repo this size found one live data-loss defect and one
availability defect is itself a result worth recording.

P1–P4 are independent of every other lane and can run in any order. P5–P8 have stated
prerequisites.

### [ ] P1 — The README describes this repository
**Touches:** `README.md` (only).
**Done when:** every version claim matches the tree (.NET 10, ASP.NET Core 10, EF Core 10,
Angular 22, PostgreSQL 17); the architecture link is `docs/ARCHITECTURE.md`, a path inside this
repository; the phase pointer is §14 not §11; "device gateway" is gone from *Next* and the line
reflects §14's actual phase order; the *Status* paragraph matches §14's phase table; and the
infrastructure line matches whatever P4 decides about Redis (or is written to be true under either
answer, if P4 has not landed).
**Tests:** none automatable, and the portion says so rather than inventing one. The reviewer checks
each of the eight rows in the F1 table against its cited source.
**Risk:** low.
**Note:** the drift dates from [#14](https://github.com/00008550/WM/pull/14). Worth one line in the
PR on *why* it survived: nothing reads the README, so nothing contradicted it. That is the argument
for P3 and the architecture test, in miniature.

### [ ] P2 — The first frontend spec, and CI stops warning
**Touches:** `frontend/portal/src/app/core/auth/auth.interceptor.spec.ts` (new),
`frontend/portal/src/app/core/auth/auth.guard.spec.ts` (new), `.github/workflows/ci.yml:105-116`.
**Done when:** `npm run test -- --watch=false --browsers=ChromeHeadless` passes locally **and on a
GitHub runner**; the `find src -name '*.spec.ts'` guard is deleted and `ng test` runs
unconditionally, so a repo that loses its last spec fails rather than warns; and CI wall-clock
before/after is stated in the PR.
**Tests:** the specs *are* the deliverable. Each must be proven by mutation — break the interceptor's
token attachment and the guard's redirect, and confirm the suite goes red. A spec that cannot fail is
not a test, and this repo has twice caught exactly that in backend reviews.
**Risk:** medium, and it is **CI risk, not code risk** — the same shape 009 P4 names. Chrome headless
in a container is a known-fiddly path that has never run here. If it cannot be made reliable,
**saying so and reverting is a successful outcome**; shipping a flaky gate is not.
**Note:** the harness already exists — `angular.json` (`@angular/build:karma`), `tsconfig.spec.json`,
karma + jasmine + `karma-chrome-launcher` in `devDependencies`. Nothing has ever executed it.
**⚠️ This portion must land before 009 P3.** 009 P3 writes `employees.component.spec.ts`, which flips
the CI guard for the first time inside a PR whose subject is a phone field. Debugging headless Chrome
belongs here, attributed correctly.

### [ ] P3 — Invariant 1 is enforced by a test, not by a reviewer's memory
**Touches:** new `src/ArchitectureTests/WM.ArchitectureTests/` wired into `WM.sln`;
`src/Modules/People/WM.Modules.People/Domain/Employee.cs` and
`Contracts/EmployeeDirectory.cs` (move `EmployeeStatus` and `Employment` into `Contracts`);
`src/Modules/TimeAttendance/**` and `WM.Modules.People.Tests` for the resulting `using` updates.
**Done when:** a test asserts that no type in one `WM.Modules.*` assembly references a type in
another outside that module's `.Contracts` namespace; that no `DbContext` crosses a module boundary;
that the exempt assemblies are a **named list** that fails when a new name is needed; and the rule
holds with **no exception** — which requires the `EmployeeStatus`/`Employment` move, because today
`People.Contracts` returns `People.Domain` types.
**Tests:** the mutation is the point and must be run, not described. Add
`using WM.Modules.People.Data;` and a `PeopleDbContext` reference to `PunchService`, confirm the
test fails **by name**; remove the exemption for `WM.Api` and confirm it fails there too, proving
the exemption is load-bearing rather than decorative.
**Risk:** low-medium. Low for the test; medium for the type move, which touches three files across
two modules and one test project. If the move proves larger than it looks, splitting it out as P3a
is the right call — but the test must not ship with a hard-coded exception for it, because an
architecture test with a grandfather clause is the rubber stamp this repo keeps warning about.
**Note:** the code is **clean today** — every cross-module `using` is `People.Contracts`. This
portion is not fixing a violation; it is closing the door before there is one. Say that in the PR,
because "no violations found" is the expected and correct result.

### [ ] P4 — Redis gets a job or leaves the compose file
**Touches:** `deploy/docker-compose.yml:43-54, 112`, `deploy/docker-compose.prod.yml:32-42, 92-94,
103, 140`; `README.md:19` if P1 has landed; `ARCHITECTURE.md` §13A:690 **only if** the user chooses
removal — and that edit is proposed, not applied, because §13A is a design section.
**Done when:** unconditionally — `wm-api` no longer has a `depends_on` on `redis` and no longer
receives `Redis__ConnectionString`, because a hard startup dependency on a service with no consumer
is wrong under every option; **and** whichever of *Open questions* 2's options the user picks is
implemented, with the reason in a comment in the compose file rather than only in this plan.
**Tests:** `docker compose -f deploy/docker-compose.prod.yml config` validates; a smoke check that
`wm-api` reaches ready with the `redis` service **stopped** — which is the defect this portion
closes and must be demonstrated failing first.
**Risk:** low.
**Note:** the file currently contradicts itself — `:106-107` says brokers are not hard dependencies,
`:94` makes the one component with no consumer the only hard one. Quote that in the PR.

### [ ] P5 — A concurrent edit is refused, not silently lost
**Touches:** `src/SharedKernel/WM.SharedKernel/Domain/Entity.cs` (a `Version` token on
`AuditableEntity`), `PeopleDbContext.cs` + `IdentityDbContext.cs`, one migration and snapshot per
module, `PeopleModule.cs` (`PUT /{id:guid}`) and `UserManagementService.UpdateAsync`, the read DTOs
so the token round-trips, `frontend/portal/src/app/pages/employees/employees.component.ts` and
`users.component.ts`, `WM.Modules.People.Tests`, `WM.Modules.Identity.Tests`.
**Done when:** a `PUT` carrying a stale version is refused with **409** and a message the SPA can
show; the version is returned on read and echoed on write; the **scope check runs first**, so a 409
never confirms the existence of a record outside the caller's scope; and all four full-replace `PUT`s
either carry the token or are named as deliberately exempt in the same inventory test 009 P3 builds.
**Tests:** edge cases 5–8. Specifically: two loads, two writes, second is 409 (EF's in-memory provider
enforces concurrency tokens, so the *behaviour* needs no Postgres); a 409 is never returned for an
out-of-scope record — it is 404, as today; and the mutation that proves it, deleting the token from
the entity and confirming the concurrency test fails rather than the whole suite.
**Risk:** medium — it changes the wire contract of four endpoints and both editor screens.
**Prerequisites and constraint:** **009 P3 should land first.** It builds the full-replace-`PUT`
inventory test and the three-layer round-trip rule this portion extends; built the other way round,
P5 adds a field to a `PUT` contract while 009 P3 is writing the rule about what a `PUT` contract must
round-trip, and the two collide in `employees.component.ts` and `LeaverRecordEndpointTests.cs`. If
the user wants P5 first because it is the live data-loss defect, that is defensible — but 009 P3's
*Touches* must then be amended in the same commit.
**Also:** the migration's **SQL** wants **009 P4**'s harness. The behaviour does not. If P4 has not
landed, follow 007 P1's precedent: test the migration's *operations* in xUnit, run the SQL by hand,
and label the two halves differently in the PR rather than implying one covers the other.
**Note:** legacy detects this and crashes (`UpdateCheck.Always` on 148/153 `dbo.Employees` columns;
no `ChangeConflictException` handling in `Logic/`). Cite it — it is the strongest argument that
detection is genuine domain truth and not gold-plating.

### [ ] P6 — The platform can be observed, and a dropped event is counted
**Touches:** `src/Api/WM.Api/WM.Api.csproj` + `src/Worker/WM.Worker/WM.Worker.csproj` (OTel
packages), `src/Api/WM.Api/Program.cs:18-21`, `src/Worker/WM.Worker/Program.cs:9-14`, a new
`src/SharedKernel/WM.SharedKernel/Observability/` (meters and the activity source),
`src/Api/WM.Api/Infrastructure/EventStreamProducers.cs:22-27, 50-59`,
`src/Api/WM.Api/Infrastructure/WmHealthChecks.cs`, `appsettings.json`, `WM.Api.Tests`.
**Done when:** traces and metrics are emitted for ASP.NET Core, HttpClient, EF Core and Npgsql via
an OTLP exporter that is **disabled by default and enabled by configuration**, so an on-prem install
with no collector is unaffected and no telemetry ever leaves a customer's box unasked; every Kafka
publish increments `wm.eventstream.published` or `wm.eventstream.dropped` with a `reason` tag
distinguishing `unconfigured` from `delivery_failed`; and Kafka consumer lag appears on
`/api/health/ready` per §13A:702.
**Tests:** edge case 9 — a publish with `Kafka:BootstrapServers` unset increments `dropped` with
`reason=unconfigured`, and a publish to an unreachable broker increments it with a *different*
reason. Assert against the meter, not the log. Plus: the OTLP exporter is **not** registered when
the configuration section is absent — the mutation is enabling it by default and confirming a test
fails.
**Risk:** low-medium. The packages are additive; the risk is scope creep into a dashboard. There is
no dashboard in this portion.
**Note:** §2:100 already promises OpenTelemetry, so this is a defect against the design and needs no
approval beyond the plan's. The `reason=unconfigured` counter is the half that matters most and is
the cheapest: it turns a one-line startup warning into an ongoing signal, which is the actual failure
mode a customer will hit.

### [ ] P7 — The punch event survives a broker outage
**Touches:** new `src/SharedKernel/WM.SharedKernel/Outbox/` (entity, `IOutboxWriter`, dispatcher),
`TimeAttendanceDbContext.cs` + a migration, `PunchService.cs:54-59`,
`EventStreamProducers.cs:29-35` (`Acks.All`), `Program.cs` registration, a new
`WM.Modules.TimeAttendance.Tests` if 007 P2 or 010 P1 has not already created it.
**Done when:** the punch row and its outbox row commit in **one** transaction; the request path never
touches the broker, so §13A:703's guarantee is preserved and strengthened; a background dispatcher
drains the outbox preserving per-employee order; a broker down for the length of the test loses
nothing; and the outbox has a bound, a lag metric (P6's meter) and a documented behaviour at the
bound.
**Tests:** edge cases 10–12. A publish that throws leaves the punch committed **and** the outbox row
pending; the dispatcher is re-entrant, so two dispatchers do not double-publish; per-key ordering
survives an outage and drain; and the mutation — move the outbox write outside the transaction and
confirm a test fails.
**Risk:** high. It is the largest portion here and the only one that changes how a shipped write
path commits. **It should be the last code portion built**, and if it grows past ~8 files it should
be split (entity + writer; then dispatcher; then `Acks.All`).
**Prerequisites:** **009 P4's Postgres harness.** An outbox is a claim about transaction boundaries,
and EF's in-memory provider does not have any — testing this without real Postgres would produce a
test that passes for the wrong reason. Unlike P5, this portion should **not** proceed with a manual
run as a substitute.
**Blocked on:** *Open questions* 3 — §2:93 promises the outbox on the **MassTransit/RabbitMQ** leg,
and this is the **Kafka** leg. The mechanism is the user's call before this is built.

### [ ] P8 — A retried request does not become a second record
**Touches:** new `src/SharedKernel/WM.SharedKernel/Idempotency/` (store + a unique index),
`TimeAttendanceModule.cs` (`POST /api/punches` accepts `Idempotency-Key`),
`PunchService.RecordAsync`, `RunPayrollExportConsumer.cs:12-33`, `WM.Worker` registration,
`WM.Modules.TimeAttendance.Tests`, `WM.Api.Tests` (the header on the endpoint inventory).
**Done when:** a `POST /api/punches` carrying an `Idempotency-Key` already seen returns the
**original** punch and its original status code, creating nothing; the same key with a different
payload is refused rather than silently replaying; `RunPayrollExportConsumer` skips a
`command.RequestId` it has already completed, so `UseMessageRetry` can no longer re-run
`plugin.ExportAsync`; and the key retention window is configured, enforced and documented.
**Tests:** edge cases 1–4. Explicitly: the **offline-replay** case 007 P2's window cannot catch — an
IN timestamped 08:00 arriving at 14:00 with a key already seen returns the original; concurrent
arrival of one key inserts once, proven by driving two callers at the unique index rather than by
pre-check (`FakeHub.cs:54` shows the repo already has this test shape); and a key reused with a
different body is refused.
**Risk:** medium.
**Prerequisites and constraint:** **007 P2 must land first.** It is approved, it is the next portion
in lane C, it is a live defect ("a terminated employee can punch"), and it rewrites the same method
(`PunchService.RecordAsync`) including its own idempotent-return path. Building P8 first would mean
writing an idempotency layer around a method that is about to change shape underneath it — and would
force 007 P2, a bleeding-defect fix, to rebase onto unapproved work. **The two are complementary,
not alternatives**, and the PR must say so: 007 P2's window suppresses double-click noise from a
client that cannot know it duplicated; P8's key answers a client that *knows* it is retrying. Neither
subsumes the other.

---

## Corrections made to WM's records

Applied directly, per this role's boundaries:

1. **`CLAUDE.md` — *Commands*.** "the only two test projects" → **four**, named. The warning it
   carries is unchanged and still correct: TimeAttendance has no test project.
2. **`ARCHITECTURE.md` §13** — six new rows for platform capabilities that had none, per the
   standing convention that *"a WM-only capability has no row in the coverage matrix, so nothing
   prompts anyone to check it"*. Each carries its measurement and its owning portion.
3. **`docs/plans/STATE.md`** — 011 registered in the Queue table; lane D added to the sequence;
   the 007 P2 / 009 P3 / 009 P4 constraints recorded where a builder will see them.

Proposed, **not** applied (design sections — see *Open questions*):

4. `ARCHITECTURE.md §14:549` still describes Phase 1b as *"several groups per user combining as a
   **union**"*. The user ruled the opposite on 2026-08-05 (option A, one object one membership) and
   §4 was rewritten; §14's phase table was not. It now contradicts §4:157 in the same document.
5. `ARCHITECTURE.md §4:157` ends *"**Multi-tenancy-ready** (tenant id + filter)"* and §14A:797 lists
   *"multi-tenancy"* as a SharedKernel cross-cutting concern — both against §15:914's *"decided:
   database per customer"*. One of the three is wrong.
6. `ARCHITECTURE.md §13A:739 vs :740` — see *Open questions* 4.

---

## Open questions for the user

**1. Is the audit's framing right that this is "production readiness" at all?**
Six of the eight findings are absent machinery rather than broken behaviour, and this plan says so
in its portion table. The counter-argument for doing it now is that P2, P3 and P6 are all
*cheaper before* Phase 2 than during it — an architecture test written against 3 modules is trivial
and against 8 is archaeology. The counter-argument against is that lane A and lane C both have live
defects queued and 011 competes for the same reviewer. **My recommendation: P1 immediately (an
hour), P2 before 009 P3 (forced), and P3–P8 behind the current lane A/C defect work.**

**2. Redis — which of these?** §2:92 and §13A:690 both put it in the design, so I have not decided
this unilaterally.
   - **(a) Give it its designed job now** — Redis-backed SignalR backplane, making
     `AttendanceConnectionRegistry` and `AttendanceAudience` multi-replica-safe. *Honest but
     premature:* §13A ships one container per customer, so there is no second replica to serve.
   - **(b) Remove it from both compose files, keep it in §2 as a future component.** *My
     recommendation.* Nothing is lost, one fewer container on the customer's box, and the design
     intent survives in writing. Requires a proposed edit to §13A:690's ship list.
   - **(c) Leave it, with a comment saying why it is there.** Cheapest, but it keeps the false claim
     that the running system uses a cache.
   Under all three, P4 removes the `depends_on` and the dead environment key.

**3. The outbox mechanism — MassTransit's, or one of ours?** §2:93 and §7A:264 promise a
transactional outbox and both name **MassTransit**, which supplies one. But the dual write that is
actually losing events is the **Kafka** publish in `PunchService`, and the API host does not
currently reference MassTransit at all (`WM.Api.csproj` has no MassTransit package; only
`WM.Worker` does). So:
   - **(a)** Bring MassTransit into the API purely for its outbox, and have the outbox dispatch to
     Kafka. Uses the promised machinery; adds a dependency to the API for one feature.
   - **(b)** A small outbox in `SharedKernel`, which §2:797 already designates as its home, publishing
     to `IEventStreamProducer`. Fewer moving parts, honours §797, but is the hand-rolled thing §7A:264
     warns is *"a known multi-week detour"* — though that warning is about sagas, retry and DLQ, not
     about a table and a dispatcher.
   **This is a design decision and P7 is blocked on it.** I lean **(b)** and would want that
   recorded as an ADR, but it is your call, not mine.

**4. `ARCHITECTURE.md` §13A promises two things that cannot both be true, and I could not resolve
it from the code.** `:739` promises *"**Versioned rollout and instant rollback** — pin `WM_VERSION`
per customer, `docker compose pull && up -d` to upgrade, repin to roll back."* `:740` promises
*"**Schema upgrades itself** — the API applies EF Core migrations on start in every environment."*
**Repinning `WM_VERSION` downward does not un-migrate the schema.** 007 P1's
`20260814080315_EmploymentWindowAndLeaverRecord` `DROP COLUMN "Status"` is the concrete case: roll
that container back and the previous build queries a column that no longer exists. Nothing in the
repo requires migrations to be backward-compatible with the previous image, and no portion in any
plan owns this. The honest options are (a) adopt expand/contract as a documented rule with a
reviewer check, (b) narrow §13A:739 to "rollback requires a database restore" and make per-customer
backup (§13A:765) a hard prerequisite of upgrade, or (c) both. **This is the audit's "schema
migrations and backward compatibility" item, and it is the finding I would most want an answer on** —
it is a live promise to customers, not a code smell. I have not written a portion for it because the
answer determines whether it is a documentation change or a rollout-tooling workstream.

**5. Idempotency key retention.** How long must WM remember a key on an unattended on-prem box, and
what happens after? A short window (24h) bounds the table and matches an offline queue's realistic
lifetime; an unbounded one grows forever on a server we cannot reach (§13A). And a replay *after*
expiry creates a second punch — is that acceptable, or must expiry be long enough that it cannot
happen? P8 needs a number.

**6. The concurrent-edit UX (edge cases 5–7).** A row version 409s a second manager even when they
edited a different field. Under full-replace `PUT`s the alternative is not a merge — it is today's
silent revert. Confirm 409 is what you want, and whether the response should carry the current
record so the SPA can show a diff, or just say *"someone else changed this"*.

**7. Three proposed doc edits I did not apply**, listed in *Corrections* 4–6: §14:549's stale
"union", the three-way multi-tenancy contradiction (§4:157 / §14A:797 vs §15:914), and §13A:739/740
above. All are in design sections. Say the word and I will apply them; each is a one-line edit
except the last.

**8. The `CLAUDE.md` edit in *Corrections* 1.** I corrected a factual test-project count inside an
advisory paragraph. If you would rather no agent edits `CLAUDE.md` for any reason, revert it and I
will carry the correction in this plan instead.
