using System.Net;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Notifications;

/// <summary>Who an email is about: the customer record, or the details entered online.</summary>
public sealed record EmailContact(string FullName, string Email, string? Phone);

/// <summary>
/// Everything a template can refer to. Booking emails carry a <see cref="Booking"/>; the abandoned-booking alert
/// (#10) carries a <see cref="Draft"/> instead, and booking-only placeholders render empty.
/// </summary>
public sealed record EmailData(
    Boat Boat,
    EmailContact Contact,
    BusinessSettings Settings,
    IReadOnlyList<AddonDefinition> Addons,
    Booking? Booking = null,
    IReadOnlyList<PaymentMilestone>? Milestones = null,
    string? PaymentLink = null,
    decimal AmountDue = 0m,
    DateOnly? DueDate = null,
    bool PaymentDueNow = false,
    BookingDraft? Draft = null);

/// <summary>
/// Fills template sections and placeholders. Values are HTML-encoded in the body; dates are dd/MM/yyyy, times
/// 11am with AEST/AEDT, and money $1,234.00.
/// </summary>
public static class EmailComposer
{
    public static (string Subject, string HtmlBody) Compose(EmailTemplate template, EmailData data)
    {
        var values = Values(data);
        bool Condition(string section) => section == "PaymentDueNow" && data.PaymentDueNow;
        var subject = EmailPlaceholders.Replace(EmailPlaceholders.ApplySections(template.Subject, Condition),
            name => values.TryGetValue(name, out var v) ? v.Text.ReplaceLineEndings(" ") : string.Empty);
        var body = EmailPlaceholders.Replace(EmailPlaceholders.ApplySections(template.HtmlBody, Condition),
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
        var values = new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["CustomerName"] = Value.Of(d.Contact.FullName),
            ["CustomerEmail"] = Value.Of(d.Contact.Email),
            ["CustomerPhone"] = Value.Of(Format.Phone(d.Contact.Phone)),
            ["BoatName"] = Value.Of(d.Boat.Name),
            ["SecurityBond"] = Value.Of(Format.Money(d.Boat.SecurityBond)),
            ["HireTermsUrl"] = Value.Of(d.Settings.HireTermsUrl),
            ["ContactPhone"] = Value.Of(d.Settings.ContactPhone),
            ["ContactEmail"] = Value.Of(d.Settings.ContactEmail),
        };

        if (d.Booking is { } b)
        {
            values["BookingReference"] = Value.Of(b.Reference.Value);
            AddStay(values, b.Stay, b.CheckInTime, b.CheckOutTime);
            AddGuests(values, b.NumberOfAdults, b.NumberOfChildren);
            values["HirePrice"] = Value.Of(Format.Money(b.HirePrice));
            values["TotalPrice"] = Value.Of(Format.Money(b.TotalPrice));
            values["AmountPaid"] = Value.Of(Format.Money(b.AmountPaid));
            values["AmountOwing"] = Value.Of(Format.Money(b.AmountOwing));
            values["AmountDue"] = Value.Of(Format.Money(d.AmountDue));
            values["DueDate"] = Value.Of(d.DueDate is { } due ? Format.Date(due) : string.Empty);
            values["PaymentLink"] = Value.Of(d.PaymentLink ?? string.Empty);
            values["RequestedAddons"] = Value.Lines(AddonLines(b, d.Addons));
            values["PaymentSchedule"] = Value.Lines(ScheduleLines(b, d.Milestones ?? []));
        }
        else if (d.Draft is { } draft)
        {
            AddStay(values, draft.Stay, Stay.CheckInTime, Stay.CheckOutTime);
            if (draft.NumberOfAdults is { } adults)
            {
                AddGuests(values, adults, draft.NumberOfChildren ?? 0);
            }

            values["RequestedAddons"] = Value.Lines(DraftAddonLines(draft, d.Addons));
            values["LastStepReached"] = Value.Of(StepLabel(draft.LastStepReached));
        }

        return values;
    }

    private static void AddStay(Dictionary<string, Value> values, Stay stay, TimeOnly checkIn, TimeOnly checkOut)
    {
        values["CheckInDate"] = Value.Of(Format.Date(stay.StartDate));
        values["CheckOutDate"] = Value.Of(Format.Date(stay.EndDate));
        values["CheckInTime"] = Value.Of(checkIn == Stay.CheckInTime
            ? Format.CheckInWindow(stay.StartDate)
            : Format.TimeWithZone(stay.StartDate, checkIn));
        values["CheckOutTime"] = Value.Of(Format.TimeWithZone(stay.EndDate, checkOut));
    }

    private static void AddGuests(Dictionary<string, Value> values, int adults, int children)
    {
        values["NumberOfGuests"] = Value.Of((adults + children).ToString());
        values["NumberOfAdults"] = Value.Of(adults.ToString());
        values["NumberOfChildren"] = Value.Of(children.ToString());
    }

    /// <summary>Lines in the admin-defined add-on order, with their price or "price on request".</summary>
    private static List<string> AddonLines(Booking booking, IReadOnlyList<AddonDefinition> addons)
    {
        var order = AddonCatalogue.InDisplayOrder(addons).Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var lines = booking.AddonLines.OrderBy(l => order.GetValueOrDefault(l.AddonId, int.MaxValue)).Select(l =>
        {
            var quantity = l.Quantity > 1 ? $" × {l.Quantity}" : string.Empty;
            var price = l.LineTotal is { } amount ? Format.Money(amount) : "price on request";
            return $"{l.NameSnapshot}{quantity} — {price}";
        }).ToList();
        return lines.Count > 0 ? lines : ["None"];
    }

    private static List<string> DraftAddonLines(BookingDraft draft, IReadOnlyList<AddonDefinition> addons)
    {
        var lines = AddonCatalogue.InDisplayOrder(addons)
            .SelectMany(a => draft.AddonRequests.Where(r => r.AddonId == a.Id)
                .Select(r => r.Quantity > 1 ? $"{a.Name} × {r.Quantity}" : a.Name))
            .ToList();
        return lines.Count > 0 ? lines : ["None"];
    }

    public static string StepLabel(BookingStep step) => step switch
    {
        BookingStep.Details => "Your details",
        BookingStep.Guests => "Guests",
        BookingStep.Addons => "Add-ons",
        BookingStep.Summary => "Summary",
        _ => "Payment",
    };

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
