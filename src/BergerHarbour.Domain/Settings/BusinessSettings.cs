using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>Singleton aggregate holding the business values staff maintain in Settings → General.</summary>
public sealed class BusinessSettings : AggregateRoot<string>
{
    public const string SingletonId = "business";
    public const decimal DefaultDepositAmount = 1000m;
    public const int DefaultMinimumLeadTimeDays = 30;
    public const string DefaultHireTermsUrl = "https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/";

    private BusinessSettings(int version, decimal depositAmount, int minimumLeadTimeDays, DateOnly bookingsOpenUntil,
        string staffAlertEmail, string contactPhone, string contactEmail, string hireTermsUrl)
        : base(SingletonId, version)
    {
        DepositAmount = depositAmount;
        MinimumLeadTimeDays = minimumLeadTimeDays;
        BookingsOpenUntil = bookingsOpenUntil;
        StaffAlertEmail = staffAlertEmail;
        ContactPhone = contactPhone;
        ContactEmail = contactEmail;
        HireTermsUrl = hireTermsUrl;
    }

    public decimal DepositAmount { get; private set; }

    public int MinimumLeadTimeDays { get; private set; }

    /// <summary>The last night that can be booked online.</summary>
    public DateOnly BookingsOpenUntil { get; private set; }

    public string StaffAlertEmail { get; private set; }

    public string ContactPhone { get; private set; }

    public string ContactEmail { get; private set; }

    public string HireTermsUrl { get; private set; }

    public static BusinessSettings CreateDefault(DateOnly bookingsOpenUntil, string staffAlertEmail, string contactPhone,
        string contactEmail)
    {
        var settings = new BusinessSettings(0, DefaultDepositAmount, DefaultMinimumLeadTimeDays, bookingsOpenUntil,
            string.Empty, string.Empty, string.Empty, DefaultHireTermsUrl);
        settings.Update(DefaultDepositAmount, DefaultMinimumLeadTimeDays, bookingsOpenUntil, staffAlertEmail,
            contactPhone, contactEmail, DefaultHireTermsUrl);
        return settings;
    }

    public void Update(decimal depositAmount, int minimumLeadTimeDays, DateOnly bookingsOpenUntil,
        string staffAlertEmail, string contactPhone, string contactEmail, string hireTermsUrl)
    {
        Guard.PositiveMoney(depositAmount, "depositAmount", "Deposit");
        if (minimumLeadTimeDays is < 0 or > 730)
        {
            throw new DomainValidationException("minimumLeadTimeDays", "Minimum lead time must be between 0 and 730 days.");
        }

        if (!Uri.TryCreate(hireTermsUrl?.Trim(), UriKind.Absolute, out var termsUri) || termsUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DomainValidationException("hireTermsUrl", "Hire terms URL must be an https:// address.");
        }

        DepositAmount = depositAmount;
        MinimumLeadTimeDays = minimumLeadTimeDays;
        BookingsOpenUntil = bookingsOpenUntil;
        StaffAlertEmail = EmailAddress.Create(staffAlertEmail, "staffAlertEmail").Value;
        ContactPhone = Guard.Required(contactPhone, "contactPhone", "Contact phone", 40);
        ContactEmail = EmailAddress.Create(contactEmail, "contactEmail").Value;
        HireTermsUrl = termsUri.ToString();
    }

    /// <summary>The first start date a customer can book online.</summary>
    public DateOnly EarliestOnlineStartDate(DateOnly today) => today.AddDays(MinimumLeadTimeDays);

    /// <summary>Online bookings must start at least the minimum lead time from today (Australia/Sydney).</summary>
    public bool SatisfiesLeadTime(Stay stay, DateOnly today) => stay.StartDate >= EarliestOnlineStartDate(today);

    /// <summary>Every night of an online booking must be on or before <see cref="BookingsOpenUntil"/>.</summary>
    public bool IsOpenForOnlineBooking(Stay stay) => stay.LastNight <= BookingsOpenUntil;

    public BusinessSettingsSnapshot ToSnapshot() =>
        new(Version, DepositAmount, MinimumLeadTimeDays, BookingsOpenUntil, StaffAlertEmail, ContactPhone, ContactEmail,
            HireTermsUrl);

    public static BusinessSettings FromSnapshot(BusinessSettingsSnapshot s) =>
        new(s.Version, s.DepositAmount, s.MinimumLeadTimeDays, s.BookingsOpenUntil, s.StaffAlertEmail, s.ContactPhone,
            s.ContactEmail, s.HireTermsUrl);
}

public sealed record BusinessSettingsSnapshot(
    int Version,
    decimal DepositAmount,
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string StaffAlertEmail,
    string ContactPhone,
    string ContactEmail,
    string HireTermsUrl);
