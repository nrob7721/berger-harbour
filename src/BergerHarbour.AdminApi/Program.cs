using BergerHarbour.AdminApi.Controllers;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Seeding;
using BergerHarbour.Infrastructure;
using BergerHarbour.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddBergerHarbourWeb(maxRequestBodyBytes: 1024 * 1024, defaultCorsOrigin: "https://admin.bergerhouseboats.com.au");
builder.Services.AddBergerHarbourInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

// Staff bookings only check dates, guests and the customer; the availability invariant still applies.
builder.Services.AddSingleton<IBookingRequestValidationStrategy, InternalBookingValidationStrategy>();

var app = builder.Build();

// `dotnet BergerHarbour.AdminApi.dll seed` runs the idempotent seed and exits.
if (args.Contains("seed"))
{
    using var scope = app.Services.CreateScope();
    var created = await scope.ServiceProvider.GetRequiredService<SeedService>().RunAsync(new SeedService.SeedOptions(
        app.Configuration["SEED_STAFF_ALERT_EMAIL"] ?? "bookings@bergerhouseboats.com.au",
        app.Configuration["SEED_CONTACT_PHONE"] ?? "(02) 0000 0000",
        app.Configuration["SEED_CONTACT_EMAIL"] ?? "bookings@bergerhouseboats.com.au"));
    Console.WriteLine($"Seed complete: {created} records created.");
    return;
}

app.UseBergerHarbourWeb(JobsController.Route);
app.AddCloudflareAccessGuard(JobsController.Route);
app.MapControllers();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("SEED_ON_STARTUP", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SeedService>().RunAsync(new SeedService.SeedOptions(
        "staff@example.test", "(02) 0000 0000", "bookings@example.test"));
}

app.Run();

public partial class Program;
