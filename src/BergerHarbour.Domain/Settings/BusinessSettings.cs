using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

public sealed record BusinessSettingsDetails(
    decimal DepositAmount,
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    string? StaffAlertEmail,
    string? ContactPhone,
    string? ContactEmail,
    string? HireTermsUrl,
    string? AccountName,
    string? Bsb,
    string? AccountNumber);

/// <summary>Singleton business settings.</summary>
public sealed class BusinessSettings : AggregateRoot
{
    public const string DefaultHireTermsUrl = "https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/";

    public decimal DepositAmount { get; private set; } = 1000m;
    public int MinimumLeadTimeDays { get; private set; } = 30;
    public DateOnly BookingsOpenUntil { get; private set; }
    public string? StaffAlertEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactEmail { get; private set; }
    public string HireTermsUrl { get; private set; } = DefaultHireTermsUrl;
    public string? AccountName { get; private set; }
    /// <summary>6 digits, stored without a separator.</summary>
    public string? Bsb { get; private set; }
    public string? AccountNumber { get; private set; }

    private BusinessSettings() { }

    public static BusinessSettings Create(BusinessSettingsDetails details, DateTimeOffset now)
    {
        var s = new BusinessSettings();
        s.Update(details);
        s.InitialiseAudit(now);
        return s;
    }

    public static BusinessSettings Rehydrate(BusinessSettingsDetails d, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var s = new BusinessSettings
        {
            DepositAmount = d.DepositAmount, MinimumLeadTimeDays = d.MinimumLeadTimeDays, BookingsOpenUntil = d.BookingsOpenUntil,
            StaffAlertEmail = d.StaffAlertEmail, ContactPhone = d.ContactPhone, ContactEmail = d.ContactEmail,
            HireTermsUrl = string.IsNullOrWhiteSpace(d.HireTermsUrl) ? DefaultHireTermsUrl : d.HireTermsUrl,
            AccountName = d.AccountName, Bsb = d.Bsb, AccountNumber = d.AccountNumber,
        };
        s.RestoreAudit(version, created, modified);
        return s;
    }

    public void Update(BusinessSettingsDetails d)
    {
        var errors = new ValidationErrors();
        errors.AddIf(d.DepositAmount <= 0, "depositAmount", "The default deposit must be more than $0.");
        errors.AddIf(d.MinimumLeadTimeDays is < 0 or > 365, "minimumLeadTimeDays", "Lead time must be between 0 and 365 days.");
        string? staffEmail = null, contactEmail = null;
        if (!string.IsNullOrWhiteSpace(d.StaffAlertEmail)) staffEmail = errors.Capture(() => EmailAddress.Require(d.StaffAlertEmail, "staffAlertEmail"));
        if (!string.IsNullOrWhiteSpace(d.ContactEmail)) contactEmail = errors.Capture(() => EmailAddress.Require(d.ContactEmail, "contactEmail"));
        var phone = errors.Capture(() => Text.Optional(d.ContactPhone, "contactPhone", "Contact phone", 40));
        var terms = string.IsNullOrWhiteSpace(d.HireTermsUrl) ? DefaultHireTermsUrl : d.HireTermsUrl.Trim();
        if (!Uri.TryCreate(terms, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            errors.Add("hireTermsUrl", "Enter a full web address starting with https://.");
        var accountName = errors.Capture(() => Text.Optional(d.AccountName, "accountName", "Account name", 100));
        string? bsb = null;
        if (!string.IsNullOrWhiteSpace(d.Bsb))
        {
            bsb = new string(d.Bsb.Where(c => c is not ('-' or ' ')).ToArray());
            if (bsb.Length != 6 || !bsb.All(char.IsAsciiDigit)) errors.Add("bsb", "BSB must be 6 digits, e.g. 123-456.");
        }
        string? account = null;
        if (!string.IsNullOrWhiteSpace(d.AccountNumber))
        {
            account = new string(d.AccountNumber.Where(c => c != ' ').ToArray());
            if (account.Length is < 6 or > 10 || !account.All(char.IsAsciiDigit)) errors.Add("accountNumber", "Account number must be 6 to 10 digits.");
        }
        errors.ThrowIfAny();

        DepositAmount = Money.Round(d.DepositAmount);
        MinimumLeadTimeDays = d.MinimumLeadTimeDays;
        BookingsOpenUntil = d.BookingsOpenUntil;
        StaffAlertEmail = staffEmail;
        ContactPhone = phone;
        ContactEmail = contactEmail;
        HireTermsUrl = terms;
        AccountName = accountName;
        Bsb = bsb;
        AccountNumber = account;
    }

    /// <summary>123-456, or empty.</summary>
    public string BsbDisplay => Bsb is { Length: 6 } b ? $"{b[..3]}-{b[3..]}" : "";

    /// <summary>The first date online bookings may start.</summary>
    public DateOnly EarliestOnlineStart(DateOnly today) => today.AddDays(MinimumLeadTimeDays);
}

public interface IBusinessSettingsRepository
{
    Task<BusinessSettings?> GetAsync(CancellationToken ct = default);
    Task SaveAsync(BusinessSettings settings, CancellationToken ct = default);
}
