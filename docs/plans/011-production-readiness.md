# 011 — Production readiness: the machinery this repository claims and does not have

Status: **in-review** — P5 built 2026-08-29 (`feat/011-p5`); P2 passed review 2026-08-29 (`feat/011-p2`); approved by user 2026-08-29, **all 8 portions**   <!-- draft → approved → in-progress → in-review → merged -->
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

## Decisions taken (user, 2026-08-29)

**All 8 portions approved.** Three open questions ruled on, and they change this plan materially —
one portion is rewritten, one is unblocked, and one question left the plan entirely.

### D1 — the outbox is a small one in `SharedKernel`, not MassTransit in the API

**P7 is unblocked.** Written up as an ADR before it is built. This was the recommendation and the
reason stands: §14A:797 already designates `SharedKernel` as the outbox's home, `WM.Api.csproj`
carries no MassTransit today, and §7A:264's *"known multi-week detour"* warning is about sagas,
retry and DLQ — not about a table and a dispatcher.

**Consequence: §2:93 is now wrong.** It reads `Commands & jobs | RabbitMQ via MassTransit (retry,
outbox, sagas)`, which after this ruling describes a mechanism WM has decided not to use for the
leg that actually loses events. §2 is a design section, so the correction is **proposed, not
applied** — concrete diff in *Open questions* 1.

### D2 — Redis gets the SignalR backplane job now

This **overrules** the survey's "premature — one container per customer" recommendation. The
reasoning was put to the user and the user chose otherwise, so it is settled: build it, and this
plan does not re-argue it.

**P4 is rewritten rather than edited, because its character changes completely.** It was a defect
fix; it is now a capability. Specifically, the defect it was written to close **evaporates**: under
D2 the hard `depends_on: service_healthy` at `docker-compose.prod.yml:92-94` becomes *correct*,
because the API genuinely will hold a Redis connection. The finding was real when measured and is
void under the ruling — recorded here rather than quietly deleted, because separating what is
bleeding from what is machinery is the thing this plan is for.

D2 also surfaces two consequences the original P4 did not contain, both now in the portion: what a
Redis outage does to a hub that has a hard dependency on it, and the fact that a backplane
**silently breaks live scope revocation** unless something is done about the in-process connection
registry. The second is not speculative — `AttendanceConnectionRegistry.cs:19-21` predicted it in a
comment written when 003 P1 shipped.

### D3 — rollback becomes real tooling, and it is not part of 011

The user chose the tooling answer over the documentation answer for the §13A:739 / §13A:740
contradiction (repinning `WM_VERSION` does not un-migrate the schema). **This is its own plan and
011 does not absorb it.** Registered by name in `STATE.md` — *"Rollback that actually rolls back"* —
alongside the other named-but-unwritten plans, per the convention that a plan number is claimed when
the file is written, never when the work is merely named. The ruling itself is recorded against
§13A so the contradiction stops being live.

### D4 — P5's 409 carries a message, not the current record

**Decided: the simple message.** A stale write is refused with the equivalent of *"someone else
changed this record — reload and try again"*. The response does **not** carry the current record for
a client-side diff.

**The reasoning: it closes the defect at the lowest cost.** The bleeding thing is *silent* loss —
the losing manager is never told. A message ends that completely. Everything beyond it improves how
gracefully the user recovers, not whether their work survives, and it would change the response
contract of four endpoints for a benefit nobody has asked for yet.

**What it is deliberately not doing**, stated so the richer version stays a recognisable next step
rather than reading later as an oversight: no current-record payload, so **the SPA cannot show a
diff** and cannot offer "keep mine / keep theirs". The user reloads and re-enters their edit. That
is a real cost when two managers collide on a long form, and the moment it is felt, the fix is
additive — put the current record in the 409 body and the field-level UI on top. **Nothing in P5
forecloses it**; the token and the refusal are the load-bearing parts and they do not change.

**This still puts WM ahead of legacy, which is worth saying because P5 is the one place WM was
behind.** TLW *detects* the conflict — full-column `UpdateCheck.Always` on 148 of `dbo.Employees`'
153 columns — and then throws a `ChangeConflictException` that **nothing in `Logic/` catches**, so
the losing manager gets a crash. WM refuses the write and tells them what happened, which is
presumably the behaviour those 8,173-minus-356 checked columns were meant to produce and never did.
Detection without handling is not a feature; it is a stack trace.

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

### F4 — Redis is deployed and unused · **CONFIRMED, then VOIDED by D2**

> **Read this finding as history.** It was confirmed as measured, and the *availability defect* half
> was **dissolved by the user's ruling on 2026-08-29** (D2): once the API holds a Redis connection,
> the hard `depends_on` below is correct rather than wrong. The measurement stands; the conclusion
> does not. It is kept in full because the reasoning is what justified asking the question, and
> because P4 now inherits the *other* half — that nothing consumed Redis — as its brief.

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

Removing Redis outright would contradict §2:92 and §13A:690, so P4 did not propose it unilaterally.
**Ruled by the user 2026-08-29 (D2): Redis gets the backplane, and the `depends_on` therefore stays
and becomes correct.** This paragraph is the half of the finding that D2 voided.

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

**Is P5 testable without the Postgres harness? Measured, not assumed — yes, and the answer
constrains the design.** The concern is fair: a concurrency token is SQL behaviour, `009 P4` is the
standing testing gap, and this repo has been burned by a promised migration test that no harness
existed to run (001 P2). So it was run rather than reasoned about, against EF Core **10.0.10**'s
InMemory provider, two `DbContext`s over one store:

```
A loaded version 45f9…  B loaded version 45f9…
A saved OK
>>> RESULT: DbUpdateConcurrencyException — InMemory DOES enforce the token.
final Name = A wins
```

And the mutation, with `.IsConcurrencyToken()` removed, which reproduces **WM's exact current
behaviour**:

```
>>> RESULT: NO CONFLICT DETECTED — B silently overwrote A.
final Name = B overwrites
```

So the test can fail, and it fails by reproducing the defect. **P5's behaviour is honestly testable
today with no harness — on one condition, which is now a design constraint on the portion: the
token must be an explicit mapped column, not Npgsql's `xmin`.** A shadow `xmin` is populated by the
Postgres storage engine; under InMemory nothing writes it, so it would sit at its default forever
and every concurrency test would pass *vacuously* — a green suite proving the opposite of what it
claims. That is precisely the failure this repository keeps catching by mutation, and it is worth
paying a real column for. The migration's **SQL** still wants 009 P4; the behaviour does not.

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
| ~~Redis as a hard `depends_on` for a service that never connects~~ | ⏹ **void under D2** | Was **Invert**: an unused component must never block startup. Once the API holds a Redis connection the dependency is *correct*. The finding was real when measured; the ruling dissolved it. Kept struck through rather than deleted, because "which findings survived a decision" is the part that is expensive to reconstruct later. |
| Redis in the design as cache + SignalR backplane | **Keep** (the design) | §2:92. **D2 promotes this from "a real future job" to the job P4 builds now.** |
| Redis shipped in every compose file with no consumer | **Improve** | Settled by **D2** — it gets the backplane, not the exit. → P4. |
| `AttendanceConnectionRegistry` in-process under a backplane | **Improve** | Not wrong today (one instance) and wrong the moment there are two — live scope revocation would silently become per-instance, downgrading 003 P1's shipped ✅. The registry stays local; the *notification* is what distributes. → P4, consequence 2. |
| Hub group sends as a hard Redis dependency | **Invert** | The realtime leg is best-effort by design (`EventStreamProducers.cs:125-140`) and must stay so. A Redis blip must degrade the feed, never fail a connection or an HTTP request. → P4, consequence 1. |
| `UseMessageRetry` configured with no dedup | **Improve** | Retry without idempotency is a guarantee of double-execution, not resilience. → P8. |
| `Contracts` returning `Domain` types | **Improve** | Makes the only enforceable module rule unenforceable. → P3. |
| Invariant 1 enforced by reviewer instruction | **Improve** | The reviewer prompt saying *"every single time; it is the most common violation"* is an admission. → P3. |
| CI emitting `::warning` instead of running `ng test` | **Improve** | Correct behaviour for an empty suite; wrong to leave standing. → P2. |
| Migrate-on-start (`Program.cs:96-98`) | **Keep** *(with a caveat)* | §13A:740 makes it the on-prem upgrade mechanism, and it is the right call. It collides with §13A:739's rollback promise, and nothing in the repo records or enforces the discipline that reconciles them. **Ruled 2026-08-29 (D3): fixed with tooling, in its own plan** — *"Rollback that actually rolls back"*, registered in `STATE.md`. **Explicitly not 011's.** |
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
   creates a second punch — is that acceptable?). Named in *Open questions* 4.
5. **Concurrent edit, disjoint fields.** Manager A edits `phone`, Manager B edits `jobTitle`, both
   from the same load. Under a row version the second gets a **409**. **Settled by D4** — under
   full-replace `PUT`s the alternative is silent loss, not a merge, so the 409 is correct even
   though the edits do not overlap. (P5.)
6. **Concurrent edit, same field, same value.** Two managers set the same phone number. A row version
   still 409s the second. Accepted under D4, but the message must not read as data loss — nothing
   was lost, and the wording has to survive this case without alarming anyone.
7. **The 409 says *that* something changed, not *what*.** **Settled by D4: the simple message**, no
   current-record payload and no diff. The bar it must clear is that the user understands they should
   reload rather than retry blindly — legacy's answer here is an uncaught `ChangeConflictException`.
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

**P4** — `Microsoft.AspNetCore.SignalR.StackExchangeRedis` on `AddSignalR()`, plus a
`wm.scope-changed` Redis pub/sub channel so `IScopeChangeNotifier` reaches every instance and each
re-groups its own local connections. `AttendanceConnectionRegistry` stays in-process by design.
Backplane absent when `Redis:ConnectionString` is absent. Per `ARCHITECTURE.md §2:92`, §13A:690, and
§13:453 — the realtime row whose ✅ this portion must not downgrade.

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
- **Distributed caching as a feature.** P4 builds the **backplane** half of §2:92's
  `Cache / realtime backplane | Redis`, not the cache half. Nothing in WM has a *measured* cache
  need; adding one without a measurement would be the "infrastructure nothing consumes" mistake in a
  new costume — which is the finding P4 started life as.
- **Making rollback real (D3).** The §13A:739/740 contradiction — repinning `WM_VERSION` does not
  un-migrate the schema — is now **its own plan**, named *"Rollback that actually rolls back"* in
  `STATE.md` and not yet written. **011 does not absorb it**, and no portion here should grow to
  cover it. Its concrete test case is 007 P1's `20260814080315_EmploymentWindowAndLeaverRecord`,
  which `DROP COLUMN "Status"`: repin to the previous image and the old build queries a column that
  is gone.
- **Branch protection on `master`.** Still not enabled; already raised as 006 open question 5.

---

## Portions

Ordered by the standing rule, **fix what is bleeding first** — with the honest caveat that **almost
none of this plan is bleeding.** Separating the two, as the task required, **revised under D2**:

| | Portion | Category |
|---|---|---|
| **Defects in shipped code** | **P5 only** — silent data loss on a concurrent edit | **1 of 8** |
| **Documentation that is factually wrong** | P1 | 1 of 8 |
| **Missing capability promised by ARCHITECTURE.md** | P4 (§2:92), P6 (§2:100), P7 (§14A:797, and see D1 on §2:93) | 3 of 8 |
| **Missing capability, no promise — hygiene** | P2, P3, P8 | 3 of 8 |

**P4 has moved out of the defect row.** It held the second live defect — a hard `depends_on` on a
service nothing connected to — and **D2 voids it**: once the API holds a Redis connection, that
dependency is correct. So an audit of a repository this size found, in the end, **exactly one live
defect**, and it is the one place WM is measurably worse than the product it replaces.

### Build order

**P2 → P5 → P1 → P3 → P6 → P4 → P7 → P8.**

This is the coordinator's proposed order with **one correction: P2 moves ahead of P5**, for a
concrete reason rather than a preference.

**Why P2 first, before the bleeding fix.** P5 adds a `version` field that must round-trip through
the SPA — `employees.component.ts` and `users.component.ts` must read it on load and echo it on
save. **That is the exact class of bug 009 P3 exists to fix**: `phone` is silently nulled today
because a full-replace `PUT` body dropped a field the server expected. If P5's SPA half drops
`version`, the failure is worse than `phone`'s — the client sends `version: null`, and depending on
how the server reads it either every save 409s (loud, survivable) or **the concurrency check is
silently bypassed and P5 ships a token that never fires**. A green backend suite would prove
nothing, because the defect lives in the browser. P5's frontend half needs a spec, and there is no
harness to write one in until P2 lands. P2 is one portion and two small specs.

**Escape hatch, so this cannot become a blocker on the one thing that is bleeding.** P2 is the
plan's CI-risk portion — headless Chrome has never run in this repository. If it does not go green
inside a reasonable timebox, **revert it and build P5 anyway**, with its SPA half covered by a
manual round-trip check and *labelled as manual* in the PR — exactly the split 007 P1 used for its
migration, where seven xUnit tests read the operations and a one-off manual run covered the SQL.
What is not acceptable is P5 shipping with an untested SPA half that the PR describes as tested.

**The rest of the order, and why.** P1 (an hour, no dependencies) and P3 (independent, and cheapest
now — an architecture test over 3 modules is trivial, over 8 it is archaeology) come next. P6 before
P4 and P7 because both of those are better with metrics already in place: P4 wants to see backplane
health and P7 wants outbox lag, and P6 builds the meter both use. P4 and P7 are the two heavy
portions and go last. P8 is last of all, behind 007 P2.

### What the re-sequencing changed elsewhere

**One thing broke, and it is fixable in the same commit.** The original plan sequenced
**P5 after 009 P3**, so that P5 could extend the full-replace-`PUT` inventory test and round-trip
rule that 009 P3 builds. Putting P5 second inverts that — and 009 is a `draft` awaiting approval, so
gating the only live defect in this plan on approving *another* plan is the wrong trade. **P5 goes
first and takes on 009 P3's amendment as part of its own work**, which the original portion text
already pre-authorised. Concretely, P5 must:

- add the `version` field to `LeaverRecordEndpointTests.cs`'s `PortalEditBody` helper (`:410-438`),
  which mirrors the SPA payload key-for-key on purpose and will otherwise pin a body that no longer
  round-trips;
- re-verify and update **009 P3's *Touches* line citations** in `009-what-the-running-app-does.md`,
  since P5 moves `employees.component.ts:291` and `PeopleModule.cs:75-88`;
- and state in its PR that it is **carrying a piece of 009 P3's rule early** — the round-trip
  discipline — without claiming to have built the inventory test, which stays 009 P3's.

**Nothing else broke.** P2's constraint is with 009 P3, not with P5, and moving P2 earlier only
strengthens it. P8's constraint with 007 P2 is untouched. P7's dependency on 009 P4 is untouched.
P4's new consequences create no cross-lane dependency — the backplane touches `Realtime/`, which no
other plan opens.

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

### [x] P2 — The first frontend spec, and CI stops warning
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

### [ ] P4 — Redis does the job it was deployed for: the SignalR backplane
**Rewritten 2026-08-29 under D2.** It was *"Redis gets a job or leaves the compose file"*, and it was
a **defect fix** — the defect being a hard `depends_on` on a service nothing connected to. Under D2
that defect is **void**: the API will genuinely hold a Redis connection, so `docker-compose.prod.yml:92-94`
becomes correct rather than wrong. This portion is now **added capability**, and it is the largest
change to a shipped subsystem in this plan after P7. It is not bleeding and the ordering reflects
that.

**Touches:** `src/Api/WM.Api/WM.Api.csproj` (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`),
`src/Api/WM.Api/Program.cs:35` (`AddSignalR().AddStackExchangeRedis(...)`),
`src/Api/WM.Api/Realtime/AttendanceAudience.cs:111-121` (`UserScopeChangedAsync`), a new
`src/Api/WM.Api/Realtime/ScopeChangeChannel.cs`, `appsettings.json` + `appsettings.Development.json`,
`deploy/docker-compose.yml:1-20` (the header's graceful-degradation paragraph) and `:43-54`,
`deploy/docker-compose.prod.yml:92-94, 103`, `WM.Api.Tests/Realtime/`,
`ARCHITECTURE.md` §13 (the Redis row this survey added, and the realtime-feed row at `:453`).

**Done when:**
1. The hub uses a Redis backplane, enabled by `Redis:ConnectionString` and **absent when the key is
   absent**, so `dotnet run` with no Docker still works — the property `docker-compose.yml:1-20`
   promises for the other brokers.
2. **Live scope revocation still works across instances.** See the note below; the mechanism is a
   Redis pub/sub channel that every instance subscribes to, so each re-groups *its own* local
   connections. `AttendanceConnectionRegistry` stays in-process and stays correct.
3. **A Redis outage has a stated, tested behaviour** rather than an assumed one — see the note.
4. Two API instances against one Redis: a punch recorded on instance A reaches a subscribed client
   on instance B, and a scope edit on A re-groups a socket held by B.

**Tests:** the two-instance case is the whole point and must be exercised, not reasoned about — two
composed hosts sharing one Redis (or a fake backplane if a container is unavailable, labelled as
such per 007 P1's precedent). Plus: the backplane is **not** registered when the configuration key is
absent, proven by mutation (register it unconditionally and confirm a test fails); and a scope edit on
instance A re-groups a connection on instance B, which is the assertion that fails today and is the
reason this portion is more than a package reference.
**Risk:** **medium-high**, and higher than the original P4's *low*. It changes how the punch feed —
003 P1's shipped guarantee, currently ✅ in §13 — is delivered.

> #### ⚠️ Consequence 1: a Redis outage now degrades realtime, and that is a new failure mode
>
> Today the documented and true property is *"the app degrades gracefully — `IEventStreamProducer`
> no-ops without Kafka and **no request ever fails because a broker is missing**"*
> (`docker-compose.yml:17-18`), reinforced by `EventStreamProducers.cs:41-42, 56-59` and by
> §13A:703. The dev header even offers `docker compose stop kafka rabbitmq` as a supported move to
> reclaim RAM.
>
> **A backplane is not like the other brokers.** Once the hub's lifetime manager is Redis-backed,
> a Redis outage means group sends fail — the punch feed goes dark — and `AddToGroupAsync` /
> `RemoveFromGroupAsync` can throw, which reaches `AttendanceAudience.SubscribeAsync:68-71`, inside
> the connection gate.
>
> **The ruling this portion must implement:** the realtime leg is already best-effort by design and
> must stay that way. `BroadcastingEventStreamProducer.PublishAsync:125-140` already wraps the hub
> send in a timeout and two catches with *"clients will refresh on poll"* — that treatment extends
> to the backplane and needs no change. What **does** need changing is `SubscribeAsync`: a
> throwing `AddToGroupAsync` currently propagates out of a hub `OnConnectedAsync`, which would turn
> a Redis blip into failed *connections* rather than a degraded feed. It must fail the way
> `ResolveAsync:139-146` already fails — **closed and logged**, the connection kept and hearing
> nothing, never left holding stale groups. **A Redis outage must degrade the feed; it must never
> fail an HTTP request or drop a punch**, because the punch write path does not touch the hub at
> all. State that property in the PR and test it.
>
> `docker-compose.yml`'s header paragraph must be amended in this portion: `stop redis` is no longer
> equivalent to `stop kafka rabbitmq`, and a dev who follows the current text will silently lose the
> live dashboard and conclude the feed is broken.

> #### ⚠️ Consequence 2: a backplane silently breaks live scope revocation — and the code said so
>
> This is not a discovery; it is a prediction the repository already made.
> `AttendanceConnectionRegistry.cs:19-21`, written when 003 P1 shipped:
>
> > *"In-process on purpose: it mirrors the default single-node hub lifetime manager. **A Redis
> > backplane would have to move this with it** — the punch fan-out would keep working, but a scope
> > change would only re-group the sockets attached to the node that handled the edit."*
>
> Exactly right, and here is the mechanism. `UserScopeChangedAsync:115` calls
> `registry.ConnectionsFor(userId)`, which scans an in-process `ConcurrentDictionary`
> (`AttendanceConnectionRegistry.cs:27, 60-61`). Under two instances, an administrator narrowing a
> manager's scope on instance A re-groups only A's sockets. **A manager connected to instance B
> keeps the old groups and keeps receiving punches they may no longer see, until they reconnect.**
> That is precisely the leak 003 P1 exists to close, and §13:453 currently records it as ✅ **scoped**.
>
> **A capability portion must not silently downgrade a ✅.** So this portion fixes it, and the fix is
> small — deliberately smaller than the comment's *"move this with it"* implies:
>
> **Do not distribute the registry.** Making the connection→groups map shared would add a
> consistency problem (two nodes racing the same socket's membership) to solve a problem that is not
> about *where connections are known* — each node already knows its own, which is all it needs. The
> gap is only that **the notification does not travel.**
>
> **Distribute the notification instead.** `IScopeChangeNotifier.UserScopeChangedAsync` publishes
> the affected user ids to a Redis pub/sub channel (`wm.scope-changed`) on the multiplexer the
> backplane already opens; every instance subscribes and runs today's exact loop over **its own**
> registry. `AttendanceConnectionRegistry` is unchanged, `AttendanceAudience`'s per-connection
> semaphore stays correct (it only ever serializes operations on a local socket), and the local path
> is unchanged when no Redis is configured. One new file, one changed method.
>
> **If that is judged too large for this portion**, the fallback is *not* to leave it unstated: ship
> the backplane, and record in `ARCHITECTURE.md` §13:453 and in `AttendanceConnectionRegistry.cs`
> that live revocation is **per-instance** and therefore that WM must run exactly one API instance
> until it is fixed — which would make the backplane pointless, since a backplane's only purpose is
> the second instance. **That contradiction is the argument for doing it here**, and the reviewer
> should treat a PR that ships the backplane without it as incomplete rather than as a deferral.

**Note:** the compose file also currently contradicts itself — `:106-107` says brokers are not hard
dependencies while `:94` makes Redis the only hard one. Under D2 the `depends_on` is correct and the
**comment** is what needs narrowing, to say Kafka and RabbitMQ rather than "brokers". The opposite of
the original P4's fix, from the same measurement.

### [x] P5 — A concurrent edit is refused, not silently lost
**Touches:** `src/SharedKernel/WM.SharedKernel/Domain/Entity.cs` (a `Version` token on
`AuditableEntity`), `PeopleDbContext.cs` + `IdentityDbContext.cs`, one migration and snapshot per
module, `PeopleModule.cs` (`PUT /{id:guid}`) and `UserManagementService.UpdateAsync`, the read DTOs
so the token round-trips, `frontend/portal/src/app/pages/employees/employees.component.ts` and
`users.component.ts`, `WM.Modules.People.Tests`, `WM.Modules.Identity.Tests`.
**Done when:** a `PUT` carrying a stale version is refused with **409** and a message the SPA shows
— per **D4, the simple message** (*"someone else changed this record — reload and try again"*),
**not** the current record and **not** a diff; the version is returned on read and echoed on write;
the **scope check runs first**, so a 409 never confirms the existence of a record outside the
caller's scope; and all four full-replace `PUT`s either carry the token or are named as deliberately
exempt in the same inventory test 009 P3 builds.
**Scope of the 409, per D4 — build this and stop.** No current-record payload, no field-level
"keep mine / keep theirs". The user reloads and re-enters. That is a real cost on a long form and it
is accepted deliberately, because the defect being fixed is *silence*, not friction. Note in the PR
that the richer version is **purely additive** — the body gains a field, the token and the refusal do
not change — so this is a first step, not a ceiling.
**Design constraint, measured — the token is an explicit mapped column, never `xmin`.** See
*F8* above for the transcript. EF Core 10's InMemory provider enforces an explicit
`IsConcurrencyToken()` and throws `DbUpdateConcurrencyException`; a shadow `xmin` would never be
populated in memory and every concurrency test would pass **vacuously**. Npgsql's
`UseXminAsConcurrencyToken()` is therefore out, and the PR must say why it was rejected — otherwise
the next reader will "simplify" to it.
**Tests:** edge cases 5–8. Specifically: two loads, two writes, second is 409 — **the behaviour
needs no Postgres and this was verified before the portion was written**, not assumed; a 409 is
never returned for an out-of-scope record — it is 404, as today; and the mutation that proves it,
removing `IsConcurrencyToken()` and confirming the test fails with *"B silently overwrote A"*, which
is WM's behaviour today.
**Risk:** medium — it changes the wire contract of four endpoints and both editor screens. D4 keeps
that change as small as it can be: one field out, one field back, no new response shape.
**Prerequisites, revised 2026-08-29.** P5 is now **second in the build order** and **009 P3 no
longer precedes it** — see *What the re-sequencing changed elsewhere*. P5 therefore carries 009 P3's
`PortalEditBody` amendment (`LeaverRecordEndpointTests.cs:410-438`) and re-verifies 009 P3's
*Touches* citations in the same commit. **P2 lands first**, so the SPA half of the round-trip has a
spec; if P2 is reverted for CI flakiness, P5 proceeds with a *labelled manual* SPA check rather than
an unstated gap.
**Also:** the migration's **SQL** wants **009 P4**'s harness. The behaviour does not. If 009 P4 has
not landed, follow 007 P1's precedent: test the migration's *operations* in xUnit, run the SQL by
hand, and label the two halves differently in the PR rather than implying one covers the other.
**Note:** legacy detects this and crashes (`UpdateCheck.Always` on 148/153 `dbo.Employees` columns;
no `ChangeConflictException` handling in `Logic/`). Cite it — it is the strongest argument that
detection is genuine domain truth and not gold-plating.

**As built (2026-08-29, `feat/011-p5`).** Five things a reader should not have to reconstruct:

1. **`AuditableEntity` reaches three entities, not two — and `Punch` is not one of them.** *Touches*
   naming migrations for People and Identity only is **correct**: `Punch : Entity`, not
   `AuditableEntity`, so `TimeAttendance` is untouched and the module with no test project stays
   that way. The third entity is `SecurityGroup`, in Identity's own migration.
2. **`SecurityGroup` gets the column but not the token.** `PUT /api/security-groups/{id:guid}` is
   outside *Touches* and reads no echoed token, so configuring `IsConcurrencyToken()` there could
   only fire on an intra-request race that `SecurityGroupService` would surface as a **500**. It is
   recorded as **Outstanding — a real gap, not a principled exemption** in the new
   `ConcurrencyTokenInventoryTests`, which also holds the fourth `PUT`
   (`/users/{id}/security-groups`, exempt on principle: a set of ids has no row to be stale against).
3. **A missing token is a 400, not a default.** Both write paths refuse a `PUT` that carries no
   version. An optional token restores last-write-wins for any client that forgets it, silently —
   which is the defect. This is slightly stronger than *Done when* asks for, and deliberate.
4. **The migrations backfill.** `AddColumn` stamps existing rows with the all-zero uuid, which (3)
   then refuses — so every pre-upgrade record would be permanently uneditable. Both migrations carry
   a `gen_random_uuid()` `UPDATE`, and `ConcurrencyMigrationTests` fails if a regeneration drops it.
5. **Both halves of the migration are covered, by different means, labelled separately.**
   *Operations* — `ConcurrencyMigrationTests`, in xUnit, on every run. *SQL* — executed **by hand**
   against **Postgres 17** (`wm-dev-postgres-1`) on 2026-08-29, per 007 P1's precedent; there is
   still no automated Postgres harness and **009 P4 continues to own that**, so neither half is
   evidence for the other. The manual run migrated a database to the *pre-P5* schema, seeded **8
   rows** across `Employees`/`Users`/`SecurityGroups`, then: `Up` → all 8 backfilled to **distinct,
   non-zero** uuids, all three columns `uuid NOT NULL`; `Down` → all three dropped, no rows lost;
   `Up` again → re-applied and re-backfilled. **Falsified**: with the backfill removed the rows
   landed **all-zero** and were then refused **400 on every save, twice running** — the
   "permanently uneditable" defect in (4), observed rather than argued. Restored byte-identical.

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
and EF's in-memory provider does not have any — measured for P5 and true in the other direction
here: InMemory enforces a concurrency token but has no transaction to roll back, so an outbox test
against it would pass for the wrong reason. Unlike P5, this portion should **not** proceed with a
manual run as a substitute.
**Unblocked 2026-08-29 by D1** — was blocked on the mechanism question. The answer is **a small
outbox in `SharedKernel`**, not MassTransit in the API.
**Write the ADR first.** `docs/adr/0001-outbox-in-sharedkernel.md`, before any code, recording: that
§14A:797 already designates `SharedKernel` as the outbox's home; that the leaking dual write is
**Kafka** while §2:93's promise names **MassTransit/RabbitMQ**, which is why the promise no longer
fits; that §7A:264's *"known multi-week detour"* warning is about sagas, retry and DLQ rather than a
table and a dispatcher; and — the part a future reader will want — **what would make us reverse
this**, namely the first saga or the first job needing compensation, at which point MassTransit's
outbox earns its place and this one is replaced rather than extended. Cite `EventStreamProducers.cs:22-27`
as the failure that motivated it.

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

4. **`ARCHITECTURE.md` §14:549** — the Phase 1b row still described *"several groups per user
   combining as a **union**"*, which the user ruled against on 2026-08-05 and for which §4 was
   rewritten. §14 is the roadmap, not a design section, and this is staleness against a decision
   already taken rather than a new proposal, so it is **corrected in place**. Flagged as
   *Open questions* 2 in case you want it reverted.
5. **`ARCHITECTURE.md` §13A:739-740** — the rollback/migrate-on-start contradiction now carries the
   **D3 ruling** and a pointer to the named plan, so it stops reading as a live promise nobody owns.
6. **`ARCHITECTURE.md` §13** — the Redis and realtime rows updated for D2, and the concurrency row
   for the measured InMemory result.
7. **`docs/plans/STATE.md`** — 011 marked `approved`, the build order recorded, and
   *"Rollback that actually rolls back"* registered among the named-but-unwritten plans.

Proposed, **not** applied (design sections — see *Open questions*):

8. **`ARCHITECTURE.md` §2:93** — wrong under D1; concrete diff in *Open questions* 1.
9. **`ARCHITECTURE.md` §14A:797** — lists *"multi-tenancy"* as a `SharedKernel` cross-cutting concern
   when nothing implements one and §15:914 decided against it. **The draft's companion claim about
   §4:157 is withdrawn** — see *Open questions* 3.

---

## Open questions for the user

**Questions 2, 3 and 4 of the original draft were ruled on 2026-08-29** and are recorded as D1, D2
and D3 above. Question 1 ("is this production readiness at all?") is answered by the approval. What
**Question 5 (the concurrent-edit UX) was ruled on 2026-08-29 and is closed** — recorded as D4.
What remains is **two** substantive questions, correctly sequenced ahead of the portions that need
them (§2:93 before P7, key retention before P8), plus three documentation items.

**1. §2:93 is wrong under D1, and this is the diff.** A design section, so proposed rather than
applied. The tech-stack table currently reads:

```
| Commands & jobs | **RabbitMQ** via **MassTransit** (retry, outbox, sagas) |
```

After D1 that names a mechanism WM has decided *not* to use for the leg that actually loses events.
Proposed:

```
| Commands & jobs | **RabbitMQ** via **MassTransit** (retry, DLQ, sagas) |
| Reliable event publication | **Transactional outbox in `SharedKernel`** (ADR 0001) — the Kafka
  leg is the dual write that loses events; MassTransit's own outbox is not used, see §7A |
```

§7A:264's sentence *"MassTransit gives all of that plus a **transactional outbox**, so a database
commit and its message can never diverge"* stays true as a statement about MassTransit and becomes
misleading as a statement about WM. Suggest appending one clause: *"— which WM does not use for the
Kafka leg; see ADR 0001."* **P7 should not be built until this is settled**, not because the code
depends on it but because the ADR will cite §2 and should not cite something it is about to
contradict.

**2. §14:549 — applied, flagging it here.** The Phase 1b row still described *"several groups per
user combining as a **union**"*, which the user ruled against on 2026-08-05 (option A, one object
one membership) and for which §4 was rewritten. §14 is the roadmap rather than a design section, and
this is stale relative to a decision already taken rather than a new proposal, so it has been
**corrected in place**. Revert it if you would rather §14 changed only alongside the plans it
tracks.

**3. Multi-tenancy — I overstated this in the draft and am correcting my own claim.** The draft said
§4:157's *"**Multi-tenancy-ready** (tenant id + filter)"* and §14A:797's cross-cutting *"multi-tenancy"*
were **both** contradicted by §15:914's *"decided: database per customer"*, and that *"one of the
three is wrong"*. On re-reading, **§4:157 is defensible**: "multi-tenancy-*ready*" is a statement
about design posture, not about a shipped capability, and a single-tenant system can be built ready
for it. So there is no contradiction there and I withdraw that half.

  **§14A:797 does still read oddly** — it lists *"multi-tenancy"* among the cross-cutting concerns
  `SharedKernel` owns, alongside audit emission and the outbox, which reads as work to be built
  rather than a posture. Nothing in `src/SharedKernel/**` implements a tenant id or a tenant filter.
  It needs one word (`multi-tenancy-readiness`) or removal, and it is a design section, so it is
  yours. **Low stakes** — nobody has planned against it.

**4. Idempotency key retention — still open, and P8 needs a number.** Unchanged from the draft and
not covered by any ruling. How long must WM remember a key on an unattended on-prem box (§13A)? A
short window (24h) bounds the table and matches an offline queue's realistic lifetime; unbounded
grows forever on a server we cannot reach. And a replay *after* expiry creates a second punch — is
that acceptable, or must the window be long enough that it cannot happen? **P8 is last in the build
order, so there is time**, but the builder cannot invent this.

**5. The `CLAUDE.md` edit.** Unchanged from the draft: a factual test-project count corrected inside
an advisory paragraph (**four**, not two). If you would rather no agent edits `CLAUDE.md` for any
reason, revert it and the correction will live in this plan instead.
