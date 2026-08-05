using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using WM.Modules.Identity.Services;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// 006 P2's first half: the <c>wm-api</c> image used to carry a working <c>Jwt:SigningKey</c>, and
/// nothing refused to boot on it — anyone holding this repository, or an image built from it,
/// could mint an administrator token.
///
/// These assertions are about <em>composition</em>, not about a request: the guard throws while
/// the modules register, which is before <c>Program.cs</c> opens a connection to migrate. A
/// container missing its key therefore dies immediately with a readable message instead of after
/// a database timeout, and that ordering is exactly what <see cref="ApiTestHost.Compose"/>
/// exercises here.
/// </summary>
public sealed class SigningKeyGuardTests
{
    [Fact]
    public void A_production_host_refuses_to_compose_with_no_signing_key()
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: Without("Jwt:SigningKey")));

        // Names the setting and the environment variable that fixes it — one line, not a stack
        // trace to read.
        Assert.Contains("Jwt:SigningKey", fault.Message, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_production_host_refuses_when_the_deployment_configures_nothing_under_Jwt()
    {
        // Whatever a deployment leaves unset, appsettings.json still supplies an issuer and an
        // audience — it is the key that has no default any more, so this is the shape a container
        // started with no Jwt__* variables at all actually takes.
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: Without(
                "Jwt:Issuer", "Jwt:Audience", "Jwt:SigningKey")));

        Assert.Contains("Jwt__SigningKey", fault.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("thirty-one-bytes-is-not-enough!")]
    public void A_production_host_refuses_a_signing_key_shorter_than_32_bytes(string tooShort)
    {
        Assert.True(Encoding.UTF8.GetByteCount(tooShort) < JwtOptions.MinimumSigningKeyBytes);

        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: With("Jwt:SigningKey", tooShort)));

        Assert.Contains("32 bytes", fault.Message, StringComparison.Ordinal);
        // The message reaches the log of a host that is about to crash-loop. It may say what the
        // rule is; it may not say what the key is.
        Assert.DoesNotContain(tooShort, fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exactly_thirty_two_bytes_is_enough()
    {
        await using var host = ApiTestHost.Compose(
            configurationOverrides: With("Jwt:SigningKey", new string('k', 32)));

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public async Task The_minimum_is_measured_in_bytes_and_not_in_characters()
    {
        // 20 characters, 40 UTF-8 bytes. It passes, because bytes are what HMAC-SHA256 consumes —
        // the same Encoding.UTF8.GetBytes the token service signs with. Counting characters would
        // reject this key while accepting genuinely weaker ASCII ones.
        // U+00E9 by code point rather than as a character: a literal whose byte count is the
        // whole point of the test should not depend on how this file gets decoded.
        var twentyTwoByteCharacters = new string((char)0x00E9, 20);
        Assert.Equal(20, twentyTwoByteCharacters.Length);
        Assert.Equal(40, Encoding.UTF8.GetByteCount(twentyTwoByteCharacters));

        await using var host = ApiTestHost.Compose(
            configurationOverrides: With("Jwt:SigningKey", twentyTwoByteCharacters));

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public void A_production_host_refuses_every_signing_key_this_repository_publishes()
    {
        // The retired literal from appsettings.json is the one that matters historically; the
        // development file's replacement is listed so that copying that value onto a server is
        // refused too. Both are asserted, so dropping either from the block list fails here.
        Assert.NotEmpty(JwtOptions.PublishedSigningKeys);

        foreach (var published in JwtOptions.PublishedSigningKeys)
        {
            var fault = Assert.Throws<InvalidOperationException>(() =>
                ApiTestHost.Compose(configurationOverrides: With("Jwt:SigningKey", published)));

            Assert.Contains("published", fault.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(published, fault.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_key_committed_for_development_is_one_a_production_host_refuses()
    {
        // Without this, someone edits appsettings.Development.json, the block list silently goes
        // stale, and the committed key becomes usable in Production again.
        var developmentKey = SettingIn("appsettings.Development.json", "Jwt", "SigningKey");

        Assert.False(string.IsNullOrWhiteSpace(developmentKey));
        Assert.Contains(developmentKey!, JwtOptions.PublishedSigningKeys, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Jwt", "SigningKey")]
    [InlineData("Bootstrap", "AdminPassword")]
    public void The_file_that_ships_inside_the_image_carries_no_credential(
        string section, string key)
    {
        // .dockerignore excludes appsettings.Development.json and nothing else under WM.Api, so
        // whatever appsettings.json holds is inside every image built from this repository. Both
        // of these used to be in it, and both were working values.
        Assert.Null(SettingIn("appsettings.json", section, key));
    }

    [Fact]
    public async Task A_production_host_composes_on_a_key_of_its_own()
    {
        // The guard has to let a real deployment through, or it is only an outage.
        await using var host = ApiTestHost.Compose();

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public async Task Development_still_works_with_nothing_configured()
    {
        // The risk this portion carries is breaking every developer at once. No signing key in
        // the environment and none in memory: appsettings.Development.json is the whole answer,
        // which is what `dotnet run` relies on.
        await using var host = ApiTestHost.Compose(
            environmentName: Environments.Development,
            configurationOverrides: Without("Jwt:SigningKey"));

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public async Task Development_accepts_a_published_key()
    {
        await using var host = ApiTestHost.Compose(
            environmentName: Environments.Development,
            configurationOverrides: With("Jwt:SigningKey", JwtOptions.PublishedSigningKeys[0]));

        Assert.NotEmpty(host.Endpoints);
    }

    [Fact]
    public void Development_still_needs_a_key_that_could_sign_something()
    {
        // "Untouched" does not stretch to a key HMAC-SHA256 cannot use: that one fails at the
        // first sign-in with IDX10653 whatever the environment, so it fails at boot instead.
        Assert.Throws<InvalidOperationException>(() => ApiTestHost.Compose(
            environmentName: Environments.Development,
            configurationOverrides: With("Jwt:SigningKey", "tiny")));
    }

    private static Dictionary<string, string?> With(string key, string value) =>
        new() { [key] = value };

    private static Dictionary<string, string?> Without(params string[] keys) =>
        keys.ToDictionary(key => key, _ => (string?)null);

    /// <summary>
    /// Reads the settings file the host itself would read — the copy next to the test assembly,
    /// which is the one <c>WM.Api</c> produced and therefore the one that would be published.
    /// </summary>
    private static string? SettingIn(string fileName, string section, string key)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        Assert.True(File.Exists(path), $"{fileName} is not next to the test assembly.");

        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        return document.RootElement.TryGetProperty(section, out var node)
               && node.TryGetProperty(key, out var value)
            ? value.GetString()
            : null;
    }
}
