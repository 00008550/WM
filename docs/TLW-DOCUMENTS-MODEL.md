# TLW Documents — legacy model, measured

**Surveyed 2026-08-31** (wm-surveyor). Sources opened, not summaries:

- **Schema (authoritative):** `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — every table/column
  counted mechanically (script, not eye).
- **Source:** `Logic\ESignature\ESignatureDocumentsService.cs`, `ESignatureAccessService.cs`,
  `ESignatureNotifierService.cs`, `Logic\HR\CompanyDocumentsService.cs`,
  `Logic\Security\HrDocument\RoleHrDocumentSecurityService.cs`,
  `SharedLogic\Enums\EmployeeDocumentCategory.cs`, `SharedLogic\Enums\AccessPermission.cs`,
  `Core\Enumeration\Enums.cs` (SignDocumentRequestStatus, DocumentAttachmentType,
  FileVirusScanStatus, FileVirusScanTable).
- **WM:** `docs/ARCHITECTURE.md` §10, §14; `docs/COVERAGE-AUDIT.md:68`.

This document is the reference for plan **019**. Where it contradicts `COVERAGE-AUDIT.md` the
correction is **proposed here** (that file is off-limits this wave) for a consolidated pass.

---

## 1. The count was inflated by report views — corrected

`COVERAGE-AUDIT.md:68` records **Documents = 14 tables / 114 columns**. That number is dominated by
**report views**, exactly the over-count flagged this wave.

Measured (script over the designer, base vs view separated):

| | Tables | Columns |
|---|---:|---:|
| **Document-domain base tables** (proper) | **26** | **125** |
| Report **views** in the domain | 3 | **113** |
| — `UnifiedEsignatureDocumentsReportView` | | 55 (VIEW) |
| — `UnifiedEmployeeDocumentsReportView` | | 55 (VIEW) |
| — `SignedDocumentNamesView` | | 3 (VIEW) |

The audit's "114" is within rounding of the **two 55-column views (110) + a base handful** — i.e. it
counted the report projection, not the store. **Proposed correction to `COVERAGE-AUDIT.md:68`:**
Documents base footprint is **~20 tables / ~97 columns for the module proper** (e-sign + company +
tags + security + the two employee-document tables), with **4 cross-module link tables (17 cols)**
that belong to Expenses / TimeAttendance / Absence, and **3 report views (113 cols)** that belong to
Reporting. Report views must never be summed into a module's base total.

### The document domain is larger than any one bucket

The "Documents bucket" understates the product because the **eight HR document categories live in the
People/HR bucket**, yet each is structurally a stored file + metadata + optional signature. Measured
column counts of the document-**carrying** tables (VarBinary blob present):

| Table | Cols | Bucket today |
|---|---:|---|
| `dbo.EmployeeObjectives` | 19 | People/HR |
| `dbo.EmployeeRemuneration` | 18 | People/HR |
| `dbo.EmployeeAppraisal` | 17 | People/HR |
| `dbo.EmployeeCertificates` | 17 | People/HR |
| `dbo.EmployeeDisciplinary` | 17 | People/HR |
| `dbo.EmployeeQualifications` | 17 | People/HR |
| `dbo.EmployeeOnboardingDocuments` | 13 | Documents |
| `dbo.EmployeeDocuments` | 10 | Documents |
| `dbo.SignDocumentRequests` | 11 | Documents |
| `dbo.ClockingActivitySignatures` | 8 | TimeAttendance |
| `dbo.ClockingActivityDocuments` | 6 | TimeAttendance |
| `dbo.CompanyDocuments` | 6 | Documents |
| `dbo.QualificationSignDocumentRequests` | 6 | Documents |
| `dbo.UnsignedDocuments` / `SignedUserDocuments` / `CompanyDocumentAttachments` / `ExpenseDocuments` | 5 each | mixed |
| tag / link / token / category tables | 2–3 each | Documents |

**This validates the ARCHITECTURE claim: "TLW's HR is largely a document system."** Seven of the eight
HR categories (Appraisals, Certificates, Disciplinary, Objectives, Qualifications, Remunerations,
Documents; Onboarding is the eighth) are **file-bearing tables** — a title/description, a
`*File VarBinary(MAX)`, `DateCreate`, `SignDocumentRequestId`, tags, virus-scan status. The structured
HR fields are thin; the payload is the uploaded file.

---

## 2. What a "document" is, and where the bytes live

**A legacy document is an uploaded file stored as a DB blob**, plus a metadata row. Confirmed storage
columns (designer):

- `EmployeeDocuments._DocumentsFile` — `VarBinary(MAX)`
- `EmployeeOnboardingDocuments._DocumentFile` — `VarBinary(MAX)`
- `CompanyDocumentAttachments._FileContent` — `VarBinary(MAX) NULL`
- `UnsignedDocuments._File` / `SignedUserDocuments._File` — `varbinary(max)`
- `ClockingActivitySignatures._DocumentFile`, and each HR `*File` column — `VarBinary(MAX)`

**Every document byte lives inside the SQL database.** There is no filesystem path, no blob-store
reference column, no FILESTREAM. The `FileVirusScanTable` enum enumerates the ten blob-bearing tables
that hold uploaded content: the 8 HR categories, `EsignatureUnsignedDocuments`, `CompanyDocumentAttachments`.

**This is the single decision that shapes WM's approach.** ARCHITECTURE §10/§2 already resolves it:
move bytes to **object storage (MinIO/S3/Azure Blob)**, keep only metadata + a storage key in Postgres.
That is an **Improve** (blobs-in-DB → object store), not a mirror.

A company document is **not** always a file — `DocumentAttachmentType` is `{ AttachedFile=1, ExternalUrl }`.
A company document can instead be an **external URL** (`CompanyDocumentExternalUrls.ExternalUrl nvarchar(MAX)`).

---

## 3. Legacy behaviour (concrete)

### 3.1 E-signature workflow — real, token-based, with an external system hook

`ESignatureDocumentsService.SaveDocumentForSignature` (`ESignatureDocumentsService.cs:56`):

1. One uploaded `UnsignedDocument` is created (bytes + virus-scan status).
2. **One `SignDocumentRequest` per signer** is created (fan-out), each linked back to the shared
   `UnsignedDocumentId`. Signers are of two kinds: **employees** (`SignDocumentRequestEmployees`) and
   **users** (`SignDocumentRequestUsers`).
3. Request carries `DocumentCategory` (the `EmployeeDocumentCategory` enum), a description, tags, and
   for the `Qualifications` category a `QualificationSignDocumentRequest` (qualification id, start/end,
   reference number).
4. `CreationDate = today`; **`ExpirationDate = today + 7 days`** — a **hardcoded 7-day ceiling**
   (`:81`, and again in the notifier resend `:62`).
5. Notifier (`ESignatureNotifierService`) generates a signed **token** per signer
   (`SignDocumentTokens`), emails a link. **The link is only sent if `VirusScanStatus == Safe`**
   (`:72`, `:102`) — fail-closed, correct.
6. Signer opens link → `ESignatureAccessService.GetDocumentToSignByToken` validates token string
   matches DB, request `Status == Pending`, and `ExpirationDate > now`. **Any failure returns
   `ESignatureTokenInfo.Invalid()`** — fail-closed.
7. On signing, `SaveSignedDocument` (`:181`) routes the signed bytes **into the category-specific HR
   table** via a `switch (DocumentCategory)` (`:439`) — Onboarding→`EmployeeOnboardingDocument`,
   Disciplinary→`EmployeeDisciplinary`, …, Documents→`EmployeeDocument`, Qualifications→
   `EmployeeQualification`. For user-signers it writes `SignedUserDocument`. Request →
   `Status=Signed`, `SignatureDate=today`, `UnsignedDocumentId=null`; the shared `UnsignedDocument`
   is deleted once no pending request still references it (`:217`).
8. `ESignSystemDocumentId` / `ESignSystemToken` / `Processing` status exist for an **external e-sign
   provider** integration (the app can hand off to a third-party signing system).

There is a distinct `ClockingActivitySignatures` table (client name + email + signature file) — an
on-site **client sign-off on a clocking activity**, separate from the HR e-sign flow.

### 3.2 Company documents — categorised, targeted, virus-scanned

`CompanyDocumentsService` (`:33`): a company document has a **category**
(`CompanyDocumentCategories`, e.g. policies/handbooks), an `AttachmentType` (file or external URL),
and is **targeted at employees by Department and/or Location** join tables
(`CompanyDocumentsDepartments`, `CompanyDocumentsLocations`). `AddCompanyDocument` enqueues a virus
scan when the attachment status is `Pending` (`:52`).

### 3.3 HR-document access — a third rights vocabulary, fail-closed

`RoleHrDocumentSecurityService.GetPermissionForTheDocument` (`:64`): permission is per
`(RoleId, HrDocumentType)` where type is the `EmployeeDocumentCategory`. `AccessPermission` =
`{ Deny=1, ReadOnly, ReadWrite }`. **No matching rule → `Deny`; exception → `Deny`.** This is a
correct fail-closed model (contrast the access-scope fail-opens elsewhere in the estate). Note it is a
**third, separate rights vocabulary** in legacy (screen rights, scope rights, and this) — ARCHITECTURE
`:464` already flags WM should unify to one.

---

## 4. Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| A document = file + metadata; e-sign request/sign lifecycle | **Keep** | genuine domain truth |
| HR categories as document types (`EmployeeDocumentCategory`) | **Keep** | real taxonomy; drives access + routing |
| Fail-closed HR-document access (`Deny` default) | **Keep** | already the right model |
| Virus-scan-gated distribution (link only when `Safe`) | **Keep** | correct security posture |
| Bytes in `VarBinary(MAX)` DB columns | **Improve** | → object storage (MinIO/S3), metadata+key in Postgres (ARCHITECTURE §2/§10) |
| **8 near-identical HR document tables** (Appraisal/Certificate/Disciplinary/Objective/Qualification/Remuneration/Document/Onboarding), each with its own `*File`/`FileName`/`ContentType`/`SignDocumentRequestId`/tags | **Improve** | one `documents` table with a `category` discriminator + polymorphic metadata; removes an 8-way `switch` (`ESignatureDocumentsService.cs:439`) repeated everywhere |
| **8 parallel `*Tags` tables + `FileVirusScanTable` 10-way enum** | **Improve** | one tag table + one scan queue keyed by `(documentId)` once documents are unified |
| Company-doc targeting **by Department/Location only** | **Improve** | ARCHITECTURE wants **population-group** targeting (§9 matrix); groups are in scope but unbuilt |
| **Company doc with no dept AND no location ⇒ visible to everyone** (`CompanyDocumentsService.cs:38`) | **Invert** | fail-open: empty target = "all". WM should make "all employees" an **explicit** choice; empty target = **nobody** |
| Hardcoded **7-day** signature expiry (`:81`) | **Improve** | make configurable per request/tenant, not a magic constant |
| `SignatureDate = currentTime.Date` (time truncated) | **Improve** | store full timestamp (UTC) for a real audit trail |
| Third separate HR-document rights vocabulary | **Improve** | unify into WM's single RBAC + permission model (ARCHITECTURE `:464`) |
| One `SignDocumentRequest` **row per signer** (fan-out) | **Improve** | one request with a signer collection; per-signer status |
| External e-sign provider hook (`ESignSystem*`) | **Keep (as plugin)** | route through `PluginSdk`, not a hardcoded integration |
| Onboarding **as a checklist/task model** | **Drop-from-legacy / WM-new** | *legacy has no such model* — see §6 |

---

## 5. Edge cases (from real code)

1. **Virus scan not Safe** → signer link is silently **not sent** (`ESignatureNotifierService.cs:72,102`);
   the request sits `Pending`. WM must surface this state, not swallow it.
2. **Token expired / status not Pending / string mismatch** → `Invalid()`, no document served
   (`ESignatureAccessService.cs:41`). Test: expired link, already-signed link, tampered token.
3. **Fan-out sharing:** N signer requests share one `UnsignedDocument`; it is deleted only when the
   **last** non-signed request stops referencing it (`ESignatureDocumentsService.cs:217`). Test:
   signer A signs, signer B still pending → unsigned bytes must remain.
4. **Company doc, empty target set** → visible to **all** employees (`CompanyDocumentsService.cs:38`).
   Test the inverted WM behaviour: empty target = visible to nobody; "all" is explicit.
5. **Signer is an employee without a user account** → email falls back to `Employee.ContactEmail`
   (`ESignatureNotifierService.cs:403`). Token access works without a login. Keep this capability.
6. **Category = Qualifications** requires a qualification payload or the save is rejected
   (`ESignatureDocumentsService.cs:65`); on signing it creates an `EmployeeQualification` with
   start/end/reference (expiry-tracked).
7. **Cancel** deletes tokens and clears external-system ids (`:156`); **resend** deletes old tokens,
   resets expiry to +7d and status to Pending (`ESignatureNotifierService.cs:53`).
8. **AttachmentType mismatch on edit** throws (`CompanyDocumentsService.cs:116,149`) — a file document
   cannot be edited as a URL document and vice-versa.

---

## 6. Onboarding is NOT a checklist in legacy — correction to ARCHITECTURE

ARCHITECTURE §10 (`:356`) describes onboarding packs as **"a checklist of documents/tasks for new
hires."** **Legacy has no checklist or task model.** `EmployeeOnboardingDocuments` (13 cols) is a plain
**document table** — description, note, file, dates, created/updated-by, optional
`SignDocumentRequestId`, virus-scan status, plus `EmployeeOnboardingDocumentsTags`. "Onboarding" is
simply the `Onboarding` value of `EmployeeDocumentCategory`: a document filed under that category,
optionally e-signed. **A checklist/task onboarding model is therefore WM-new**, not a legacy port —
build it as such or descope it. (Not editing ARCHITECTURE this wave; flagged for the consolidated pass.)

---

## 7. Target design in WM (per ARCHITECTURE §10, §14 item 9)

- **`WM.Modules.Documents`** (▢ planned) — object-storage-backed, metadata in Postgres.
- One `documents` table with `category` (the 8 HR categories + general), polymorphic owner
  (employee / company / attached-to record), storage key, content-type, tags, virus-scan status.
- **E-signature**: a signature request with a signer collection (employee/user, token access for
  account-less signers), per-signer status, configurable expiry, external-provider hook via
  `PluginSdk`, immutable signed output, notifications through the Notifications module (§9 matrix
  rows: "Document awaiting your signature", "Onboarding task/document assigned").
- **Company documents**: category + **population-group** targeting (needs the unbuilt population-group
  primitive), fail-**closed** visibility.
- **Access**: unified RBAC (retire the separate `RoleHrDocumentSecurity` vocabulary), special-category
  encryption.
- **Self-service** (`/api/me/documents`): view, download, sign.

---

## 8. What I did NOT measure (numbered)

1. **`E:\Tlw\Database\Versioning\*.sql` was not exhaustively diffed** for these tables. The grep for
   `CREATE OR ALTER … SignDocument/CompanyDocument/EmployeeOnboarding/UnsignedDocument` returned no
   hits (scripts likely use `CREATE TABLE`), so I did **not** confirm there is no higher-numbered
   redefinition adding columns or an external-storage migration. The **live LINQ model is authoritative**
   for current shape, and it shows all bytes in-DB, but a versioning-log sweep is outstanding.
2. **Frontend screens** (`Source\WebSite`) for documents/e-sign were not opened — `SCREEN-TREE.md`
   not reconciled this wave (off-limits). Screen inventory for the Documents area is unverified.
3. **The two 55-column report views** (`Unified*ReportView`) were counted but their column semantics
   were not enumerated — they belong to the Reporting bucket, not this survey.
4. **`AbsenceRequestEmployeeDocuments`, `ExpenseDocuments`/`EmployeeExpenseDocuments`,
   `ClockingActivityDocuments`/`ClockingEmployeeDocuments`** were counted and classified as
   cross-module links but their owning services were not read (they belong to Absence / Expenses /
   TimeAttendance plans).
5. **External e-sign provider identity** — `ESignSystem*` columns prove a third-party hook exists but I
   did not locate the concrete provider/adapter (which vendor). Outstanding for the plugin design.
