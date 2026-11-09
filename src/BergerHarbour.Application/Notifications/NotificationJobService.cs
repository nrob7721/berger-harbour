using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Notifications.Dtos;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Notifications;

/// <summary>
/// The hourly <c>process-notifications</c> job (<c>JobsController</c>). Idempotent and safe at any frequency: expires lapsed
/// holds, sends due #4, #6 and #7, reports abandoned drafts (#10) and deletes old drafts.
/// </summary>
public sealed class NotificationJobService
{
    private readonly IBookingRepository bookings;
    private readonly IBookingDraftRepository drafts;
    private readonly ICustomerRepository customers;
    private readonly PaymentScheduleService schedule;
    private readonly BookingNotifier notifier;
    private readonly BookingOptions options;
    private readonly IClock clock;
    private readonly ILogger<NotificationJobService> logger;

    public NotificationJobService(
        IBookingRepository bookings, IBookingDraftRepository drafts, ICustomerRepository customers, PaymentScheduleService schedule,
        BookingNotifier notifier, IOptions<BookingOptions> options, IClock clock, ILogger<NotificationJobService> logger)
    {
        this.bookings = bookings;
        this.drafts = drafts;
        this.customers = customers;
        this.schedule = schedule;
        this.notifier = notifier;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<NotificationJobResult> RunAsync(CancellationToken ct = default)
    {
        var failures = 0;
        var expired = await ExpireHoldsAsync(ct);
        var (sent, bookingFailures) = await SendBookingEmailsAsync(ct);
        failures += bookingFailures;
        var (reported, draftFailures) = await ReportAbandonedDraftsAsync(ct);
        failures += draftFailures;
        var deleted = await DeleteOldDraftsAsync(ct);
        var result = new NotificationJobResult(expired, sent, reported, deleted, failures);
        if (failures > 0)
        {
            logger.LogError("process-notifications finished with {Failures} failures: {@Result}", failures, result);
        }
        else
        {
            logger.LogInformation("process-notifications finished: {@Result}", result);
        }

        return result;
    }

    private async Task<int> ExpireHoldsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var count = 0;
        foreach (var hold in await bookings.ListByStatusAsync(BookingStatus.PendingPayment, ct))
        {
            if (hold.HoldExpiresAt > now)
            {
                continue;
            }

            hold.Expire(now);
            try
            {
                await bookings.SaveAsync(hold, ct);
                count++;
            }
            catch (ConcurrencyException)
            {
                // Paid or released meanwhile; the next run sees the new state.
            }
        }

        return count;
    }

    private async Task<(int Sent, int Failures)> SendBookingEmailsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sent = 0;
        var failures = 0;
        foreach (var booking in await bookings.ListActiveStartingFromAsync(Sydney.DateOf(now), ct))
        {
            if (!booking.IsNotifiable)
            {
                continue;
            }

            try
            {
                var count = await SendDueAsync(booking, now, ct);
                if (count > 0)
                {
                    await bookings.SaveAsync(booking, ct);
                    sent += count;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures++;
                logger.LogError(e, "process-notifications failed for booking {BookingId}.", booking.Id.Value);
            }
        }

        return (sent, failures);
    }

    /// <summary>
    /// #4 at <c>due − reminder</c> and at <c>due</c>, #7 once <c>now &gt; due</c>, each per milestone with a shortfall and
    /// never when its trigger was at or before the booking's creation; #6 at <c>check-in − pre-hire</c>.
    /// </summary>
    private async Task<int> SendDueAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        var sent = 0;
        foreach (var m in schedule.Milestones(booking).Where(m => m.Kind != MilestoneKind.Deposit))
        {
            var shortfall = m.ShortfallFor(booking.AmountPaid);
            if (shortfall <= 0m)
            {
                continue;
            }

            var reminderAt = schedule.ReminderAt(m);
            if (now >= m.DueAt && m.DueAt > booking.CreatedDate)
            {
                if (await notifier.SendPaymentDueAsync(booking, shortfall, m.DueDate, NotificationKeys.PaymentDue(m.Kind), ct))
                {
                    sent++;
                }

                // A run that comes after the due instant never sends the earlier reminder as well.
                booking.RecordNotificationSent(NotificationKeys.PaymentDueReminder(m.Kind), now);
                if (now > m.DueAt && await notifier.SendBalanceOverdueAsync(booking, m, ct))
                {
                    sent++;
                }
            }
            else if (now >= reminderAt && now < m.DueAt && reminderAt > booking.CreatedDate
                && await notifier.SendPaymentDueAsync(booking, shortfall, m.DueDate, NotificationKeys.PaymentDueReminder(m.Kind), ct))
            {
                sent++;
            }
        }

        if (now >= schedule.PreHireInstructionsAt(booking.StartDate) && await notifier.SendPreHireInstructionsAsync(booking, ct))
        {
            sent++;
        }

        return sent;
    }

    private async Task<(int Reported, int Failures)> ReportAbandonedDraftsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reported = 0;
        var failures = 0;
        foreach (var draft in await drafts.ListAbandonCandidatesAsync(now - options.AbandonedDraftAfter, ct))
        {
            try
            {
                if (!draft.IsAbandoned(now, options.AbandonedDraftAfter) || draft.AbandonedNotifiedAt is not null || !await IsLatestUnbookedAsync(draft, ct))
                {
                    continue;
                }

                if (await notifier.SendAbandonedDraftAsync(draft, ct))
                {
                    draft.MarkAbandonedNotified(now);
                    await drafts.SaveAsync(draft, ct);
                    reported++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures++;
                logger.LogError(e, "process-notifications failed for booking draft {DraftId}.", draft.Id.Value);
            }
        }

        return (reported, failures);
    }

    /// <summary>Only the latest draft per email, and not when a booking for that email became Active after the draft was created.</summary>
    private async Task<bool> IsLatestUnbookedAsync(BookingDraft draft, CancellationToken ct)
    {
        var email = draft.Contents.Email;
        var latest = (await drafts.ListByEmailAsync(email, ct)).MaxBy(d => d.LastActivityAt);
        if (latest is not null && latest.Id != draft.Id)
        {
            return false;
        }

        var customer = await customers.GetByEmailAsync(email, ct);
        if (customer is null)
        {
            return true;
        }

        return !(await bookings.ListByCustomerAsync(customer.Id, ct)).Any(b => b.ActivatedAt > draft.CreatedDate);
    }

    private async Task<int> DeleteOldDraftsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var deleted = 0;
        foreach (var draft in await drafts.ListCreatedBeforeAsync(now - options.DraftRetention, ct))
        {
            await drafts.DeleteAsync(draft.Id, ct);
            deleted++;
        }

        return deleted;
    }
}
