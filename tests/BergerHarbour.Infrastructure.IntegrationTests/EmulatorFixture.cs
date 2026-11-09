using BergerHarbour.Application;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Seed;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure;
using Google.Cloud.Firestore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.IntegrationTests;

public sealed class RecordingEmailSender : IEmailSender
{
    public readonly List<EmailMessage> Sent = [];
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real Firestore infrastructure against the emulator (FIRESTORE_EMULATOR_HOST), seeded, in a project of its own so
/// tests never see each other's data.
/// </summary>
public sealed class EmulatorFixture
{
    public IServiceProvider Services { get; }
    public FirestoreDb Db => Services.GetRequiredService<FirestoreDb>();

    public EmulatorFixture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            throw new InvalidOperationException("Set FIRESTORE_EMULATOR_HOST (e.g. run under `firebase emulators:exec --only firestore`).");
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddFirestore("test-" + Guid.NewGuid().ToString("N")[..12]);
        s.AddSingleton<IEmailSender, RecordingEmailSender>();
        s.AddSingleton(Options.Create(new NotificationOptions()));
        s.AddSingleton(Options.Create(new BookingOptions()));
        s.AddSingleton(Options.Create(new AppUrlOptions { PublicBookingBaseUrl = "https://bookings.test" }));
        s.AddSingleton(new PaymentLinkTokens("integration-test-secret-integration-test"));
        s.AddApplicationCore();
        s.AddAdminUseCases();
        Services = s.BuildServiceProvider();
        Get<SeedService>().SeedAsync(new SeedOptions("staff@example.com", "02 0000 0000", "info@example.com")).GetAwaiter().GetResult();
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async Task<Boat> BoatAsync(string slug) => (await Get<IBoatRepository>().GetBySlugAsync(slug))!;

    public async Task<Boat> ActivateAsync(string slug)
    {
        var boat = await BoatAsync(slug);
        if (boat.IsActive) return boat;
        boat.Activate(await Get<Domain.Settings.ISeasonRepository>().ListAsync());
        await Get<IBoatRepository>().SaveAsync(boat);
        return boat;
    }

    /// <summary>A fresh scope's admin service: concurrent requests each get their own.</summary>
    public AdminBookingService Admin() => Services.CreateScope().ServiceProvider.GetRequiredService<AdminBookingService>();

    public static DateOnly D(string iso) => Dates.ParseIso(iso);

    public async Task<CreateStaffBookingRequest> StaffRequestAsync(string start, string end, string slug = "pacific-blue", string email = "john@example.com",
        IReadOnlyList<StaffAddonLineInput>? lines = null) =>
        new((await BoatAsync(slug)).Id.Value, PeriodType.Custom, D(start), D(end), 2, 0, "11:00", "08:00", false, null, null, 4000, email, "John",
            "Smith", null, null, lines ?? []);
}

[CollectionDefinition(Name)]
public sealed class EmulatorCollection : ICollectionFixture<EmulatorFixture>
{
    public const string Name = "emulator";
}
