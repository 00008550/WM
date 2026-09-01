# TLW Expenses — measured model

Survey date: 2026-09-01. Measured against `E:\Tlw` only. Every count below is derived from
`E:\Tlw\Source\Logic\Entities\HorioDB.designer.cs` (the LINQ-to-SQL model) and named source files;
WM's own documents were treated as claims under audit.

## 1. The measurement (base vs view, re-derived from scratch)

Tables whose name contains `expense`, with column counts from `ColumnAttribute` under each
`TableAttribute` in `HorioDB.designer.cs`:

| Table | Columns | Kind |
|---|---:|---|
| `dbo.EmployeeExpenses` | 25 | base — the claim line |
| `dbo.ExpenseTypes` | 10 | base — category |
| `dbo.ExpenseMileagePeriods` | 7 | base |
| `dbo.ExpenseMileages` | 6 | base |
| `dbo.ExpenseRates` | 5 | base — mileage rate header |
| `dbo.ExpensePaymentCategories` | 5 | base |
| `dbo.ExpenseDocuments` | 5 | base — receipt blob |
| `dbo.ExpenseTypeGroups` | 4 | base |
| `dbo.ExpensesLastUsed` | 4 | base |
| `dbo.ExpenseRateLimits` | 4 | base — mileage rate band |
| `dbo.DepartmentExpenseManagers` | 4 | base — approver map |
| `dbo.EmployeeExpenseDocuments` | 3 | base — claim↔receipt join |
| `dbo.ExpenseVehicleTypes` | 2 | base |
| **Base total** | **84** | **13 tables** |
| `dbo.UnifiedExpensesReportView` | 88 | **view** (reporting projection, not storage) |

**`COVERAGE-AUDIT.md:65` records "Expenses — 10 tables / 135 columns". Both numbers are wrong.**
The real base footprint is **13 tables / 84 columns**; the "135" appears to fold the 88-column
report *view* into a base total (the exact over-count pattern flagged for every bucket this
programme re-measured). Reported here separately: **base 84, view 88.**

**Two adjacent lookups belong to the domain but are not `expense`-named**, so a name search misses
them: `dbo.ReceiptStatus` (2 cols) and `dbo.PaymentType` (2 cols), both FK'd from
`EmployeeExpenses`. Counting them, the functional footprint is **15 tables**. Additionally the
**`Employees` table itself carries two expense columns** — `ExpenseManagerId` and
`ExpensePaymentManagerId` (`HorioDB.designer.cs:31335,31459`) — which is where the approver
override lives (§4).

Versioning trap checked: `E:\Tlw\Database\Versioning\42.V3.6.6.0.sql` and `84.V5.30.0.0.sql` carry
expense DDL; `HorioDB.designer.cs` is the current consolidated model and is what the counts above
reflect.

## 2. What a claim is

There is **no header/line-items split.** A "claim" in TLW is a single row of
`dbo.EmployeeExpenses` — one expense line. The Expense Dashboard batches many rows for a person on
screen, but each is an independent record with its own status. Columns
(`HorioDB.designer.cs:104379`+):

`Id, EmployeeId, ExpenseTypeId, SubmissionDate, ExpenseDate, Amount, Vat, Note, Status,
PaymentCategoryId, IsBillable, AmountToReimburse, ReceiptStatusId, PaymentTypeId, CountryId,
CurrencyId, FromLocation, ToLocation, Distance, CostCentreId, VehicleTypeId, UpdatedManually,
UpdatedBy, ActivityId, ClientId`.

It references, by id: **employee, expense type, payment category, payment type, country, currency,
cost centre, vehicle type, activity, client.** Mileage fields (`FromLocation/ToLocation/Distance/
VehicleTypeId`) are inlined on the same row as monetary fields — a mileage claim and a receipt
claim are the same table, discriminated by `ExpenseType.IsMileage`.

`Amount`/`Vat`/`AmountToReimburse` are stored derived values from the VAT split (§5), not free
inputs.

## 3. Status lifecycle

`EmployeeExpenseStatus` (`E:\Tlw\Source\SharedLogic\Enums\EmployeeExpenseStatus.cs`):

```
Rejected=0  Approved=1  Pending=2  Saved=3
CancelRequested=4  CancelRejected=5  CancelApproved=6  ApprovedPaid=7
```

Flow: **Saved** (draft) → **Pending** (submitted) → **Approved** → **ApprovedPaid** (paid by the
payment manager), or **Rejected**. A separate cancellation sub-workflow lets a *claimant* request
reversal of an already-Approved claim: **CancelRequested** → **CancelApproved** / **CancelRejected**
(`ExpensesService.cs:594` blocks `CancelRequested` unless the current status is `Approved`).

`Status` is `int NULL` in the DB. `EmployeeExpense.CanBeDeleted()`
(`E:\Tlw\Source\Logic\Entities\BusinessRules\EmployeeExpense.cs:5`) fails closed on null
(un-deletable), and only permits delete in Saved / CancelApproved / Pending / Rejected. A separate
`EmployeeExpenseStatusFilter` (`Core\Enumeration\Enums.cs:1497`) drives the dashboard filter tabs.

## 4. Approval — department-scoped, employee-overridable, single approver

Two roles, both resolved the same way and both stored in **two places**:

- **Expense manager** — approves/rejects.
- **Payment manager** (`ExpensePaymentManagerId`) — a distinct role that moves Approved → paid.

Resolution precedence (`ExpenseDashboardController.cs:2585`):

1. `Employee.ExpenseManagerId` if set (**employee-level override wins**);
2. else `DepartmentExpenseManagers.ExpenseManagerId` for the employee's department;
3. else if `SoftwareMainOptions.ManagerCanAuthorizeOwnExpense` → self-approve;
4. else `throw InvalidOperationException("Expense submitted with no ExpenseManager")` — **fail
   closed.**

This is a genuine **two-level config resolution** (Rule-one smell #6) and it changes who can see
what: the dashboard visibility filter (`ExpenseDashboardController.cs:2036-2040`) shows a claim to a
department manager **only when the employee has no personal expense manager**
(`!e.ExpenseManagerId.HasValue && isDepartmentExpenseManager && dept matches`). A personal
assignment therefore *suppresses* the department manager. There is also an `isGlobalExpenseManager`
role that sees everything.

**This is a different shape from absence approval.** `DepartmentExpenseManagers` has **no `Order`
column** and holds a **single** manager + single payment manager per department — there are no
ordered deputies/escalation levels. It is one approver, department-scoped, with an employee-level
override. (Absence approval, per plan 015, is a different mechanism.) A shared approval engine is
possible but the two are not the same rule today; see §8 open question.

## 5. Mileage and VAT — the tunable rules, and they are data

**Mileage rates are fully config-driven** (HMRC AMAP-style banded rates):

- `ExpenseRates` — one per (financial year window `StartOfFinancialYear`..`EndOfFinancialYear`,
  `VehicleTypeId`).
- `ExpenseRateLimits` — the bands: `Limit` (cumulative-miles ceiling of the band) + `Rate`
  (pence/mile).

The amount for a claim depends on the employee's **cumulative distance in that financial year** for
that vehicle type: `GetMileageExpenseAmount` (`ExpensesService.cs:1462`) sums prior `Distance` for
the employee/vehicle/FY, then `CalculateMileageAmount` (`ExpensesService.cs:1488`) walks the bands,
splitting the new distance across tier boundaries. **Config trap:** the code comments that the last
band's `Limit` must be a magic huge number (`999999999999`) or the walk fails
(`ExpensesService.cs:1537`) — an unbounded top tier is expressed as a sentinel, not modelled.

`ExpenseMileagePeriods` (with `Locked`/`Active`/`View` flags) + `ExpenseMileages` provide a
period-based mileage register per employee with a running `Adjustment`, used by the Expense Mileages
screen and reports.

**VAT** (`CalculateVat`, `ExpensesService.cs:1804`): the user enters a VAT-*inclusive* gross;
`ExpenseType.VatPercentage` splits it into net `Amount` + `Vat`, storing gross in
`AmountToReimburse`. Per-type flags `IsVAT`, `IsMileage`, `IsSystem` on `ExpenseTypes`.

## 6. The payroll / cost seam

- **Payroll export:** `ExpenseTypes.AccountCode` + `ExpenseTypes.ExportCode` (both `NVarChar(10)`,
  uniqueness-checked at `ExpensesService.cs:397,403`) are the codes a payroll plugin (plan 014)
  would map. Approved expenses are intended to export to payroll (`ARCHITECTURE.md:309`).
- **Cost centre / job costing (plans 016/017):** each claim line carries `CostCentreId`,
  `ActivityId`, `ClientId`, and `IsBillable` — so an approved expense can be attributed to a cost
  centre and/or billed to a client/activity. This is the same cost seam Activities uses.

An approved expense therefore reaches **two** downstream integrations (payroll reimbursement and
cost attribution), not one.

## 7. Screens (legacy)

`E:\Tlw\Source\WebSite\Views`: `Expense/` (types), `ExpenseDashboard/` (claim entry, `_Approvals`,
receipts, `_QuickExpenses`), `ExpenseMileages/` (periods + print), `ExpensePaymentCategory/`,
`ExpenseRate/` (+ `_MileageRateLimit`), `ExpenseTypeGroup/`, `ExpenseVehicleType/`, `PaymentType/`,
and `Personnel/.../_AssignExpenseManagersDialog` + `Tabs/_ExpenseManagers`. Controller:
`Controllers\Expenses\ExpenseDashboardController.cs` (2600+ lines).

## 8. Corrections to WM's records (proposed — consolidated pass, not applied here)

1. **`COVERAGE-AUDIT.md:65`** — "Expenses | 10 | 135" → **13 base tables / 84 cols, plus
   `UnifiedExpensesReportView` 88 cols (view, not base); +2 adjacent lookups**. Wrong on both axes.
2. **`ARCHITECTURE.md:434`** — "Manager relations: … expense … per employee **and** per department,
   with `Order`". For expenses this is inaccurate: `DepartmentExpenseManagers` has **no `Order`
   column** and is a **single** manager per department; the mechanism is employee-override →
   department-default, not ordered deputies. (Absence/notification/fire-marshal may differ; this
   note is expenses-only.)
3. **§13 coverage matrix / `ARCHITECTURE.md:808` item 11** — Expenses is `▢` unbuilt in WM. Confirmed:
   `src/**` contains no expense code (only a doc-comment reference in
   `People/Domain/Employee.cs:107`). Accurate; no change needed beyond leaving the ▢.

## 9. What I did NOT measure (explicit)

1. **`UnifiedExpensesReportView`** internals — 88 columns, treated as a reporting projection; its
   join graph and any embedded calculation were not traced.
2. **The DevExpress report SQL** (`Database\DevExpressReports\Expenses*.sql`, 6 files) — named, not
   opened; they are the reporting seam for the §16 reporting decision.
3. **`ExpensesLastUsed`** semantics beyond "recently-used shortcut per user" (inferred from columns).
4. **`ExpenseDashboardController.cs`** in full — read the approver-resolution and visibility paths;
   the 2600-line controller's quick-expense and multi-edit paths were sampled, not exhaustively read.
5. **Localization rows** (`Database\Localization\*expense*`) — string catalogues, out of scope.
6. **Currency/Country tables** referenced by `CurrencyId`/`CountryId` — not measured (shared lookups).
