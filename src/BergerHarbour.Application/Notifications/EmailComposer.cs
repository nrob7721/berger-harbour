using System.Net;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Notifications;

/// <summary>Everything a template can refer to.</summary>
public sealed record EmailData(
    Booking Booking,
    Boat Boat,
    Customer Customer,
    BusinessSettings Settings,
    IReadOnlyList<PaymentMilestone> Milestones,
    string PaymentLink,
    decimal AmountDue,
    DateOnly? DueDate);

/// <summary>
/// Fills template placeholders. Values are HTML-encoded in the body; dates are dd/MM/yyyy and money $1,234.00.
/// </summary>
public static class EmailComposer
{
    public static (string Subject, string HtmlBody) Compose(EmailTemplate template, EmailData data)
    {
        var values = Values(data);
        var subject = EmailPlaceholders.Replace(template.Subject,
            name => values.TryGetValue(name, out var v) ? v.Text.ReplaceLineEndings(" ") : string.Empty);
        var body = EmailPlaceholders.Replace(template.HtmlBody,
            name => values.TryGetValue(name, out var v) ? v.Html : string.Empty);
        return (subject, body);
    }

    private sealed record Value(string Text, string Html)
    {
        public static Value Of(string text) => new(text, WebUtility.HtmlEncode(text));

        /// <summary>Multi-line values: each line is encoded and lines are joined with &lt;br&gt;.</summary>
        public static Value Lines(IReadOnlyList<string> lines) =>
            new(string.Join("\n", lines), string.Join("<br>", lines.Select(WebUtility.HtmlEncode)));
    }

    private static Dictionary<string, Value> Values(EmailData d)
    {
        var b = d.Booking;
        return new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["CustomerName"] = Value.Of(d.Customer.FullName),
            ["BookingReference"] = Value.Of(b.Reference.Value),
            ["BoatName"] = Value.Of(d.Boat.Name),
            ["CheckInDate"] = Value.Of(Format.Date(b.StartDate)),
            ["CheckOutDate"] = Value.Of(Format.Date(b.EndDate)),
            ["CheckInTime"] = Value.Of(Format.Time(Stay.CheckInTime)),
            ["CheckOutTime"] = Value.Of(Format.Time(Stay.CheckOutTime)),
            ["NumberOfGuests"] = Value.Of(b.NumberOfGuests.ToString()),
            ["HirePrice"] = Value.Of(Format.Money(b.HirePrice)),
            ["TotalPrice"] = Value.Of(Format.Money(b.TotalPrice)),
            ["AmountPaid"] = Value.Of(Format.Money(b.AmountPaid)),
            ["AmountOwing"] = Value.Of(Format.Money(b.AmountOwing)),
            ["AmountDue"] = Value.Of(Format.Money(d.AmountDue)),
            ["DueDate"] = Value.Of(d.DueDate is { } due ? Format.Date(due) : string.Empty),
            ["PaymentLink"] = Value.Of(d.PaymentLink),
            ["SecurityBond"] = Value.Of(Format.Money(d.Boat.SecurityBond)),
            ["RequestedAddons"] = Value.Lines(AddonLines(b)),
            ["PaymentSchedule"] = Value.Lines(ScheduleLines(b, d.Milestones)),
            ["HireTermsUrl"] = Value.Of(d.Settings.HireTermsUrl),
            ["ContactPhone"] = Value.Of(d.Settings.ContactPhone),
            ["ContactEmail"] = Value.Of(d.Settings.ContactEmail),
        };
    }

    private static List<string> AddonLines(Booking booking)
    {
        var lines = booking.AddonLines.Where(l => l.Status != AddonLineStatus.Declined).Select(l =>
        {
            var quantity = l.Quantity > 1 ? $" × {l.Quantity}" : string.Empty;
            var price = l.EstimatedAmount is { } amount ? Format.Money(amount) : "price to be confirmed";
            var status = l.Status == AddonLineStatus.Confirmed ? "confirmed" : "subject to confirmation";
            return $"{l.NameSnapshot}{quantity} — {price} ({status})";
        }).ToList();
        return lines.Count > 0 ? lines : ["None"];
    }

    /// <summary>One line per milestone with the instalment amount (not the cumulative amount).</summary>
    public static List<string> ScheduleLines(Booking booking, IReadOnlyList<PaymentMilestone> milestones)
    {
        var lines = new List<string>();
        var previous = 0m;
        foreach (var m in milestones)
        {
            var instalment = Math.Max(0m, m.RequiredCumulative - previous);
            previous = Math.Max(previous, m.RequiredCumulative);
            var paid = booking.AmountPaid >= m.RequiredCumulative ? " — paid" : string.Empty;
            lines.Add(m.Kind switch
            {
                MilestoneKind.Deposit => $"Deposit: {Format.Money(instalment)} at booking{paid}",
                MilestoneKind.FirstInstalment => $"First instalment (50%): {Format.Money(instalment)} by {Format.Date(m.DueDate)}{paid}",
                MilestoneKind.Final => $"Final balance: {Format.Money(instalment)} by {Format.Date(m.DueDate)}{paid}",
                _ => $"Balance: {Format.Money(instalment)} by {Format.Date(m.DueDate)}{paid}",
            });
        }

        return lines;
    }
}
