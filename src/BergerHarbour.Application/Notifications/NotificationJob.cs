using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Notifications;

public sealed record NotificationJobResult(int ExpiredHolds, int EmailsSent, int Failures, int DraftsDeleted = 0);

/// <summary>
/// The hourly process-notifications job. Idempotent and safe to run at any frequency. Each run expires lapsed holds,
/// sends due #4, #6 and #7 emails for Active non-stand-by bookings starting today or later, sends #10 for abandoned
/// booking drafts, and deletes drafts past their retention.
/// </summary>
public sealed class NotificationJob(
    IBookingRepository bookings,
    IBookingDraftRepository drafts,
    ICustomerRepository customers,
    NotificationService notifications,
    PaymentScheduleService paymentSchedule,
    IOptions<NotificationOptions> options,
    IOptions<BookingOptions> bookingOptions,
    IClock clock,
    ILogger<NotificationJob> logger)
{
    public async Task<NotificationJobResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var expired = 0;
        var sent = 0;
        var failures = 0;

        foreach (var hold in await bookings.ListByStatusAsync(BookingStatus.PendingPayment, ct))
        {
            if (hold.HoldExpiresAt is { } expiresAt && expiresAt <= now)
            {
                try
                {
                    hold.Expire(now);
                    await bookings.SaveAsync(hold, ct);
                    expired++;
                }
                catch (Exception ex)
                {
                    // A concurrent webhook may have just activated it; the next run looks again.
                    logger.LogWarning(ex, "Could not expire hold {Reference}", hold.Reference.Value);
                }
            }
        }

        var upcoming = await bookings.ListByStatusStartingFromAsync(BookingStatus.Active, SydneyTime.Today(now), ct);
        foreach (var booking in upcoming.Where(b => !b.IsStandby).OrderBy(b => b.StartDate))
        {
            try
            {
                sent += await ProcessBookingAsync(booking, now, ct);
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "Notification processing failed for booking {Reference}", booking.Reference.Value);
            }
        }

        var (abandonedSent, abandonedFailures) = await NotifyAbandonedDraftsAsync(now, ct);
        sent += abandonedSent;
        failures += abandonedFailures;
        var deleted = await DeleteOldDraftsAsync(now, ct);

        logger.LogInformation(
            "process-notifications: {Expired} holds expired, {Sent} emails sent, {Deleted} drafts deleted, {Failures} failures",
            expired, sent, deleted, failures);
        return new NotificationJobResult(expired, sent, failures, deleted);
    }

    /// <summary>
    /// #4 at the reminder and due instants, #7 once overdue, #6 before check-in. A #4 or #7 whose trigger had already
    /// passed when the booking was created is never sent (the confirmation email carried the payment link instead).
    /// </summary>
    private async Task<int> ProcessBookingAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        var sent = 0;
        var paid = booking.AmountPaid;
        foreach (var milestone in paymentSchedule.Milestones(booking).Where(m => m.Kind != MilestoneKind.Deposit))
        {
            if (milestone.RequiredCumulative - paid <= 0)
            {
                continue;
            }

            if (now >= milestone.DueAt)
            {
                if (!PaymentScheduleService.TriggerPassedAtCreation(milestone.DueAt, booking) &&
                    await notifications.SendPaymentDueAsync(booking, milestone, NotificationKeys.PaymentDueOnDueDate(milestone.Kind), ct))
                {
                    sent++;
                }

                if (now > milestone.DueAt && !PaymentScheduleService.WasDueAtCreation(milestone, booking) &&
                    await notifications.SendStaffBalanceOverdueAsync(booking, milestone, ct))
                {
                    sent++;
                }
            }
            else if (now >= paymentSchedule.ReminderAt(milestone) &&
                     !PaymentScheduleService.TriggerPassedAtCreation(paymentSchedule.ReminderAt(milestone), booking) &&
                     await notifications.SendPaymentDueAsync(booking, milestone, NotificationKeys.PaymentDueReminder(milestone.Kind), ct))
            {
                sent++;
            }
        }

        // A booking created inside the pre-hire window gets #6 at the next run.
        var preHireAt = SydneyTime.CheckInInstant(booking.StartDate) - options.Value.PreHireInstructionsBeforeHire;
        if (now >= preHireAt && await notifications.SendPreHireInstructionsAsync(booking, ct))
        {
            sent++;
        }

        return sent;
    }

    /// <summary>#10 once per abandoned draft: only the latest draft per email, and not after that email booked.</summary>
    private async Task<(int Sent, int Failures)> NotifyAbandonedDraftsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var abandonedAfter = bookingOptions.Value.AbandonedDraftAfter;
        var sent = 0;
        var failures = 0;
        var candidates = (await drafts.ListInactiveSinceAsync(now - abandonedAfter, ct))
            .Where(d => d.AbandonedNotifiedAt is null && d.IsAbandoned(now, abandonedAfter))
            .OrderBy(d => d.LastActivityAt);
        foreach (var draft in candidates)
        {
            try
            {
                var draftsForEmail = await drafts.ListByEmailAsync(draft.Email, ct);
                var customer = await customers.GetByEmailAsync(draft.Email, ct);
                var bookingsForEmail = customer is null ? [] : await bookings.ListByCustomerAsync([customer.Id], ct);
                if (!AbandonedDraftPolicy.ShouldNotify(draft, draftsForEmail, bookingsForEmail, now, abandonedAfter))
                {
                    continue;
                }

                await notifications.SendStaffAbandonedBookingAsync(draft, ct);
                draft.MarkAbandonedNotified(now);
                await drafts.SaveAsync(draft, ct);
                sent++;
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "Abandoned-booking alert failed for a draft created {Created}", draft.CreatedDate);
            }
        }

        return (sent, failures);
    }

    private async Task<int> DeleteOldDraftsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var deleted = 0;
        foreach (var draft in await drafts.ListCreatedBeforeAsync(now - bookingOptions.Value.DraftRetention, ct))
        {
            try
            {
                await drafts.DeleteAsync(draft, ct);
                deleted++;
            }
            catch (ConcurrencyConflictException)
            {
                // Updated meanwhile; the next run deletes it.
            }
        }

        return deleted;
    }
}
