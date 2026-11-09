using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>The emails the system sends. #3 and #9 are deliberately not implemented.</summary>
public enum EmailTemplateKey
{
    BookingConfirmed,
    StaffNewOnlineBooking,
    PaymentDue,
    PaymentReceived,
    PreHireInstructions,
    StaffBalanceOverdue,
    StaffStandbySuperseded,
    StaffAbandonedBooking,
}

public static partial class EmailPlaceholders
{
    public static readonly IReadOnlyList<string> All =
    [
        "CustomerName", "CustomerFirstName", "CustomerEmail", "CustomerMobile", "BookingReference", "BoatName",
        "CheckInDate", "CheckOutDate", "CheckInTime", "CheckOutTime", "NumberOfGuests", "NumberOfAdults", "NumberOfChildren",
        "HirePrice", "TotalPrice", "AmountPaid", "AmountOwing", "AmountDue", "DueDate", "PaymentLink", "SecurityBond",
        "RequestedAddons", "PaymentSchedule", "HireTermsUrl", "ContactPhone", "ContactEmail", "LastStepReached",
        "FrequentFloaterNotice", "AccountName", "BSB", "AccountNumber",
    ];

    [GeneratedRegex(@"\{\{\s*([A-Za-z]+)\s*\}\}")]
    public static partial Regex Pattern();

    public static IEnumerable<string> Unknown(string text) =>
        Pattern().Matches(text).Select(m => m.Groups[1].Value).Where(n => !All.Contains(n)).Distinct();
}

public sealed class EmailTemplate : AggregateRoot
{
    public EmailTemplateKey Key { get; private set; }
    public string Subject { get; private set; } = "";
    public string HtmlBody { get; private set; } = "";

    private EmailTemplate() { }

    public static EmailTemplate Create(EmailTemplateKey key, string subject, string htmlBody, DateTimeOffset now)
    {
        var t = new EmailTemplate { Key = key };
        t.Update(subject, htmlBody);
        t.InitialiseAudit(now);
        return t;
    }

    public static EmailTemplate Rehydrate(EmailTemplateKey key, string subject, string htmlBody, int version, DateTimeOffset created, DateTimeOffset modified)
    {
        var t = new EmailTemplate { Key = key, Subject = subject, HtmlBody = htmlBody };
        t.RestoreAudit(version, created, modified);
        return t;
    }

    public void Update(string subject, string htmlBody)
    {
        var errors = new ValidationErrors();
        var s = errors.Capture(() => Text.Required(subject, "subject", "Subject", 200));
        var b = errors.Capture(() => Text.Required(htmlBody, "htmlBody", "Body", 50_000));
        foreach (var unknown in EmailPlaceholders.Unknown(subject ?? ""))
            errors.Add("subject", $"Unknown placeholder {{{{{unknown}}}}}.");
        foreach (var unknown in EmailPlaceholders.Unknown(htmlBody ?? ""))
            errors.Add("htmlBody", $"Unknown placeholder {{{{{unknown}}}}}.");
        errors.ThrowIfAny();
        Subject = s!;
        HtmlBody = b!;
    }
}

public interface IEmailTemplateRepository
{
    Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default);
    Task<EmailTemplate?> GetAsync(EmailTemplateKey key, CancellationToken ct = default);
    Task SaveAsync(EmailTemplate template, CancellationToken ct = default);
}
