using BergerHarbour.Application;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Seed;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.IntegrationTests.Fakes;
using Google.Cloud.Firestore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.IntegrationTests.Support;

/// <summary>
/// The real Firestore infrastructure against the emulator (<c>FIRESTORE_EMULATOR_HOST</c>), seeded, in a project of its
/// own so test runs never see each other's data. Email, Stripe and Turnstile are fakes.
/// </summary>
public sealed class EmulatorFixture
{
    public EmulatorFixture()
    {
        RequireEmulator();
        ProjectId = "test-" + Guid.NewGuid().ToString("N")[..12];
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddFirestore(ProjectId);
        s.AddSingleton<IClock>(Clock);
        s.AddSingleton<IEmailSender>(Emails);
        s.AddSingleton<IPaymentGateway, StubPaymentGateway>();
        s.AddSingleton<ITurnstileVerifier, PassingTurnstileVerifier>();
        s.AddSingleton(Options.Create(new NotificationOptions()));
        s.AddSingleton(Options.Create(new BookingOptions()));
        s.AddSingleton(Options.Create(new PaymentLinkOptions { PublicBookingBaseUrl = "https://bookings.test", Secret = new string('x', 40) }));
        s.AddBergerApplication();
        s.AddSingleton<IBookingRequestValidationStrategy, InternalBookingValidationStrategy>();
        Services = s.BuildServiceProvider();
        Get<SeedService>().SeedAsync(new SeedOptions("staff@example.com", "02 0000 0000", "info@example.com")).GetAwaiter().GetResult();
    }

    public string ProjectId { get; }

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));

    public RecordingEmailSender Emails { get; } = new();

    public IServiceProvider Services { get; }

    public FirestoreDb Db => Get<FirestoreDb>();

    public static void RequireEmulator()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
        {
            throw new InvalidOperationException(
                "Set FIRESTORE_EMULATOR_HOST, e.g. run: firebase emulators:exec --only firestore --project demo-berger-harbour \"dotnet test ...\"");
        }
    }

    public static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>A fresh scope's staff service, as each concurrent request gets its own.</summary>
    public StaffBookingService Staff() => Services.CreateScope().ServiceProvider.GetRequiredService<StaffBookingService>();

    public async Task<Boat> BoatAsync(string slug) => (await Get<IBoatRepository>().GetBySlugAsync(slug))!;

    public async Task<CreateStaffBookingRequest> StaffRequestAsync(string start, string end, string slug = "pacific-blue", string email = "john@example.com",
        IReadOnlyList<StaffAddonLineInput>? lines = null, string lastName = "Smith") =>
        new((await BoatAsync(slug)).Id.Value, PeriodType.Custom, D(start), D(end), 2, 0, "11:00", "08:00", false, null, null, 4000, email, "John",
            lastName, null, null, null, lines ?? []);

    public async Task ActivateAsync(string slug)
    {
        var boat = await BoatAsync(slug);
        if (!boat.IsActive)
        {
            boat.Activate(await Get<Domain.Settings.ISeasonRepository>().ListAsync(), Clock.UtcNow);
            await Get<IBoatRepository>().SaveAsync(boat);
        }
    }
}
