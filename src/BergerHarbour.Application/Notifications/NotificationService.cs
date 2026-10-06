using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Notifications;

/// <summary>Keys recorded in <see cref="Booking.SentNotifications"/> so each email is sent once.</summary>
public static class NotificationKeys
{
    public const string BookingConfirmed = "BookingConfirmed";
    public const string StaffNewOnlineBooking = "StaffNewOnlineBooking";
    public const string PreHire = "PreHire";
    public const string StandbySuperseded = "StandbySuperseded";

    public static string PaymentDueReminder(MilestoneKind kind) => $"PaymentDue:{kind}:Reminder";

    public static string PaymentDueOnDueDate(MilestoneKind kind) => $"PaymentDue:{kind}:Due";

    public static string Overdue(MilestoneKind kind) => $"Overdue:{kind}";

    public static string PaymentReceived(string paymentId) => $"PaymentReceived:{paymentId}";
}

/// <summary>
/// Sends the system's emails (#1, #2, #4, #5, #6, #7, #8). No email of any kind is sent for stand-by bookings or
/// for bookings that are not Active, except #8 to staff. Keyed emails are recorded on the booking after a
/// successful send.
/// </summary>
public sealed class NotificationService(
    IEmailSender sender,
    IEmailTemplateRepository templates,
    IBoatRepository boats,
    ICustomerRepository customers,
    IBusinessSettingsRepository settingsRepository,
    IBookingRepository bookings,
    PaymentScheduleService paymentSchedule,
    PaymentLinkTokens paymentLinks,
    IClock clock,
    ILogger<NotificationService> logger)
{
    /// <summary>#1 to the customer.</summary>
    public Task<bool> SendBookingConfirmedAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.BookingConfirmed, Recipient.Customer, NotificationKeys.BookingConfirmed,
            null, ct);

    /// <summary>#2 to staff.</summary>
    public Task<bool> SendStaffNewOnlineBookingAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.StaffNewOnlineBooking, Recipient.StaffAboutActiveBooking,
            NotificationKeys.StaffNewOnlineBooking, null, ct);

    /// <summary>#4 to the customer. A null key means a manual "Send payment link", which is not deduplicated.</summary>
    public Task<bool> SendPaymentDueAsync(Booking booking, PaymentMilestone? milestone, string? key,
        CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.PaymentDue, Recipient.Customer, key, milestone, ct);

    /// <summary>#5 to the customer.</summary>
    public Task<bool> SendPaymentReceivedAsync(Booking booking, Payment payment, CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.PaymentReceived, Recipient.Customer, NotificationKeys.PaymentReceived(payment.Id),
            null, ct);

    /// <summary>#6 to the customer.</summary>
    public Task<bool> SendPreHireInstructionsAsync(Booking booking, CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.PreHireInstructions, Recipient.Customer, NotificationKeys.PreHire, null, ct);

    /// <summary>#7 to staff.</summary>
    public Task<bool> SendStaffBalanceOverdueAsync(Booking booking, PaymentMilestone milestone, CancellationToken ct = default) =>
        SendAsync(booking, EmailTemplateKey.StaffBalanceOverdue, Recipient.StaffAboutActiveBooking,
            NotificationKeys.Overdue(milestone.Kind), milestone, ct);

    /// <summary>#8 to staff, about a stand-by booking that was superseded.</summary>
    public Task<bool> SendStaffStandbySupersededAsync(Booking standby, CancellationToken ct = default) =>
        SendAsync(standby, EmailTemplateKey.StaffStandbySuperseded, Recipient.StaffAboutStandby,
            NotificationKeys.StandbySuperseded, null, ct);

    /// <summary>Sends a sample of the template to the staff alert address.</summary>
    public async Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default)
    {
        var settings = await settingsRepository.GetAsync(ct);
        var template = await templates.GetAsync(key, ct) ?? throw new InvalidOperationException($"Template {key} is missing.");
        var now = clock.UtcNow;
        var boat = Boat.Create("Pacific Blue", "pacific-blue", BoatType.House, 12, now);
        boat.UpdateDetails("Pacific Blue", "pacific-blue", 12, 8, "4 queen bedrooms, bunk area (2 singles)", 2000m, now);
        var customer = Customer.Create("Sample Customer", "sample@example.com", "0400 000 000", now);
        var start = SydneyTime.Today(now).AddDays(60);
        var booking = Booking.CreateByStaff(BookingReference.Create(start.Year, 1), customer.Id, boat.Id, PeriodType.Midweek,
            new Stay(start, start.AddDays(4)), 8, false, null, 4060m, settings.DepositAmount, PaymentSchedule.Standard,
            "sample", now);
        booking.AddAddon(new RequestedAddon(AddonId.New(), "Outboard motor", 1, 95m), now);
        booking.AddAddon(new RequestedAddon(AddonId.New(), "Welcome hamper", 1, null), now);
        booking.RecordManualPayment(settings.DepositAmount, now, null, now);
        var milestones = paymentSchedule.Milestones(booking);
        var data = new EmailData(booking, boat, customer, settings, milestones,
            $"{paymentLinks.LinkFor(booking.Id).Split('?')[0]}?token=SAMPLE", booking.AmountOwing, milestones[^1].DueDate);
        var (subject, body) = EmailComposer.Compose(template, data);
        await sender.SendAsync(new EmailMessage(settings.StaffAlertEmail, "Berger Houseboats staff", "[TEST] " + subject, body), ct);
    }

    private enum Recipient
    {
        Customer,
        StaffAboutActiveBooking,
        StaffAboutStandby,
    }

    private async Task<bool> SendAsync(Booking booking, EmailTemplateKey templateKey, Recipient recipient, string? key,
        PaymentMilestone? milestone, CancellationToken ct)
    {
        var allowed = recipient switch
        {
            Recipient.StaffAboutStandby => booking.IsStandby,
            _ => booking.ReceivesCustomerEmails,
        };
        if (!allowed)
        {
            logger.LogInformation("Not sending {Template} for {Reference}: status {Status}, stand-by {Standby}",
                templateKey, booking.Reference.Value, booking.Status, booking.IsStandby);
            return false;
        }

        if (key is not null && booking.HasSentNotification(key))
        {
            return false;
        }

        var settings = await settingsRepository.GetAsync(ct);
        var template = await templates.GetAsync(templateKey, ct)
                       ?? throw new InvalidOperationException($"Email template {templateKey} is missing. Run the seed.");
        var boat = await boats.GetAsync(booking.BoatId, ct)
                   ?? throw new InvalidOperationException($"Boat {booking.BoatId} not found.");
        var customer = await customers.GetAsync(booking.CustomerId, ct)
                       ?? throw new InvalidOperationException($"Customer {booking.CustomerId} not found.");
        var now = clock.UtcNow;
        var milestones = paymentSchedule.Milestones(booking);
        var amountDue = paymentSchedule.AmountDueNow(booking, now);
        var dueDate = milestone?.DueDate ?? NextDueDate(milestones, booking, now);
        var data = new EmailData(booking, boat, customer, settings, milestones, paymentLinks.LinkFor(booking.Id),
            amountDue, dueDate);
        var (subject, body) = EmailComposer.Compose(template, data);

        var message = recipient == Recipient.Customer
            ? new EmailMessage(customer.Email.Value, customer.FullName, subject, body)
            : new EmailMessage(settings.StaffAlertEmail, "Berger Houseboats staff", subject, body);
        await sender.SendAsync(message, ct);
        logger.LogInformation("Sent {Template} for {Reference}", templateKey, booking.Reference.Value);

        if (key is not null)
        {
            await RecordSentAsync(booking, key, now, ct);
        }

        return true;
    }

    /// <summary>The earliest milestone that is not yet met, or today.</summary>
    private static DateOnly NextDueDate(IReadOnlyList<PaymentMilestone> milestones, Booking booking, DateTimeOffset now) =>
        milestones.Where(m => m.Kind != MilestoneKind.Deposit && booking.AmountPaid < m.RequiredCumulative)
            .Select(m => (DateOnly?)m.DueDate)
            .FirstOrDefault() ?? SydneyTime.Today(now);

    private async Task RecordSentAsync(Booking booking, string key, DateTimeOffset sentAt, CancellationToken ct)
    {
        booking.RecordNotificationSent(key, sentAt);
        try
        {
            await bookings.SaveAsync(booking, ct);
            return;
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else saved the booking meanwhile; record the key on the latest version.
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var fresh = await bookings.GetAsync(booking.Id, ct);
            if (fresh is null)
            {
                return;
            }

            fresh.RecordNotificationSent(key, sentAt);
            try
            {
                await bookings.SaveAsync(fresh, ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
            }
        }
    }
}
