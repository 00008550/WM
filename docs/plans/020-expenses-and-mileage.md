# 020 — Expenses & mileage (claims, banded rates, two-level approval)

Status: draft            <!-- draft → approved → in-progress → in-review → merged -->
Roadmap: ARCHITECTURE.md §14 Phase 7 / §13 item 11 (Expenses); events §9 line 341
Legacy sources surveyed:
- `E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` — measured: 13 base expense tables = **84 cols**;
  `UnifiedExpensesReportView` = 88 cols (view); `Employees.ExpenseManagerId`/`ExpensePaymentManagerId`
  at lines 31335/31459; `EmployeeExpenses` at 104379.
- `E:\Tlw\Source\Logic\Personnel\ExpensesService.cs` (2256 lines — VAT split 1804, mileage band walk
  1462/1488, status transitions 518/594)
- `E:\Tlw\Source\Logic\Personnel\DepartmentExpenseManagersService.cs`
- `E:\Tlw\Source\Logic\Entities\BusinessRules\EmployeeExpense.cs`
- `E:\Tlw\Source\SharedLogic\Enums\EmployeeExpenseStatus.cs`
- `E:\Tlw\Source\WebSite\Controllers\Expenses\ExpenseDashboardController.cs` (approver resolution 2585,
  visibility filter 2036)
- Full detail: `docs/TLW-EXPENSES-MODEL.md`.

## Ground truth

- **13 base tables / 84 columns** (not COVERAGE-AUDIT's "10 / 135" — that folds the 88-col report
  view into the base total; corrected in `TLW-EXPENSES-MODEL.md §1`). +2 adjacent lookups
  (`ReceiptStatus`, `PaymentType`).
- **WM has no expense code.** `src/**` contains only a doc-comment reference
  (`People/Domain/Employee.cs:107`). This plan builds the module from zero.
- A **claim = one `EmployeeExpenses` row** (no header/lines). Discriminated mileage-vs-receipt by
  `ExpenseType.IsMileage`.
- Corrections recorded in `TLW-EXPENSES-MODEL.md §8` (COVERAGE-AUDIT:65, ARCHITECTURE:434); applied
  in a later consolidated pass, not by this plan.

## Legacy behaviour (what we are replacing)

- **Taxonomy:** `ExpenseType` (flags `IsVAT`/`IsMileage`/`IsSystem`, `VatPercentage`, `AccountCode`,
  `ExportCode`) grouped by `ExpenseTypeGroup`; plus `ExpensePaymentCategory`, `PaymentType`,
  `ExpenseVehicleType`, `ReceiptStatus` — all data-driven lookups.
- **Mileage rate = banded, config-driven** (`ExpenseRates` per FY+vehicle → `ExpenseRateLimits`
  bands). Amount depends on the employee's **cumulative FY distance** for the vehicle; the band walk
  splits new distance across tiers (`ExpensesService.cs:1488`).
- **VAT split:** gross in → net `Amount` + `Vat`, gross stored as `AmountToReimburse`
  (`ExpensesService.cs:1804`).
- **Status machine** (8 states) with a claimant-driven cancel sub-flow (§3 of the model).
- **Two-level approver resolution** employee-override → department-default → self (if config) → fail
  closed, plus a separate payment manager for the pay step (§4 of the model).
- **Cost/payroll seam:** `CostCentreId`/`ActivityId`/`ClientId`/`IsBillable` per line; `AccountCode`/
  `ExportCode` on the type.

## Keep / Improve / Invert / Drop

| Structure | Class | Reason |
|---|---|---|
| Expense claim as a domain record with status lifecycle | **Keep** | genuine domain truth |
| Config-driven banded mileage rates (per FY, per vehicle) | **Keep** | exactly the tunable-rule-as-config the estate wants; do not hard-code rates |
| VAT % per expense type, gross→net split | **Keep** | correct tax modelling |
| Two roles (approver + payment manager) | **Keep** | approve and pay are genuinely distinct acts |
| Magic `999999999999` top-band sentinel | **Improve** | model an explicit **unbounded top tier** (nullable/`IsUpperBound` on the band) instead of a sentinel that silently breaks the walk if forgotten |
| `Status int NULL` | **Improve** | non-nullable enum column; null is an illegal state |
| Fixed-shape approver stored on both `Employees` and `DepartmentExpenseManagers` | **Improve** | one resolution service with explicit precedence, not two columns read in two controllers |
| Claim = single flat row mixing mileage + monetary + cost fields | **Improve** | model a claim with typed variants (receipt vs mileage) sharing a base; keep single-line semantics |
| `AccountCode`/`ExportCode` as free `NVarChar(10)` | **Keep** (validate uniqueness) | payroll plugins depend on them |
| `UnifiedExpensesReportView` (88-col denormalised view) | **Drop** | reporting projection; WM builds reporting per §16, not a stored view |
| `ExpensesLastUsed` (recently-used shortcut) | **Drop** | UI convenience, rebuild as a client concern if wanted |
| Self-approval when no manager configured | **Invert (guarded)** | keep fail-closed default; self-approve only behind an explicit tenant setting, never silent |

## Edge cases

- **Mileage band boundary:** a single claim that crosses a tier (cumulative 9,900 + 300 miles over a
  10,000-mile band) must split — miles-to-limit at the lower rate, remainder at the next. Test the
  three legacy paths: below first limit, within one band, spanning ≥2 bands
  (`CalculateMileageAmount` 1495/1526/1531).
- **Missing top band:** if bands don't cover the distance, legacy relies on the sentinel. WM must
  reject rate config that lacks an unbounded top tier, or treat the highest band as unbounded.
- **No rate for the date/vehicle:** `GetMileageExpenseAmount` returns null → claim has no computed
  amount. Decide: reject submission vs allow manual amount.
- **Approver absent:** no employee manager, no department manager, `ManagerCanAuthorizeOwnExpense`
  off → **fail closed** (legacy throws). Preserve.
- **Employee override suppresses department manager visibility** — a department manager must NOT see
  claims of employees who have a personal expense manager.
- **Cancel of a paid claim:** cancel-request is only legal from `Approved` (not `ApprovedPaid`) in
  legacy (`ExpensesService.cs:594`). Confirm target policy for `ApprovedPaid` reversal.
- **Delete guard:** only Saved/Pending/Rejected/CancelApproved deletable; null status un-deletable.
- **FY boundary:** cumulative distance resets at the `ExpenseRate` financial-year window, not the
  calendar year.

## Target design in WM

- New module `src/Modules/Expenses/WM.Modules.Expenses` (schema-isolated, ARCHITECTURE §1), with
  `WM.Modules.Expenses.Tests` wired into `WM.sln` (CLAUDE.md test rule — module starts with a test
  project).
- Contracts/events (ARCHITECTURE §9): `ExpenseClaimSubmitted`, `ExpenseClaimDecided`
  (approved/rejected), `ExpenseClaimPaid` on Kafka → approver/claimant notifications.
- Cross-module references via contracts only: employee/department (People), cost centre (plan 016),
  activity/client (plan 017), documents/receipts (Documents §10), payroll export (Plugin SDK / plan
  014). No direct cross-module DB reads.
- REST-first (`/api/expenses/*`, `/api/me/expenses` for self-service — ARCHITECTURE §Employee/§189);
  every endpoint gets an authorization policy (§5).

## Out of scope for this plan

- `UnifiedExpensesReportView` and the 6 DevExpress expense reports (reporting is §16, separate).
- Actual payroll-plugin wiring (plan 014) — this plan exposes the export codes, not the exporter.
- Cost-centre and activity/job-costing internals (plans 016/017) — referenced by id only.
- `ExpensesLastUsed` recently-used UX.
- Currency/country multi-currency conversion (fields carried, not converted).

## Portions

### [ ] P1 — Expenses module skeleton + taxonomy
**Touches:** new `src/Modules/Expenses/*` (module + `.Tests`), `WM.sln`, migration for
`ExpenseTypeGroup`, `ExpenseType`, `PaymentCategory`, `PaymentType`, `VehicleType`, `ReceiptStatus`;
CRUD endpoints + policies.
**Done when:** taxonomy tables exist with CRUD APIs; `AccountCode`/`ExportCode` uniqueness enforced;
`IsVAT`/`IsMileage`/`IsSystem` flags present; module registered in the API host.
**Tests:** uniqueness of account/export codes; system-type protection from delete.
**Risk:** low

### [ ] P2 — Banded mileage rates as config + the band-walk calculator
**Touches:** `ExpenseRate` (+ FY window, vehicle) and `ExpenseRateLimit` (band) entities + migration;
a `MileageCalculator` porting `CalculateMileageAmount`; endpoints to edit rate tables.
**Done when:** rates are editable data; calculator splits a distance across bands given cumulative
FY distance; top tier modelled as explicit unbounded (no `999999999999` sentinel).
**Tests:** below-first-limit, within-one-band, spanning-two-bands, missing-top-band rejected,
FY-boundary reset (lift the three legacy branches as cases).
**Risk:** medium

### [ ] P3 — Expense claim entity + submit + VAT split
**Touches:** `ExpenseClaim` entity (typed receipt/mileage) + migration; create/save/submit
endpoints; VAT split ported; non-nullable status enum.
**Done when:** a claim can be Saved then Pending; VAT gross→net split stored; mileage claim computes
amount via P2 calculator; status is non-nullable.
**Tests:** VAT split rounding vs legacy formula; mileage claim amount end-to-end; delete guard by
status; illegal transition rejected.
**Risk:** medium

### [ ] P4 — Approver resolution + approval/cancel workflow + events
**Touches:** approver map (`DepartmentExpenseManagers` equivalent) + employee-override; resolution
service with explicit precedence; approve/reject + cancel-request sub-flow; payment step;
`ExpenseClaimSubmitted/Decided/Paid` events.
**Done when:** precedence employee→department→self(config)→fail-closed works; department manager
cannot see overridden employees' claims; cancel-request only from `Approved`; pay step sets
`ApprovedPaid`; events emitted.
**Tests:** each precedence branch incl. fail-closed throw; visibility suppression; cancel from wrong
state rejected; event payloads.
**Risk:** high

### [ ] P5 — Receipts + mileage periods register
**Touches:** receipt attach (via Documents contract) + `ReceiptStatus`; `ExpenseMileagePeriod`
(+lock) and per-employee mileage register endpoints.
**Done when:** a receipt can be attached to a claim and its status tracked; mileage periods
lockable; per-employee running mileage viewable.
**Tests:** attach/detach receipt; locked period rejects edits; running total matches summed claims.
**Risk:** medium

### [ ] P6 — Self-service + cost/payroll export seam
**Touches:** `/api/me/expenses` (submit + track own claims); expose `AccountCode`/`ExportCode` and
`CostCentreId`/`ActivityId`/`ClientId`/`IsBillable` on the claim contract for downstream consumers.
**Done when:** an employee submits and tracks their own claims (own record only, §Employee); an
approved claim exposes cost attribution + export codes via contract for plan 014/016/017 consumers.
**Tests:** self-service scope (cannot see others'); approved-claim contract carries cost/export
fields.
**Risk:** medium

## Open questions for the user

1. **Shared approval engine?** Expense approval (single approver, department-scoped, employee
   override, separate payment role) is a **different shape** from absence approval (plan 015). Build
   a shared approval mechanism now, or keep expenses' own resolution and unify later? This is a
   product/architecture call. (`TLW-EXPENSES-MODEL.md §4`.)
2. **`ApprovedPaid` reversal:** legacy only allows cancel-request from `Approved`, not from paid.
   Should WM allow reversing a *paid* claim (with a compensating payroll adjustment), or lock it?
3. **Self-approval:** keep the legacy `ManagerCanAuthorizeOwnExpense` escape hatch (guarded, opt-in)
   or drop self-approval entirely and require an approver?
4. **Multi-currency:** claims carry `CurrencyId`/`CountryId` but legacy does no conversion. Does WM
   need FX conversion at approval/export time, or store-as-entered like legacy?
5. **Proposed doc corrections** (`TLW-EXPENSES-MODEL.md §8`) to `COVERAGE-AUDIT.md:65` and
   `ARCHITECTURE.md:434` — apply in the consolidated pass? (Not touched here per survey boundaries.)
