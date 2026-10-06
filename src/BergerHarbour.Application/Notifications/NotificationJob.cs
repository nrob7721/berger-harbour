using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Notifications;

public sealed record NotificationJobResult(int ExpiredHolds, int EmailsSent, int Failures);

/// <summary>
/// The hourly process-notifications job. Idempotent and safe to run at any frequency: it expires lapsed holds and
/// sends due #4, #6 and #7 emails for Active non-stand-by bookings starting today or later.
/// </summary>
public sealed class NotificationJob(
    IBookingRepository bookings,
    NotificationService notifications,
    PaymentScheduleService paymentSchedule,
    IOptions<NotificationOptions> options,
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

        logger.LogInformation("process-notifications: {Expired} holds expired, {Sent} emails sent, {Failures} failures",
            expired, sent, failures);
        return new NotificationJobResult(expired, sent, failures);
    }

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
                if (await notifications.SendPaymentDueAsync(booking, milestone,
                        NotificationKeys.PaymentDueOnDueDate(milestone.Kind), ct))
                {
                    sent++;
                }

                if (now > milestone.DueAt && await notifications.SendStaffBalanceOverdueAsync(booking, milestone, ct))
                {
                    sent++;
                }
            }
            else if (now >= paymentSchedule.ReminderAt(milestone) &&
                     await notifications.SendPaymentDueAsync(booking, milestone,
                         NotificationKeys.PaymentDueReminder(milestone.Kind), ct))
            {
                sent++;
            }
        }

        var preHireAt = SydneyTime.CheckInInstant(booking.StartDate) - options.Value.PreHireInstructionsBeforeHire;
        if (now >= preHireAt && await notifications.SendPreHireInstructionsAsync(booking, ct))
        {
            sent++;
        }

        return sent;
    }
}
