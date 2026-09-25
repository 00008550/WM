using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WM.Modules.People.Contracts;
using WM.Modules.TimeAttendance;
using WM.SharedKernel.Events;
using WM.SharedKernel.Security;
using WM.SharedKernel.Time;
using Xunit;

namespace WM.Api.Tests.Security;

/// <summary>
/// 008 P5 review: the punch clock contract must be readable by an API client, not only by someone
/// reading C#. The generated OpenAPI document for <c>POST /api/punches</c> carries it.
/// </summary>
public sealed class PunchContractOpenApiTests
{
    [Fact]
    public async Task The_punch_post_documents_its_clock_contract_in_openapi()
    {
        var doc = await DocumentAsync();
        var post = doc.GetProperty("paths").GetProperty("/api/punches").GetProperty("post");
        var description = post.GetProperty("description").GetString()!;
        Assert.Contains("offset is required", description);
        Assert.Contains("`Late`", description);
        Assert.Contains("`OffsetMismatch`", description);
        Assert.Contains("`receivedAt`", description);
        Assert.Contains("more than 5 minutes ahead", description);

        var timestamp = doc.GetProperty("components").GetProperty("schemas")
            .GetProperty("RecordPunchRequest").GetProperty("properties").GetProperty("timestamp");
        Assert.Equal("date-time", timestamp.GetProperty("format").GetString());
        Assert.Contains("UTC offset", timestamp.GetProperty("description").GetString());
    }

    /// <summary>
    /// 022 P1: every punch read says which clock and which day. A Flutter client learns the fields
    /// from the document, so the response schemas must be published and must carry them.
    /// </summary>
    [Theory]
    [InlineData("RecentPunchEntry", "localDate", "localZone")]
    [InlineData("LivePresenceEntry", "sinceLocalDate", "sinceLocalZone")]
    [InlineData("TimesheetInterval", "inZone", "outZone")]
    public async Task Punch_reads_document_their_zone_and_day_in_openapi(string schema, string day, string zone)
    {
        var properties = (await DocumentAsync()).GetProperty("components").GetProperty("schemas")
            .GetProperty(schema).GetProperty("properties");
        Assert.True(properties.TryGetProperty(day, out _), $"{schema}.{day} is not documented");
        Assert.True(properties.TryGetProperty(zone, out _), $"{schema}.{zone} is not documented");
    }

    private static async Task<JsonElement> DocumentAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=unused" });

        new TimeAttendanceModule().RegisterServices(builder.Services, builder.Configuration);
        // Handler parameters must be known services to be inferred as such; nothing is resolved.
        builder.Services.AddScoped<IEmployeeDirectory>(_ => null!);
        builder.Services.AddScoped<ISiteTimeZones>(_ => null!);
        builder.Services.AddScoped<IEventStreamProducer>(_ => null!);
        builder.Services.AddScoped<ICurrentUser>(_ => null!);
        builder.Services.AddSingleton<IClock>(_ => null!);
        builder.Services.AddAuthorization();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        await using var app = builder.Build();
        app.UseSwagger();
        new TimeAttendanceModule().MapEndpoints(app);
        await app.StartAsync();

        return await app.GetTestClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
    }
}
