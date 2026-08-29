using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// Two managers, one employee, one form each. The second save is refused, not swallowed.
///
/// <para>
/// This is 011 finding F8, and it is the one place WM was measurably behind the product it replaces.
/// TLW <i>detects</i> this — full-column <c>UpdateCheck.Always</c> on 148 of <c>dbo.Employees</c>'
/// 153 columns — and then throws a <c>ChangeConflictException</c> that nothing in <c>Logic/</c>
/// catches, so the losing manager gets a crash. WM, before this portion, did not detect it at all:
/// last write won, silently, and the loser was never told. Detection without handling is not a
/// feature; it is a stack trace. Both halves are here.
/// </para>
/// <para>
/// <b>The mutation these tests exist to fail against</b>: remove <c>.IsConcurrencyToken()</c> from
/// <c>PeopleDbContext</c> and <see cref="The_second_of_two_edits_from_the_same_load_is_refused"/>
/// fails with B's surname on the record — which is WM's behaviour on <c>master</c>. Run it; a
/// concurrency test that cannot reproduce the defect is proving nothing.
/// </para>
/// <para>
/// <b>Why these run without Postgres.</b> The token is an explicit mapped column and the in-memory
/// provider enforces an explicit <c>IsConcurrencyToken()</c>. Had it been Npgsql's <c>xmin</c> —
/// a shadow property the storage engine populates — nothing in memory would ever write it, it would
/// sit at its default forever, and every test below would pass <i>vacuously</i>. That is why
/// <c>UseXminAsConcurrencyToken()</c> was rejected; see <c>AuditableEntity.Version</c>.
/// </para>
/// </summary>
public sealed class ConcurrentEditEndpointTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteB = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Invisible = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly DateOnly Hired = new(2024, 1, 15);

    [Fact]
    public async Task The_second_of_two_edits_from_the_same_load_is_refused()
    {
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        // Both managers open the record. One load, one token, two forms.
        var shared = host.VersionOf(Ada);

        var first = await Put(client, Ada, Edit("Lovelace-First", shared));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await Put(client, Ada, Edit("Lovelace-Second", shared));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(ConcurrentEdit.Message, await second.Content.ReadAsStringAsync());

        // The heart of it: the first manager's work is still there. On master this assertion reads
        // "Lovelace-Second" and nobody is told.
        Assert.Equal("Lovelace-First", Saved(host).LastName);
    }

    [Fact]
    public async Task A_refused_edit_leaves_every_field_of_the_record_alone()
    {
        // A full-replace PUT writes every column, so "the loser did not win" has to mean the whole
        // row, not just the field the assertion above happens to look at.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var shared = host.VersionOf(Ada);
        await Put(client, Ada, Edit("Lovelace-First", shared) with { JobTitle = "Shift supervisor" });

        var refused = await Put(client, Ada, Edit("Lovelace-Second", shared) with { JobTitle = "Analyst" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var saved = Saved(host);
        Assert.Equal("Lovelace-First", saved.LastName);
        Assert.Equal("Shift supervisor", saved.JobTitle);
    }

    [Fact]
    public async Task Reloading_after_a_conflict_lets_the_second_manager_save()
    {
        // The whole recovery path D4 leaves the user: reload, re-enter, save. If the token did not
        // move forward with the write, this would 409 for ever and the record would be bricked.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var shared = host.VersionOf(Ada);
        await Put(client, Ada, Edit("Lovelace-First", shared));
        await Put(client, Ada, Edit("Lovelace-Second", shared));

        var retried = await Put(client, Ada, Edit("Lovelace-Second", host.VersionOf(Ada)));

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Lovelace-Second", Saved(host).LastName);
    }

    [Fact]
    public async Task Two_managers_saving_the_same_value_still_collide_and_are_not_told_anything_was_lost()
    {
        // Edge case 6. A row version cannot tell "you overwrote something" from "you agreed", so the
        // second save is refused either way — and the message has to survive that without alarming
        // anyone. It says the record CHANGED, never that anything was lost.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var shared = host.VersionOf(Ada);
        await Put(client, Ada, Edit("Lovelace", shared) with { Phone = "0100 000 000" });
        var second = await Put(client, Ada, Edit("Lovelace", shared) with { Phone = "0100 000 000" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("reload and try again", body);
        Assert.DoesNotContain("lost", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0100 000 000", Saved(host).Phone);
    }

    [Fact]
    public async Task Disjoint_edits_from_the_same_load_still_collide()
    {
        // Edge case 5, and the argument for a row version over legacy's per-column check. A stale
        // full-replace PUT does not edit one field — it writes all of them — so "A edited phone,
        // B edited jobTitle" is not a merge opportunity, it is B reverting A's phone in silence.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var shared = host.VersionOf(Ada);
        await Put(client, Ada, Edit("Lovelace", shared) with { Phone = "0100 111 111" });
        var second = await Put(client, Ada, Edit("Lovelace", shared) with { JobTitle = "Analyst" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        // Which is the point: had it succeeded it would have written phone: null over A's number.
        Assert.Equal("0100 111 111", Saved(host).Phone);
    }

    [Fact]
    public async Task A_stale_edit_to_a_record_outside_the_callers_scope_is_404_and_not_409()
    {
        // Edge case 8 — the version token must not become a scope oracle. A 409 here would confirm
        // that this employee exists, which is exactly what the 404 on the read path refuses to do.
        // The scope check runs first, so a caller who cannot see the record learns nothing from a
        // token at all: right one or wrong one, the answer is the same 404.
        await using var host = await PeopleEndpointHost.StartAsync(SiteScope(SiteA), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var real = host.VersionOf(Invisible);
        var stale = Guid.CreateVersion7();

        var withStaleToken = await Put(client, Invisible, Edit("Probe", stale, siteId: SiteB));
        var withRealToken = await Put(client, Invisible, Edit("Probe", real, siteId: SiteB));

        Assert.Equal(HttpStatusCode.NotFound, withStaleToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withRealToken.StatusCode);
        Assert.Equal("South", host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Invisible)).LastName);
    }

    [Fact]
    public async Task An_edit_that_carries_no_version_is_refused_rather_than_waved_through()
    {
        // Defaulting a missing token to "whatever is stored" would restore last-write-wins for any
        // client that forgot it — the check would be bypassed in silence, which is the defect. A 400
        // rather than a 409: nothing was concurrent, the request was simply not one WM accepts.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage);

        var response = await Put(client, Ada, Edit("Lovelace-Second", version: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("did not carry a record version", await response.Content.ReadAsStringAsync());
        Assert.Equal("Lovelace", Saved(host).LastName);
    }

    [Fact]
    public async Task The_version_is_returned_on_read_and_changes_when_the_record_is_written()
    {
        // The other half of the round-trip: a token the client cannot read is a token it cannot
        // echo. Asserted on the LIST, because the list is what the employee drawer opens from.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.EmployeesManage, WmPermissions.EmployeesView);

        var before = await ListedVersion(client, Ada);
        Assert.NotEqual(Guid.Empty, before);
        Assert.Equal(host.VersionOf(Ada), before);

        await Put(client, Ada, Edit("Lovelace-First", before));

        var after = await ListedVersion(client, Ada);
        Assert.NotEqual(before, after);

        // And the write echoes the new token back in its own response body, so a client that saves
        // twice without reloading does not have to re-fetch between saves.
        var response = await Put(client, Ada, Edit("Lovelace-Second", after));
        var written = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(host.VersionOf(Ada), written.GetProperty("version").GetGuid());
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, Guid id, EmployeeUpsertRequest request) =>
        client.PutAsJsonAsync($"/api/employees/{id}", request);

    /// <summary>A full-replace body carrying a token the test chooses — the point of the exercise.</summary>
    private static EmployeeUpsertRequest Edit(string lastName, Guid? version, Guid? siteId = null) =>
        new("E1001", "Ada", lastName, null, null, null, siteId ?? SiteA, null,
            EmployedFrom: Hired, Version: version);

    private static Employee Saved(PeopleEndpointHost host) =>
        host.Read(db => db.Employees.AsNoTracking().Single(e => e.Id == Ada));

    private static async Task<Guid> ListedVersion(HttpClient client, Guid employeeId)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/employees");
        return page.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == employeeId)
            .GetProperty("version").GetGuid();
    }

    private static EffectiveDataScope SiteScope(params Guid[] siteIds) =>
        new(DataScopeKind.Sites, new HashSet<Guid>(siteIds), new HashSet<Guid>(), null);

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = SiteA, Name = "North" });
        db.Sites.Add(new Site { Id = SiteB, Name = "South" });
        db.Employees.Add(new Employee
        {
            Id = Ada, Code = "E1001", FirstName = "Ada", LastName = "Lovelace",
            SiteId = SiteA, EmployedFrom = Hired,
        });
        // At site B, so a site-A caller cannot see her. She is the scope-oracle probe.
        db.Employees.Add(new Employee
        {
            Id = Invisible, Code = "E1002", FirstName = "Grace", LastName = "South",
            SiteId = SiteB, EmployedFrom = Hired,
        });
    }
}
