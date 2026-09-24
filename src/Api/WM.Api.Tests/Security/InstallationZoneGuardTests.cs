using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// Plan 008 P1: the installation zone is configuration, validated at composition. The shape is
/// <see cref="SigningKeyGuardTests"/>' — the guard throws while services register, so the host
/// dies with a readable line before it opens a database.
/// </summary>
public sealed class InstallationZoneGuardTests
{
    [Fact]
    public void A_host_refuses_to_compose_with_no_installation_zone()
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: new Dictionary<string, string?>
            {
                [InstallationZone.ConfigurationKey] = null,
            }));

        Assert.Contains(InstallationZone.ConfigurationKey, fault.Message, StringComparison.Ordinal);
        Assert.Contains("Time__InstallationZone", fault.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Europe/Nowhere")]
    [InlineData("not a zone at all")]
    [InlineData("Central European Standard Time")]
    public void A_host_refuses_to_compose_on_a_zone_that_is_not_real(string garbage)
    {
        var fault = Assert.Throws<InvalidOperationException>(() =>
            ApiTestHost.Compose(configurationOverrides: new Dictionary<string, string?>
            {
                [InstallationZone.ConfigurationKey] = garbage,
            }));

        Assert.Contains(InstallationZone.ConfigurationKey, fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_gets_no_fallback_either()
    {
        // No "UTC if unset" in any environment — that is legacy's fail-open, renamed. Blank rather
        // than removed: a removed key would fall through to appsettings.Development.json, which
        // is the next test's point.
        Assert.Throws<InvalidOperationException>(() => ApiTestHost.Compose(
            environmentName: Environments.Development,
            configurationOverrides: new Dictionary<string, string?>
            {
                [InstallationZone.ConfigurationKey] = string.Empty,
            }));
    }

    [Fact]
    public async Task A_valid_zone_composes_and_is_what_the_container_hands_out()
    {
        await using var host = ApiTestHost.Compose(configurationOverrides: new Dictionary<string, string?>
        {
            [InstallationZone.ConfigurationKey] = "Pacific/Auckland",
        });

        Assert.Equal("Pacific/Auckland", host.Services.GetRequiredService<InstallationZone>().Zone.Id);
        Assert.IsType<SystemClock>(host.Services.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task Development_composes_on_the_committed_development_file_alone()
    {
        // appsettings.Development.json is the whole answer for `dotnet run`.
        await using var host = ApiTestHost.Compose(
            environmentName: Environments.Development,
            configurationOverrides: new Dictionary<string, string?>
            {
                [InstallationZone.ConfigurationKey] = null,
                ["Jwt:SigningKey"] = null,
            });

        Assert.Equal("Europe/Ljubljana", host.Services.GetRequiredService<InstallationZone>().Zone.Id);
    }

    [Fact]
    public void The_file_that_ships_inside_the_image_chooses_no_zone()
    {
        // A zone in appsettings.json would be a default every install inherits without deciding —
        // "UTC means nobody decided" moved one file over.
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(path),
            new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });

        Assert.False(document.RootElement.TryGetProperty("Time", out _));
    }
}
