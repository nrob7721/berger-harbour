using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Seed;
using BergerHarbour.Application.Settings;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Tests;

/// <summary>The application layer wired to in-memory fakes and the seed data. "Now" is Mon 09/11/2026 11am Sydney.</summary>
public sealed class Harness
{
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    public const string BaseUrl = "https://bookings.test";

    public FakeClock Clock { get; } = new(Start);
    public InMemoryDb Db { get; }
    public FakePaymentGateway Payments { get; } = new();
    public FakeEmailSender Emails { get; } = new();
    public FakeTurnstile Turnstile { get; } = new();
    public FakeReferenceGenerator References { get; } = new();
    public IServiceProvider Services { get; }
    public NotificationOptions NotificationOptions { get; } = new();
    public BookingOptions BookingOptions { get; } = new();

    public Harness()
    {
        Db = new InMemoryDb(Clock);
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IClock>(Clock);
        s.AddSingleton(Db);
        s.AddSingleton<IBoatRepository>(Db);
        s.AddSingleton<IBookingRepository>(Db);
        s.AddSingleton<IBookingDraftRepository>(Db);
        s.AddSingleton<ICustomerRepository>(Db);
        s.AddSingleton<IBoatUnavailabilityRepository>(Db);
        s.AddSingleton<ISeasonRepository>(Db);
        s.AddSingleton<IBlockedPeriodRepository>(Db);
        s.AddSingleton<IAddonDefinitionRepository>(Db);
        s.AddSingleton<IEmailTemplateRepository>(Db);
        s.AddSingleton<IBusinessSettingsRepository>(Db);
        s.AddSingleton<IBoatScheduleLock>(new InMemoryScheduleLock(Db));
        s.AddSingleton<IBookingReferenceGenerator>(References);
        s.AddSingleton<IPaymentGateway>(Payments);
        s.AddSingleton<IEmailSender>(Emails);
        s.AddSingleton<ITurnstileVerifier>(Turnstile);
        s.AddSingleton(Options.Create(NotificationOptions));
        s.AddSingleton(Options.Create(BookingOptions));
        s.AddSingleton(Options.Create(new AppUrlOptions { PublicBookingBaseUrl = BaseUrl }));
        s.AddSingleton(new PaymentLinkTokens("test-secret-test-secret-test-secret"));
        s.AddSingleton(sp => new PaymentScheduleService(NotificationOptions.ToScheduleOptions()));
        s.AddSingleton<ReferenceDataLoader>();
        s.AddSingleton<CustomerRegistry>();
        s.AddSingleton<BookingNotifier>();
        s.AddSingleton<PublicCatalogueService>();
        s.AddSingleton<PaymentLinkService>();
        s.AddSingleton<StripeWebhookService>();
        s.AddSingleton<CustomerAdminService>();
        s.AddSingleton<FleetService>();
        s.AddSingleton<SettingsService>();
        s.AddSingleton<NotificationJob>();
        s.AddSingleton<SeedService>();
        Services = s.BuildServiceProvider();
        Get<SeedService>().SeedAsync(new SeedOptions("staff@berger.test", "02 4455 6677", "info@berger.test")).GetAwaiter().GetResult();
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public OnlineBookingService Online =>
        ActivatorUtilities.CreateInstance<OnlineBookingService>(Services, new OnlineBookingValidationStrategy(Db));

    public AdminBookingService Admin =>
        ActivatorUtilities.CreateInstance<AdminBookingService>(Services, new InternalBookingValidationStrategy(Db));

    public Boat Boat(string slug) => Db.Boats.All.Single(b => b.Slug == slug);

    /// <summary>Activates a seeded boat (all seeded boats have complete data).</summary>
    public Boat Activate(string slug)
    {
        var boat = Boat(slug);
        boat.Activate(Db.Seasons.All);
        Db.Boats.Save(boat);
        return boat;
    }

    public static DateOnly D(string iso) => Dates.ParseIso(iso);

    // ----- Online flow helpers -----

    public async Task<string> CreateDraftAsync(string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek,
        string email = "jane@example.com", string lastName = "Smith", int adults = 2, int children = 0) =>
        (await Online.CreateDraftAsync(new CreateDraftRequest(slug, type, D(start), adults, children, "Jane", lastName, email, "0412 345 678", "ok"), null)).DraftId;

    public CreateBookingRequest BookingRequest(string draftId, string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek,
        string email = "jane@example.com", string lastName = "Smith", IReadOnlyList<AddonRequestDto>? addons = null, int adults = 2, int children = 0) =>
        new(draftId, slug, type, D(start), adults, children, "Jane", lastName, email, "0412 345 678", addons ?? [], false, null, true);

    /// <summary>Draft → hold → paid deposit webhook. Returns the booking.</summary>
    public async Task<Booking> BookOnlineAsync(string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek,
        string email = "jane@example.com", string lastName = "Smith", IReadOnlyList<AddonRequestDto>? addons = null)
    {
        var draftId = await CreateDraftAsync(slug, start, type, email, lastName);
        var created = await Online.CreateBookingAsync(BookingRequest(draftId, slug, start, type, email, lastName, addons));
        await PayDepositAsync(created.BookingId);
        return Db.Bookings.Get(created.BookingId)!;
    }

    public async Task PayDepositAsync(string bookingId, string paymentIntent = "pi_deposit")
    {
        var b = Db.Bookings.Get(bookingId)!;
        Payments.SessionStatus[b.StripeCheckoutSessionId!] = "complete";
        await Get<StripeWebhookService>().HandleCompletedAsync(new CheckoutCompleted(b.StripeCheckoutSessionId!, bookingId, CheckoutPurpose.Deposit,
            paymentIntent + bookingId, PaymentScheduleService.CheckoutAmount(b.DepositAmount, b.HirePrice), Clock.UtcNow));
    }

    // ----- Staff helpers -----

    public CreateStaffBookingRequest StaffRequest(string start, string end, string? boatSlug = "pacific-blue", PeriodType type = PeriodType.Custom,
        string email = "john@example.com", string lastName = "Smith", bool standby = false, decimal hire = 4000,
        IReadOnlyList<StaffAddonLineInput>? lines = null, string? customerComments = null, int adults = 2, string? address = null) =>
        new(Boat(boatSlug!).Id.Value, type, D(start), D(end), adults, 0, "11:00", "08:00", standby, null, customerComments, hire, email, "John",
            lastName, "0412 000 111", null, address, lines ?? []);

    public UpdateStaffBookingRequest UpdateRequest(AdminBookingDetail d, Func<UpdateStaffBookingRequest, UpdateStaffBookingRequest>? change = null)
    {
        var r = new UpdateStaffBookingRequest(d.BoatId, d.PeriodType, d.StartDate, d.EndDate, d.NumberOfAdults, d.NumberOfChildren, d.CheckInTime,
            d.CheckOutTime, d.IsStandby, d.BookingComments, d.Customer?.Comments, d.HirePrice, d.Status, d.Customer?.Email, d.Customer?.FirstName,
            d.Customer?.LastName, d.Customer?.Mobile, d.Customer?.SecondaryMobile, d.Customer?.Address,
            d.AddonLines.Select(l => new StaffAddonLineInput(l.Id, l.AddonId, l.Quantity, l.UnitPrice)).ToList(), d.Version);
        return change is null ? r : change(r);
    }
}
