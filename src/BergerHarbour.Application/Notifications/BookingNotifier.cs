using System.Net;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Notifications;

/// <summary>
/// Sends the system's emails (§9). It never sends customer emails for stand-bys or for bookings that are not Active, and
/// records idempotency keys on the booking: the caller saves the booking afterwards.
/// </summary>
public sealed class BookingNotifier
{
    private readonly IEmailSender sender;
    private readonly IEmailTemplateRepository templates;
    private readonly ReferenceDataLoader referenceData;
    private readonly IBoatRepository boats;
    private readonly ICustomerRepository customers;
    private readonly IBookingRepository bookings;
    private readonly PaymentScheduleService schedule;
    private readonly FrequentFloaterService frequentFloaters;
    private readonly PaymentLinkTokens links;
    private readonly EmailRenderer renderer;
    private readonly IClock clock;
    private readonly ILogger<BookingNotifier> logger;

    public BookingNotifier(
        IEmailSender sender, IEmailTemplateRepository templates, ReferenceDataLoader referenceData, IBoatRepository boats, ICustomerRepository customers,
        IBookingRepository bookings, PaymentScheduleService schedule, FrequentFloaterService frequentFloaters, PaymentLinkTokens links,
        EmailRenderer renderer, IClock clock, ILogger<BookingNotifier> logger)
    {
        this.sender = sender;
        this.templates = templates;
        this.referenceData = referenceData;
        this.boats = boats;
        this.customers = customers;
        this.bookings = bookings;
        this.schedule = schedule;
        this.frequentFloaters = frequentFloaters;
        this.links = links;
        this.renderer = renderer;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// #1 to the customer. If a milestone was already due at creation, the schedule states the balance now due and the
    /// payment link.
    /// </summary>
    public async Task<bool> SendBookingConfirmedAsync(Booking booking, CancellationToken ct = default)
    {
        if (!booking.IsNotifiable || booking.HasSentNotification(NotificationKeys.BookingConfirmed))
        {
            return false;
        }

        var amountDue = schedule.AmountDueNow(booking, clock.UtcNow);
        var dueAtCreation = schedule.Milestones(booking).Any(m => m.Kind != MilestoneKind.Deposit && m.DueAtCreation && m.ShortfallFor(booking.AmountPaid) > 0);
        var sent = await SendForBookingAsync(EmailTemplateKey.BookingConfirmed, booking, toStaff: false, amountDue, null, dueAtCreation, ct);
        if (sent)
        {
            booking.RecordNotificationSent(NotificationKeys.BookingConfirmed, clock.UtcNow);
        }

        return sent;
    }

    /// <summary>#2 to staff when an online booking becomes Active, with the Frequent Floater notice.</summary>
    public async Task<bool> SendStaffNewOnlineBookingAsync(Booking booking, CancellationToken ct = default)
    {
        if (!booking.IsNotifiable || booking.HasSentNotification(NotificationKeys.StaffNewOnlineBooking))
        {
            return false;
        }

        var sent = await SendForBookingAsync(EmailTemplateKey.StaffNewOnlineBooking, booking, toStaff: true, booking.AmountOwing, null, false, ct);
        if (sent)
        {
            booking.RecordNotificationSent(NotificationKeys.StaffNewOnlineBooking, clock.UtcNow);
        }

        return sent;
    }

    /// <summary>#4 to the customer, with the payment link. Deduplicated by the caller's key (none for manual sends).</summary>
    public async Task<bool> SendPaymentDueAsync(Booking booking, decimal amountDue, DateOnly? dueDate, string? key, CancellationToken ct = default)
    {
        if (!booking.IsNotifiable || (key is not null && booking.HasSentNotification(key)))
        {
            return false;
        }

        var sent = await SendForBookingAsync(EmailTemplateKey.PaymentDue, booking, toStaff: false, amountDue, dueDate, false, ct);
        if (sent && key is not null)
        {
            booking.RecordNotificationSent(key, clock.UtcNow);
        }

        return sent;
    }

    /// <summary>#5 to the customer for any payment other than the deposit checkout.</summary>
    public Task<bool> SendPaymentReceivedAsync(Booking booking, Payment payment, CancellationToken ct = default) =>
        booking.IsNotifiable
            ? SendForBookingAsync(EmailTemplateKey.PaymentReceived, booking, toStaff: false, payment.Amount, null, false, ct)
            : Task.FromResult(false);

    /// <summary>#6 pre-hire instructions.</summary>
    public async Task<bool> SendPreHireInstructionsAsync(Booking booking, CancellationToken ct = default)
    {
        if (!booking.IsNotifiable || booking.HasSentNotification(NotificationKeys.PreHire))
        {
            return false;
        }

        var sent = await SendForBookingAsync(EmailTemplateKey.PreHireInstructions, booking, toStaff: false, booking.AmountOwing, null, false, ct);
        if (sent)
        {
            booking.RecordNotificationSent(NotificationKeys.PreHire, clock.UtcNow);
        }

        return sent;
    }

    /// <summary>#7 to staff, once per milestone.</summary>
    public async Task<bool> SendBalanceOverdueAsync(Booking booking, PaymentMilestone milestone, CancellationToken ct = default)
    {
        var key = NotificationKeys.Overdue(milestone.Kind);
        if (!booking.IsNotifiable || booking.HasSentNotification(key))
        {
            return false;
        }

        var sent = await SendForBookingAsync(EmailTemplateKey.StaffBalanceOverdue, booking, toStaff: true, milestone.ShortfallFor(booking.AmountPaid),
            milestone.DueDate, false, ct);
        if (sent)
        {
            booking.RecordNotificationSent(key, clock.UtcNow);
        }

        return sent;
    }

    /// <summary>#8 to staff: a stand-by was superseded (the only email about a non-Active booking).</summary>
    public Task<bool> SendStandbySupersededAsync(Booking standby, CancellationToken ct = default) =>
        SendForBookingAsync(EmailTemplateKey.StaffStandbySuperseded, standby, toStaff: true, standby.AmountOwing, null, false, ct);

    /// <summary>#10 to staff: an abandoned booking draft, so they can follow up.</summary>
    public async Task<bool> SendAbandonedDraftAsync(BookingDraft draft, CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        var boat = await boats.GetAsync(draft.Contents.BoatId, ct);
        var c = draft.Contents;
        var values = CommonValues(reference.Settings);
        values["CustomerName"] = EmailValue.Text($"{c.FirstName} {c.LastName}");
        values["CustomerFirstName"] = EmailValue.Text(c.FirstName);
        values["CustomerEmail"] = EmailValue.Text(c.Email.Value);
        values["CustomerMobile"] = EmailValue.Text(c.MobileNumber.Display);
        values["BoatName"] = EmailValue.Text(boat?.Name);
        values["CheckInDate"] = EmailValue.Text(DisplayFormat.Date(c.Stay.Start));
        values["CheckOutDate"] = EmailValue.Text(DisplayFormat.Date(c.Stay.End));
        values["CheckInTime"] = EmailValue.Text(DisplayFormat.CheckIn(c.Stay.Start, Domain.Shared.Sydney.StandardCheckIn));
        values["CheckOutTime"] = EmailValue.Text(DisplayFormat.CheckOut(c.Stay.End, Domain.Shared.Sydney.StandardCheckOut));
        values["NumberOfAdults"] = EmailValue.Text(c.NumberOfAdults.ToString(System.Globalization.CultureInfo.InvariantCulture));
        values["NumberOfChildren"] = EmailValue.Text(c.NumberOfChildren.ToString(System.Globalization.CultureInfo.InvariantCulture));
        values["NumberOfGuests"] = EmailValue.Text((c.NumberOfAdults + c.NumberOfChildren).ToString(System.Globalization.CultureInfo.InvariantCulture));
        values["SecurityBond"] = EmailValue.Text(boat is null ? null : DisplayFormat.Money(boat.SecurityBond));
        values["LastStepReached"] = EmailValue.Text(c.LastStepReached.ToString());
        values["RequestedAddons"] = AddonList(c.AddonRequests.Select(a => (a.AddonId, (string?)null, a.Quantity, (decimal?)null)), reference, c.PeriodType);
        return await SendAsync(EmailTemplateKey.StaffAbandonedBooking, reference.Settings.StaffAlertEmail?.Value, values, ct);
    }

    /// <summary>Sends a template with sample values to the staff alert address.</summary>
    public async Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default)
    {
        var settings = await referenceData.SettingsAsync(ct);
        var to = settings.StaffAlertEmail?.Value ?? throw new Domain.Shared.BusinessRuleException("Set the staff alert email in Settings first.");
        var values = CommonValues(settings);
        var start = Domain.Shared.Sydney.DateOf(clock.UtcNow).AddDays(60);
        var sample = new Dictionary<string, string>
        {
            ["CustomerName"] = "Jane Smith",
            ["CustomerFirstName"] = "Jane",
            ["CustomerEmail"] = "jane@example.com",
            ["CustomerMobile"] = "0412 345 678",
            ["BookingReference"] = "SMITH-" + start.ToString("yyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "0",
            ["BoatName"] = "Pacific Blue",
            ["CheckInDate"] = DisplayFormat.Date(start),
            ["CheckOutDate"] = DisplayFormat.Date(start.AddDays(4)),
            ["CheckInTime"] = DisplayFormat.CheckIn(start, Domain.Shared.Sydney.StandardCheckIn),
            ["CheckOutTime"] = DisplayFormat.CheckOut(start.AddDays(4), Domain.Shared.Sydney.StandardCheckOut),
            ["NumberOfGuests"] = "6",
            ["NumberOfAdults"] = "4",
            ["NumberOfChildren"] = "2",
            ["HirePrice"] = DisplayFormat.Money(4060),
            ["TotalPrice"] = DisplayFormat.Money(4155),
            ["AmountPaid"] = DisplayFormat.Money(1000),
            ["AmountOwing"] = DisplayFormat.Money(3155),
            ["AmountDue"] = DisplayFormat.Money(3155),
            ["DueDate"] = DisplayFormat.Date(start.AddDays(-30)),
            ["PaymentLink"] = links.BaseUrl + "/pay/?token=sample",
            ["SecurityBond"] = DisplayFormat.Money(2000),
            ["LastStepReached"] = "Summary",
            ["FrequentFloaterNotice"] = "⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one.",
        };
        foreach (var (name, value) in sample)
        {
            values[name] = EmailValue.Text(value);
        }

        values["RequestedAddons"] = EmailValue.Markup("Outboard motor × 1", "<ul><li>Outboard motor × 1 — $95.00</li></ul>");
        values["PaymentSchedule"] = EmailValue.Markup("Deposit, Balance", "<ul><li>Deposit: $1,000.00 (paid)</li><li>Balance: $4,155.00 by " +
            DisplayFormat.Date(start.AddDays(-30)) + "</li></ul>");
        await SendAsync(key, to, values, ct);
    }

    private async Task<bool> SendForBookingAsync(
        EmailTemplateKey key, Booking booking, bool toStaff, decimal amountDue, DateOnly? dueDate, bool balanceDueAtCreation, CancellationToken ct)
    {
        var reference = await referenceData.LoadAsync(ct);
        var boat = await boats.GetAsync(booking.BoatId, ct);
        var customer = booking.CustomerId is { } id ? await customers.GetAsync(id, ct) : null;
        var values = CommonValues(reference.Settings);
        var firstName = customer?.FirstName ?? booking.CustomerDetails?.FirstName ?? string.Empty;
        var lastName = customer?.LastName ?? booking.CustomerDetails?.LastName ?? string.Empty;
        var email = customer?.Email.Value ?? booking.CustomerDetails?.Email.Value;
        var mobile = customer?.MobileNumber?.Display ?? booking.CustomerDetails?.MobileNumber.Display;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        values["CustomerName"] = EmailValue.Text($"{firstName} {lastName}".Trim());
        values["CustomerFirstName"] = EmailValue.Text(firstName);
        values["CustomerEmail"] = EmailValue.Text(email);
        values["CustomerMobile"] = EmailValue.Text(mobile);
        values["BookingReference"] = EmailValue.Text(booking.Reference?.Value);
        values["BoatName"] = EmailValue.Text(boat?.Name);
        values["CheckInDate"] = EmailValue.Text(DisplayFormat.Date(booking.StartDate));
        values["CheckOutDate"] = EmailValue.Text(DisplayFormat.Date(booking.EndDate));
        values["CheckInTime"] = EmailValue.Text(DisplayFormat.CheckIn(booking.StartDate, booking.CheckInTime));
        values["CheckOutTime"] = EmailValue.Text(DisplayFormat.CheckOut(booking.EndDate, booking.CheckOutTime));
        values["NumberOfGuests"] = EmailValue.Text(booking.NumberOfGuests.ToString(inv));
        values["NumberOfAdults"] = EmailValue.Text(booking.NumberOfAdults.ToString(inv));
        values["NumberOfChildren"] = EmailValue.Text(booking.NumberOfChildren.ToString(inv));
        values["HirePrice"] = EmailValue.Text(DisplayFormat.Money(booking.HirePrice));
        values["TotalPrice"] = EmailValue.Text(DisplayFormat.Money(booking.TotalPrice));
        values["AmountPaid"] = EmailValue.Text(DisplayFormat.Money(booking.AmountPaid));
        values["AmountOwing"] = EmailValue.Text(DisplayFormat.Money(booking.AmountOwing));
        values["AmountDue"] = EmailValue.Text(DisplayFormat.Money(amountDue));
        values["DueDate"] = EmailValue.Text(dueDate is { } d ? DisplayFormat.Date(d) : null);
        var link = links.LinkFor(booking.Id);
        values["PaymentLink"] = EmailValue.Text(link);
        values["SecurityBond"] = EmailValue.Text(boat is null ? null : DisplayFormat.Money(boat.SecurityBond));
        values["RequestedAddons"] = AddonList(booking.AddonLines.Select(l => (l.AddonId, (string?)l.NameSnapshot, l.Quantity, l.UnitPrice)), reference,
            booking.PeriodType);
        values["PaymentSchedule"] = ScheduleList(booking, amountDue, balanceDueAtCreation, link);
        if (key == EmailTemplateKey.StaffNewOnlineBooking && booking.CustomerId is { } customerId && booking.ActivatedAt is { } anchor)
        {
            var result = frequentFloaters.Evaluate(anchor, await bookings.ListByCustomerAsync(customerId, ct), booking.Id);
            if (result.IsFrequentFloater)
            {
                var plural = result.OtherBookingCount == 1 ? "booking" : "bookings";
                values["FrequentFloaterNotice"] = EmailValue.Text(
                    $"⭐ Frequent Floater: {firstName} {lastName} has {result.OtherBookingCount} other {plural} within a year of this one.");
            }
        }

        return await SendAsync(key, toStaff ? reference.Settings.StaffAlertEmail?.Value : email, values, ct);
    }

    private async Task<bool> SendAsync(EmailTemplateKey key, string? to, Dictionary<string, EmailValue> values, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Email {Key} not sent: no recipient (set the staff alert email in Settings).", key);
            return false;
        }

        var template = await templates.GetAsync(key, ct);
        if (template is null)
        {
            logger.LogError("Email {Key} not sent: the template is missing. Run the seed.", key);
            return false;
        }

        var email = renderer.Render(template, values);
        await sender.SendAsync(new EmailMessage(to, email.Subject, email.HtmlBody), ct);
        logger.LogInformation("Sent email {Key}.", key);
        return true;
    }

    private static Dictionary<string, EmailValue> CommonValues(BusinessSettings settings) => new(StringComparer.Ordinal)
    {
        ["HireTermsUrl"] = EmailValue.Text(settings.HireTermsUrl),
        ["ContactPhone"] = EmailValue.Text(settings.ContactPhone),
        ["ContactEmail"] = EmailValue.Text(settings.ContactEmail?.Value),
        ["AccountName"] = EmailValue.Text(settings.AccountName),
        ["BSB"] = EmailValue.Text(settings.BsbDisplay),
        ["AccountNumber"] = EmailValue.Text(settings.AccountNumber),
    };

    /// <summary>Requested add-ons in the admin-defined order.</summary>
    private static EmailValue AddonList(
        IEnumerable<(AddonId AddonId, string? Name, int Quantity, decimal? UnitPrice)> lines, ReferenceData reference, Domain.Shared.PeriodType periodType)
    {
        var ordered = lines
            .Select(l => (Line: l, Addon: reference.Addon(l.AddonId)))
            .OrderBy(x => x.Addon?.SortOrder ?? int.MaxValue)
            .Select(x =>
            {
                var name = x.Line.Name ?? x.Addon?.Name ?? "Add-on";
                var unit = x.Line.UnitPrice ?? (x.Line.Name is null ? x.Addon?.PriceFor(periodType) : null);
                var price = unit is { } p ? DisplayFormat.Money(p * x.Line.Quantity) : "price on request";
                return $"{name} × {x.Line.Quantity} — {price}";
            })
            .ToList();
        if (ordered.Count == 0)
        {
            return EmailValue.Markup("None", "<p>None</p>");
        }

        return EmailValue.Markup(
            string.Join(", ", ordered),
            "<ul>" + string.Concat(ordered.Select(o => $"<li>{WebUtility.HtmlEncode(o)}</li>")) + "</ul>");
    }

    private EmailValue ScheduleList(Booking booking, decimal amountDue, bool balanceDueAtCreation, string link)
    {
        var items = schedule.Milestones(booking)
            .Select(m =>
            {
                var shortfall = m.ShortfallFor(booking.AmountPaid);
                var when = m.Kind == MilestoneKind.Deposit ? "at booking" : $"by {DisplayFormat.Date(m.DueDate)}";
                var status = shortfall == 0 ? " (paid)" : string.Empty;
                return $"{m.Label}: {DisplayFormat.Money(m.RequiredCumulative)} paid in total {when}{status}";
            })
            .ToList();
        var html = "<ul>" + string.Concat(items.Select(i => $"<li>{WebUtility.HtmlEncode(i)}</li>")) + "</ul>";
        var plain = string.Join("; ", items);
        if (balanceDueAtCreation && amountDue > 0)
        {
            var notice = $"The balance of {DisplayFormat.Money(amountDue)} is now due. Please pay it using your payment link: ";
            html += $"<p><strong>{WebUtility.HtmlEncode(notice)}</strong><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p>";
            plain += ". " + notice + link;
        }

        return EmailValue.Markup(plain, html);
    }
}
