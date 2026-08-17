using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WM.Modules.People.Data;
using WM.Modules.People.Domain;
using WM.SharedKernel.Security;
using Xunit;

namespace WM.Modules.People.Tests.Endpoints;

/// <summary>
/// <c>GET /api/me/employee</c> — the one endpoint whose reader is the <i>subject</i> of the record.
///
/// <para>
/// Which makes one field different in kind from the rest. <c>LeaverComments</c> is legacy's
/// <c>AdditionalLeaverComments</c>: HR's free text <i>about</i> the person — "poor timekeeping",
/// "would not re-hire", the exit-interview note — and returning the whole entity here put it in the
/// JSON their own browser receives. Excluded by the user's decision of 2026-08-15. Everything else
/// stays: the employment window and the leaving reason are facts about their own employment, and a
/// person is entitled to know when they were employed and under what reason they left.
/// </para>
///
/// <para>
/// This is a hard-coded exclusion at one endpoint, not a permission model. Field-group rights are
/// plan 004's job, and until they exist the comments remain readable through the list and detail
/// endpoints by anyone holding <c>employees.view</c>.
/// </para>
/// </summary>
public sealed class SelfEmployeeEndpointTests
{
    private static readonly Guid SiteA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Resignation = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private const string HrNote = "Repeated lateness; would not re-hire. Exit interview declined.";

    [Fact]
    public async Task The_subject_does_not_receive_HR_s_comments_about_them()
    {
        await using var host = await PeopleEndpointHost.StartAsync(
            EffectiveDataScope.All(), Seed, callerEmployeeId: Ada);
        var client = host.ClientWith(WmPermissions.SelfService);

        var response = await client.GetAsync("/api/me/employee");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Not merely null — absent. A null field would still tell a reader the concept exists, and
        // more to the point a projection that kept the property is one edit away from filling it.
        Assert.False(me.TryGetProperty("leaverComments", out _));
        // Belt and braces: the text itself is nowhere in the payload under any casing or name.
        Assert.DoesNotContain("would not re-hire", me.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_subject_still_receives_their_own_employment_facts()
    {
        // The exclusion must be one field wide. An employee who cannot see their own start date, or
        // that they are recorded as having left, has been handed a worse problem than the one this
        // fixes.
        await using var host = await PeopleEndpointHost.StartAsync(
            EffectiveDataScope.All(), Seed, callerEmployeeId: Ada);
        var client = host.ClientWith(WmPermissions.SelfService);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me/employee");

        Assert.Equal("E1001", me.GetProperty("code").GetString());
        Assert.Equal("2024-01-15", me.GetProperty("employedFrom").GetString());
        Assert.Equal("2026-09-30", me.GetProperty("employedUntil").GetString());
        Assert.Equal(Resignation, me.GetProperty("leavingReasonId").GetGuid());
        Assert.False(me.GetProperty("isSuspended").GetBoolean());
    }

    [Fact]
    public async Task An_account_with_no_employee_record_gets_a_404_rather_than_somebody_else_s()
    {
        // The id comes from the token and never from the request, so "no linked employee" has to end
        // the request rather than fall through to a lookup with an empty id.
        await using var host = await PeopleEndpointHost.StartAsync(EffectiveDataScope.All(), Seed);
        var client = host.ClientWith(WmPermissions.SelfService);

        var response = await client.GetAsync("/api/me/employee");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not linked to an employee", await response.Content.ReadAsStringAsync());
    }

    private static void Seed(PeopleDbContext db)
    {
        db.Sites.Add(new Site { Id = SiteA, Name = "North" });
        db.LeavingReasons.Add(new LeavingReason { Id = Resignation, Name = "Resignation" });
        db.Employees.Add(new Employee
        {
            Id = Ada,
            Code = "E1001",
            FirstName = "Ada",
            LastName = "Lovelace",
            SiteId = SiteA,
            EmployedFrom = new DateOnly(2024, 1, 15),
            EmployedUntil = new DateOnly(2026, 9, 30),
            LeavingReasonId = Resignation,
            LeaverComments = HrNote,
        });
    }
}
