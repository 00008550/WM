# 006 — A public demo, kept current by CI/CD

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §13A — *"Consequence: rollout tooling is a first-class deliverable"*
(`:683-692`), the running workstream named in §14 `:505`. This is its first instalment, and the
demo box is the first WM deployment that runs unattended.

**Legacy sources surveyed: none, deliberately.** TLW is an on-prem Windows product with no demo
mode, no container story and no CI — there is nothing in `E:\Tlw` to measure for this, and
opening it would be waste. The only legacy claim in the neighbourhood is §13A `:685`
("legacy needed `AutoSiteUpdater`, `AutoScriptExecutor`, WinSCP and a pile of batch scripts"),
which carries **no `file:line`** and is therefore a hypothesis by this repo's own convention. It
is not load-bearing for anything below. **Everything measured here was measured against WM's own
tree at `master` = `18ca773`, on 2026-08-05.**

## Why this plan exists

WM has no running instance anyone can look at. Every demonstration today is `dotnet run` plus
`ng serve` on the author's laptop, which means the product cannot be shown, cannot be shared, and
— more quietly — **is never exercised in the topology customers will actually run**. The
containers exist and are described in §13A, but nothing has ever run them continuously.

A permanently-hosted demo pays twice: it is the thing you send a link to, and it is the canary for
the on-prem deployment model. Every bug it finds is a bug a customer would otherwise find.

It also has one blocker that is not obvious from the outside, and four security facts that are.

## Ground truth

Measured against `master` = `18ca773` (CI merged as [#22](https://github.com/00008550/WM/pull/22);
003 P2a merged as `e948481`). `STATE.md` still said `master` is at `3f05071` with #21 open —
corrected as part of this plan.

| Question | Measured |
|---|---|
| Demo data in Production | **none.** `Program.cs:89-95` gates `IdentitySeeder` + `PeopleSeeder` + `PunchSeeder` + `DemoUserSeeder` behind `IsDevelopment()`; `:96-100` runs `IdentitySeeder` alone otherwise |
| What a Production boot therefore has | one `admin` user, three roles, one `All employees` group (`IdentitySeeder.cs:71-97`). **No employees, no punches, no `manager` login** |
| Demo dataset size, when it does run | 3 sites, 5 departments, **40 employees** (`PeopleSeeder.cs:43`), fixed RNG seed `20260720` (`:41`), 5 working days of punch history + today (`PunchSeeder.cs:29-45`), 3 linked users (`DemoUserSeeder.cs:37-46`) |
| Demo passwords | **two string literals hard-coded in the shipping assembly** (`DemoUserSeeder.cs:37`, `:44`, and logged at `:48`). Not reproduced here — read them there |
| `wm-api` hard dependencies | `postgres` + `redis` only (`docker-compose.prod.yml:92-94`); the brokers are deliberately soft (`:106-107`), and `KafkaEventStreamProducer` self-disables when unconfigured (`EventStreamProducers.cs:22-27`) |
| Image base images | `dotnet/sdk:10.0-alpine` → `aspnet:10.0-alpine` (`src/Api/WM.Api/Dockerfile:2,18`), `runtime:10.0-alpine` (worker `:16`), `node:20-alpine` → `nginx:1.27-alpine` (portal `:2,11`) |
| `--platform` / RID pinning in any Dockerfile | **none** — they build for whatever the builder is |
| Node used to build the portal image vs CI | **20 vs 24.** `frontend/portal/Dockerfile:2` pins `node:20-alpine`; `ci.yml:80-82` builds on Node 24. Node 20 left maintenance in April 2026, so the shipped image is built on an EOL, unpatched Node that no CI job exercises |
| Test projects in `WM.sln` | 3 — `WM.SharedKernel.Tests`, `WM.Modules.Identity.Tests`, `WM.Api.Tests` |
| CI | `.github/workflows/ci.yml` — build + test on push/PR to `master`. **No CD workflow exists.** Branch protection is not enabled, so nothing enforces CI yet |
| Registry | none. `docker-compose.prod.yml:87` defaults `WM_REGISTRY` to the literal `wm`, i.e. local images only |

### Four security facts, measured not assumed

**1. The shipped image contains a working JWT signing key and an admin password.**
`src/Api/WM.Api/appsettings.json:17` sets `Jwt:SigningKey` to a literal whose own text says it is
for development, and `:25` sets `Bootstrap:AdminPassword` to another. Neither is reproduced here.
That file is **not** in `.dockerignore` (only `appsettings.Development.json` is, `.dockerignore:12`),
so it ships inside `wm-api`. Nothing anywhere refuses to boot on those values: `IdentityModule.cs:41-51`
reads the section and builds a `SymmetricSecurityKey` from whatever it finds. A container started
without `Jwt__SigningKey` runs happily **on a key published in a git repository** — anyone who can
read it can mint an administrator token. `docker-compose.prod.yml:99,101` guards this with `:?`,
but that guard protects only people who use that compose file; the image itself is fail-open.

This also makes §13A `:679` false where it says *"There is no hard-coded administrator password in
a production build."* Corrected in this pass.

**2. Account lockout turns published credentials into a self-service outage.**
`AuthService.cs:17-18,36-41`: five failed attempts locks the account for fifteen minutes, and the
thresholds are `const`, not configuration. On a box whose credentials are printed on the internet,
the first bored visitor or credential-stuffing bot locks the demo out of itself. `:29-30` also
answers *"Account is temporarily locked"* where `:27` answers *"Invalid credentials"*, which is a
username oracle — irrelevant on a demo with published usernames, noted so it is not rediscovered.

**3. Of §5's day-one OWASP invariant, this is what is actually live** (grepped, not assumed):

| Live | Where | Not live |
|---|---|---|
| Default-deny fallback policy | `IdentityModule.cs:74` | **Rate limiting — nothing in `src/**` calls `AddRateLimiter`** |
| Per-endpoint permission policies | `:76-77` and every `Map*` | **Real client IP — no `UseForwardedHeaders`, so every request appears to come from the nginx container** |
| Lockout | `AuthService.cs:36-41` | **HSTS / HTTPS redirect — no `UseHsts`, no `UseHttpsRedirection`** |
| Refresh rotation + reuse-revokes-the-family | `AuthService.cs:65-73` | **CSP — `nginx.conf:10` says "CSP stays conservative"; `:11-13` sets three headers and none of them is a CSP.** The comment describes a header that does not exist |
| Password hashing (ASP.NET `PasswordHasher`) | `IdentityModule.cs:39` | **Audit trail — no audit event type, emitter or store exists** (§5 calls it day-one; §13 `:420` already says "topic designed") |
| Non-root containers, health-checked | `Dockerfile:26,36` | `AllowedHosts: "*"` (`appsettings.json:33`) |
| Swagger dev-only | `Program.cs:64-68` | |

**4. `/health` cannot fail.** `Program.cs:46` registers `AddHealthChecks()` with **no checks**, so
it returns `Healthy` with the database on fire. The Dockerfile's `HEALTHCHECK` (`:36-37`) targets
it, so Docker will report a broken API as healthy, and any `depends_on: service_healthy` built on
it means "the process started". Also: nginx proxies only `/api/` and `/hubs/` (`nginx.conf:32,42`),
so `/health` is **not reachable from outside at all** — a smoke check cannot use it as-is.

### Corrections made to WM's records in this pass

| Document | Was | Now |
|---|---|---|
| §13A `:679` | *"There is no hard-coded administrator password in a production build."* | **False, corrected.** `appsettings.json:17,25` ship a signing key and an admin password inside the image; the `:?` guard belongs to the compose file, not the image |
| §13A image table `:670-671` | `dotnet/aspnet:9.0-alpine`, `dotnet/runtime:9.0-alpine` | `10.0-alpine` — stale since [#14](https://github.com/00008550/WM/pull/14). Added: **no Dockerfile pins a platform**. Sizes flagged as un-remeasured |
| §13A `:660` | *"WM is .NET 9 (Kestrel — no IIS)"* | .NET 10 |
| §13 matrix | no row for deployment, CI/CD or a hosted instance | two rows added — rollout tooling (◐) and the hosted demo (▢). Per this repo's own convention, a WM-only capability with no row gets no scrutiny |
| §13 matrix | — | the `AutoSiteUpdater` / `AutoScriptExecutor` claim at §13A `:685` is marked as **carrying no `file:line`**, i.e. a hypothesis, rather than being repeated as fact |
| `STATE.md` | *"`master` is at `3f05071`"*, #21 open, #22 absent | `18ca773`; #21 and #22 recorded as merged; In flight cleared |

## Legacy behaviour (what we are replacing)

There is none. This is a WM-only capability, which by this repo's own convention (`STATE.md` →
Conventions, *"A WM-only capability has no row in the coverage matrix, so nothing prompts anyone
to check it"*) is exactly the kind of thing that escapes scrutiny — the realtime feed went three
phases unexamined for the same reason. §13 now carries a row for deployment and CI/CD.

The one thing worth saying about legacy: WM's demo will be *the* reference install, so anything
that only works because a developer's laptop is unusual will show up here first.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Demo data gated on `IsDevelopment()` (`Program.cs:89`) | **Invert** | It is a fail-open of the exact shape §14 decision 5 rejects, inverted twice over: it is *off* where it is wanted (a demo in Production mode) and *on* behind a single environment variable that also enables Swagger and developer exception pages. Replace the gate with **absence**: the seeders leave the shipping assemblies entirely. |
| Demo passwords as string literals in `DemoUserSeeder.cs:37,44` | **Invert** | A credential compiled into every customer's image. Configuration, no default, refuse to run without it. |
| `Jwt:SigningKey` / `Bootstrap:AdminPassword` defaults in `appsettings.json` | **Invert** | A published signing key that silently works. Move both to `appsettings.Development.json` (already `.dockerignore`d) and **refuse to boot** outside Development without a real key. |
| Lockout thresholds as `const` (`AuthService.cs:17-18`) | **Improve** | Right policy, unconfigurable. Options-bound, defaults unchanged. Benefits customers too, not just the demo. |
| `/health` with no checks | **Improve** | Split liveness from readiness and make readiness mean *migrated and connectable* — which is also what the demo seeder needs to wait for. |
| Full stack (postgres · redis · rabbitmq · kafka · api · worker · portal) | **Keep, and run it on the demo** | See the stack decision below. |
| `node:20-alpine` in the portal image | **Improve** | EOL and not what CI builds. Align on 24. |
| Building images on the deployment box | **Drop** | Considered and rejected — see the image decision below. |
| A self-hosted GitHub runner on the demo box | **Drop** | Rejected on the threat model — see the deploy decision below. |
| `ASPNETCORE_ENVIRONMENT=Development` on the box | **Drop** | Rejected by the user. It would also enable Swagger (`Program.cs:64-68`) and developer exception pages on a public host. |

## The four decisions this plan takes

### 1. The demo runs the **full** stack, not the slim one

`wm-api` needs only Postgres and Redis, so a slim demo is possible and would be cheaper. Run the
full stack anyway:

- **§13A `:627-637` already decided that maintaining two topologies costs more than running one
  well.** A demo running a topology no customer runs is not evidence about the product.
- The demo is the only continuously-running WM deployment that will exist. Its second job is to be
  the canary for the unattended-server risks §13A `:641-649` names — Kafka disk growth, startup
  ordering after a reboot, brokers arriving late. None of those can be observed on a slim stack.
- On 24 GB the cost is noise: Kafka in KRaft ≈ 1 GB, RabbitMQ ≈ 200 MB (§13A `:629`).
- It is also the only way we find the arm64 problem below **before a customer does**.

**One demo-specific deviation, and it is a safety one:** Kafka retention drops from 400 days /
20 GB (`.env.example:31-32`) to something like 7 days / 2 GB. The Always Free box has ~200 GB of
block storage for everything, and "unbounded log fills the disk on a server nobody watches" is
§13A's own first named risk. The demo has no recalculation window to protect.

### 2. Images are **cross-compiled multi-arch in CI**, not built on the box, not QEMU-emulated

Oracle's Always Free compute is Ampere (`VM.Standard.A1.Flex`), so every image must be
`linux/arm64`. Today nothing pins a platform, and CI runs on `ubuntu-latest` (amd64) — GitHub's
free arm64 hosted runners are **public-repo only**, and WM is private.

Three options, decided:

- **QEMU emulation (`docker/setup-qemu-action`) — rejected.** Correct but 10–20× slower for a
  .NET SDK build; a private repo has 2,000 free Actions minutes/month and CI already spends on
  every push and PR.
- **Building on the box — rejected.** It would put the source and a build toolchain on a host we
  have decided to treat as untrusted, make the deployed artefact something CI never tested, and
  have four Ampere cores compiling .NET and Angular while serving the demo.
- **Cross-compilation with buildx — chosen.** The mechanism is cheap because *WM's publish output
  is already architecture-neutral*: the entrypoint is `dotnet WM.Api.dll` (`Dockerfile:39`), so no
  apphost is needed and a framework-dependent portable publish runs anywhere. Pin the build stage
  to the native builder (`FROM --platform=$BUILDPLATFORM …sdk:10.0-alpine`) and let the runtime
  stage take `TARGETPLATFORM`. The portal is the same story for free — `ng build` output is
  arch-independent, so only the `nginx` stage is arm64.

> **The one thing most likely to break, named:** `Confluent.Kafka` (`WM.Api.csproj:7`) P/Invokes
> **librdkafka**, a native library, and the runtime base is **musl** Alpine. `linux-musl-arm64` is
> the least-supported combination librdkafka ships. The failure is nasty because it is **lazy**:
> `KafkaEventStreamProducer` builds its producer in the constructor (`EventStreamProducers.cs:29-35`)
> and is a singleton resolved on first use, so a missing native library does not fail at startup —
> **it 500s on the first punch**, with everything else looking perfectly healthy. Mitigation, in
> order: (a) P4 runs the arm64 image and records a punch before anything is deployed; (b) if the
> native library is absent, switch the API runtime stage to the glibc base
> (`mcr.microsoft.com/dotnet/aspnet:10.0`, non-alpine), keeping `icu-libs`/`tzdata` equivalents;
> (c) only if both fail, run the demo without Kafka — which is the least acceptable answer,
> because it hides the problem rather than solving it.

### 3. GHCR, with an explicit eye on the free-tier storage limit

`GITHUB_TOKEN` already carries package rights, so no secret is needed to push. But **GitHub
Packages storage for *private* packages is metered** (500 MB on Free, 2 GB on Pro at the time of
writing — verify against the account's plan; I cannot see it from here), while **public packages
are unmetered**. Four images (`wm-api`, `wm-worker`, `wm-portal`, `wm-demo-seed`) pushed on every
merge to `master` will exhaust a 500 MB allowance quickly even with layer sharing.

So P6's "done when" includes **measuring the actual pushed size on the first run and recording
it**, and the workflow prunes to the last N versions per package from day one. If the measurement
says the allowance cannot hold one full set, the fallbacks in order are: upgrade the plan, or make
the four packages public (which publishes compiled product — the user's call, raised as an open
question), or fall back to building on the box after all.

### 4. Deploy over SSH with a **forced command**, and no long-lived credential anywhere

The threat model the user set is the right one: **assume the demo box is eventually compromised,
and make that cost nothing but the box.** That rules more out than it first appears.

- **A self-hosted GitHub runner on the box — rejected.** It is tempting (native arm64, no registry
  quota, no inbound SSH, logs in GitHub) but it inverts the threat model: a compromised box then
  holds a job token for a **private** repository and can intercept future jobs. Box compromise
  would cost the source.
- **A pull agent on the box — rejected.** It needs a long-lived `read:packages` PAT stored on the
  box, and a failed pull is invisible to the pipeline.
- **SSH push from Actions — chosen**, with three qualifications that do the actual work:
  1. A dedicated non-root `deploy` user whose `authorized_keys` entry is
     `command="/usr/local/bin/wm-deploy",restrict` — the CI key **cannot get a shell**. Worst case
     for a stolen key is "the demo redeploys".
  2. The box stores **no GitHub credential**. Each deploy pipes the job's own ephemeral
     `GITHUB_TOKEN` into `docker login ghcr.io --password-stdin` on the box, pulls, and logs out.
     The token dies with the workflow run.
  3. The host key is **pinned** via a secret, not `StrictHostKeyChecking=no`.

## Security posture — a box that will eventually be compromised

The demo is the first WM deployment reachable from the internet, with its credentials published on
purpose. Treat it as **untrusted**, and design so that its total loss costs exactly one box.

**What the box holds — the whole list, and nothing may be added to it without a reason:**

| Secret | Where it comes from | Blast radius if stolen |
|---|---|---|
| `POSTGRES_PASSWORD` | `openssl rand` **on the box**, into `deploy/.env`, never committed, never reused | the demo's fake data |
| `JWT_SIGNING_KEY` | same, **distinct from every other environment**; P2 makes the shipped default unusable | forged tokens for the demo only |
| `WM_ADMIN_PASSWORD` | same. **Never published** — `admin` holds `users.manage` | the demo's own accounts |
| `RABBITMQ_PASSWORD` | same | the demo's broker |
| Demo user passwords | configuration (P1), published deliberately in the README | precisely what the demo already grants any visitor |
| A GitHub credential | **none.** The deploy user's ephemeral `GITHUB_TOKEN` arrives per run and dies with it (decision 4) | — |

**Rules, stated so a reviewer can check them:**

1. **No credential, key or connection string on the demo box is reused anywhere else** — not in
   development, not in CI, not in any customer install. Every one is generated on the box.
2. **The database holds only generated fake data** — `PeopleSeeder.cs` names and `@wm.demo`
   addresses. **Never a customer backup, never anonymised real data.** A demo box is not a place
   where "anonymised" needs to be argued about.
3. **GitHub holds nothing that reaches beyond the box**: an SSH key restricted to a forced command,
   a host-key pin, a base URL, and the published demo login. Nothing else.
4. **Inbound surface is one port on one container.** Caddy publishes 443 (and 80 for the ACME
   redirect); the portal's `ports:` mapping is removed; the API, worker, database and both brokers
   stay on the internal network exactly as §13A `:676` requires. SSH is key-only and non-root.
5. **Recovery is rebuild, not repair.** Nothing on the box is worth restoring, and P7's reset
   script plus P5's runbook mean a fresh instance is an hour of work. If it is compromised: destroy
   the instance, rotate the deploy key and the SSH host entry, create a new one. There is no
   forensics obligation on a box that holds nothing.

**What this plan adds to the product's own posture** (the "not live" column above): P2 closes the
published-signing-key fail-open, P3 adds rate limiting and a real client IP. **What it does not
close, and should be read as still open:** the audit trail, CSP, HSTS in the product itself, and
`AllowedHosts`. Those are named in *Out of scope* with owners rather than quietly omitted.

## Edge cases

- **First boot ordering.** Migrations run inside the API at startup (`Program.cs:85-87`). The demo
  seeder must therefore wait for *migrated*, not *process up* — which is precisely why P2's
  readiness check exists and why the seeder's `depends_on` uses it.
- **A deploy where the pull silently fails.** The old container keeps serving and every "is it up"
  check passes. This is why the smoke check asserts the **build identity**, not liveness.
- **Two merges within a minute.** The deploy job takes a concurrency group with
  `cancel-in-progress: false` — cancelling a deploy mid-`compose up` leaves the box in a mixed
  state. CI's existing group (`ci.yml:19-21`) cancels, correctly, and must not be reused.
- **Published credentials + lockout** (`AuthService.cs:36-41`) — the demo locks itself out of
  itself. P3 makes the thresholds configurable; the demo sets them lenient; the nightly reset is
  the backstop.
- **A visitor deletes the employees.** They can: the demo `manager` login holds
  `employees.view` … `timesheets.edit` (`IdentitySeeder.cs:48-60`). That is the point of a demo,
  and the answer is the scheduled reset, not a read-only account. **The `admin` password is never
  published** — it holds `users.manage`, and handing that out means handing out the demo's own
  account management.
- **Host reboot** (Oracle maintenance, or the reclamation of idle Always Free instances). Every
  service is `restart: unless-stopped`, which restores a running container when the daemon starts —
  provided the Docker service itself is enabled at boot. Both must be checked; the 6-hourly smoke
  run is what tells you when they were not.
- **Certificate renewal fails while nobody is looking.** HTTPS expiring is silent until someone
  clicks the link. The scheduled smoke run uses `https://` and therefore fails on an expired cert.
- **The demo gets indexed by search engines.** A public demo out-ranking the real product site, and
  a crawler clocking employees in and out, are both avoidable: `noindex` and a `robots.txt`.
- **Two proxies, one X-Forwarded-For.** Caddy → nginx → Kestrel means the header carries
  `client, caddy` by the time the API sees it. `ForwardLimit` must be 2 and both proxy hops must be
  trusted, or the rate limiter in P3 buckets the entire internet under one key — which is either
  useless or a global denial of service.
- **`/health` is not publicly routable** (`nginx.conf:32,42` proxy only `/api/` and `/hubs/`). The
  smoke check must run from GitHub against the public URL, exactly as a visitor would, so P2 maps
  readiness at **`/api/health/ready`** — under the path nginx already proxies, so no nginx rule and
  no second origin. Liveness stays at `/health` for the orchestrator.
- **Readiness is anonymous and therefore public.** It reports status plus a build identity that is
  **empty unless set**, so a customer install discloses nothing; the demo sets it deliberately,
  because a smoke check that cannot tell which build answered is not a smoke check.
- **The demo seeder meets a database that already has data.** Decided, not deferred: it refuses,
  loudly, with a non-zero exit — unless it can prove from its own provenance record that it created
  that data itself. It never deletes anything it did not create.

## Target design in WM

**Demo data lives outside the product.** A new `src/Demo/WM.Demo` console project takes
`PeopleSeeder`, `PunchSeeder` and `DemoUserSeeder` out of the shipping assemblies and becomes its
own one-shot image, `wm-demo-seed`, run as a `restart: "no"` service in the demo compose only. The
guarantee that matters is structural rather than procedural: **no environment variable set on a
customer's stack can produce demo data, because the code is not in the image they have.**

It keeps its own provenance in its own schema — `demo.seed_state`, created by the seeder itself
and never by an EF migration, so the schema's existence *is* the proof that a database is a demo
database. That single fact decides every case:

| Situation | Behaviour |
|---|---|
| Empty database, `Demo__Enabled=true` | seed, and record what was created |
| Already seeded, same version | no-op, exit 0 (a compose restart must not duplicate) |
| `Demo__Reset=true` **and** `demo.seed_state` present | drop the module schemas, let the API re-migrate, reseed |
| Any data present and **no** `demo.seed_state` | **refuse**, non-zero exit, explain why |
| `Demo__Enabled` unset | refuse |

Demo user passwords come from configuration with **no default**; the seeder refuses to create a
user without one. No password literal survives anywhere in `src/**`.

This costs one thing and the plan states it plainly: **`dotnet run` on the API no longer seeds a
dev database.** Developers gain a one-line command (`dotnet run --project src/Demo/WM.Demo`),
documented in the README. 005 P3's "re-measure the manager's employee count against the running
stack" uses that same command.

**Everything else** is the deployment surface: `deploy/docker-compose.demo.yml` (prod stack +
Caddy + the seeder, demo-sized Kafka retention), `.github/workflows/cd.yml`, and a runbook under
`docs/` for standing the box up. Cites §13A `:658-682` (containers, single origin, only the portal
exposed) and §5 (no endpoint without a policy — the readiness endpoint P2 adds is a new anonymous
transport and must go through `EndpointAuthorizationInventoryTests` deliberately).

### What "the demo is working" means — the smoke check, as an assertion

Run by the CD pipeline after deploying, and on a 6-hourly schedule. Every step is a public HTTPS
request from GitHub, in the order a failure is most diagnostic:

1. `GET /` → 200, body contains the SPA root element. *(Caddy, cert, nginx, the bundle.)*
2. `GET /api/health/ready` → 200 **and the reported build matches the SHA just deployed**.
   *(The proxy, the API, Postgres, migrations — and that the new image is the one running.)*
3. `POST /api/auth/login` with the demo manager → 200 with an access token. *(Identity, the seeded
   users, password hashing.)*
4. `GET /api/employees` → 200 with a non-empty list. *(People, the seed, scope resolution.)*
5. `POST /api/punches` for `E1000` → 201. **This is the step that matters most**: it is the only
   path that loads librdkafka (§2 above), and it crosses all three modules plus the event stream.
6. `GET /api/punches/recent` → the punch from step 5 is present.

A failure at any step fails the workflow, which is how a broken deploy gets noticed by the pipeline
instead of by the user clicking the link.

## Out of scope for this plan

- **The audit trail.** Measured as absent above; §5 calls it a day-one invariant and §13 `:420`
  already tracks it. It is a module-sized piece of work, not a demo task.
- **HSTS, CSP and the security headers the nginx comment claims.** P5 sets them at the edge (Caddy)
  for the demo; making the *product* emit a correct CSP is its own slice, and the false comment at
  `nginx.conf:10` is noted rather than fixed here.
- **A "this is a demo" banner or credentials shown on the sign-in page.** Wanted, but it needs a
  config surface the SPA can read; the credentials go in the README for now.
- **Multi-tenant or multi-instance hosting**, staging environments, blue/green, zero-downtime
  deploys. One box, one stack, a few seconds of downtime on `compose up -d`.
- **Backups.** All demo data is fake and regenerable by design; a backup would be a liability, not
  an asset. On-prem backup/restore stays where §13A `:690` puts it.
- **Anything in 001, 003, 004 or 005.** No portion here touches `ScopeModel.cs`,
  `DataScopeResolver.cs`, `EmployeeScopeExtensions.cs` or `SecurityGroup*`.

## Portions

**P1–P3 are code**: they go through builder + reviewer with real tests, and each improves the
product for customers, not only the demo. **P4–P7 are infrastructure**: they cannot be unit-tested
from this repo, and their evidence is the smoke check plus pasted output. The plan says which is
which rather than implying every portion is equally proven.

### [ ] P1 — Demo data leaves the product
**Touches:** new `src/Demo/WM.Demo/` (project + `Dockerfile`), `WM.sln`,
move `src/Modules/People/…/Data/PeopleSeeder.cs`,
`src/Modules/TimeAttendance/…/Data/PunchSeeder.cs`,
`src/Api/WM.Api/Infrastructure/DemoUserSeeder.cs`,
`src/Api/WM.Api/Program.cs:82-101` (drop the dev-only seed block and the `DemoUserSeeder`
registration at `:33`), module `RegisterServices` (`TimeAttendanceModule.cs:26` and the People
equivalent), new `src/Demo/WM.Demo.Tests/`, `README.md:20-22`
**Done when:** `wm-api` contains no fake-data generator and no password literal; the seeder is a
separate program and image; `Demo__Enabled` is required; passwords come from configuration with no
default; the five rows of the behaviour table above hold; `demo.seed_state` records what was
created; re-running without `--reset` is a no-op.
**Tests:** a new test project — seeds an empty database; second run is a no-op; refuses when data
exists with no `seed_state`; refuses without `Demo__Enabled`; refuses without passwords; `--reset`
on a seeded database restores exactly the same dataset (the RNG seed is fixed at
`PeopleSeeder.cs:41`, so assert equality, not "about 40"); **and a test asserting the API assembly
no longer references the seeder types**, so a later merge cannot quietly put them back.
**Risk:** medium. The behaviour is testable; the risk is the dev-loop change, which must be in the
README in the same commit.
**Sequencing:** see the note below — this portion and 005 P3 both rewrite `DemoUserSeeder.cs`.

### [ ] P2 — A host that can be verified from outside
**Touches:** `src/Modules/Identity/…/IdentityModule.cs:41-51` (guard),
`src/Modules/Identity/…/Services/TokenService.cs:12-22` (`JwtOptions` validation),
`src/Api/WM.Api/appsettings.json:14-26` → `appsettings.Development.json`,
`src/Api/WM.Api/Infrastructure/PlatformEndpoints.cs:26`, `src/Api/WM.Api/Program.cs:46`,
`src/Api/WM.Api/Dockerfile:36-37`, `src/Api/WM.Api.Tests/Security/*`
**Done when:** outside Development the host **refuses to start** if `Jwt:SigningKey` is missing,
shorter than 32 bytes, or equal to the value that used to ship in `appsettings.json`; the same file
no longer carries a `Bootstrap:AdminPassword`; `/health` stays a liveness check; a new
**`/api/health/ready`** — under the prefix nginx already proxies, so the smoke check needs no nginx
change — reports the database and applied migrations and includes a build identity taken from an
env var that is **empty by default** (so a customer install discloses nothing); the container
`HEALTHCHECK` targets readiness.
**Tests:** `ApiTestHost` already composes the host in `Environments.Production`
(`ApiTestHost.cs:78`) — compose it with (a) no key, (b) a short key, (c) the retired literal, and
assert each refuses; assert a good key still starts; assert `/api/health/ready` is 200 with a
database and non-200 without; assert the build identity is absent when unset.
**`EndpointAuthorizationInventoryTests` will fail on purpose** — readiness is a fifth
anonymous transport, and that test exists so adding one is a decision with a reviewer attached
(003 P2a). Update it deliberately and say so in the PR.
**Risk:** medium. It can stop the app booting — which is the point — but a wrong guard breaks
every developer at once. Keep Development untouched.

### [ ] P3 — The public edge: real client IPs, rate limits, configurable lockout
**Touches:** `src/Api/WM.Api/Program.cs` (forwarded headers before auth; `AddRateLimiter`),
`src/Modules/Identity/…/Services/AuthService.cs:17-18,36-41` (options-bound thresholds),
`src/Api/WM.Api.Tests/`
**Done when:** `UseForwardedHeaders` runs before authentication with `ForwardLimit` and known
networks configured for the compose bridge, so `RemoteIpAddress` is the visitor and not nginx; the
three anonymous auth endpoints carry a per-IP rate limit that returns 429; the SignalR hub and
authenticated traffic are **not** limited by the same policy; lockout threshold and duration come
from configuration with today's values (5 / 15 min) as defaults.
**Tests:** requests over the limit get 429 and under it get through; two different forwarded IPs
get independent buckets (the test that would have caught the "everyone shares one bucket" trap);
`X-Forwarded-For` from an **untrusted** hop is ignored; lockout still fires at the configured
threshold and a raised threshold changes it.
**Risk:** medium. Forwarded-headers misconfiguration is a spoofing surface, not just a bug — a
trusted-proxy list that is too broad lets a caller forge their own client IP.
**This portion must land before the URL is shared**, not merely before P6.

### [ ] P4 — arm64 images
**Touches:** `src/Api/WM.Api/Dockerfile`, `src/Worker/WM.Worker/Dockerfile`,
`frontend/portal/Dockerfile` (also Node 20 → 24), `src/Demo/WM.Demo/Dockerfile`, `.dockerignore`
(add `deploy/.env`)
**Done when:** `docker buildx build --platform linux/arm64` produces all four images on an amd64
machine with the build stages running natively; the portal image builds on the same Node major CI
uses; each image's `docker manifest inspect` shows `linux/arm64`; **an arm64 `wm-api` container
starts and records a punch** with `Kafka__BootstrapServers` set — the librdkafka check, run before
anything is deployed.
**Tests:** **none in the test suite** — this portion's evidence is the build output and the punch,
pasted into the PR. Say so; do not report it as covered.
**Risk:** **high, and partly unverifiable from the dev machine.** The Docker daemon was not running
when this was surveyed, so the multi-arch availability of `apache/kafka:3.8.0`,
`rabbitmq:3-management-alpine` and the .NET 10 alpine images is **assumed, not measured** — the
first step of this portion is to check each with `docker manifest inspect`. The librdkafka
musl-arm64 question is the single most likely first-deploy failure in the whole plan.

### [ ] P5 — The demo stack and the box
**Touches:** new `deploy/docker-compose.demo.yml`, new `deploy/.env.demo.example` (placeholders
only, never values), new `deploy/Caddyfile`, new `docs/DEMO-RUNBOOK.md`
**Done when:** the demo compose runs the full stack plus the one-shot seeder plus Caddy; **only
Caddy publishes a port** (the portal's `ports:` mapping at `docker-compose.prod.yml:135-136` is
removed for the demo); TLS is automatic; Kafka retention is demo-sized; `noindex` and a
`robots.txt` are served; the runbook covers instance creation, **both** the VCN security list
*and* the host firewall (an Oracle image blocks 80/443 in its own iptables even when the security
list allows them — the classic "why is it unreachable" hour), Docker install on aarch64, enabling
the Docker service at boot, generating every secret **on the box** with `openssl rand`, and the
recovery steps for a reclaimed or rebooted instance.
**Tests:** none — infrastructure. Evidence is the stack running and P6's smoke check passing.
**Risk:** medium, mostly environmental. **Unverifiable from here:** I cannot create an Oracle
instance, and A1 capacity ("Out of host capacity") is a known first-hurdle failure that no amount
of planning avoids. Oracle's reclamation policy for idle Always Free instances should be
re-checked at deploy time rather than trusted from this document.
**Open question 1 blocks this portion** — Caddy needs a hostname.

### [ ] P6 — CD: build, push, deploy, prove it
**Touches:** new `.github/workflows/cd.yml`, `docs/DEMO-RUNBOOK.md`
**Done when:** merge to `master` builds the four arm64 images, tags them with the commit SHA and
`demo`, pushes to GHCR, deploys over SSH to a **forced-command** `deploy` user with an ephemeral
GHCR login, runs the six-step smoke check against the public URL, and fails the workflow on any
step. Old package versions are pruned to the last 3. The workflow runs on `push: master` and
`workflow_dispatch` **only — never on `pull_request`**, so a fork can never reach the secrets, and
the secrets live in a GitHub **Environment** named `demo`. Concurrency group with
`cancel-in-progress: false`. A separate scheduled job runs the smoke check every 6 hours.
**Required repository/environment secrets, named:** `DEMO_SSH_HOST`, `DEMO_SSH_USER`,
`DEMO_SSH_KEY`, `DEMO_SSH_KNOWN_HOSTS`, `DEMO_BASE_URL`, `DEMO_SMOKE_USER`,
`DEMO_SMOKE_PASSWORD`. **GHCR needs no secret** — `GITHUB_TOKEN` with `packages: write`.
**Also done when:** the first successful run records the **actual pushed size** of the four
packages in the PR, against the account's package-storage allowance (see decision 3).
**Tests:** none in the suite. The smoke check *is* the test, and it is the only portion of the
plan whose correctness is demonstrated by running it.
**Risk:** **high.** First-run failures are near-certain and mostly environmental (host key format,
`docker login` over a forced command, buildx cache). None of it can be rehearsed from here.

### [ ] P7 — Reset on a schedule, and publish the demo
**Touches:** `deploy/` (a `wm-demo-reset` script + systemd timer or cron), `docs/DEMO-RUNBOOK.md`,
`README.md`, `docs/plans/STATE.md`
**Done when:** a nightly job resets the demo to the seeded dataset via P1's `--reset` path and
clears any lockouts by recreating the users; the README carries the demo URL and the credential
block (**values pasted by the user after the box exists — no password is ever committed by the
builder**), states plainly that the demo is public, disposable and reset nightly, and that its
credentials grant nothing anywhere else; the `admin` password is documented as **not published**.
**Tests:** none directly; P1's `--reset` test covers the behaviour the timer invokes. The 6-hourly
smoke run is what proves the reset did not leave the demo broken — schedule the reset **at least
an hour before** a smoke run so a bad reset is caught the same night.
**Risk:** low, with one sharp edge: a reset that runs while the API is mid-migration. Sequence it
as stop-api → reset → start-api → seed, in the script, not by luck.
**Drive-by:** `README.md:43` still lists "device gateway" as upcoming work; devices were dropped by
decision on 2026-07-20 (§13 `:440`). Fix it while in the file.

## Where this sits in the queue — and why I agree with you

**Alongside the authorization work, not ahead of it.** 003 P2b is a live defect in shipped code
(a manager can create and move employees outside their own scope); this plan ships a nicety. The
ordering principle the repo already uses — *"fix what is bleeding first"* — settles it.

But "alongside" is doing real work in that sentence, because **006's three code portions are
independent of the access model**. P2 and P3 touch host configuration and the request pipeline;
none of them reads `WithinScope`, `ScopeModel` or `DataScopeResolver`. The only file 006 and 005
both open is `IdentityModule.RegisterServices` — 006 P2 adds a guard at `:41-51`, 005 P3 edits
`AddAuthorization` at `:67-78`. Adjacent statements in one method: a trivial conflict, not a
design collision.

**P1 is the exception and it needs a real decision.** 005 P3's touch list already names
`DemoUserSeeder.cs` — it is where the demo manager finally gets a `Site managers` group and where
`STATE.md`'s long-wrong "manager sees 16" gets re-measured. 006 P1 *moves that file*. Two orders
work and they differ in cost:

- **006 P1 after 005 P3** *(recommended)* — the seeder is moved once, with its final content, and
  005 P3 is written against the file it already expects. One merge, no rewrite.
- **006 P1 before 005 P3** — the demo ships weeks sooner; 005 P3 re-targets its touch list to
  `src/Demo/WM.Demo`. Nothing is lost except that the plan file needs amending.

So the honest shape is **two lanes, not one line**:

- **Lane A — the access model, unchanged:**
  **003 P2b → 005 P1…P5 → 003 P3 → 001 P4 → 001 P5 → 005 P6 → 004.**
  005 P1–P5 stay contiguous: between P3 and P4 the system is half-refactored — the group carries
  permissions while the resolver still walks the old union — and that is not a state to park in
  for the length of a deployment plan.
- **Lane B — this plan:** **P2 → P3 → P1 → P4 → P5 → P6 → P7**, with the single cross-lane
  constraint that **P1 lands after 005 P3**.

**P2 and P3 may be pulled forward past 003 P2b if you want them sooner.** They are security fixes
to code that is merged and running, worth having whether or not a demo ever exists, and they are
the useful thing to build whenever lane A is waiting on a decision. **P4–P7 touch `src/**` only in
the four Dockerfiles**, so they never collide with lane A at all.

## Open questions for the user

1. **What hostname does the demo answer on?** Caddy's automatic TLS needs a name pointing at the
   box, and P5 cannot be built without an answer. Options: a domain you own (cleanest — an `A`
   record to the instance's public IP); a free dynamic-DNS name such as DuckDNS (works, needs
   Caddy's DNS-challenge build); or a wildcard-resolver name like `<ip>.sslip.io` (no registration,
   but Let's Encrypt rate-limits the shared parent domain and issuance can fail unpredictably).
   **Recommendation: a real domain.** Plain HTTP is not on the list: a public sign-in form over
   `http://` is indefensible even when the credentials are published.
2. **May the four demo images be public GHCR packages?** Public packages have no storage limit
   and no token requirement; private ones are metered and may not fit the free allowance (decision
   3). Public means anyone can `docker pull` and decompile the compiled product. My read: keep them
   **private**, prune hard, and revisit if the measurement in P6 says the allowance cannot hold one
   set — but it is a commercial call, not a technical one.
3. **Does the demo publish a `manager` login only, or an employee login too?** Two accounts show
   the self-service story and the manager story, which is most of the product. Three is what
   `DemoUserSeeder` creates today. The `admin` account is not published under any option.
4. **Proposal, not applied — two version claims outside a surveyor's remit.** §13A's were corrected
   in this pass (it is a deployment record); these two are not mine to edit. Concrete diff:
   - `CLAUDE.md:3` — *"**Workforce Management Platform** — .NET 9 + Angular rebuild"* →
     *".NET 10 + Angular"*. The repo moved in [#14](https://github.com/00008550/WM/pull/14);
     `Directory.Build.props:3` says `net10.0`.
   - `ARCHITECTURE.md:96` (§2 tech stack) — *"Angular 19 (standalone, signals, zoneless)"* →
     *"Angular 22"*. `frontend/portal/package.json:13-19` pins `^22.1.0`, and `ci.yml:80-82`
     builds it on Node 24. §2's .NET row is already correct at 10.
5. **Should branch protection be enabled on `master` now?** CI has been green since #22 but nothing
   enforces it, and this plan adds a workflow that deploys whatever lands there. A demo that
   auto-publishes from an unprotected branch is a strange combination. Not part of any portion —
   it is a repository setting, and yours to make.
