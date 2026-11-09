using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Bookings;

/// <summary>A paid Checkout session (only synchronous methods are allowed, so completed means paid).</summary>
public sealed record CheckoutCompleted(string SessionId, string BookingId, CheckoutPurpose Purpose, string PaymentIntentId, decimal Amount, DateTimeOffset PaidAt);

public sealed record CheckoutExpired(string SessionId, string BookingId);

public static class LogEvents
{
    /// <summary>A log-based alert watches for this: staff refund by hand.</summary>
    public static readonly EventId PaidButExpired = new(9001, "PaidButExpired");
    public static readonly EventId WebhookFailed = new(9002, "WebhookFailed");
    public static readonly EventId JobFailed = new(9003, "JobFailed");
}

/// <summary>Handles Stripe webhooks. Every step is idempotent so Stripe can retry safely.</summary>
public sealed class StripeWebhookService(
    IBookingRepository bookings,
    IBookingDraftRepository drafts,
    IBoatScheduleLock scheduleLock,
    IBookingReferenceGenerator references,
    CustomerRegistry customerRegistry,
    BookingNotifier notifier,
    IClock clock,
    ILogger<StripeWebhookService> logger)
{
    private enum DepositOutcome { Activated, AlreadyActive, PaidButExpired, Ignored }

    public async Task HandleCompletedAsync(CheckoutCompleted e, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(e.BookingId, ct);
        if (booking is null)
        {
            logger.LogError(LogEvents.WebhookFailed, "checkout.session.completed for unknown booking {BookingId} (session {SessionId})", e.BookingId, e.SessionId);
            return;
        }
        var payment = new Payment(Ids.New(), e.Amount, PaymentMethod.Stripe, e.PaymentIntentId, e.PaidAt, null, BookingCreator.Customer);
        if (e.Purpose == CheckoutPurpose.Payment) await HandleBalancePaymentAsync(booking, payment, ct);
        else await HandleDepositAsync(booking, payment, ct);
    }

    private async Task HandleBalancePaymentAsync(Booking booking, Payment payment, CancellationToken ct)
    {
        if (booking.RecordPayment(payment)) await bookings.SaveAsync(booking, ct);
        var key = NotificationKeys.PaymentReceived(payment.StripePaymentIntentId!);
        if (booking.Status == BookingStatus.Active && !booking.IsStandby && !booking.HasSentNotification(key))
        {
            await notifier.SendPaymentReceivedAsync(booking, ct);
            booking.MarkNotificationSent(key, clock.UtcNow);
            await bookings.SaveAsync(booking, ct);
        }
    }

    private async Task HandleDepositAsync(Booking booking, Payment payment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reference = booking.Reference;
        if (reference is null && booking.Status is BookingStatus.PendingPayment or BookingStatus.Expired)
        {
            var prefix = BookingReference.Prefix(booking.CustomerDetails?.LastName, booking.StartDate);
            reference = BookingReference.Compose(prefix, await references.NextSuffixAsync(prefix, ct));
        }

        var superseded = new List<string>();
        var outcome = await scheduleLock.RunExclusiveAsync(booking.BoatId, async session =>
        {
            superseded.Clear();
            var b = await session.GetBookingAsync(booking.Id) ?? throw new NotFoundException("Booking not found.");
            var recorded = b.RecordPayment(payment);
            if (b.Status == BookingStatus.Active)
            {
                if (recorded) session.SaveBooking(b);
                return DepositOutcome.AlreadyActive;
            }
            if (b.Status is not (BookingStatus.PendingPayment or BookingStatus.Expired) || reference is null)
            {
                if (recorded) session.SaveBooking(b);
                return DepositOutcome.Ignored;
            }
            var others = await session.GetPotentiallyOverlappingBookingsAsync(b.Stay);
            var unavailable = await session.GetUnavailabilitiesAsync(b.Stay);
            if (BookingAvailabilityService.FindConflicts(b.Stay, others, unavailable, now, b.Id).Any)
            {
                if (b.Status == BookingStatus.PendingPayment) b.Expire();
                b.AssignReference(reference);
                session.SaveBooking(b);
                return DepositOutcome.PaidButExpired;
            }
            b.ActivateAfterDeposit(reference, now);
            foreach (var standby in BookingAvailabilityService.StandbysToSupersede(b, others))
            {
                standby.Supersede();
                session.SaveBooking(standby);
                superseded.Add(standby.Id);
            }
            session.SaveBooking(b);
            return DepositOutcome.Activated;
        }, ct);

        switch (outcome)
        {
            case DepositOutcome.PaidButExpired:
                logger.LogError(LogEvents.PaidButExpired,
                    "PAID_BUT_EXPIRED: deposit paid for booking {BookingId} ({Reference}) but its dates were taken. Refund the customer by hand.",
                    booking.Id, reference);
                return;
            case DepositOutcome.Ignored:
                logger.LogWarning("Deposit for booking {BookingId} recorded without activation (status {Status})", booking.Id, booking.Status);
                return;
        }

        await FinishActivationAsync(booking.Id, ct);
        foreach (var id in superseded)
            await NotifySupersededAsync(id, notifier, bookings, clock, ct);
    }

    /// <summary>Customer, draft and emails after activation. Safe to repeat.</summary>
    private async Task FinishActivationAsync(string bookingId, CancellationToken ct)
    {
        var b = await bookings.GetAsync(bookingId, ct) ?? throw new NotFoundException("Booking not found.");
        if (b.CustomerId is null && b.CustomerDetails is { } details)
        {
            var customer = await customerRegistry.UpsertFromOnlineBookingAsync(details, ct);
            b.LinkCustomer(customer.Id);
            await bookings.SaveAsync(b, ct);
        }
        if (b.BookingDraftId is { } draftId && await drafts.GetAsync(draftId, ct) is { IsConverted: false } draft)
        {
            draft.MarkConverted(b.Id, clock.UtcNow);
            await drafts.SaveAsync(draft, ct);
        }
        if (!b.HasSentNotification(NotificationKeys.BookingConfirmed))
        {
            await notifier.SendBookingConfirmedAsync(b, ct);
            b.MarkNotificationSent(NotificationKeys.BookingConfirmed, clock.UtcNow);
            await bookings.SaveAsync(b, ct);
        }
        if (!b.HasSentNotification(NotificationKeys.StaffNewOnlineBooking))
        {
            await notifier.SendStaffNewOnlineBookingAsync(b, ct);
            b.MarkNotificationSent(NotificationKeys.StaffNewOnlineBooking, clock.UtcNow);
            await bookings.SaveAsync(b, ct);
        }
    }

    internal static async Task NotifySupersededAsync(string standbyId, BookingNotifier notifier, IBookingRepository bookings, IClock clock, CancellationToken ct)
    {
        var standby = await bookings.GetAsync(standbyId, ct);
        if (standby is null || standby.Status != BookingStatus.Superseded || standby.HasSentNotification(NotificationKeys.StandbySuperseded)) return;
        await notifier.SendStaffStandbySupersededAsync(standby, ct);
        standby.MarkNotificationSent(NotificationKeys.StandbySuperseded, clock.UtcNow);
        await bookings.SaveAsync(standby, ct);
    }

    public async Task HandleExpiredAsync(CheckoutExpired e, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(e.BookingId, ct);
        // A booking already released from the Payment step is left as it is.
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.StripeCheckoutSessionId != e.SessionId) return;
        booking.Expire();
        await bookings.SaveAsync(booking, ct);
    }
}
