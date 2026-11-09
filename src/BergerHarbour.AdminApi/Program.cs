using BergerHarbour.AdminApi;
using BergerHarbour.Application;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Seed;
using BergerHarbour.Infrastructure;
using BergerHarbour.Infrastructure.Cloudflare;
using BergerHarbour.Infrastructure.Google;
using BergerHarbour.Infrastructure.Web;

// `dotnet run --project src/BergerHarbour.AdminApi -- seed` seeds the database and exits (it only needs FIRESTORE_PROJECT_ID).
if (args.FirstOrDefault() == "seed")
{
    await SeedCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var local = builder.Environment.IsLocal();
var checks = new ApiHosting.StartupChecks(config, builder.Environment);

ApiHosting.ConfigureCommon(builder, "Berger Houseboats admin API", checks);
var proxySecret = checks.RequireOutsideLocal(ConfigKeys.EdgeProxySecret);

var devAdmin = local && !string.IsNullOrWhiteSpace(config[ConfigKeys.DevAdminEmail]);
var teamDomain = devAdmin ? config[ConfigKeys.CfAccessTeamDomain] ?? "" : checks.Require(ConfigKeys.CfAccessTeamDomain);
var audience = devAdmin ? config[ConfigKeys.CfAccessAud] ?? "" : checks.Require(ConfigKeys.CfAccessAud);
var allowed = devAdmin ? config[ConfigKeys.AdminAllowedEmails] ?? "" : checks.Require(ConfigKeys.AdminAllowedEmails);
builder.Services.Configure<AccessOptions>(o =>
{
    o.TeamDomain = teamDomain;
    o.Audience = audience;
    o.AllowedEmails = allowed;
});
var jobsBypass = local && bool.TryParse(config[ConfigKeys.DevJobsBypass], out var b) && b;
var schedulerEmail = jobsBypass ? "" : checks.Require(ConfigKeys.SchedulerServiceAccountEmail);
var schedulerAudience = jobsBypass ? "" : checks.Require(ConfigKeys.SchedulerAudience);
builder.Services.Configure<SchedulerOptions>(o =>
{
    o.ServiceAccountEmail = schedulerEmail;
    o.Audience = schedulerAudience;
});
builder.Services.AddHttpClient<AccessJwtValidator>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<SchedulerOidcValidator>();
builder.Services.AddSingleton<JobsAuthentication>();
builder.Services.AddAdminUseCases();

checks.ThrowIfAny();

var app = builder.Build();
ApiHosting.UseCommonPipeline(app, proxySecret, [JobsAuthentication.Path]);
app.UseMiddleware<AccessAuthenticationMiddleware>();
ApiHosting.MapCommonEndpoints(app);

if (local && !ApiHosting.IsOpenApiGeneration)
{
    // Local development seeds the emulator on startup.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync(SeedCommand.OptionsFrom(config));
}

app.Run();

public partial class Program;

internal static class SeedCommand
{
    public static SeedOptions OptionsFrom(IConfiguration config) =>
        new(config["SEED_STAFF_ALERT_EMAIL"], config["SEED_CONTACT_PHONE"], config["SEED_CONTACT_EMAIL"]);

    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());
        var projectId = builder.Configuration[ConfigKeys.FirestoreProjectId];
        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException($"{ConfigKeys.FirestoreProjectId} is required to seed.");
        builder.Services.AddFirestore(projectId);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddScoped<SeedService>();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync(OptionsFrom(builder.Configuration));
    }
}
