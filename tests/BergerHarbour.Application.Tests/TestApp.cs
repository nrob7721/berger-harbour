using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Seeding;
using BergerHarbour.Application.Tests.Fakes;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.DependencyInjection;

namespace BergerHarbour.Application.Tests;

/// <summary>The application wired to in-memory fakes, seeded, with a controllable clock.</summary>
public sealed class TestApp
{
    /// <summary>Monday 5 October 2026, 11:00 in Sydney.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private TestApp(IServiceProvider services, FakeClock clock, InMemoryStore store, FakeEmailSender emails,
        FakePaymentGateway gateway, FakeTurnstile turnstile)
    {
        Services = services;
        Clock = clock;
        Store = store;
        Emails = emails;
        Gateway = gateway;
        Turnstile = turnstile;
    }

    public IServiceProvider Services { get; }
    public FakeClock Clock { get; }
    public InMemoryStore Store { get; }
    public FakeEmailSender Emails { get; }
    public FakePaymentGateway Gateway { get; }
    public FakeTurnstile Turnstile { get; }

    public static readonly BoatId PacificBlue = new("pacific-blue");

    public static async Task<TestApp> CreateAsync(bool online = true, NotificationOptions? notifications = null,
        DateTimeOffset? now = null)
    {
        var clock = new FakeClock(now ?? DefaultNow);
        var store = new InMemoryStore();
        var emails = new FakeEmailSender();
        var gateway = new FakePaymentGateway();
        var turnstile = new FakeTurnstile();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBergerHarbourApplication();
        services.Configure<NotificationOptions>(o =>
        {
            var n = notifications ?? new NotificationOptions();
            o.BalanceDueBeforeHire = n.BalanceDueBeforeHire;
            o.ExtendedFirstInstalmentBeforeHire = n.ExtendedFirstInstalmentBeforeHire;
            o.ExtendedFinalInstalmentBeforeHire = n.ExtendedFinalInstalmentBeforeHire;
            o.PaymentReminderBeforeDue = n.PaymentReminderBeforeDue;
            o.PreHireInstructionsBeforeHire = n.PreHireInstructionsBeforeHire;
        });
        services.Configure<BookingOptions>(_ => { });
        services.Configure<PaymentLinkOptions>(o =>
        {
            o.PublicBookingBaseUrl = "https://book.example.test";
            o.Secret = new string('s', 40);
        });
        services.AddSingleton<IClock>(clock);
        services.AddSingleton(store);
        services.AddSingleton<IEmailSender>(emails);
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddSingleton<ITurnstileVerifier>(turnstile);
        services.AddSingleton<IBookingReferenceGenerator, FakeReferenceGenerator>();
        services.AddSingleton<InMemoryBookingRepository>();
        services.AddSingleton<IBookingRepository>(sp => sp.GetRequiredService<InMemoryBookingRepository>());
        services.AddSingleton<InMemoryUnavailabilityRepository>();
        services.AddSingleton<IBoatUnavailabilityRepository>(sp => sp.GetRequiredService<InMemoryUnavailabilityRepository>());
        services.AddSingleton<IBoatRepository, InMemoryBoatRepository>();
        services.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();
        services.AddSingleton<ISeasonRepository, InMemorySeasonRepository>();
        services.AddSingleton<IBlockedPeriodRepository, InMemoryBlockedPeriodRepository>();
        services.AddSingleton<IAddonDefinitionRepository, InMemoryAddonRepository>();
        services.AddSingleton<IEmailTemplateRepository, InMemoryEmailTemplateRepository>();
        services.AddSingleton<IBusinessSettingsRepository, InMemoryBusinessSettingsRepository>();
        services.AddSingleton<IBoatScheduleLock, InMemoryBoatScheduleLock>();
        if (online)
        {
            services.AddSingleton<IBookingRequestValidationStrategy, OnlineBookingValidationStrategy>();
        }
        else
        {
            services.AddSingleton<IBookingRequestValidationStrategy, InternalBookingValidationStrategy>();
        }

        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SeedService>()
            .RunAsync(new SeedService.SeedOptions("staff@example.test", "02 4444 4444", "info@example.test"));
        return new TestApp(provider, clock, store, emails, gateway, turnstile);
    }

    public T Get<T>() where T : notnull => Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<Booking> BookingAsync(string reference) =>
        (await Get<IBookingRepository>().GetByReferenceAsync(BookingReference.Parse(reference)))!;

    public DateOnly Today => SydneyTime.Today(Clock.UtcNow);

    /// <summary>The first Monday at least <paramref name="days"/> days from today.</summary>
    public DateOnly MondayAfter(int days)
    {
        var d = Today.AddDays(days);
        while (d.DayOfWeek != DayOfWeek.Monday)
        {
            d = d.AddDays(1);
        }

        return d;
    }
}
