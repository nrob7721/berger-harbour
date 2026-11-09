using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Notifications;

public sealed record NotificationJobResult(int HoldsExpired, int EmailsSent, int AbandonedDraftsReported, int DraftsDeleted, int Failures);

/// <summary>
/// The hourly job. Idempotent and safe to run at any frequency:
/// 1. expire lapsed holds; 2. payment (#4, #7) and pre-hire (#6) emails; 3. abandoned drafts (#10); 4. delete old drafts.
/// </summary>
public sealed class NotificationJob(
    IBookingRepository bookings,
    IBookingDraftRepository drafts,
    ICustomerRepository customers,
    BookingNotifier notifier,
    PaymentScheduleService schedule,
    IOptions<NotificationOptions> notificationOptions,
    IOptions<BookingOptions> bookingOptions,
    IClock clock,
    ILogger<NotificationJob> logger)
{
    private int _sent, _failures;

    public async Task<NotificationJobResult> RunAsync(CancellationToken ct = default)
    {
        _sent = 0;
        _failures = 0;
        var now = clock.UtcNow;
        var expired = await ExpireHoldsAsync(now, ct);
        await SendBookingEmailsAsync(now, ct);
        var abandoned = await ReportAbandonedDraftsAsync(now, ct);
        var deleted = await DeleteOldDraftsAsync(now, ct);
        var result = new NotificationJobResult(expired, _sent, abandoned, deleted, _failures);
        logger.LogInformation("process-notifications finished: {@Result}", result);
        return result;
    }

    private async Task<int> ExpireHoldsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var count = 0;
        foreach (var b in (await bookings.ListByStatusAsync(BookingStatus.PendingPayment, ct)).Where(b => b.IsHoldLapsed(now)))
        {
            try
            {
                b.Expire();
                await bookings.SaveAsync(b, ct);
                count++;
            }
            catch (Exception e) when (e is ConcurrencyConflictException or ConflictException)
            {
                logger.LogInformation("Hold {BookingId} changed while expiring it; skipped", b.Id);
            }
        }
        return count;
    }

    private async Task SendBookingEmailsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = SydneyTime.Today(now);
        var preHire = notificationOptions.Value.PreHireInstructionsBeforeHire;
        foreach (var b in await bookings.ListActiveStartingFromAsync(today, ct))
        {
            if (b.IsStandby || b.Status != BookingStatus.Active) continue;
            var paid = b.AmountPaid;
            foreach (var m in schedule.Milestones(b).Where(m => m.Kind != MilestoneKind.Deposit))
            {
                var shortfall = m.Shortfall(paid);
                if (shortfall <= 0) continue;
                var dueDate = SydneyTime.DateOf(m.DueAt);
                // Triggers at or before the booking's creation are never sent (decision 23).
                if (now >= m.ReminderAt && now < m.DueAt && m.ReminderAt > b.CreatedDate)
                    await SendOnceAsync(b, NotificationKeys.PaymentDueReminder(m.Kind), () => notifier.SendPaymentDueAsync(b, shortfall, dueDate, ct), ct);
                if (now >= m.DueAt && m.DueAt > b.CreatedDate)
                    await SendOnceAsync(b, NotificationKeys.PaymentDue(m.Kind), () => notifier.SendPaymentDueAsync(b, shortfall, dueDate, ct), ct);
                if (now > m.DueAt && !m.WasDueAtCreation(b.CreatedDate))
                    await SendOnceAsync(b, NotificationKeys.Overdue(m.Kind), () => notifier.SendStaffOverdueAsync(b, m, ct), ct);
            }
            if (now >= SydneyTime.CheckInInstant(b.StartDate) - preHire)
                await SendOnceAsync(b, NotificationKeys.PreHire, () => notifier.SendPreHireAsync(b, ct), ct);
        }
    }

    private async Task SendOnceAsync(Booking b, string key, Func<Task> send, CancellationToken ct)
    {
        if (b.HasSentNotification(key)) return;
        try
        {
            await send();
            b.MarkNotificationSent(key, clock.UtcNow);
            await bookings.SaveAsync(b, ct);
            _sent++;
        }
        catch (Exception e)
        {
            _failures++;
            logger.LogError(LogEvents.JobFailed, e, "JOB_FAILED: sending {Key} for booking {BookingId} failed", key, b.Id);
        }
    }

    private async Task<int> ReportAbandonedDraftsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var after = bookingOptions.Value.AbandonedDraftAfter;
        var count = 0;
        var all = await drafts.ListAsync(ct);
        // Only the latest draft per email is ever reported.
        foreach (var latest in all.GroupBy(d => d.Email).Select(g => g.MaxBy(d => d.LastActivityAt)!))
        {
            if (latest.AbandonedNotifiedAt is not null || !latest.IsAbandoned(now, after)) continue;
            try
            {
                if (await BookedSinceAsync(latest, ct))
                {
                    latest.MarkAbandonedNotified(now); // suppressed: they booked anyway
                    await drafts.SaveAsync(latest, ct);
                    continue;
                }
                await notifier.SendStaffAbandonedAsync(latest, ct);
                latest.MarkAbandonedNotified(clock.UtcNow);
                await drafts.SaveAsync(latest, ct);
                count++;
                _sent++;
            }
            catch (Exception e)
            {
                _failures++;
                logger.LogError(LogEvents.JobFailed, e, "JOB_FAILED: reporting abandoned draft {DraftId} failed", latest.Id);
            }
        }
        return count;
    }

    /// <summary>Whether any booking for the draft's email became Active after the draft was created.</summary>
    private async Task<bool> BookedSinceAsync(BookingDraft draft, CancellationToken ct)
    {
        var customer = await customers.GetByEmailAsync(draft.Email, ct);
        if (customer is null) return false;
        return (await bookings.ListByCustomerAsync(customer.Id, ct)).Any(b => b.ActivatedAt > draft.CreatedDate);
    }

    private async Task<int> DeleteOldDraftsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var retention = bookingOptions.Value.DraftRetention;
        var count = 0;
        foreach (var d in (await drafts.ListAsync(ct)).Where(d => d.IsPastRetention(now, retention)))
        {
            await drafts.DeleteAsync(d, ct);
            count++;
        }
        return count;
    }
}
