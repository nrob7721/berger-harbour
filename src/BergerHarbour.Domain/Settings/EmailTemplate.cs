using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>
/// The emails the system sends. Numbers 3 (add-on confirmed/declined) and 9 (cancellation) are deliberately not
/// implemented: staff contact the customer by hand.
/// </summary>
public enum EmailTemplateKey
{
    /// <summary>#1, to the customer.</summary>
    BookingConfirmed,

    /// <summary>#2, to the staff alert address.</summary>
    StaffNewOnlineBooking,

    /// <summary>#4, to the customer, with a payment link.</summary>
    PaymentDue,

    /// <summary>#5, to the customer.</summary>
    PaymentReceived,

    /// <summary>#6, to the customer.</summary>
    PreHireInstructions,

    /// <summary>#7, to the staff alert address.</summary>
    StaffBalanceOverdue,

    /// <summary>#8, to the staff alert address.</summary>
    StaffStandbySuperseded,
}

public static partial class EmailPlaceholders
{
    public static readonly IReadOnlyList<string> All =
    [
        "CustomerName", "BookingReference", "BoatName", "CheckInDate", "CheckOutDate", "CheckInTime", "CheckOutTime",
        "NumberOfGuests", "HirePrice", "TotalPrice", "AmountPaid", "AmountOwing", "AmountDue", "DueDate", "PaymentLink",
        "SecurityBond", "RequestedAddons", "PaymentSchedule", "HireTermsUrl", "ContactPhone", "ContactEmail",
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static IReadOnlyList<string> FindUnknown(string text) =>
        Pattern().Matches(text).Select(m => m.Groups[1].Value).Where(n => !Known.Contains(n)).Distinct().ToList();

    public static string Replace(string text, Func<string, string> valueFor) =>
        Pattern().Replace(text, m => valueFor(m.Groups[1].Value));

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Pattern();
}

public sealed class EmailTemplate : AggregateRoot<EmailTemplateKey>
{
    private EmailTemplate(EmailTemplateKey key, int version, string subject, string htmlBody)
        : base(key, version)
    {
        Subject = subject;
        HtmlBody = htmlBody;
    }

    public EmailTemplateKey Key => Id;

    public string Subject { get; private set; }

    public string HtmlBody { get; private set; }

    public static EmailTemplate Create(EmailTemplateKey key, string subject, string htmlBody)
    {
        var template = new EmailTemplate(key, 0, string.Empty, string.Empty);
        template.Update(subject, htmlBody);
        return template;
    }

    /// <summary>Saving a template with an unknown placeholder is rejected.</summary>
    public void Update(string subject, string htmlBody)
    {
        var s = Guard.Required(subject, "subject", "Subject", 200);
        var body = Guard.Required(htmlBody, "htmlBody", "Body", 100_000);
        var unknownInSubject = EmailPlaceholders.FindUnknown(s);
        if (unknownInSubject.Count > 0)
        {
            throw new DomainValidationException("subject", $"Unknown placeholder(s): {Format(unknownInSubject)}.");
        }

        var unknownInBody = EmailPlaceholders.FindUnknown(body);
        if (unknownInBody.Count > 0)
        {
            throw new DomainValidationException("htmlBody", $"Unknown placeholder(s): {Format(unknownInBody)}.");
        }

        Subject = s;
        HtmlBody = body;
    }

    public EmailTemplateSnapshot ToSnapshot() => new(Key, Version, Subject, HtmlBody);

    public static EmailTemplate FromSnapshot(EmailTemplateSnapshot s) => new(s.Key, s.Version, s.Subject, s.HtmlBody);

    private static string Format(IEnumerable<string> names) => string.Join(", ", names.Select(n => "{{" + n + "}}"));
}

public sealed record EmailTemplateSnapshot(EmailTemplateKey Key, int Version, string Subject, string HtmlBody);
