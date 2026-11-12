using System.Globalization;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.BookingDrafts;
using BergerHarbour.Application.BookingDrafts.Dtos;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Seed;
using BergerHarbour.Application.Tests.Fakes;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Tests.Support;

/// <summary>The application layer over in-memory fakes and the seed data. "Now" is Mon 09/11/2026 11am Sydney.</summary>
public sealed class ApplicationHarness
{
    public const string StaffEmail = "staff@berger.test";
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    public ApplicationHarness(Action<BookingOptions>? booking = null, Action<NotificationOptions>? notifications = null)
    {
        booking?.Invoke(BookingOptions);
        notifications?.Invoke(NotificationOptions);
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton(Db);
        s.AddSingleton<IClock>(Clock);
        s.AddSingleton<IBoatRepository, InMemoryBoatRepository>();
        s.AddSingleton<IBookingRepository, InMemoryBookingRepository>();
        s.AddSingleton<IBookingDraftRepository, InMemoryBookingDraftRepository>();
        s.AddSingleton<ICustomerRepository, InMemoryCustomerRepository>();
        s.AddSingleton<IBoatUnavailabilityRepository, InMemoryBoatUnavailabilityRepository>();
        s.AddSingleton<ISeasonRepository, InMemorySeasonRepository>();
        s.AddSingleton<IBlockedPeriodRepository, InMemoryBlockedPeriodRepository>();
        s.AddSingleton<IAddonDefinitionRepository, InMemoryAddonDefinitionRepository>();
        s.AddSingleton<IEmailTemplateRepository, InMemoryEmailTemplateRepository>();
        s.AddSingleton<IBusinessSettingsRepository, InMemoryBusinessSettingsRepository>();
        s.AddSingleton<IBoatScheduleLock, InMemoryBoatScheduleLock>();
        s.AddSingleton<IBookingReferenceGenerator>(References);
        s.AddSingleton<IPaymentGateway>(Payments);
        s.AddSingleton<IEmailSender>(Emails);
        s.AddSingleton<ITurnstileVerifier>(Turnstile);
        s.AddSingleton(Options.Create(BookingOptions));
        s.AddSingleton(Options.Create(NotificationOptions));
        s.AddSingleton(Options.Create(new PaymentLinkOptions { PublicBookingBaseUrl = "https://bookings.test", Secret = new string('s', 40) }));
        s.AddBergerApplication();
        s.AddSingleton<IBookingRequestValidationStrategy, OnlineBookingValidationStrategy>();
        Services = s.BuildServiceProvider();
        Get<SeedService>().SeedAsync(new SeedOptions(StaffEmail, "02 4455 6677", "info@berger.test")).GetAwaiter().GetResult();
    }

    public InMemoryDatabase Db { get; } = new();

    public FakeClock Clock { get; } = new(Start);

    public FakePaymentGateway Payments { get; } = new();

    public FakeEmailSender Emails { get; } = new();

    public FakeTurnstileVerifier Turnstile { get; } = new();

    public FakeBookingReferenceGenerator References { get; } = new();

    public BookingOptions BookingOptions { get; } = new();

    public NotificationOptions NotificationOptions { get; } = new();

    public IServiceProvider Services { get; }

    public OnlineBookingService Online => Get<OnlineBookingService>();

    public StaffBookingService Staff => ActivatorUtilities.CreateInstance<StaffBookingService>(Services, new InternalBookingValidationStrategy());

    public static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public Boat Boat(string slug) => Db.Boats.All.Single(b => b.Slug == slug);

    public Booking Booking(string id) => Db.Bookings.Get(new BookingId(id))!;

    /// <summary>Seeded boats are complete, so any can be activated.</summary>
    public Boat Activate(string slug)
    {
        var boat = Boat(slug);
        if (!boat.IsActive)
        {
            boat.Activate(Db.Seasons.All, Clock.UtcNow);
            Db.Boats.Save(boat);
        }

        return boat;
    }

    public AddonDefinition Addon(string id) => Db.Addons.Get(new AddonId(id))!;

    public void PriceAddon(string id, decimal midweek, decimal weekend, decimal week)
    {
        var a = Addon(id);
        a.Update(a.Name, a.Description, false, new AddonPrices(midweek, weekend, week), a.QuantityApplies, a.Capacity, a.CapacityUnitLabel, true);
        Db.Addons.Save(a);
    }

    public void AddUnavailability(string slug, string first, string last) =>
        Db.Unavailabilities.Save(BoatUnavailability.Create(BoatUnavailabilityId.New(), Boat(slug).Id, NightRange.Create(D(first), D(last)), null, Clock.UtcNow));

    // ----- Online flow -----

    public async Task<string> CreateDraftAsync(
        string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek, string email = "jane@example.com",
        string lastName = "Smith", int adults = 2, int children = 0) =>
        (await Get<BookingDraftService>().CreateAsync(
            new CreateDraftRequest(slug, type, D(start), adults, children, "Jane", lastName, email, "+61412345678", "token"), "1.2.3.4")).DraftId;

    public static CreateBookingRequest BookingRequest(
        string draftId, string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek, string email = "jane@example.com",
        string lastName = "Smith", IReadOnlyList<AddonRequestDto>? addons = null, int adults = 2, int children = 0, string? roomingText = null) =>
        new(draftId, slug, type, D(start), adults, children, "Jane", lastName, email, "+61412345678", addons ?? [], roomingText is not null, roomingText, true);

    public async Task<Booking> BookOnlineAsync(
        string slug = "pacific-blue", string start = "2027-03-08", PeriodType type = PeriodType.Midweek, string email = "jane@example.com",
        string lastName = "Smith", IReadOnlyList<AddonRequestDto>? addons = null)
    {
        var draftId = await CreateDraftAsync(slug, start, type, email, lastName);
        var created = await Online.CreateAsync(BookingRequest(draftId, slug, start, type, email, lastName, addons));
        await PayDepositAsync(created.BookingId);
        return Booking(created.BookingId);
    }

    public async Task PayDepositAsync(string bookingId, string paymentIntent = "pi_deposit")
    {
        var b = Booking(bookingId);
        Payments.Completed.Add(b.StripeCheckoutSessionId!);
        await Get<StripeWebhookService>().HandleCompletedAsync(new CheckoutSessionCompleted(b.StripeCheckoutSessionId!, b.Id, CheckoutPurpose.Deposit,
            paymentIntent + bookingId, PaymentScheduleService.CheckoutAmount(b.DepositAmount, b.HirePrice), Clock.UtcNow));
    }

    // ----- Staff -----

    public CreateStaffBookingRequest StaffRequest(
        string start, string end, string slug = "pacific-blue", PeriodType type = PeriodType.Custom, string email = "john@example.com",
        string lastName = "Smith", bool standby = false, decimal hire = 4000, IReadOnlyList<StaffAddonLineInput>? lines = null,
        string? customerComments = null, int adults = 2, AddressDto? address = null, string? checkIn = "11:00", string? checkOut = "08:00") =>
        new(Boat(slug).Id.Value, type, D(start), D(end), adults, 0, checkIn, checkOut, standby, null, customerComments, hire, email, "John", lastName,
            "+61412000111", null, address, lines ?? []);

    public static UpdateStaffBookingRequest UpdateRequest(AdminBookingDetail d, Func<UpdateStaffBookingRequest, UpdateStaffBookingRequest>? change = null)
    {
        var r = new UpdateStaffBookingRequest(d.BoatId, d.PeriodType, d.StartDate, d.EndDate, d.NumberOfAdults, d.NumberOfChildren, d.CheckInTime,
            d.CheckOutTime, d.IsStandby, d.BookingComments, d.Customer?.Comments, d.HirePrice, d.Status, d.Customer?.Email, d.Customer?.FirstName,
            d.Customer?.LastName, d.Customer?.Mobile, d.Customer?.SecondaryMobile, d.Customer?.Address,
            d.AddonLines.Select(l => new StaffAddonLineInput(l.Id, l.AddonId, l.Quantity, l.UnitPrice)).ToList(), d.Version);
        return change is null ? r : change(r);
    }

    public IReadOnlyList<EmailMessage> EmailsTo(string address) => Emails.To(address);

    public int CountEmails(string subjectContains) => Emails.Sent.Count(m => m.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
}
