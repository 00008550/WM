using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// Which full-replace <c>PUT</c>s refuse a stale write, and which do not — stated, so that "not yet"
/// cannot be mistaken later for "considered and rejected".
///
/// <para>
/// <b>This is not the inventory test 009 P3 will build, and it does not pretend to be.</b> That one
/// enumerates the composed host's endpoints and fails when a new <c>PUT</c> appears un-covered;
/// this one is a hand-maintained list that fails only when someone edits it. 011 P5's <i>Done when</i>
/// asks that all four <c>PUT</c>s "carry the token or be named as deliberately exempt in the same
/// inventory test 009 P3 builds" — 009 P3 has not landed, so the four are recorded here in a shape
/// 009 P3 can absorb, and the weaker guarantee is said out loud rather than implied away.
/// </para>
/// <para>
/// The four are the ones <c>009-what-the-running-app-does.md:718-719</c> names, at
/// <c>EndpointAuthorizationInventoryTests.cs:85, 89, 93, 101</c>. Every one of them is a FULL
/// REPLACE: a stale form does not edit one field, it writes them all. That is why a row version is
/// the right granularity and legacy's per-column <c>UpdateCheck.Always</c> is not — there is no such
/// thing as a disjoint edit through a body that carries every column.
/// </para>
/// </summary>
public class ConcurrencyTokenInventoryTests
{
    private enum TokenStatus
    {
        /// <summary>Reads the caller's echoed version and answers 409 when it is stale.</summary>
        Enforced,

        /// <summary>Nothing to be stale against — see the reason on the row.</summary>
        NotApplicable,

        /// <summary>A real remaining gap, not a principled exemption. Named so it stays visible.</summary>
        Outstanding,
    }

    private static readonly (string Transport, TokenStatus Status, string Reason)[] FullReplacePuts =
    [
        ("PUT /api/employees/{id:guid}", TokenStatus.Enforced,
            "011 P5. PeopleModule forces the entity's original Version to the caller's echoed token, "
            + "so a stale body matches no row. The scope check runs FIRST, so an out-of-scope record "
            + "is 404 and never 409 — a 409 there would confirm the person exists."),

        ("PUT /api/users/{id:guid}", TokenStatus.Enforced,
            "011 P5. UserManagementService.UpdateAsync, same mechanism; the endpoint maps the "
            + "resulting Conflict flag to 409 rather than the 400 every other refusal uses."),

        ("PUT /api/users/{id:guid}/security-groups", TokenStatus.NotApplicable,
            "Deliberately exempt, and it is the one exemption on principle here. The body is a SET "
            + "OF GROUP IDS, not a record: there is no row with a version to be stale against, the "
            + "membership rows are a join table, and the last writer's set is by definition the "
            + "administrator's current intent. A token would have to be invented for it, and it "
            + "would be the User's — which would make an unrelated profile edit fail a membership "
            + "save for no reason a user could understand."),

        ("PUT /api/security-groups/{id:guid}", TokenStatus.Outstanding,
            "NOT exempt on principle — this endpoint has exactly the defect 011 F8 describes, and "
            + "two administrators editing one group's site list still collide silently. It is out of "
            + "011 P5's Touches, which names People and Identity's USER paths only, so it was left "
            + "rather than quietly expanded. SecurityGroup carries the Version COLUMN already (it is "
            + "an AuditableEntity) but IdentityDbContext deliberately does not configure it as a "
            + "concurrency token: enforcing it while the endpoint reads no echoed token could only "
            + "fire on an intra-request race, and SecurityGroupService has no answer for "
            + "DbUpdateConcurrencyException, so that would be a 500 instead of a 409. The remaining "
            + "work is three lines and a DTO field, and it is a follow-up, not a decision."),
    ];

    [Fact]
    public void Every_full_replace_put_has_a_stated_position_on_optimistic_concurrency()
    {
        Assert.Equal(4, FullReplacePuts.Length);
        Assert.All(FullReplacePuts, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason)));
        // A reason is required for every arm, but the two that do NOT refuse a stale write are the
        // ones a reader will want the argument for, so those are held to a length that cannot be
        // satisfied by "n/a".
        Assert.All(
            FullReplacePuts.Where(e => e.Status != TokenStatus.Enforced),
            entry => Assert.True(entry.Reason.Length > 120, entry.Transport));
    }

    [Fact]
    public void The_two_endpoints_011_P5_covers_are_the_two_that_enforce_a_token()
    {
        // Pins the portion's actual scope. If a later change wires security-groups up, this fails
        // and whoever did it updates the list — which is the entire purpose of writing it down.
        var enforced = FullReplacePuts
            .Where(e => e.Status == TokenStatus.Enforced)
            .Select(e => e.Transport)
            .ToArray();

        Assert.Equal(
            ["PUT /api/employees/{id:guid}", "PUT /api/users/{id:guid}"],
            enforced);
    }

    [Fact]
    public void Nothing_is_left_undecided()
    {
        // "Outstanding" is an acceptable answer; silence is not. A row added without a status would
        // default to Enforced and read as covered when it is not, so the enum has no default arm and
        // every value in the list is one somebody chose.
        Assert.DoesNotContain(FullReplacePuts, e => e.Transport.Length == 0);
        Assert.Single(FullReplacePuts, e => e.Status == TokenStatus.Outstanding);
    }
}
