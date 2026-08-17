using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using Xunit;

namespace WM.Modules.People.Tests.Domain;

/// <summary>
/// The employment window, one edge case per test, lifted from legacy rather than invented — plan 007's
/// edge cases 1–6 and <c>TLW-PEOPLE-MODEL.md</c> §4.1.
///
/// <para>
/// These are separate named tests on purpose. Employment is the predicate every historical question in
/// the product resolves through (a timesheet, an accrual pro-rate, plan 002's replay), so when one of
/// these regresses the test name should say <i>which</i> boundary moved rather than "employment is
/// wrong".
/// </para>
/// </summary>
public sealed class EmploymentPredicateTests
{
    private static readonly DateOnly Today = new(2026, 8, 14);
    private static readonly DateOnly Hired = new(2024, 1, 15);

    [Fact]
    public void Edge_1_the_last_day_of_employment_is_inclusive()
    {
        // dbo.IsActiveEmployment is `dischargeDate < referenceDate -> 0` (76.V5.22.0.0.sql:33-36),
        // so someone whose last day is today is still employed today. `<=` would fire a day early —
        // and on that day WM would refuse a punch from someone standing at work.
        var leavingToday = Employed(until: Today);

        Assert.True(leavingToday.IsEmployedOn(Today));
        Assert.False(leavingToday.IsEmployedOn(Today.AddDays(1)));
        Assert.Equal(EmployeeStatus.Active, leavingToday.StatusOn(Today));
        Assert.Equal(EmployeeStatus.Leaver, leavingToday.StatusOn(Today.AddDays(1)));
    }

    [Fact]
    public void Edge_2_no_leaving_date_means_employed_and_says_so_explicitly()
    {
        // In T-SQL `NULL < date` is NULL, so legacy's CASE falls through to ELSE 1 — it reaches the
        // right answer by accident of three-valued logic. WM states it: null is an OPEN window.
        var openEnded = Employed(until: null);

        Assert.True(openEnded.IsEmployedOn(Today));
        Assert.True(openEnded.IsEmployedOn(Today.AddYears(20)));
        Assert.Equal(EmployeeStatus.Active, openEnded.StatusOn(Today.AddYears(20)));
    }

    [Fact]
    public void Edge_3_a_future_leaving_date_is_employed_now_and_a_leaver_from_that_date()
    {
        // The case WM could not represent at all before this portion, and the most common thing an HR
        // user types: "leaves on 30 September".
        var leavingSoon = Employed(until: Today.AddDays(47));

        Assert.True(leavingSoon.IsEmployedOn(Today));
        Assert.True(leavingSoon.IsEmployedOn(Today.AddDays(47)));
        Assert.False(leavingSoon.IsEmployedOn(Today.AddDays(48)));
        Assert.Equal(EmployeeStatus.Active, leavingSoon.StatusOn(Today));
        Assert.Equal(EmployeeStatus.Leaver, leavingSoon.StatusOn(Today.AddDays(48)));
    }

    [Fact]
    public void Edge_4_suspension_wins_over_a_future_leaving_date()
    {
        // Legacy's dbo.ActiveEmployeesView says this person is ACTIVE, because `AND` binds tighter
        // than `OR` in `IsActive = 1 AND DischargeDate IS NULL OR DischargeDate >= today`
        // (78.V5.24.0.0.sql:74-80) — so a suspended employee with a future discharge date can badge
        // in. WM answers not-available, and this is the test that says so.
        //
        // "Wins" is about the COMPOSED answer, not about the window: IsEmployedOn is the window
        // alone, so it still says this person is on the payroll (they are — they are being paid while
        // suspended). What must not happen is the composed answer coming out as available.
        var suspended = Employed(until: Today.AddDays(47), suspended: true);

        Assert.False(Available(suspended, Today));
        Assert.Equal(EmployeeStatus.Suspended, suspended.StatusOn(Today));
        // And not merely today: suspension is undated, so it holds across the whole window.
        Assert.False(Available(suspended, Hired));
        Assert.False(Available(suspended, Today.AddDays(47)));
        // The window itself is untouched by it — the fact legacy keeps separate, and the one an
        // accrual pro-rate reads (EmployeeAccrualCalculationsService.cs:728-741 never reads IsActive).
        Assert.True(suspended.IsEmployedOn(Today));
        Assert.True(suspended.IsEmployedOn(Today.AddDays(47)));
        Assert.False(suspended.IsEmployedOn(Today.AddDays(48)));
    }

    [Fact]
    public void The_window_predicate_does_not_read_suspension_at_all()
    {
        // The defect this pins: IsEmployedOn once took `isSuspended` and ANDed it in, so every
        // "who was employed on D?" question — a replay, a payroll re-run, an entitlement pro-rate —
        // silently answered "…and not currently suspended", a fact about TODAY leaking into a
        // question about the past. Legacy never fuses the two: ActiveNotFired() is literally
        // Active().NotFired() (EmployeeExtensions.cs:22-27), NotFired() ships alone in production
        // paths (PersonnelService.cs:1841, ServiceTasks.cs:214, GlobalNotificationsService.cs:557),
        // dbo.IsActiveEmployment takes only dates, and TipsService.cs:75-79 gates the two on
        // independent caller flags.
        var suspended = Employed(until: null, suspended: true);
        var notSuspended = Employed(until: null, suspended: false);

        foreach (var reference in ReferenceDates)
            Assert.Equal(notSuspended.IsEmployedOn(reference), suspended.IsEmployedOn(reference));

        // And the composition the callers who DO care must write is still available and still right.
        Assert.False(Available(suspended, Today));
        Assert.True(Available(notSuspended, Today));
    }

    [Fact]
    public void Edge_5_employment_can_be_asked_as_at_a_date_in_the_past()
    {
        // The question plan 002's replay and every payroll re-run needs, and the whole reason the
        // enum had to go: a leaver was still employed in March, and a stored status cannot say so.
        var leftInJune = Employed(until: new DateOnly(2026, 6, 30));

        Assert.True(leftInJune.IsEmployedOn(new DateOnly(2026, 3, 3)));
        Assert.False(leftInJune.IsEmployedOn(Today));
        Assert.Equal(EmployeeStatus.Active, leftInJune.StatusOn(new DateOnly(2026, 3, 3)));
        Assert.Equal(EmployeeStatus.Leaver, leftInJune.StatusOn(Today));
    }

    [Fact]
    public void Edge_6_a_starter_whose_first_day_is_in_the_future_is_not_employed_yet()
    {
        // Legacy's EnterDate is NOT NULL but nothing stops it being future-dated, and a pre-boarded
        // starter is a real record: they exist in the system, with a badge, before they may punch.
        var starter = Employed(from: Today.AddDays(14), until: null);

        Assert.False(starter.IsEmployedOn(Today));
        Assert.True(starter.IsEmployedOn(Today.AddDays(14)));
        Assert.Equal(EmployeeStatus.NotYetStarted, starter.StatusOn(Today));
        Assert.Equal(EmployeeStatus.Active, starter.StatusOn(Today.AddDays(14)));
    }

    [Fact]
    public void The_query_filter_and_the_object_predicate_answer_the_same_question()
    {
        // Employee.EmployedOn (an expression, for the database) and Employee.IsEmployedOn (the object)
        // are two shapes of one rule, and the object one is the definition. Legacy's six copies of
        // this predicate drifted precisely because nothing compared them — two of the six disagree
        // today (TLW-PEOPLE-MODEL.md §4.1a). So they are asserted against each other, over every
        // window shape crossed with every interesting reference date, rather than reviewed.
        List<string> mismatches = [];

        foreach (var reference in ReferenceDates)
        {
            var byQuery = EveryWindow.AsQueryable()
                .Where(Employee.EmployedOn(reference))
                .Select(e => e.Code)
                .Order()
                .ToArray();

            var byObject = EveryWindow
                .Where(e => e.IsEmployedOn(reference))
                .Select(e => e.Code)
                .Order()
                .ToArray();

            if (!byQuery.SequenceEqual(byObject))
                mismatches.Add($"{reference:O}: query [{string.Join(',', byQuery)}] vs object [{string.Join(',', byObject)}]");
        }

        Assert.Equal([], mismatches);
    }

    [Fact]
    public void The_query_filter_translates_to_SQL()
    {
        // The one thing the equivalence test above cannot prove: that Npgsql can translate the
        // expression at all. A predicate that agrees perfectly in memory and throws
        // "could not be translated" against Postgres is a runtime failure on the live attendance
        // board — where ListEmployedOnAsync runs — and this repository has no Postgres harness to
        // catch it (see EmploymentMigrationTests for the full note).
        //
        // ToQueryString compiles the query through the real Npgsql SQL generator and never opens a
        // connection, so it is the strongest offline check available. The connection string below is
        // never dialled.
        var options = new DbContextOptionsBuilder<PeopleDbContext>()
            .UseNpgsql("Host=nowhere.invalid;Database=wm;Username=none;Password=none")
            .Options;
        using var db = new PeopleDbContext(options);

        var sql = db.Employees.Where(Employee.EmployedOn(Today)).ToQueryString();

        Assert.Contains("\"EmployedFrom\"", sql);
        Assert.Contains("\"EmployedUntil\"", sql);

        // The predicate itself, not the projection: IsSuspended is a column of the entity, so it is
        // in every SELECT list and only its appearance in the WHERE clause means anything.
        var where = sql[(sql.IndexOf("WHERE", StringComparison.Ordinal) is var i && i >= 0
            ? i
            : throw new Xunit.Sdk.XunitException($"the filter produced no WHERE clause:\n{sql}"))..];

        // The pieces of the rule, in SQL: started, and either open or not yet ended.
        Assert.Contains("IS NULL", where);
        // And the unfold, at the level where it would actually bite: a WHERE clause that mentions
        // IsSuspended is a query that has quietly dropped suspended people from a payroll question.
        Assert.DoesNotContain("IsSuspended", where);
    }

    [Fact]
    public void Suspension_is_not_the_same_fact_as_leaving()
    {
        // Legacy proves it treats them separately: SetEmployeesLeaver:122-145 writes DischargeDate and
        // pointedly does NOT touch IsActive, and the accrual calculation pro-rates on the dates while
        // never reading IsActive (EmployeeAccrualCalculationsService.cs:728-741). Folding suspension
        // into the window would collapse two facts into one and change entitlement figures.
        var suspendedButEmployed = Employed(until: null, suspended: true);

        Assert.False(Available(suspendedButEmployed, Today));
        Assert.Null(suspendedButEmployed.EmployedUntil);
        Assert.Equal(EmployeeStatus.Suspended, suspendedButEmployed.StatusOn(Today));

        // Un-suspending restores availability with no date having been touched — and the window said
        // "employed" throughout, because suspension never entered it.
        Assert.True(suspendedButEmployed.IsEmployedOn(Today));
        suspendedButEmployed.IsSuspended = false;
        Assert.True(Available(suspendedButEmployed, Today));
    }

    /// <summary>
    /// The composition a caller writes when its question needs both facts — "may this person be at
    /// work on that day?". Exactly <c>StatusOn(...) == Active</c>, and exactly what
    /// <c>GET /api/employees</c>'s <c>isEmployed</c> column and TimeAttendance's live feed compute.
    /// </summary>
    private static bool Available(Employee employee, DateOnly on) =>
        !employee.IsSuspended && employee.IsEmployedOn(on);

    [Fact]
    public void A_one_day_engagement_is_employed_on_exactly_that_day()
    {
        // Both boundaries inclusive, on the same date — the degenerate window, and the one an
        // off-by-one in either comparison breaks.
        var oneDay = Employed(from: Today, until: Today);

        Assert.True(oneDay.IsEmployedOn(Today));
        Assert.False(oneDay.IsEmployedOn(Today.AddDays(-1)));
        Assert.False(oneDay.IsEmployedOn(Today.AddDays(1)));
    }

    [Fact]
    public void Status_Active_is_exactly_the_two_facts_composed()
    {
        // The unfold leaves callers composing, so the composition needs one authority — otherwise
        // "!IsSuspended && IsEmployedOn(d)" spreads as a copied incantation and drifts, which is how
        // legacy ended up with six predicates. StatusOn is that authority: Active means available.
        List<string> mismatches = [];

        foreach (var employee in EveryWindow)
        foreach (var reference in ReferenceDates)
        {
            var composed = !employee.IsSuspended && employee.IsEmployedOn(reference);
            var byStatus = employee.StatusOn(reference) == EmployeeStatus.Active;
            if (composed != byStatus)
                mismatches.Add($"{employee.Code} at {reference:O}: composed {composed}, status {byStatus}");
        }

        Assert.Equal([], mismatches);
    }

    private static readonly DateOnly[] ReferenceDates =
    [
        new(2023, 12, 31), Hired.AddDays(-1), Hired, Hired.AddDays(1),
        new(2026, 3, 3), new(2026, 6, 30), new(2026, 7, 1),
        Today.AddDays(-1), Today, Today.AddDays(1),
        Today.AddDays(47), Today.AddDays(48), new(2099, 1, 1),
    ];

    /// <summary>Every shape a window can take, including the ones that only differ at a boundary.</summary>
    private static readonly List<Employee> EveryWindow =
    [
        Employed(code: "open", until: null),
        Employed(code: "open-suspended", until: null, suspended: true),
        Employed(code: "left-june", until: new DateOnly(2026, 6, 30)),
        Employed(code: "leaving-today", until: Today),
        Employed(code: "leaving-later", until: Today.AddDays(47)),
        Employed(code: "leaving-later-suspended", until: Today.AddDays(47), suspended: true),
        Employed(code: "starter", from: Today.AddDays(14), until: null),
        Employed(code: "one-day", from: Today, until: Today),
    ];

    private static Employee Employed(
        DateOnly? from = null, DateOnly? until = null, bool suspended = false, string code = "E1000") => new()
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            FirstName = "Test",
            LastName = "Employee",
            SiteId = Guid.Empty,
            EmployedFrom = from ?? Hired,
            EmployedUntil = until,
            IsSuspended = suspended,
        };
}
