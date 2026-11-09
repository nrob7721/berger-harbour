using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Seed;
using BergerHarbour.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddBergerApi(ApiKind.Admin);
builder.Services.AddSingleton<IBookingRequestValidationStrategy, InternalBookingValidationStrategy>();

var app = builder.Build();

// `dotnet run -- seed` runs the setup seed and exits; Local also seeds the emulator on every start (idempotent).
var seedOnly = args.Contains("seed", StringComparer.OrdinalIgnoreCase);
if ((seedOnly || StartupConfiguration.IsLocal(app.Environment)) && !StartupConfiguration.IsGeneratingOpenApiDocument)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync(new SeedOptions(
        app.Configuration[ConfigKeys.SeedStaffAlertEmail], app.Configuration[ConfigKeys.SeedContactPhone], app.Configuration[ConfigKeys.SeedContactEmail]));
    if (seedOnly)
    {
        return;
    }
}

app.UseBergerApi(ApiKind.Admin);
app.Run();

/// <summary>The admin API entry point (staff, behind Cloudflare Access, and the jobs endpoint).</summary>
public partial class Program;
