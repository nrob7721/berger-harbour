using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>The singleton business settings (deposit default, lead time, open-until, contacts, bank details).</summary>
public sealed partial class BusinessSettings : AggregateRoot
{
    public const decimal DefaultDeposit = 1000m;
    public const int DefaultLeadTimeDays = 30;
    public const string DefaultHireTermsUrl = "https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/";

    private BusinessSettings()
    {
        HireTermsUrl = DefaultHireTermsUrl;
    }

    public decimal DepositAmount { get; private set; }

    public int MinimumLeadTimeDays { get; private set; }

    public DateOnly BookingsOpenUntil { get; private set; }

    public EmailAddress? StaffAlertEmail { get; private set; }

    public string? ContactPhone { get; private set; }

    public EmailAddress? ContactEmail { get; private set; }

    public string HireTermsUrl { get; private set; }

    public string? AccountName { get; private set; }

    /// <summary>6 digits without a separator; shown 123-456.</summary>
    public string? Bsb { get; private set; }

    public string? AccountNumber { get; private set; }

    public string? BsbDisplay => Bsb is null ? null : $"{Bsb[..3]}-{Bsb[3..]}";

    public static BusinessSettings Create(
        decimal depositAmount, int minimumLeadTimeDays, DateOnly bookingsOpenUntil, string? staffAlertEmail, string? contactPhone,
        string? contactEmail, string? hireTermsUrl, string? accountName, string? bsb, string? accountNumber)
    {
        var settings = new BusinessSettings();
        settings.Update(depositAmount, minimumLeadTimeDays, bookingsOpenUntil, staffAlertEmail, contactPhone, contactEmail, hireTermsUrl,
            accountName, bsb, accountNumber);
        return settings;
    }

    public static BusinessSettings Rehydrate(
        decimal depositAmount, int minimumLeadTimeDays, DateOnly bookingsOpenUntil, EmailAddress? staffAlertEmail, string? contactPhone,
        EmailAddress? contactEmail, string hireTermsUrl, string? accountName, string? bsb, string? accountNumber, int version)
    {
        var settings = new BusinessSettings
        {
            DepositAmount = depositAmount,
            MinimumLeadTimeDays = minimumLeadTimeDays,
            BookingsOpenUntil = bookingsOpenUntil,
            StaffAlertEmail = staffAlertEmail,
            ContactPhone = contactPhone,
            ContactEmail = contactEmail,
            HireTermsUrl = hireTermsUrl,
            AccountName = accountName,
            Bsb = bsb,
            AccountNumber = accountNumber,
        };
        settings.SetPersistedVersion(version);
        return settings;
    }

    public void Update(
        decimal depositAmount, int minimumLeadTimeDays, DateOnly bookingsOpenUntil, string? staffAlertEmail, string? contactPhone,
        string? contactEmail, string? hireTermsUrl, string? accountName, string? bsb, string? accountNumber)
    {
        var errors = new ValidationErrors();
        if (depositAmount <= 0)
        {
            errors.Add("depositAmount", "The default deposit must be more than $0.");
        }

        if (minimumLeadTimeDays is < 0 or > 365)
        {
            errors.Add("minimumLeadTimeDays", "Lead time must be between 0 and 365 days.");
        }

        var staff = errors.Capture(() => EmailAddress.CreateOptional(staffAlertEmail, "staffAlertEmail"));
        var contact = errors.Capture(() => EmailAddress.CreateOptional(contactEmail, "contactEmail"));
        var phone = Optional(contactPhone);
        if (phone?.Length > 40)
        {
            errors.Add("contactPhone", "Contact phone must be 40 characters or fewer.");
        }

        var terms = string.IsNullOrWhiteSpace(hireTermsUrl) ? DefaultHireTermsUrl : hireTermsUrl.Trim();
        if (!Uri.TryCreate(terms, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("hireTermsUrl", "Enter a full web address starting with https://.");
        }

        var name = Optional(accountName);
        if (name?.Length > 100)
        {
            errors.Add("accountName", "Account name must be 100 characters or fewer.");
        }

        var bsbDigits = Optional(bsb)?.Replace("-", string.Empty).Replace(" ", string.Empty);
        if (bsbDigits is not null && (!Digits().IsMatch(bsbDigits) || bsbDigits.Length != 6))
        {
            errors.Add("bsb", "BSB must be 6 digits, e.g. 123-456.");
        }

        var number = Optional(accountNumber)?.Replace(" ", string.Empty);
        if (number is not null && (!Digits().IsMatch(number) || number.Length is < 6 or > 10))
        {
            errors.Add("accountNumber", "Account number must be 6 to 10 digits.");
        }

        errors.ThrowIfAny();
        DepositAmount = depositAmount;
        MinimumLeadTimeDays = minimumLeadTimeDays;
        BookingsOpenUntil = bookingsOpenUntil;
        StaffAlertEmail = staff;
        ContactPhone = phone;
        ContactEmail = contact;
        HireTermsUrl = terms;
        AccountName = name;
        Bsb = bsbDigits;
        AccountNumber = number;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex Digits();
}
