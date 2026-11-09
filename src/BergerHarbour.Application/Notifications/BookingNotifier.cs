using System.Globalization;
using System.Net;
using System.Text;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Notifications;

/// <summary>Keys recorded in <see cref="Booking.SentNotifications"/> so emails are sent once.</summary>
public static class NotificationKeys
{
    public const string BookingConfirmed = "BookingConfirmed";
    public const string StaffNewOnlineBooking = "StaffNewOnlineBooking";
    public const string PreHire = "PreHire";
    public const string StandbySuperseded = "StandbySuperseded";
    public static string PaymentDueReminder(MilestoneKind k) => $"PaymentDue:{k}:Reminder";
    public static string PaymentDue(MilestoneKind k) => $"PaymentDue:{k}:Due";
    public static string Overdue(MilestoneKind k) => $"Overdue:{k}";
    public static string PaymentReceived(string paymentId) => $"PaymentReceived:{paymentId}";
}

/// <summary>Renders templates with booking data and sends them.</summary>
public sealed class BookingNotifier(
    IEmailSender sender,
    IEmailTemplateRepository templates,
    ReferenceDataLoader referenceData,
    IBoatRepository boats,
    ICustomerRepository customers,
    IBookingRepository bookings,
    PaymentScheduleService schedule,
    PaymentLinkTokens tokens,
    IOptions<AppUrlOptions> urls,
    IClock clock,
    ILogger<BookingNotifier> logger)
{
    public async Task SendBookingConfirmedAsync(Booking b, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        await SendAsync(EmailTemplateKey.BookingConfirmed, ctx.CustomerEmail, Values(ctx, schedule.AmountDueNow(b, clock.UtcNow), null, lateNotice: true), ct);
    }

    public async Task SendStaffNewOnlineBookingAsync(Booking b, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        var values = Values(ctx, schedule.AmountDueNow(b, clock.UtcNow), null);
        var ff = await FrequentFloaterAsync(b, ct);
        values["FrequentFloaterNotice"] = PlaceholderValue.Plain(FrequentFloaterService.Notice(ctx.CustomerName, ff));
        await SendToStaffAsync(EmailTemplateKey.StaffNewOnlineBooking, ctx.Reference.Settings, values, ct);
    }

    public async Task SendPaymentDueAsync(Booking b, decimal amountDue, DateOnly dueDate, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        await SendAsync(EmailTemplateKey.PaymentDue, ctx.CustomerEmail, Values(ctx, amountDue, dueDate), ct);
    }

    public async Task SendPaymentReceivedAsync(Booking b, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        await SendAsync(EmailTemplateKey.PaymentReceived, ctx.CustomerEmail, Values(ctx, schedule.AmountDueNow(b, clock.UtcNow), null), ct);
    }

    public async Task SendPreHireAsync(Booking b, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        await SendAsync(EmailTemplateKey.PreHireInstructions, ctx.CustomerEmail, Values(ctx, b.AmountOwing, null), ct);
    }

    public async Task SendStaffOverdueAsync(Booking b, Milestone m, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(b, ct);
        await SendToStaffAsync(EmailTemplateKey.StaffBalanceOverdue, ctx.Reference.Settings,
            Values(ctx, m.Shortfall(b.AmountPaid), SydneyTime.DateOf(m.DueAt)), ct);
    }

    public async Task SendStaffStandbySupersededAsync(Booking standby, CancellationToken ct = default)
    {
        var ctx = await ContextAsync(standby, ct);
        await SendToStaffAsync(EmailTemplateKey.StaffStandbySuperseded, ctx.Reference.Settings, Values(ctx, 0, null), ct);
    }

    public async Task SendStaffAbandonedAsync(BookingDraft d, CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        var boat = await boats.GetAsync(d.BoatId, ct);
        var v = CommonValues(reference.Settings);
        v["CustomerName"] = PlaceholderValue.Plain($"{d.FirstName} {d.LastName}");
        v["CustomerFirstName"] = PlaceholderValue.Plain(d.FirstName);
        v["CustomerEmail"] = PlaceholderValue.Plain(d.Email);
        v["CustomerMobile"] = PlaceholderValue.Plain(MobileNumber.Display(d.MobileNumber));
        v["BoatName"] = PlaceholderValue.Plain(boat?.Name);
        v["CheckInDate"] = PlaceholderValue.Plain(Dates.Format(d.Stay.Start));
        v["CheckOutDate"] = PlaceholderValue.Plain(Dates.Format(d.Stay.End));
        v["NumberOfGuests"] = PlaceholderValue.Plain(d.Guests.Total.ToString(CultureInfo.InvariantCulture));
        v["NumberOfAdults"] = PlaceholderValue.Plain(d.Guests.Adults.ToString(CultureInfo.InvariantCulture));
        v["NumberOfChildren"] = PlaceholderValue.Plain(d.Guests.Children.ToString(CultureInfo.InvariantCulture));
        v["LastStepReached"] = PlaceholderValue.Plain(StepLabel(d.LastStepReached));
        var lines = d.AddonRequests
            .Select(r => (Addon: reference.Addon(r.AddonId), r.Quantity))
            .Where(x => x.Addon is not null)
            .OrderBy(x => x.Addon!.SortOrder)
            .Select(x => (x.Addon!.Name, x.Quantity, Price: PricingService.AddonUnitPrice(x.Addon!, d.PeriodType)))
            .ToList();
        v["RequestedAddons"] = AddonList(lines);
        await SendToStaffAsync(EmailTemplateKey.StaffAbandonedBooking, reference.Settings, v, ct);
    }

    /// <summary>Sends a template with sample data to the staff alert address.</summary>
    public async Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(reference.Settings.StaffAlertEmail))
            throw new DomainValidationException("staffAlertEmail", "Set the staff alert email in Settings → General first.");
        var v = CommonValues(reference.Settings);
        var start = SydneyTime.Today(clock.UtcNow).AddDays(60);
        void P(string k, string s) => v[k] = PlaceholderValue.Plain(s);
        P("CustomerName", "Jane Smith"); P("CustomerFirstName", "Jane"); P("CustomerEmail", "jane.smith@example.com");
        P("CustomerMobile", "0412 345 678"); P("BookingReference", "SMITH-" + start.ToString("yyMMdd", CultureInfo.InvariantCulture) + "0");
        P("BoatName", "Pacific Blue"); P("CheckInDate", Dates.Format(start)); P("CheckOutDate", Dates.Format(start.AddDays(4)));
        P("CheckInTime", $"11am – 2pm {SydneyTime.ZoneLabel(start)}"); P("CheckOutTime", $"8am {SydneyTime.ZoneLabel(start.AddDays(4))}");
        P("NumberOfGuests", "6"); P("NumberOfAdults", "4"); P("NumberOfChildren", "2"); P("HirePrice", "$4,060.00");
        P("TotalPrice", "$4,250.00"); P("AmountPaid", "$1,000.00"); P("AmountOwing", "$3,250.00"); P("AmountDue", "$3,250.00");
        P("DueDate", Dates.Format(start.AddDays(-30))); P("PaymentLink", urls.Value.PaymentLink("sample-token"));
        P("SecurityBond", "$2,000.00"); P("LastStepReached", "Summary");
        P("FrequentFloaterNotice", "⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one.");
        v["RequestedAddons"] = AddonList([("Outboard motor", 1, 95m), ("Welcome hamper", 1, null)]);
        v["PaymentSchedule"] = new PlaceholderValue("Deposit $1,000.00 – paid. Balance $3,250.00 due by " + Dates.Format(start.AddDays(-30)),
            $"<ul><li>Deposit: $1,000.00 – paid</li><li>Balance (100%): $4,250.00 due by {Dates.Format(start.AddDays(-30))}</li></ul>");
        await SendAsync(key, reference.Settings.StaffAlertEmail, v, ct);
    }

    // ----- Building values -----

    private sealed record BookingContext(Booking Booking, Boat? Boat, Customer? Customer, ReferenceData Reference)
    {
        public string CustomerName => Customer?.FullName ?? Booking.SnapshotName;
        public string CustomerFirstName => Customer?.FirstName ?? Booking.CustomerDetails?.FirstName ?? "";
        public string CustomerEmail => Customer?.Email ?? Booking.CustomerDetails?.Email ?? "";
        public string? CustomerMobile => Customer?.MobileNumber ?? Booking.CustomerDetails?.MobileNumber;
    }

    private async Task<BookingContext> ContextAsync(Booking b, CancellationToken ct)
    {
        var reference = await referenceData.LoadAsync(ct);
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        return new BookingContext(b, boat, customer, reference);
    }

    private async Task<FrequentFloaterResult> FrequentFloaterAsync(Booking b, CancellationToken ct)
    {
        if (b.CustomerId is null) return FrequentFloaterResult.None;
        var others = await bookings.ListByCustomerAsync(b.CustomerId, ct);
        return FrequentFloaterService.Evaluate(b.ActivatedAt ?? clock.UtcNow, b.Id, others);
    }

    private Dictionary<string, PlaceholderValue> CommonValues(BusinessSettings s) => new()
    {
        ["HireTermsUrl"] = PlaceholderValue.Plain(s.HireTermsUrl),
        ["ContactPhone"] = PlaceholderValue.Plain(s.ContactPhone),
        ["ContactEmail"] = PlaceholderValue.Plain(s.ContactEmail),
        ["AccountName"] = PlaceholderValue.Plain(s.AccountName),
        ["BSB"] = PlaceholderValue.Plain(s.BsbDisplay),
        ["AccountNumber"] = PlaceholderValue.Plain(s.AccountNumber),
    };

    private Dictionary<string, PlaceholderValue> Values(BookingContext ctx, decimal amountDue, DateOnly? dueDate, bool lateNotice = false)
    {
        var b = ctx.Booking;
        var v = CommonValues(ctx.Reference.Settings);
        void P(string k, string? s) => v[k] = PlaceholderValue.Plain(s);
        P("CustomerName", ctx.CustomerName);
        P("CustomerFirstName", ctx.CustomerFirstName);
        P("CustomerEmail", ctx.CustomerEmail);
        P("CustomerMobile", MobileNumber.Display(ctx.CustomerMobile));
        P("BookingReference", b.Reference);
        P("BoatName", ctx.Boat?.Name);
        P("CheckInDate", Dates.Format(b.StartDate));
        P("CheckOutDate", Dates.Format(b.EndDate));
        P("CheckInTime", CheckInTimeText(b));
        P("CheckOutTime", CheckOutTimeText(b));
        P("NumberOfGuests", b.NumberOfGuests.ToString(CultureInfo.InvariantCulture));
        P("NumberOfAdults", b.Guests.Adults.ToString(CultureInfo.InvariantCulture));
        P("NumberOfChildren", b.Guests.Children.ToString(CultureInfo.InvariantCulture));
        P("HirePrice", Money.Format(b.HirePrice));
        P("TotalPrice", Money.Format(b.TotalPrice));
        P("AmountPaid", Money.Format(b.AmountPaid));
        P("AmountOwing", Money.Format(b.AmountOwing));
        P("AmountDue", Money.Format(amountDue));
        P("DueDate", dueDate is { } d ? Dates.Format(d) : "");
        P("PaymentLink", urls.Value.PaymentLink(tokens.TokenFor(b.Id)));
        P("SecurityBond", Money.Format(ctx.Boat?.SecurityBond ?? 0));
        var addonOrder = ctx.Reference.Addons.ToDictionary(a => a.Id, a => a.SortOrder);
        v["RequestedAddons"] = AddonList(b.AddonLines
            .OrderBy(l => addonOrder.GetValueOrDefault(l.AddonId, int.MaxValue))
            .Select(l => (l.NameSnapshot, l.Quantity, l.UnitPrice)).ToList());
        v["PaymentSchedule"] = ScheduleValue(b, lateNotice ? amountDue : null);
        return v;
    }

    public static string CheckInTimeText(Booking b) =>
        (b.CheckInTime == SydneyTime.StandardCheckIn ? "11am – 2pm" : Dates.FormatTime(b.CheckInTime)) + " " + SydneyTime.ZoneLabel(b.StartDate);

    public static string CheckOutTimeText(Booking b) => Dates.FormatTime(b.CheckOutTime) + " " + SydneyTime.ZoneLabel(b.EndDate);

    private static PlaceholderValue AddonList(IReadOnlyList<(string Name, int Quantity, decimal? UnitPrice)> lines)
    {
        if (lines.Count == 0) return new PlaceholderValue("None", "<p>None</p>");
        string Line((string Name, int Quantity, decimal? UnitPrice) l) =>
            $"{l.Name}{(l.Quantity > 1 ? $" × {l.Quantity}" : "")} – {(l.UnitPrice is { } p ? Money.Format(p * l.Quantity) : "price on request")}";
        var html = new StringBuilder("<ul>");
        foreach (var l in lines) html.Append("<li>").Append(WebUtility.HtmlEncode(Line(l))).Append("</li>");
        html.Append("</ul>");
        return new PlaceholderValue(string.Join("; ", lines.Select(Line)), html.ToString());
    }

    private PlaceholderValue ScheduleValue(Booking b, decimal? lateAmountDue)
    {
        var paid = b.AmountPaid;
        var items = schedule.Milestones(b).Select(m =>
        {
            var due = Dates.Format(SydneyTime.DateOf(m.DueAt));
            var status = m.Shortfall(paid) == 0 ? "paid" : m.Kind == MilestoneKind.Deposit ? "due now" : $"due by {due}";
            return m.Kind switch
            {
                MilestoneKind.Deposit => $"Deposit: {Money.Format(m.RequiredCumulative)} – {status}",
                MilestoneKind.Balance => $"Balance: the full total of {Money.Format(m.RequiredCumulative)} – {status}",
                MilestoneKind.First => $"First instalment: 50% of the total ({Money.Format(m.RequiredCumulative)} paid in total) – {status}",
                _ => $"Final payment: the full total of {Money.Format(m.RequiredCumulative)} – {status}",
            };
        }).ToList();
        var html = new StringBuilder("<ul>");
        foreach (var i in items) html.Append("<li>").Append(WebUtility.HtmlEncode(i)).Append("</li>");
        html.Append("</ul>");
        var text = string.Join("; ", items);
        // A milestone already due when the booking was created: say so and point to the payment link.
        var lateDue = lateAmountDue is > 0 && schedule.Milestones(b).Any(m => m.Kind != MilestoneKind.Deposit && m.WasDueAtCreation(b.CreatedDate) && m.Shortfall(paid) > 0);
        if (lateDue)
        {
            var notice = $"The balance of {Money.Format(lateAmountDue!.Value)} is now due.";
            var link = urls.Value.PaymentLink(tokens.TokenFor(b.Id));
            html.Append("<p><strong>").Append(WebUtility.HtmlEncode(notice)).Append("</strong> <a href=\"")
                .Append(WebUtility.HtmlEncode(link)).Append("\">Pay now</a></p>");
            text += " " + notice + " " + link;
        }
        return new PlaceholderValue(text, html.ToString());
    }

    private static string StepLabel(DraftStep s) => s switch
    {
        DraftStep.Details => "Your details",
        DraftStep.Addons => "Add-ons",
        DraftStep.Summary => "Summary",
        _ => "Payment",
    };

    // ----- Sending -----

    private Task SendToStaffAsync(EmailTemplateKey key, BusinessSettings settings, Dictionary<string, PlaceholderValue> values, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.StaffAlertEmail))
        {
            logger.LogWarning("No staff alert email is set; {Key} was not sent", key);
            return Task.CompletedTask;
        }
        return SendAsync(key, settings.StaffAlertEmail, values, ct);
    }

    private async Task SendAsync(EmailTemplateKey key, string to, IReadOnlyDictionary<string, PlaceholderValue> values, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to)) throw new InvalidOperationException($"No recipient for {key}.");
        var template = await templates.GetAsync(key, ct);
        var (subject, body) = template is null ? DefaultEmailTemplates.All[key] : (template.Subject, template.HtmlBody);
        var message = new EmailMessage(to, EmailRenderer.Render(subject, values, html: false), EmailRenderer.Render(body, values, html: true));
        await sender.SendAsync(message, ct);
        logger.LogInformation("Sent {Key} email", key);
    }
}
