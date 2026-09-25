# 022 — Punch times in the site's zone, not the browser's

Status: approved           <!-- draft → approved → in-progress → in-review → merged -->
Approved by the user 2026-09-25 (both portions), directly in the coordinating session. P1 builds only after #98 merges.
Roadmap: ARCHITECTURE.md §14 — cross-cutting; the display half of plan **008** (a day has a place)
Legacy sources surveyed: **none, deliberately.** This is a defect in WM's own portal, found by
running it (2026-09-24). Everything below was measured against WM's tree at `master` `35a4258`, and
against PR #98 (`feat/010-p2`) for the ordering check.

---

## Why a new plan and not a portion elsewhere

- **008 is merged** (all five portions, `35a4258`). A sixth portion would reopen a finished plan and
  its approval ("all 5 portions", 2026-09-24) does not cover it.
- **011 is the wrong home.** Its approval covers "all 8 portions" of *production machinery* (idempotency,
  transactions, jobs, observability) and it is lane D, which is chosen for touching almost nothing
  domain-shaped. A display defect in TimeAttendance DTOs and two portal pages would widen 011's
  approved scope and entangle its build order (P8 already rewrites `PunchService.RecordAsync`).
- **A tiny plan** carries its own approval, its own two portions, and closes when they merge.

## Ground truth

### Where the portal formats a punch instant or day

Every `DatePipe` / `toLocale*` / `Intl` / `formatDate` in `frontend/portal/src` (grep, 2026-09-25).
No `toLocale*`, `Intl` or `formatDate` call exists; all formatting is `DatePipe` with **no zone
argument**, i.e. the browser's zone.

| Surface | Formats at | Value | Endpoint / payload | Zone in the payload? | LocalDate in the payload? |
|---|---|---|---|---|---|
| Dashboard — live feed (initial load) | `dashboard.component.ts:158` `entry.time \| date:'HH:mm:ss'` | punch instant | `GET /api/punches/recent` → `RecentPunchEntry` (`PunchService.cs:203-206`, built `:235-238`) | **No** | **No** |
| Dashboard — live feed (realtime) | same `:158` | punch instant | SignalR `punchRecorded` = `PunchRecorded` as-is (`EventStreamProducers.cs:130`; record `Contracts/PunchRecorded.cs:12-21`; portal type `realtime.service.ts:7-16`) | **No** | **No** |
| Dashboard — "Currently clocked in" | `dashboard.component.ts:101` `person.since \| date:'HH:mm'` | latest In instant | `GET /api/attendance/live` → `LivePresenceEntry` (`PunchService.cs:18-20`, built `:265-266`; portal `workforce.api.ts:95-102`) | **No** (carries `siteId` only) | **No** |
| /me — "since HH:mm" | `self-service.component.ts:36` | latest punch instant | `GET /api/me/punches` → `RecentPunchEntry` (`PunchService.cs:188-201`) | **No** | **No** |
| /me — recent punches list | `self-service.component.ts:108` `'d MMM HH:mm'` | instant, **day derived from it** | same | **No** | **No** |
| /me — timesheet day heading | `self-service.component.ts:76-77` `day.date \| date:'EEEE'` / `'d MMM'` | `DateOnly` string | `GET /api/me/timesheet` → `TimesheetDay.Date` (`PunchService.cs:24`) | n/a | **Yes — already the frozen `LocalDate`** (`GetTimesheetAsync` groups by it, `:296`) |
| /me — timesheet interval | `self-service.component.ts:85` `in`/`out \| date:'HH:mm'` | instants | `TimesheetInterval` (`PunchService.cs:25`) | **No** | n/a (the day is on the parent) |
| Admin timesheet | **no portal screen** — the endpoint exists (`TimeAttendanceModule.cs:99-114`, same `TimesheetDay` shape) and nothing in `frontend/` calls it | — | `GET /api/attendance/timesheet/{id}` | **No** | Yes |
| POST punch responses | not rendered (self-service reloads the list) | — | `POST /api/punches`, `POST /api/me/punch` return the `Punch` entity (`TimeAttendanceModule.cs:83`, `:146`) | **Yes** — `LocalZone` (`Domain/Punch.cs:64`) | **Yes** — `LocalDate` (`Punch.cs:60`) |

Not punch data, and **correctly** in the viewer's zone: `dashboard.component.ts:28` (today's date
header) and `:31` (last refresh). `employees.component.ts:85,87` format `DateOnly` employment dates
(`'MMM y'`); Angular parses a date-only ISO string as a local calendar date, so they do not shift.

`GET /api/employees` already returns `timeZone` / `timeZoneSource` (`PeopleModule.cs:119-120`); the
portal's `EmployeeRow` (`workforce.api.ts:14`) does not declare them and nothing reads them.

### Corrections to the brief this survey measured

- **Timesheet day grouping is already right.** The API groups by the frozen `LocalDate`
  (`PunchService.cs:291-296`) and the portal renders `day.date` as sent. What is wrong in the
  timesheet is only the *times inside* a day (`:85`) — which can then disagree with the day heading
  above them (a 19:44 UTC Ljubljana punch shows as 00:44 under "Thu 17 Sep" in Tashkent).
- **There is no admin timesheet screen.** Only `/me` has one. The admin endpoint still gets the zone
  (invariant 2 — Flutter and any later screen need it).
- **The /me recent-punches list derives the day from the instant** (`'d MMM HH:mm'`, `:108`). That is
  the one place the portal recomputes a day, and it is the defect the brief describes.

### PR #98 (010 P2) — does it touch these DTOs? Measured: **no**

`git diff origin/master...feat/010-p2` changes `PunchService.cs` in three hunks only — a constructor
parameter (`IClockingDays`, `:32`), the owning-day call in `RecordAsync` (`:80-86`) and an
`EnsureAsync` call (`:107-110`) — and `TimeAttendanceModule.cs:47-54` (DI registrations). No record
at `:18-25` or `:203-206`, no `PunchRecorded`, no endpoint body. The collision is textual only —
both edit `PunchService.cs` — so **P1 builds after #98 merges** and rebases onto it.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Frozen `LocalDate` / `LocalZone` on the punch (008 P4) | **Keep** | It is the truth of which day and which clock a punch belongs to; displays must read it, not recompute it. |
| Portal formats instants in the browser zone | **Invert** | A manager in Tashkent reading a Ljubljana site sees the wrong clock and, near midnight, the wrong day. The viewer's zone is irrelevant to when someone worked. |
| Zone fallback computed by each client | **Improve** | Server resolves it once (punch's `LocalZone`, else the employee's home-site zone via `ISiteTimeZones`) and always emits a non-null zone, so Flutter does not re-implement the fallback. |
| Day derived from the instant in any client | **Drop** | Every response that shows a day carries `localDate`. |

## Edge cases

- **The observed case (lift verbatim into tests):** punch `2026-09-17T19:44:00Z`, employee on a
  `Europe/Ljubljana` site, frozen `LocalDate = 2026-09-17`. Must render **21:44**, day **17 Sep**, in
  any browser zone — including `Asia/Tashkent`, where the browser alone would say 00:44 on the 18th.
- **Punch without a frozen zone** (`LocalZone` null — pre-008 rows the backfill could not reach):
  server falls back to the employee's home-site zone. The portal's own fallback (site zone, then UTC,
  labelled) exists only as a defence and must never be reached on a current API.
- **A site's zone changed after the punch:** the punch keeps its frozen zone. Old punches render in
  the old zone; that is the point of freezing, and a test pins it.
- **Mixed zones in one list** (live feed, "currently clocked in" across sites): every row whose zone
  differs from the viewer's shows a city label (e.g. `21:44 Ljubljana`); when all rows share the viewer's
  zone, no label.
- **Interval crossing DST or spanning a zone change:** `In` and `Out` each carry their own zone.
- **Night shift across midnight** (010 P2 can file a 02:00 punch under yesterday): the time shown is
  the local clock, the day is the API's `localDate`, and they may legitimately read "02:00" under
  the previous day's heading. Never "correct" that in the client.

## Target design in WM

All within **TimeAttendance** (§ ARCHITECTURE.md module boundaries: zone lookup through the existing
`ISiteTimeZones` contract from People — no new cross-module reach) and the portal.

**API — additive fields, camelCase in JSON:**

- `RecentPunchEntry` (+ `/api/punches/recent`, `/api/me/punches`): `localDate`, `localZone`.
- `LivePresenceEntry` (`/api/attendance/live`): `sinceLocalZone`, `sinceLocalDate`.
- `TimesheetInterval` (both timesheet endpoints): `inZone`, `outZone` (null when `out` is null).
- `PunchRecorded` (Kafka `wm.punches` + SignalR `punchRecorded`): `localDate`, `localZone`. Additive,
  so existing consumers are unaffected.
- POST responses already carry both; unchanged.

**Portal:** one standalone pure pipe, `punchTime` — `value | punchTime:zone:format` — built on
`Intl.DateTimeFormat` with an explicit `timeZone`, appending a zone label when `zone` differs
from the viewer's resolved zone or when the caller passes `label: 'always'` (mixed lists).

**Zone label (decision 2):** `zoneLabel(id)` is a pure function exported beside the pipe.
- **Rule:** take the **last** `/`-separated segment of the IANA id and replace every `_` with a
  space. `Europe/Ljubljana` → `Ljubljana`, `Asia/Tashkent` → `Tashkent`, `America/Los_Angeles` →
  `Los Angeles`.
- **Nested ids use the same rule:** `America/Argentina/Buenos_Aires` → `Buenos Aires`. The last
  segment is the city, and the middle segments are dropped.
- **Ids with no city:** an id whose first segment is `Etc`, or which has no `/` (`UTC`, `GMT`),
  renders as `UTC` when it is a UTC alias (`Etc/UTC`, `Etc/GMT`, `UTC`, `GMT`, `Etc/Universal`,
  `Etc/Zulu`). Otherwise it renders as the raw id (`Etc/GMT-5`). No offset is invented, because
  `Etc/GMT-5` means UTC+5 and a derived "GMT-5" would be wrong.
- **Tooltip and fallback:** the tooltip is always the full id. A null zone is shown as `UTC`
  (the portal's defensive fallback), with tooltip `UTC (zone missing)`. Day text
comes from `localDate`/`day.date` via plain string handling, never through a `Date` in the browser's
zone.

## Out of scope for this plan

- An admin timesheet screen (none exists; not created here).
- A user preference for "show in my zone" — a product call, open question 1.
- Changing *what day* a punch belongs to (008 P4 / 010 P2 own that).
- Any change to `employees.component.ts` employment-date display.

## Portions

**Why two:** the API half is a TimeAttendance change with xUnit tests; the portal half is five
frontend files plus two new specs and a browser check. Together they exceed ~8 files and cross the
backend/frontend line; each half is independently green and releasable (P1 is additive JSON).

### [ ] P1 — Every punch payload says which clock and which day

**Touches:** `Services/PunchService.cs` (the four records at `:18-25`, `:203-206` and their
builders at `:197`, `:235`, `:265`, `:299-316`; zone fallback through the injected `ISiteTimeZones`),
`Contracts/PunchRecorded.cs`, the `new PunchRecorded(` call in `RecordAsync`,
`WM.Modules.TimeAttendance.Tests/Endpoints/PunchZoneEndpointTests.cs` (new), and
`WM.Api.Tests` only if the hub payload test lives there.
**Depends on:** PR #98 merged (textual overlap in `PunchService.cs`).
**Done when:** `/api/punches/recent`, `/api/me/punches`, `/api/attendance/live`,
`/api/attendance/timesheet/{id}`, `/api/me/timesheet` and the `PunchRecorded` event all carry the
fields listed in *Target design*, zone always non-null for a recorded punch; the OpenAPI document
shows them.
**Tests:** the observed case over the endpoint (Ljubljana, `19:44Z` → `localZone =
"Europe/Ljubljana"`, `localDate = 2026-09-17`); `LocalZone` null → falls back to the site zone; site
zone changed after the punch → the frozen zone is returned, not the new one; `PunchRecorded`
published by `RecordAsync` carries both fields (assert on the fake `IEventStreamProducer`); interval
`inZone`/`outZone`, with `outZone` null for an open interval. Mutation: return the site zone instead
of the frozen one — the zone-changed test must fail by name.
**Risk:** low

### [ ] P2 — The portal reads the clock it is given

**Touches:** `frontend/portal/src/app/shared/punch-time.pipe.ts` + `.spec.ts` (new),
`core/api/workforce.api.ts` (`PunchRow`, `LivePresenceEntry`), `core/api/self-service.api.ts`
(`TimesheetInterval`), `core/realtime/realtime.service.ts` (`PunchEvent`),
`pages/dashboard/dashboard.component.ts` (`:101`, `:158`, and the feed entry at `:14` gains a zone),
`pages/self-service/self-service.component.ts` (`:36`, `:85`, `:108`) +
`self-service.component.spec.ts` (new).
**Depends on:** P1.
**Done when:** no `| date` in the portal is applied to a punch instant (grep); every punch time
renders in its payload zone; the /me list's day comes from `localDate`; zone labels appear exactly
under the rules in *Edge cases*.
**Tests (browser-zone-independent — every assertion passes an explicit zone and would fail if the
pipe ignored it):** pipe — `2026-09-17T19:44:00Z` in `Europe/Ljubljana` → `21:44`, in `Asia/Tashkent`
→ `00:44`, the two in one spec so the result cannot come from the runner's own zone; label shown when
zone ≠ viewer zone (viewer zone injected, not read from the runner), hidden when equal; null zone →
UTC with label. `zoneLabel`: `Europe/Ljubljana` → `Ljubljana`; `America/Los_Angeles` → `Los Angeles`;
`America/Argentina/Buenos_Aires` → `Buenos Aires`; `Etc/UTC` → `UTC`; `UTC` → `UTC`; `Etc/GMT-5` →
`Etc/GMT-5`; the rendered element's `title` equals the full IANA id; no output ever contains an
abbreviation or offset (assert `21:44 Ljubljana`, not `CEST`/`+02`). Component — the /me timesheet renders the observed punch as `21:44` under the `17 Sep`
heading; the recent list shows `17 Sep` from `localDate` for a punch whose UTC date is the 18th.
**Browser verification:** start the API (Development) and `portal` preview; `POST /api/dev/sign-in`
`{ "userName": "admin" }`, put the `refreshToken` in `localStorage['wm.refresh']`, reload. Set a
site's zone to `Europe/Ljubljana`, punch an employee there, override the tab's zone to
`Asia/Tashkent` (DevTools *Sensors* → location / CDP `Emulation.setTimezoneOverride`), and confirm
dashboard feed, "Currently clocked in" and /me show the Ljubljana clock with a zone label.
**Risk:** low

## Decisions (relayed 2026-09-25)

Relayed by the coordinator as the user's decisions on 2026-09-25. **Status stays `draft`.** The
surveyor never marks a plan approved, and an agent's message is not the user's approval. The user
flips `Status:` to `approved` directly. The relayed approval covers both portions, and P1 still
builds only after #98 merges.

1. **Default clock:** a punch shows on its own site clock (its frozen `LocalZone`), labelled when
   that differs from the viewer's zone. **No "show in my zone" toggle.**
2. **Label style:** a city name derived from the IANA id, with the full IANA id as the `title`
   tooltip. No abbreviations and no offsets. The derivation is in *Target design → Zone label*.
