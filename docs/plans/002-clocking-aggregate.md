# 002 — The Clocking daily aggregate

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 2 (Rules engine) — this is its missing prerequisite
Reference: [`TLW-CLOCKING-MODEL.md`](../TLW-CLOCKING-MODEL.md) — full measured anatomy
Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (579 tables, 8,173 columns)
- `E:\Tlw\Source\Logic\HoursCalculation\ProxyClocking.cs` (224 properties)
- `E:\Tlw\Source\Logic\CountersAllocation\BaseCountersHolder.cs`
- `E:\Tlw\Documentation\` — Daily Templates, Custom SQL, Swipe to clocking allocation, Glossary

## Why this plan exists

`dbo.Clockings` is **249 columns**, one row per employee per day, and it is what the Daily
Browser, the reports, the rules engine and every payroll export actually read. WM has punch
capture — the *input* to a Clocking — and nothing else. §13 recorded this as `✅ built`, which
is now corrected.

Phase 2 ("daily templates, weekly models, counters, flexi balances, pay categories, cost-centre
allocation", 10–16 wks) cannot start without the aggregate all of it reads and writes.

## The decision this plan is blocked on

Legacy denormalises everything into fixed slots: **12 badge times, 20 pay categories, 6 shifts**.
Each is a hard ceiling — a 21st pay category needs a schema migration *and* a code change
(`BaseCountersHolder.AddToCounter` is a hard-coded `switch` on counter id → column).

WM can either mirror that shape or normalise it. My recommendation is **normalise the store,
project the flat shape**:

- punches as rows (already true), counter values as rows, shifts as rows;
- no 12/20/6 ceilings;
- a `ClockingFlatView` projection reproduces the legacy column layout for payroll exports,
  reports and the legacy bridge, which are all shaped by it.

The cost is that the flat projection must be maintained and provably faithful. The benefit is
that WM stops inheriting three arbitrary limits its customers currently work around.

**This is a §8/§13A-level design decision, so it needs your sign-off before any portion runs.**

## Out of scope for this plan

- The rules engine itself (hours classification, rounding, OT) — that is Phase 2 proper.
  This plan builds the aggregate it writes into, and calculates nothing beyond what is needed
  to prove the shape.
- The safe rules expression language (see §4 of the reference doc for the power it needs —
  windowed aggregation across days, not just per-row expressions).
- Daily template editor UI (124 columns' worth).
- `SoftwareMainOptions` (227 cols of global settings) — needs its own survey.
- Employee contracts and thresholds — needed by Phase 2, flagged in plan 003.

## Portions

### [ ] P1 — Clocking aggregate + normalised counters
**Touches:** new `src/Modules/TimeAttendance/…/Domain/Clocking.cs`, counter value rows, migration
**Done when:** a Clocking exists per employee per day; punches associate to it; counter values are
rows keyed by counter definition, with no fixed ceiling; the 20-slot legacy layout is expressible.
**Tests:** counter allocation beyond 20 works; a clocking with no punches is valid.
**Risk:** medium — new schema, but additive.

### [ ] P2 — Clocking generation (the "calendar" job)
**Touches:** `src/Worker`, scheduled job
**Done when:** clockings are pre-generated for every active employee for a date range, idempotently;
re-running never duplicates; a day with no attendance still has a row.
**Tests:** idempotency; employee start/leave dates bound the range.
**Risk:** medium — this job runs unattended on customer servers.

### [ ] P3 — Swipe→day allocation
**Touches:** punch ingestion, allocation service
**Done when:** the three legacy rules are reproduced in order — night-shift end time,
offset-to-next-day, allocate-to-previous/next-day — with the documented fallback to the swipe's
own date. Reassignment works when an adjacent day's template changes.
**Tests:** every worked example in `Swipe to clocking allocation.md` becomes a test case. There
are 11 of them with explicit Given/When/Then — use them verbatim.
**Risk:** **high** — a swipe landing on the wrong day is a payroll error.

### [ ] P4 — Badge-slot provenance
**Touches:** punch model
**Done when:** each punch retains raw device time, adjusted time, provenance (swiped vs generated),
source, and location — the information legacy keeps in its seven-column-per-slot family. Rounding
stays auditable and reversible.
**Tests:** an adjusted punch still exposes its original device time.
**Risk:** low.

### [ ] P5 — Flat projection
**Touches:** projection/read model
**Done when:** a legacy-shaped flat view (`BadgeTime1..12`, `CPTN01..20`, per-shift measures) is
derivable from the normalised store, so payroll exports and the legacy bridge can consume it.
**Tests:** round-trip — normalised → flat → same values; overflow past 12 punches or 20 counters
is handled explicitly, not silently truncated.
**Risk:** medium.

## Open questions for the user

1. **Normalise or mirror?** The recommendation above. Mirroring is faster to build and trivially
   compatible; normalising removes three ceilings customers currently hit. I lean strongly to
   normalising, but this is your product decision.
2. **Does any customer actually hit the 20-pay-category ceiling today?** If yes, that settles
   question 1 immediately and raises this plan's priority above the rest of Phase 2.
3. **Sequencing.** Plan 001 (data scope) is mid-flight with P2–P5 outstanding. This plan is
   larger and more foundational. Finish 001 first (it is short and every query depends on it),
   or pause it here and start 002? I recommend finishing 001 — it is nearly done, and scope
   correctness is a prerequisite for reading clockings safely.
4. **`SoftwareMainOptions` (227 columns)** is a whole configuration surface nobody has surveyed.
   Want that as plan 003?
