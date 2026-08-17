using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// The leaver record at the transport: a leaving date, a reason from the maintained lookup, and
/// comments — kept together, cleared together.
///
/// <para>
/// Legacy backs both halves. <c>SetEmployeesLeaver:122-145</c> writes the date and the reason as one
/// act, and <c>SetEmployeesActive:151-173</c> un-leaves with
/// <c>set IsActive = 1, DischargeDate = null, LeaveReasonId = null</c> — it clears the reason with the
/// date. A re-hired employee who keeps last time's leaving reason is a defect that surfaces years
/// later, on a report, in front of the person it is about.
/// </para>
/// </summary>
public sealed class LeaverRecordEndpointTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Resignation = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Redundancy = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static readonly DateOnly Hired = new(2024, 1, 15);
    private static readonly DateOnly LastDay = new(2026, 9, 30);

    [Fact]
    public async Task A_leaver_keeps_the_reason_and_the_comments_through_a_round_trip()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(
            until: LastDay, reasonId: Resignation, comments: "Moving to another city. Exit interview done."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal(LastDay, saved.EmployedUntil);
        Assert.Equal(Resignation, saved.LeavingReasonId);
        Assert.Equal("Moving to another city. Exit interview done.", saved.LeaverComments);

        // And it comes back out again — a leaver record that only survives in the table is no answer
        // to "why did they leave?".
        var row = await Row(client, Ada);
        Assert.Equal(LastDay.ToString("yyyy-MM-dd"), row.GetProperty("employedUntil").GetString());
        Assert.Equal(Resignation, row.GetProperty("leavingReasonId").GetGuid());
        Assert.Equal("Moving to another city. Exit interview done.", row.GetProperty("leaverComments").GetString());
    }

    [Fact]
    public async Task Clearing_the_leaving_date_clears_the_reason_and_the_comments()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        // Leave, then un-leave — the re-hire path.
        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(LastDay, Resignation, "Resigned."));
        var rehire = await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(until: null));

        Assert.Equal(HttpStatusCode.OK, rehire.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Null(saved.EmployedUntil);
        Assert.Null(saved.LeavingReasonId);
        Assert.Null(saved.LeaverComments);
        Assert.True(saved.IsEmployedOn(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    [Fact]
    public async Task A_reason_without_a_leaving_date_is_refused_rather_than_dropped()
    {
        // Accepting it and silently discarding the reason would tell the user they recorded something
        // they did not. Refusing says which of the two fields is missing.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{Ada}", Leaving(until: null, reasonId: Resignation));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("need a last day of employment", await response.Content.ReadAsStringAsync());
        Assert.Null(host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada).LeavingReasonId));
    }

    [Fact]
    public async Task Retiring_a_reason_leaves_the_records_that_used_it_readable()
    {
        // The whole point of IsActive over a delete. Ada left under "Redundancy"; the customer then
        // stops using that reason. Her record must still say why she left.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);

        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(LastDay, Redundancy, "Site closure."));
        Retire(host, Redundancy);

        var row = await Row(client, Ada);
        Assert.Equal(Redundancy, row.GetProperty("leavingReasonId").GetGuid());
        Assert.Equal("Site closure.", row.GetProperty("leaverComments").GetString());

        // And the reason itself is still resolvable by name, so a screen can render the label rather
        // than a bare id — it is retired, not deleted.
        var retired = await Reasons(client, includeRetired: true);
        var reason = Assert.Single(retired, r => r.GetProperty("id").GetGuid() == Redundancy);
        Assert.Equal("Redundancy", reason.GetProperty("name").GetString());
        Assert.False(reason.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_retired_reason_is_no_longer_offered()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        Assert.Equal(2, (await Reasons(client)).Count);

        Retire(host, Redundancy);

        var offered = await Reasons(client);
        Assert.Equal(["Resignation"], offered.Select(r => r.GetProperty("name").GetString()!).ToArray());
    }

    [Fact]
    public async Task A_retired_reason_cannot_be_given_to_a_new_leaver()
    {
        // "No longer offered" has to mean the API refuses it, not merely that a dropdown hides it —
        // otherwise the rule lives in the SPA and the Flutter app can still file under it.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);
        Retire(host, Redundancy);

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{Ada}", Leaving(LastDay, Redundancy, "Site closure."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("has been retired", await response.Content.ReadAsStringAsync());
        Assert.Null(host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada).EmployedUntil));
    }

    [Fact]
    public async Task An_edit_to_a_leaver_whose_reason_was_retired_still_saves()
    {
        // The other half of "does not orphan": if a retired reason made every record carrying it
        // unsaveable, retiring one would freeze those employees — a worse failure than the delete
        // IsActive replaces. The reason already on the record is always acceptable.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(LastDay, Redundancy, "Site closure."));
        Retire(host, Redundancy);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, "Corrected job title", SiteA, null,
            EmployedFrom: Hired, EmployedUntil: LastDay, IsSuspended: null,
            LeavingReasonId: Redundancy, LeaverComments: "Site closure."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal("Corrected job title", saved.JobTitle);
        Assert.Equal(Redundancy, saved.LeavingReasonId);
    }

    [Fact]
    public async Task The_portals_own_edit_body_leaves_the_leaver_record_intact()
    {
        // The reachable version of the test above, and the defect it did not catch. The modal has no
        // editor for the reason or the comments, so until this was fixed it simply omitted them from
        // the body — and `PUT` is a full replace. Correcting Ada's JOB TITLE returned 200 with her
        // leaving date intact and her reason and comments set to null: from her last day she read as
        // LEAVER with no reason, there is no audit store, and nothing could bring the answer back.
        //
        // The body below is the one `employees.component.ts:321-339` now builds, key for key and in
        // its own order, so this test fails if that payload stops carrying the two fields. It is the
        // only cover the client fix can have: the portal has no `.spec.ts` files at all.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);

        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(LastDay, Redundancy, "Site closure."));

        var response = await client.PutAsync($"/api/employees/{Ada}", PortalEditBody(
            jobTitle: "Shift supervisor", leavingReasonId: Redundancy, leaverComments: "Site closure."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal("Shift supervisor", saved.JobTitle);   // the edit the manager actually made
        Assert.Equal(LastDay, saved.EmployedUntil);
        Assert.Equal(Redundancy, saved.LeavingReasonId);
        Assert.Equal("Site closure.", saved.LeaverComments);

        // And the list still answers "why did they leave?", which is the question this portion exists
        // to answer — a reason surviving only in the table is no answer. Asked as at the day after her
        // last one, because the last day is inclusive and hers is still in the future: today she is
        // Active with a leaving date pencilled in, which is the ordinary shape of this edit.
        var row = await Row(client, Ada, employedOn: LastDay.AddDays(1));
        Assert.Equal((int)EmployeeStatus.Leaver, row.GetProperty("status").GetInt32());
        Assert.Equal(Redundancy, row.GetProperty("leavingReasonId").GetGuid());
        Assert.Equal("Site closure.", row.GetProperty("leaverComments").GetString());
    }

    [Fact]
    public async Task A_body_that_omits_the_leaver_fields_clears_them_because_the_PUT_is_a_full_replace()
    {
        // The server half of the test above, pinned deliberately rather than fixed. `PUT` replaces the
        // whole record — 003 P2b's post-image scope check depends on the post-image being the whole
        // record — so a client that omits a field is asking for it to be cleared, and merging instead
        // would make "un-leave by blanking the date" unexpressible. This test exists so the next
        // reader knows the clearing is the contract and the client is what had to change.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(LastDay, Redundancy, "Site closure."));

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, "Shift supervisor", SiteA, null,
            EmployedFrom: Hired, EmployedUntil: LastDay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal(LastDay, saved.EmployedUntil);   // the date was in the body, so it stayed
        Assert.Null(saved.LeavingReasonId);           // these were not, so they went
        Assert.Null(saved.LeaverComments);
    }

    [Fact]
    public async Task An_employment_window_that_ends_before_it_starts_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, null, SiteA, null,
            EmployedFrom: Hired, EmployedUntil: Hired.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot be before the first", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_leaving_date_before_the_STORED_start_is_refused_even_when_the_body_omits_the_start()
    {
        // The reachable version of the test above, and the one it did not cover. On a PUT an omitted
        // EmployedFrom means "leave it alone", so a body with only a leaving date has no start date
        // in it — and comparing the two REQUEST fields therefore compared null to something and let
        // it through with a 200. The SPA sends exactly this shape: employees.component.ts:320 posts
        // `employedFrom: this.form.employedFrom || null`, which is null whenever the field is blank.
        //
        // What used to be saved: Ada hired 2024-01-15, EmployedUntil 2023-06-30. IsEmployedOn is
        // false for every date in existence including her own first day, the list reads Leaver
        // forever, and after 007 P2 she can never punch again — with no error anywhere.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, null, SiteA, null,
            EmployedFrom: null, EmployedUntil: new DateOnly(2023, 6, 30)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot be before the first", await response.Content.ReadAsStringAsync());

        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Null(saved.EmployedUntil);
        Assert.Equal(Hired, saved.EmployedFrom);
        Assert.True(saved.IsEmployedOn(Hired));
    }

    [Fact]
    public async Task A_leaving_date_after_the_stored_start_still_saves_when_the_body_omits_the_start()
    {
        // The other half: the fix must not refuse the legitimate shape it now inspects, which is the
        // ordinary "record that she leaves on the 30th" edit.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, null, SiteA, null,
            EmployedFrom: null, EmployedUntil: LastDay));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Equal(Hired, saved.EmployedFrom); // untouched, as an omitted field must be
        Assert.Equal(LastDay, saved.EmployedUntil);
    }

    [Fact]
    public async Task A_create_whose_leaving_date_precedes_the_defaulted_start_is_refused()
    {
        // Same hole on POST, one step further along: an omitted EmployedFrom defaults to today, and
        // the default was applied AFTER the guard had already skipped the comparison. So a create
        // with a past leaving date and no start produced a record that was never employed.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", new EmployeeUpsertRequest(
            "E2002", "Grace", "Hopper", null, null, null, SiteA, null,
            EmployedFrom: null, EmployedUntil: new DateOnly(2020, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cannot be before the first", await response.Content.ReadAsStringAsync());
        Assert.False(host.Read(db => db.Employees.AsNoTracking().Any(e => e.Code == "E2002")));
    }

    [Fact]
    public async Task A_leaving_reason_that_does_not_exist_is_refused()
    {
        // Nothing pinned this branch before, so deleting it left the suite green — and the failure it
        // would then produce is not a 400 but a 500: Postgres raises 23503 on the foreign key, which
        // the upsert's catch clauses only learned to handle alongside this test.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);
        var noSuchReason = Guid.Parse("cccccccc-0000-0000-0000-00000000dead");

        var response = await client.PutAsJsonAsync(
            $"/api/employees/{Ada}", Leaving(LastDay, noSuchReason, "Left."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());

        // And nothing was written — the refusal happens before the leaver record is applied.
        var saved = host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));
        Assert.Null(saved.EmployedUntil);
        Assert.Null(saved.LeavingReasonId);
    }

    [Fact]
    public async Task A_create_naming_a_leaving_reason_that_does_not_exist_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await client.PostAsJsonAsync("/api/employees", new EmployeeUpsertRequest(
            "E2003", "Grace", "Hopper", null, null, null, SiteA, null,
            EmployedFrom: Hired, EmployedUntil: LastDay,
            IsSuspended: null, LeavingReasonId: Guid.Parse("cccccccc-0000-0000-0000-00000000dead")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());
        Assert.False(host.Read(db => db.Employees.AsNoTracking().Any(e => e.Code == "E2003")));
    }

    [Fact]
    public async Task The_offered_reasons_can_be_read_with_no_query_string_at_all()
    {
        // `bool includeRetired` with no default made minimal-API binding treat it as REQUIRED, so
        // this exact call — the one the shipped SPA makes (workforce.api.ts:113-114) — was a 400.
        // The suite missed it because every other test here appends the parameter.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesView);

        var response = await client.GetAsync("/api/leaving-reasons");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reasons = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["Redundancy", "Resignation"],
            reasons.EnumerateArray().Select(r => r.GetProperty("name").GetString()!).Order().ToArray());

        // Omitting it means the offered list, not everything: a retired reason must not reappear
        // simply because the caller said nothing.
        Retire(host, Redundancy);
        var afterRetiring = await client.GetFromJsonAsync<JsonElement>("/api/leaving-reasons");
        Assert.Equal(
            ["Resignation"],
            afterRetiring.EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToArray());
    }

    [Fact]
    public async Task The_list_derives_the_status_at_the_date_it_is_asked_about()
    {
        // The same row is a leaver today and was active in March. A stored status can only ever answer
        // one of those, which is the regression this portion removes.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);
        await client.PutAsJsonAsync($"/api/employees/{Ada}", Leaving(
            until: new DateOnly(2026, 6, 30), reasonId: Resignation, comments: "Resigned."));

        var asAtToday = await Row(client, Ada, employedOn: new DateOnly(2026, 8, 14));
        Assert.Equal((int)EmployeeStatus.Leaver, asAtToday.GetProperty("status").GetInt32());
        Assert.False(asAtToday.GetProperty("isEmployed").GetBoolean());

        var asAtMarch = await Row(client, Ada, employedOn: new DateOnly(2026, 3, 3));
        Assert.Equal((int)EmployeeStatus.Active, asAtMarch.GetProperty("status").GetInt32());
        Assert.True(asAtMarch.GetProperty("isEmployed").GetBoolean());
    }

    [Fact]
    public async Task A_suspended_employee_reads_as_suspended_rather_than_as_a_leaver()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);

        var response = await client.PutAsJsonAsync($"/api/employees/{Ada}", new EmployeeUpsertRequest(
            "E1001", "Ada", "Lovelace", null, null, null, SiteA, null,
            EmployedFrom: Hired, EmployedUntil: null, IsSuspended: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await Row(client, Ada);
        Assert.Equal((int)EmployeeStatus.Suspended, row.GetProperty("status").GetInt32());
        Assert.False(row.GetProperty("isEmployed").GetBoolean());
        // Suspension is not a leaving date, and must not have invented one.
        Assert.Null(host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada).EmployedUntil));
    }

    /// <summary>
    /// The body the employee modal sends when a manager saves an edit — written out by hand rather
    /// than serialised from <see cref="EmployeeUpsertRequest"/>, because the point is to carry the
    /// SPA's own keys in the SPA's own order (<c>employees.component.ts:321-339</c>). A field the SPA
    /// stops sending is a field this test stops sending.
    /// <para>
    /// <c>phone</c> and <c>departmentId</c> are null because the modal has no editor for the first
    /// and hard-codes the second. Both are pre-existing on <c>master</c> and neither belongs to 007
    /// P1 — <c>departmentId</c> is <c>PHASE-AUDIT.md</c> B5, owned by 003 P3 and 007 P4.
    /// </para>
    /// </summary>
    private static StringContent PortalEditBody(string jobTitle, Guid leavingReasonId, string leaverComments) =>
        new($$"""
             {
               "code": "E1001",
               "firstName": "Ada",
               "lastName": "Lovelace",
               "email": null,
               "phone": null,
               "jobTitle": {{JsonSerializer.Serialize(jobTitle)}},
               "siteId": "{{SiteA}}",
               "departmentId": null,
               "employedFrom": "{{Hired:yyyy-MM-dd}}",
               "employedUntil": "{{LastDay:yyyy-MM-dd}}",
               "isSuspended": false,
               "leavingReasonId": "{{leavingReasonId}}",
               "leaverComments": {{JsonSerializer.Serialize(leaverComments)}}
             }
             """, Encoding.UTF8, "application/json");

    private static EmployeeUpsertRequest Leaving(DateOnly? until, Guid? reasonId = null, string? comments = null) =>
        new("E1001", "Ada", "Lovelace", null, null, null, SiteA, null,
            EmployedFrom: Hired, EmployedUntil: until, IsSuspended: null,
            LeavingReasonId: reasonId, LeaverComments: comments);

    /// <summary>Retires a reason the way an administration screen eventually will: <c>IsActive = false</c>.</summary>
    private static void Retire(PeopleEndpointHost host, Guid reasonId) =>
        host.Read(db =>
        {
            db.LeavingReasons.Single(r => r.Id == reasonId).IsActive = false;
            return db.SaveChanges();
        });

    private static async Task<JsonElement> Row(HttpClient client, Guid employeeId, DateOnly? employedOn = null)
    {
        var query = employedOn is { } on ? $"?employedOn={on:yyyy-MM-dd}" : "";
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/employees{query}");
        return page.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == employeeId);
    }

    private static async Task<List<JsonElement>> Reasons(HttpClient client, bool includeRetired = false)
    {
        var response = await client.GetFromJsonAsync<JsonElement>(
            $"/api/leaving-reasons?includeRetired={includeRetired}");
        return response.EnumerateArray().ToList();
    }

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = SiteA, Name = "North" });
        // Two reasons, seeded by the TEST rather than by the product: the product ships this table
        // empty, because "Resignation" and "Redundancy" are one customer's vocabulary.
        db.LeavingReasons.Add(new LeavingReason { Id = Resignation, Name = "Resignation" });
        db.LeavingReasons.Add(new LeavingReason { Id = Redundancy, Name = "Redundancy" });
        db.Employees.Add(new Employee
        {
            Id = Ada,
            Code = "E1001",
            FirstName = "Ada",
            LastName = "Lovelace",
            SiteId = SiteA,
            EmployedFrom = Hired,
        });
    }
}
