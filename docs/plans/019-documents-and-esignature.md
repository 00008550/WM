# 019 — Documents & e-signature module

Status: draft
Roadmap: ARCHITECTURE.md §14 item 9 (Documents), phase 4 (Notifications & Documents); §10 (Documents module, NEW)
Legacy sources surveyed: see [`../TLW-DOCUMENTS-MODEL.md`](../TLW-DOCUMENTS-MODEL.md) — schema measured
mechanically over `HorioDB.designer.cs`; `ESignatureDocumentsService.cs`, `ESignatureAccessService.cs`,
`ESignatureNotifierService.cs`, `CompanyDocumentsService.cs`, `RoleHrDocumentSecurityService.cs`, and the
`EmployeeDocumentCategory` / `AccessPermission` / `SignDocumentRequestStatus` / `DocumentAttachmentType` /
`FileVirusScanStatus` / `FileVirusScanTable` enums opened.

## Ground truth

- Document-domain **base tables: 26 / 125 cols**; the audit's "14 tables / 114 cols" is **report-view
  inflation** (two 55-col `Unified*ReportView` views = 110 cols). Base module proper ≈ 20 tables / 97 cols.
  Correction proposed in `TLW-DOCUMENTS-MODEL.md` §1 (COVERAGE-AUDIT off-limits this wave).
- **All document bytes live in SQL `VarBinary(MAX)` columns.** No filesystem, no blob store, no
  FILESTREAM. `FileVirusScanTable` names the 10 blob-bearing tables.
- **"HR is largely a document system" is TRUE**: 7 of 8 `EmployeeDocumentCategory` values are
  file-bearing HR tables (17–19 cols each) bucketed under People/HR.
- **Onboarding has no checklist/task model in legacy** — it is just a document category. ARCHITECTURE
  §10's "checklist of documents/tasks" is WM-new (correction in model doc §6).
- WM has **no Documents module** (`WM.Modules.Documents` planned only; `src/` has just an unrelated
  `Licensing/LicenseDocument.cs`).

## Legacy behaviour (what we are replacing)

Full detail in the model doc §3. Summary: upload → one `UnsignedDocument` blob → one
`SignDocumentRequest` per signer (employee or user) → token-emailed link (only if virus scan `Safe`) →
signer opens via token (fail-closed validation) → signed bytes routed by an 8-way category `switch` into
the matching HR table. Company documents are category + department/location-targeted, file or external
URL, virus-scanned. HR-document access is a per-`(role, category)` `Deny`/`ReadOnly`/`ReadWrite` grid,
fail-closed.

## Keep / Improve / Invert / Drop

Full table in model doc §4. Load-bearing decisions:
- **Improve:** blobs-in-DB → object storage; 8 near-identical HR tables → one `documents` table with a
  `category` discriminator; per-signer request rows → one request + signer collection; hardcoded 7-day
  expiry → configurable; `SignatureDate` date-only → full UTC timestamp.
- **Invert:** company-doc empty-target = "everyone" (fail-open) → empty = nobody, "all" explicit.
- **Keep:** fail-closed access + token validation; virus-scan-gated distribution; account-less signer
  via token + `ContactEmail`; external e-sign hook (but through `PluginSdk`).

## Edge cases

Lift into tests (model doc §5): virus-not-safe silently unsent; expired/tampered/already-signed token
rejected; shared `UnsignedDocument` kept until the last pending signer signs; empty-target visibility
(inverted); account-less signer email fallback; Qualifications category requires a qualification payload;
cancel/resend token lifecycle; file-vs-URL edit type-mismatch throws.

## Target design in WM

`WM.Modules.Documents` (schema-isolated), object-storage-backed (MinIO dev / S3 prod, ARCHITECTURE §2),
metadata + storage key in Postgres. REST-first (`/api/documents`, `/api/me/documents`). E-sign requests
emit events to Notifications (§9 matrix). Access via unified RBAC (retire `RoleHrDocumentSecurity`).
Company-doc targeting via population groups (dependency: groups primitive, unbuilt). External e-sign
provider via `PluginSdk`.

## Out of scope for this plan

- The 7 **HR category** tables' full migration (Appraisals/Certificates/Disciplinary/Objectives/
  Qualifications/Remunerations) — they belong to the **HR module** split (§14 item 4). This plan builds
  the document *substrate* (store + e-sign + company docs) that HR will file into; it does not port HR
  business fields.
- **Population-group targeting** implementation — depends on the unbuilt groups primitive; this plan
  ships department/location targeting with **fail-closed** semantics and leaves a group hook.
- Cross-module attachment links (absence / expense / clocking documents) — owned by those plans.
- Onboarding checklist/task model (WM-new; separate decision).
- Report views (`Unified*ReportView`) — Reporting bucket.

## Portions

### [ ] P1 — Object-storage document substrate
**Touches:** `src/Modules/Documents/**` (new), `src/SharedKernel` (storage abstraction if absent),
`WM.sln`, migration, `src/Modules/Documents/WM.Modules.Documents.Tests/` (new).
**Done when:** a `documents` table (owner, category, filename, content-type, storage key, virus-scan
status, tags, created-by/at) exists; upload writes bytes to object storage and metadata to Postgres;
download streams from storage; endpoints scope-checked.
**Tests:** upload/download round-trip; storage key never exposes raw bytes in DB; unauthorized scope
denied.
**Risk:** medium

### [ ] P2 — Virus-scan gate
**Touches:** `src/Modules/Documents/**`, `src/Worker` (scan consumer), migration (scan status/queue).
**Done when:** a newly uploaded document is `Pending` until scanned; distribution/e-sign send is blocked
unless `Safe`; `Infected` is surfaced, not swallowed.
**Tests:** pending blocks send; infected blocks + flags; safe allows.
**Risk:** medium

### [ ] P3 — E-signature requests & signing
**Touches:** `src/Modules/Documents/**`, migration (request + signers + tokens), `src/Api`.
**Done when:** create a signature request with a signer collection (employee/user); per-signer token
access with fail-closed validation and configurable expiry; signing produces immutable signed output and
per-signer status; cancel/resend lifecycle.
**Tests:** all §5 token edge cases; shared-source retention; account-less signer; full-timestamp
`SignatureDate`; configurable (not hardcoded 7-day) expiry.
**Risk:** high

### [ ] P4 — E-sign notifications
**Touches:** `src/Modules/Documents/**`, Notifications contracts/events, `src/Worker`.
**Done when:** "Document awaiting your signature" and signed-copy delivery fire through the Notifications
module as events (not inline SMTP); account-less signer emailed via `ContactEmail`.
**Tests:** event emitted on request/sign; no send when scan not `Safe`; email fallback path.
**Risk:** medium

### [ ] P5 — Company documents (categorised, targeted, fail-closed)
**Touches:** `src/Modules/Documents/**`, migration (company docs, categories, targets), `src/Api`,
`frontend/portal` (list + upload + "My documents").
**Done when:** company documents with category, file **or** external URL, department/location targeting
with **inverted** semantics (empty target = nobody; explicit "all" flag); employee self-service list at
`/api/me/documents`; virus-scan gated.
**Tests:** targeting incl. the inverted empty-target case; file-vs-URL edit mismatch rejected; category CRUD.
**Risk:** medium

### [ ] P6 — Unified HR-document access permission
**Touches:** `src/Modules/Documents/**`, Identity/RBAC integration.
**Done when:** per-category read/write access resolves through WM's single RBAC + permission model
(no separate `Deny/ReadOnly/ReadWrite` vocabulary); default is **deny**.
**Tests:** no rule → deny; read vs write enforced; exception → deny.
**Risk:** medium

## Open questions for the user

1. **Onboarding.** Legacy onboarding is *only* a document category — no checklist/task entity. Build the
   ARCHITECTURE §10 "checklist of documents/tasks" as WM-new now, or descope onboarding to
   document-category parity for this plan? (Product call; affects scope of a future portion.)
2. **Company-doc targeting inversion.** Confirm WM should treat an empty target set as **nobody** (with an
   explicit "all employees" option), inverting legacy's fail-open (`CompanyDocumentsService.cs:38`).
3. **Population groups vs department/location.** ARCHITECTURE §9 promises population-group targeting, but
   groups are unbuilt. Ship department/location targeting now and add groups when the primitive lands, or
   block P5 on the groups primitive?
4. **External e-sign provider.** The `ESignSystem*` columns prove a third-party signing integration
   existed; the vendor/adapter was not located. Is an external provider in scope for WM, or is WM's own
   token-based signing sufficient (external hook deferred to a plugin)?
5. **Proposed doc corrections** (consolidated pass, since those files are off-limits this wave):
   COVERAGE-AUDIT.md:68 Documents count (report-view inflation); ARCHITECTURE.md §10 onboarding-as-checklist
   claim. Apply now or hold for the batch?
