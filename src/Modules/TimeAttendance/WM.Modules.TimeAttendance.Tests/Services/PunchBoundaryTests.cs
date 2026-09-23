using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;
using WM.Modules.TimeAttendance.Services;
using WM.SharedKernel.Events;

namespace WM.Modules.TimeAttendance.Tests.Services;

/// <summary>
/// 007 P2 — the punch boundary fails closed, and a same-direction repeat within the dedupe window is
/// idempotent. This is the first test project for TimeAttendance: the module that owns the punch had
/// none (CLAUDE.md). Tests exercise <see cref="PunchService.RecordAsync"/> directly against an
/// in-memory context and a stub directory — no SQL, no host.
/// </summary>
public sealed class PunchBoundaryTests
{
    private static readonly Guid EmployeeId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string Code = "E1000";

    // Employed 2024-01-15, still employed, not suspended.
    private static EmployeeSummary Employed(DateOnly from, DateOnly? until = null, bool suspended = false) =>
        new(EmployeeId, Code, "Ada Lovelace", "Operator",
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), null, from, until, suspended);

    private static (PunchService svc, TimeAttendanceDbContext db) Build(
        EmployeeSummary? employee, int dedupeSeconds = 60)
    {
        var db = new TimeAttendanceDbContext(
            new DbContextOptionsBuilder<TimeAttendanceDbContext>()
                .UseInMemoryDatabase($"punch-{Guid.NewGuid()}").Options);
        var options = Options.Create(new PunchDeduplicationOptions { Window = TimeSpan.FromSeconds(dedupeSeconds) });
        var svc = new PunchService(db, new StubDirectory(employee), new NoopEventStream(), options);
        return (svc, db);
    }

    private static RecordPunchRequest Punch(DateTimeOffset at, PunchDirection dir = PunchDirection.In) =>
        new(Code, dir, PunchSource.Web, at);

    // ---- the employment-window boundary (edge cases 7-10) ----

    [Fact]
    public async Task A_punch_back_dated_into_the_employed_period_is_recorded()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15)));
        var result = await svc.RecordAsync(Punch(new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero)), null, default);
        Assert.NotNull(result.Punch);
        Assert.Null(result.Error);
        Assert.Equal(1, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A_punch_after_the_leave_date_is_refused()
    {
        var (svc, db) = Build(Employed(new DateOnly(2023, 7, 24), new DateOnly(2026, 6, 30)));
        var result = await svc.RecordAsync(Punch(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)), null, default);
        Assert.Null(result.Punch);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A_punch_on_the_last_day_of_employment_is_recorded_because_the_window_is_inclusive()
    {
        var (svc, _) = Build(Employed(new DateOnly(2023, 7, 24), new DateOnly(2026, 6, 30)));
        var result = await svc.RecordAsync(Punch(new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero)), null, default);
        Assert.NotNull(result.Punch);
    }

    [Fact]
    public async Task A_suspended_employee_cannot_punch_even_within_the_window()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15), suspended: true));
        var result = await svc.RecordAsync(Punch(new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero)), null, default);
        Assert.Null(result.Punch);
        Assert.NotNull(result.Error);
        Assert.Equal(0, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task The_refusal_message_is_identical_to_an_unknown_code_so_the_boundary_is_not_an_oracle()
    {
        // Not employed: a leaver.
        var (employedSvc, _) = Build(Employed(new DateOnly(2023, 7, 24), new DateOnly(2026, 6, 30)));
        var notEmployed = await employedSvc.RecordAsync(
            Punch(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)), null, default);

        // Unknown code: the directory returns null.
        var (unknownSvc, _) = Build(employee: null);
        var unknown = await unknownSvc.RecordAsync(
            Punch(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)), null, default);

        Assert.NotNull(notEmployed.Error);
        Assert.Equal(unknown.Error, notEmployed.Error);
    }

    // ---- the dedupe guard ----

    [Fact]
    public async Task A_repeated_same_direction_punch_within_the_window_returns_the_existing_punch()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15)), dedupeSeconds: 60);
        var t = new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var first = await svc.RecordAsync(Punch(t), null, default);
        var second = await svc.RecordAsync(Punch(t.AddSeconds(10)), null, default);

        Assert.NotNull(first.Punch);
        Assert.NotNull(second.Punch);
        Assert.Equal(first.Punch!.Id, second.Punch!.Id);   // same punch, not a new one
        Assert.Equal(1, await db.Punches.CountAsync());     // only one row
    }

    [Fact]
    public async Task A_same_direction_repeat_outside_the_window_creates_a_new_punch()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15)), dedupeSeconds: 60);
        var t = new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero);
        await svc.RecordAsync(Punch(t), null, default);
        var later = await svc.RecordAsync(Punch(t.AddSeconds(90)), null, default);

        Assert.NotNull(later.Punch);
        Assert.Equal(2, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task An_opposite_direction_punch_within_the_window_is_a_new_punch_not_a_duplicate()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15)), dedupeSeconds: 60);
        var t = new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero);
        await svc.RecordAsync(Punch(t, PunchDirection.In), null, default);
        var outPunch = await svc.RecordAsync(Punch(t.AddSeconds(5), PunchDirection.Out), null, default);

        Assert.NotNull(outPunch.Punch);
        Assert.Equal(2, await db.Punches.CountAsync());
    }

    [Fact]
    public async Task A_zero_window_turns_dedupe_off_so_a_rapid_repeat_creates_a_second_punch()
    {
        var (svc, db) = Build(Employed(new DateOnly(2024, 1, 15)), dedupeSeconds: 0);
        var t = new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero);
        await svc.RecordAsync(Punch(t), null, default);
        await svc.RecordAsync(Punch(t.AddSeconds(2)), null, default);
        Assert.Equal(2, await db.Punches.CountAsync());
    }

    [Fact]
    public void The_default_window_is_thirty_seconds()
    {
        // Confirmed by the user 2026-09-23. A change to the default is a product decision, not a refactor.
        Assert.Equal(TimeSpan.FromSeconds(30), new PunchDeduplicationOptions().Window);
    }

    // ---- stubs ----

    private sealed class StubDirectory(EmployeeSummary? employee) : IEmployeeDirectory
    {
        public Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(employee);
        public Task<EmployeeSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(employee);
        public Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnAsync(DateOnly on, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmployeeSummary>>(employee is null ? [] : [employee]);
        public Task<IReadOnlyList<EmployeeSummary>> ListEmployedOnUnscopedAsync(DateOnly on, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EmployeeSummary>>(employee is null ? [] : [employee]);
    }

    private sealed class NoopEventStream : IEventStreamProducer
    {
        public Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
            where TEvent : class => Task.CompletedTask;
    }
}
