using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Seeding;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Infrastructure.Firestore;
using EmulatorDetection = Google.Api.Gax.EmulatorDetection;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BergerHarbour.Infrastructure.IntegrationTests;

/// <summary>
/// Connects to the Firestore emulator (FIRESTORE_EMULATOR_HOST, default 127.0.0.1:8080). Each fixture uses its own
/// project id so test classes are isolated from each other.
/// </summary>
public sealed class EmulatorFixture
{
    public EmulatorFixture()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
        {
            Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", "127.0.0.1:8080");
        }

        ProjectId = $"it-{Guid.NewGuid():N}"[..20];
        Db = new FirestoreDbBuilder { ProjectId = ProjectId, EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    }

    public string ProjectId { get; }

    public FirestoreDb Db { get; }

    public async Task SeedAsync(IClock clock)
    {
        var seed = new SeedService(new FirestoreBusinessSettingsRepository(Db), new FirestoreSeasonRepository(Db),
            new FirestoreBlockedPeriodRepository(Db), new FirestoreAddonDefinitionRepository(Db),
            new FirestoreEmailTemplateRepository(Db), new FirestoreBoatRepository(Db), clock, NullLogger<SeedService>.Instance);
        await seed.RunAsync(new SeedService.SeedOptions("staff@example.test", "02 4444 4444", "info@example.test"));
    }
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal static class Make
{
    private static int _sequence;

    public static Booking StaffBooking(BoatId boat, DateOnly start, int nights = 4, bool standby = false, DateTimeOffset? now = null) =>
        Booking.CreateByStaff(BookingReference.Create(2026, 5000 + Interlocked.Increment(ref _sequence)), CustomerId.New(), boat,
            PeriodType.Custom, new Stay(start, start.AddDays(nights)), 4, standby, null, 4060m, 1000m,
            PaymentSchedule.Standard, "hash-" + Guid.NewGuid().ToString("N"), now ?? DateTimeOffset.UtcNow);
}
