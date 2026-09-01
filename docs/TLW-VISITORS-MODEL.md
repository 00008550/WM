# TLW Visitors — legacy model, measured

Survey date: 2026-09-01. Surveyor pass over the **last unsurveyed bucket** in the estate
(`COVERAGE-AUDIT.md:66` — "Visitors, 9 tables / 127 columns, named, not surveyed, phase 8").

**Everything below is measured against `E:\Tlw` with a `file:line`. WM's own documents are not
cited as evidence.** Corrections WM's records need are collected in the last section for a
consolidated pass — this survey does not edit `COVERAGE-AUDIT.md`, `ARCHITECTURE.md`, etc.

---

## The one-line answer

**The Visitors module is a genuine domain, not a device wrapper.** The hardware it was welded to
(the "Synergy Touch" reception kiosk — fingerprint enrolment, badge/card printing, thermal camera)
is a *delivery channel* that invariant 3 drops. What remains after the hardware is removed is a
real, safety-relevant subsystem: pre-registered and walk-in visitors, a host employee, sign-in/out,
a dynamic check-in questionnaire with sign-in-denial logic, host email + calendar invites, and —
critically — **a live on-site roll that the emergency Fire Report merges into mustering**
(`WebSite\Helpers\FireReportControllerHelper.cs:55`). It is **not** "a thin guest-book on top of
device management." A plan is warranted; it is a modest new build, not a port.

---

## Ground truth — the real footprint

### The count was wrong both ways, and the entity is hidden

`COVERAGE-AUDIT.md` records "9 tables / 127 columns". Measured from the authoritative model
`E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs`, there are **10** `Visitor*` mapped objects —
**8 base tables (102 cols)** and **2 views (57 cols)** — a base/view split the audit did not make,
the same defect found in every re-measured bucket.

**Base tables (8 / 102 columns):**

| Table | Cols | Line | What it is |
|---|---:|---|---|
| `dbo.VisitorSettings` | 51 | 131910 | Reception-kiosk + notification configuration (see below) |
| `dbo.VisitorFormQuestions` | 12 | 133330 | Dynamic check-in/out questionnaire, conditional logic |
| `dbo.VisitorTypes` | 9 | 114216 | Visitor category → badge template, host-required flag |
| `dbo.VisitorFormAnswers` | 9 | 133887 | Per-visitor answers + NDA signature capture |
| `dbo.VisitorSessions` | 7 | 205865 | One visit: visitor, host, sign-in/out log links |
| `dbo.VisitorCards` | 5 | 114580 | Time-boxed access-card assignment (device-coupled) |
| `dbo.VisitorNotes` | 5 | 134264 | Free-text notes against a visitor |
| `dbo.VisitorFormQuestionOptions` | 4 | 133712 | Multiple-choice options for a question |

**Views (2 / 57 columns), not base tables — must be excluded from any table total:**

| View | Cols | Line |
|---|---:|---|
| `dbo.VisitorActivityGridView` | 32 | 134710 |
| `dbo.UnifiedVisitorTransactionsReportView` | 25 | 230362 |

### The entity is not in these tables — it is smuggled into `dbo.Employees`

**A visitor is an `Employee` row** with `EmployeeType == AccessControlEmployeeType.Visitor`
(`Visitors\VisitorsService.cs:255,261`). The visit-specific attributes live as columns *on
`Employees`*, not in the `Visitor*` tables. From `VisitorActivityGridView` (line 134710 onward) and
the load options in `VisitorsService.cs:249-270`, these include: `IsPreregistered`, `CompanyName`,
`CarPlateNumber`, `VisitAddressLine1/2`, `VisitCityOrTown`, `VisitCounty`, `VisitPostCode`,
`VisitCountry`, `VisitorTypeId`, `HostEmployeeId`, `VisitTimeFrom`, `VisitTimeTo`, `VisitSiteName`,
`ContactEmail`, `ImageId`. So the "9 tables / 127 columns" figure **understates the real footprint
by hiding the visitor entity inside `Employees`** and by omitting the transaction store
(`dbo.sy_ac_data` / `AccessControlLog`) where every sign-in and sign-out actually lands.

**Real footprint = 8 dedicated tables + a visitor-shaped slice of `Employees` + `sy_ac_data`
rows + 2 report views.** That is materially larger than the bucket line implies, and consistent
with the programme-wide finding that the audit under-counts.

---

## Legacy behaviour (what we are replacing)

### What a visit is

- **Visitor** — an `Employee(EmployeeType=Visitor)`. Created either by pre-registration (admin/host
  invites) or as a walk-in at the kiosk (`KioskExternalAccessService.SaveVisitor`, line ~495, calls
  `_proximityCardsService.CreateVisitorOrGuard(..., AccessControlEmployeeType.Visitor)`).
- **Host** — `Employee.HostEmployeeId`, nullable. `VisitorSettings.EnableVisitorHosts` and
  `HideHostDirectory` gate whether the visitor picks a host from a directory. `VisitorType`
  can force host selection (`VisitorTypes.HostSelectionIsRequired`, line 114216 block).
- **Pre-registration** — `Employee.IsPreregistered`. A pre-registered visit exists before arrival
  with an expected `VisitTimeFrom/To` and a `SessionGuid` already minted; sign-in later attaches
  the access-log record to the waiting session (`VisitorsService.CreateOrUpdateSession:459-478`,
  "if (session != null) // preregistered").
- **Sign-in / sign-out** — recorded as `dbo.AccessControlLog` (`sy_ac_data`) rows with
  `TransactionType` In/Out, produced through `IVisitorCheckInOutProcessor.ProcessVisitorCheckInOut`
  which lives in the shared SwipeProcessor (`VisitorsService.cs:19-26,415-457`). `VisitorSessions`
  then stores `SignInRecId` / `SignOutRecId` pointing at those log rows.
- **Vehicle** — `Employee.CarPlateNumber`; there is a `CarPlatesImportService` and ANPR path
  (`ImportService\CarPlatesImportService.cs`), but ANPR itself is dropped hardware.

### Host and visitor notification — present and genuine

`Visitors\VisitorEmailSender.cs` sends, on sign-in and sign-out:
- **to the host** — `SendEmailToHost` with subject resource `VisitorArrived_Subject` /
  `VisitorLeft_Subject` (line 88). This *is* the "your visitor has arrived" flow.
- **to the visitor** — `VisitorCheckedIn_Subject` / `VisitorCheckedOut_Subject` (line 125).
- **calendar invites** — `Visitors\IcsGenerator.cs` + `Visitors\CalendarInvite.cs`;
  `VisitorSettings.EmailCalendarInvitee`, `AttachICS`, `SendInviteeReminder` gate an ICS invite for
  pre-registered visits.

### The dynamic check-in form

`VisitorFormQuestions` (line 133330) is a proper questionnaire builder: `QuestionType`, `IsRequired`,
`Position`, `VisibleVisitorTypeIds` (a **CSV of type ids** — denormalised), conditional display via
`ParentQuestionId` / `ParentQuestionOption` / `ParentQuestionYesNo`, and two safety-relevant fields:
`AnswerWhenSignInDenied` and (on the answer) `AnswerBlocksSignIn`. `VisitorFormAnswers` stores the
answer plus a **signature** (`SignatureFile`/Name/ContentType) — i.e. NDA / health-declaration
capture — and `AcRecordId` linking the answer to the access-control transaction. So **a form answer
can refuse entry**: this is real access logic, not cosmetics.

### VisitorSettings — reception experience + notifications (51 cols)

The 51 columns split into four groups (`VisitorSettings.cs` block from line 131910):
- **Kiosk/reception UX & branding** (dropped-channel-adjacent but reusable): `ClientLogoFile`,
  `BackgroundImage`, `SynergyAppScreenTimeoutSeconds`, `SynergyAppMainScreenWelcomeText`,
  `FirstTimeVisitorOptionEnabled`, `ReturningVisitorOptionEnabled`, `FastTrackIviteOptionEnabled`.
- **Data-capture policy** (keep): `IsEmailRequired`, `IsMobileNumberRequired`, `IsCompanyRequired`,
  `IsPhotoIdRequired`, `CaptureNewPhotoOnCheckIn`, `IsCarPlateNumberRequired`, form enable/pre-fill
  and pre-fill-expiry settings for check-in and check-out.
- **Notification policy** (keep): `EmailOnCheckIn`, `EmailOnCheckOut`, `EmailCalendarInvitee`,
  `SendInviteeReminder`, `AttachICS`, `EmailNotificationHosts`.
- **Device-only** (drop with hardware): `AutoPrintingEnabled`, `AutomaticMobileWebPrintingEnabled`,
  `ShowProfilePicOnBadge`, `EnablePictureSelectionFromThermalScanner`,
  `ThermalScannerPictureSelectionMinutes`.

Note also `NumberOfDaysInThePastToStoreData` — a built-in data-retention window (GDPR-relevant, a
Keep).

### The device coupling, measured

- **The check-in transaction writes a device-attributed `AccessControlLog`** — `AutoSignOutVisitor`
  (line 362-413) builds a log with `TerminalIP`, `TerminalId`, `TerminalNum` from a `Device`. The
  `LogVisitor` path routes through the SwipeProcessor with a `deviceId`.
- **`KioskExternalAccessService`** is the legacy visitor front end and it is thoroughly
  hardware-bound: fingerprint template sync (`GetNewFingerTemplatesForDevice`),
  `IsCardPrintingEnabled`, `EnrollmentCardTemplateWidth/Height`, `FingerPrintAmount`, proximity-card
  enrolment (`_proximityCardsService`), and `VisitorCustomField1Enabled..VisitorCustomField10Enabled`
  (fixed numbered slots — a ceiling smell) all read off `DeviceSettings`/`SoftwareMainOptions`
  (lines 591-723).
- **But the coupling is not intrinsic to the domain.** `VisitorSessions.SignInRecId`/`SignOutRecId`
  are **nullable**; the session, host, form answers, notes and notifications do not require a
  terminal. Strip the kiosk and you still have a coherent visit record — the log row just needs a
  non-hardware source (a web/phone check-in), exactly what invariant 3 anticipates.

### The safety seam — Visitors feeds mustering (the reason it is not droppable)

`WebSite\Helpers\FireReportControllerHelper.cs:55` calls
`_visitorsService.GetAllVisitorsForAttendanceFireReport()` and passes the result into
`PrepareFireReport(...)` **alongside** all employees (line 57). `GetAllVisitorsForAttendanceFireReport`
(`VisitorsService.cs:319-324`) returns `Employee.ActiveNotFired()` filtered to
`EmployeeType==Visitor`. The two customer fire-report plugins
(`TMDFireReportPlugin\Plugin.cs`, `SuttonFireReportPlugin\Plugin.cs`) also reference visitors. The
online fire report proc `GetOnlineFireReportData` (`DAL\DbFunctions\OnlineFireReportExecutor.cs:67`)
runs off `sy_ac_data`, into which visitor sign-ins land — so visitors appear on the muster roll
through *both* paths. **A live roll that omits on-site visitors is a life-safety defect.** This
makes Visitors a hard upstream dependency of the Safety/mustering module (`FireMarshalMusterPoints`,
177 cols, phase 6b), not an optional phase-8 nicety.

---

## Keep / Improve / Invert / Drop

Every dedicated object classified; 0 unclassified.

| Structure | Class | Reason |
|---|---|---|
| Visit record (visitor + host + expected/actual times + site) | **Keep** | genuine domain; the muster roll depends on it |
| `VisitorSessions` (sign-in/out log links, `SessionGuid`) | **Keep** | correct model of one visit; guid enables pre-reg + phone check-in |
| `VisitorFormQuestions/Options/Answers` incl. `AnswerBlocksSignIn`, signature | **Keep** | real access logic (NDA/health-declaration gating entry) |
| `VisitorNotes` | **Keep** | trivial, useful |
| `VisitorTypes` | **Keep** (drop its `CardTemplateId`/`HasAutoPrinting`) | category + host-required is domain; badge-print fields are hardware |
| Host + visitor email / ICS invites | **Keep** | the "visitor arrived" flow; phone/web native |
| Data-capture & notification policy in `VisitorSettings` | **Keep** | consolidate into settings |
| Visitor entity living on `dbo.Employees` | **Invert** | model a first-class `Visitor` in the Visitors module; do not overload the Person/Employee record. WM invariant 1 forbids the cross-module overload anyway |
| `VisitorFormQuestions.VisibleVisitorTypeIds` = CSV of ids | **Improve** | replace with a join table; CSV is unqueryable and unbounded-in-a-cell |
| `VisitorCustomField1..10Enabled` (kiosk) | **Improve** | fixed numbered slots → the dynamic form already exists; fold custom fields into it, no ceiling |
| `?? 0` host/type filters & fail-open `NOT EXISTS(...)` scope in report SQL (`VisitorsService.cs:157,167`, `593-610`) | **Invert** | fail-closed scope, consistent with plan 001 |
| Sign-in/out as `sy_ac_data` (`AccessControlLog`) rows | **Improve** | in WM these become punch/attendance events on the event stream (§4 Kafka), not device-log rows; but the *feed-into-muster* semantics are Kept |
| Synergy Touch kiosk app, fingerprint sync, card printing, thermal picture, proximity-card enrolment (`KioskExternalAccessService`, `VisitorCards`, badge-print columns) | **Drop** | hardware — invariant 3. Replaced by web reception + phone self-check-in |
| ANPR car-plate capture (the device path) | **Drop** | hardware; `CarPlateNumber` as a typed field is Kept |

---

## Edge cases (from legacy code — lift into tests)

1. **Double sign-in** — `CheckIfCanSignInOut` (`VisitorsService.cs:515-527`) refuses a second
   sign-in on the same `SessionGuid`, *and* refuses if any other session is open for that visitor
   the same calendar day. Test both.
2. **Sign-out with no session (legacy apps, no guid)** — falls back to "the first open session
   today" (`:534-545`, `:491-499`). Decide WM's rule explicitly; do not inherit the implicit
   "first open" heuristic silently.
3. **Session-guid collision across visitors** — legacy logs an error and denies
   (`:505-511`). Keep as fail-closed.
4. **Auto sign-out** — `AutoSignOutVisitor` (`:362`) closes a still-open visit at a chosen time and
   writes an `EventCode 931` "Auto" log. WM needs an equivalent end-of-day / timeout auto-close so
   the muster roll does not accumulate phantom on-site visitors overnight.
5. **Pre-registered but never arrived** — `VisitorActivitiesByFiltersAndDateRange` includes
   pre-registered/blocked visits by `VisitTimeFrom` even with no sign-in (`:143-148`). A no-show
   must not appear as "on site" on the fire roll.
6. **Form answer denies entry** — `AnswerBlocksSignIn` true ⇒ visitor is recorded but not admitted;
   what appears on the muster roll then? (Legacy still lists them via the activity view.) Product
   call needed.
7. **Visitor with no host** (`HostEmployeeId` null) — allowed unless `HostSelectionIsRequired`;
   report filters use `includeVisitsWithoutHost` (`:600-610`). Keep the null-host path.
8. **Retention window** — `NumberOfDaysInThePastToStoreData` / `DeleteOldVisitorsFromDeviceDays`
   purge old visitor data. WM must implement retention deliberately (GDPR), not as a device setting.

---

## Should WM rebuild this? Yes — small new build, phase 8

A port is the wrong shape (the entire front end is a dropped kiosk). But the *domain* survives the
no-devices decision and carries a life-safety dependency (mustering). The right move is a **modest
green-field Visitors module** with a web reception surface and phone self-check-in, reusing WM's
existing punch/event and notification infrastructure. Estimated 6 portions (see plan 021).

---

## What I did NOT measure (numbered, for honesty)

1. The exact column list of `dbo.Employees` visitor-only fields — I confirmed them via the
   `VisitorActivityGridView` projection and `VisitorsService` load options, not by reading each
   `Employee` column definition. The Person survey (`TLW-PEOPLE-MODEL.md`) owns that table.
2. `GetVisitorStatistics` and the two report views' full semantics — I read their signatures and the
   filter SQL, not the underlying stored procs (`GetVisitorStatistics`, `GetOnlineFireReportData`)
   line by line.
3. The `WebSite` visitor controllers/screens beyond `FireReportControllerHelper` and
   `VisitorActivityScreenControllerHelper` (confirmed to exist) — screen-level UX not surveyed.
4. The two fire-report plugins' internal use of visitor data (`TMDFireReportPlugin`,
   `SuttonFireReportPlugin`) — noted as callers, not read.
5. Historical schema drift: 17 versioning files (`Database\Versioning\*.sql`, V3.6.3→V6.0) redefine
   visitor tables. I trusted `HorioDB.designer.cs` as current state rather than replaying the log.
6. Licensing gating of the Visitors feature (`License\LicenseItems.cs` references visitors) — not
   traced.

---

## Corrections WM's records need (proposed — not applied here)

Per survey boundaries these are *proposed* for a consolidated pass; this document does not edit the
protected files.

- **`COVERAGE-AUDIT.md:66`** — "Visitors, 9 tables / 127 columns" is wrong. Measured: **10 mapped
  objects = 8 base tables (102 cols) + 2 views (57 cols)**, and the visitor entity additionally
  lives on `dbo.Employees` + `dbo.sy_ac_data`. Mark surveyed 2026-09-01 → `TLW-VISITORS-MODEL.md`.
  This closes the last "not surveyed" bucket in the estate.
- **`ARCHITECTURE.md` §13 / roadmap §14** — record that Visitors is a **safety dependency of
  mustering (phase 6b)**, not an isolated phase-8 module: the Fire Report merges the visitor roll
  (`FireReportControllerHelper.cs:55`). Sequencing should reflect that.
- **`SCREEN-TREE.md:145-147`** — the "Deliveries" sub-item under Visitors has **no backing table**
  in the schema; flag as aspirational or out of scope for the initial build.
</content>
