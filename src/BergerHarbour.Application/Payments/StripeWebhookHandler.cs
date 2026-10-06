using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Payments;

/// <summary>
/// Handles Stripe checkout events. Every step is idempotent so Stripe can redeliver: payments are keyed on the
/// PaymentIntent id and emails on the booking's sent-notification keys. Exceptions propagate so the webhook
/// returns non-2xx and Stripe retries.
/// </summary>
public sealed class StripeWebhookHandler(
    IBookingRepository bookings,
    IBoatScheduleLock scheduleLock,
    NotificationService notifications,
    IClock clock,
    ILogger<StripeWebhookHandler> logger)
{
    public const string PaidButExpiredLogMessage = "PAID_BUT_EXPIRED";

    public async Task HandleCompletedAsync(CheckoutSessionCompleted evt, CancellationToken ct = default)
    {
        var bookingId = new BookingId(evt.BookingId);
        var booking = await bookings.GetAsync(bookingId, ct);
        if (booking is null)
        {
            logger.LogError("Stripe {EventId}: checkout {SessionId} paid for unknown booking {BookingId}; refund by hand",
                evt.EventId, evt.SessionId, evt.BookingId);
            return;
        }

        if (evt.Purpose == CheckoutPurpose.Deposit)
        {
            await HandleDepositAsync(booking, evt, ct);
        }
        else
        {
            await HandlePaymentAsync(booking, evt, ct);
        }
    }

    public async Task HandleExpiredAsync(CheckoutSessionExpired evt, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(new BookingId(evt.BookingId), ct);
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.StripeCheckoutSessionId != evt.SessionId)
        {
            return;
        }

        booking.Expire(clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        logger.LogInformation("Hold {Reference} expired with its checkout session", booking.Reference.Value);
    }

    private async Task HandleDepositAsync(Booking loaded, CheckoutSessionCompleted evt, CancellationToken ct)
    {
        var outcome = await scheduleLock.RunExclusiveAsync(loaded.BoatId, async session =>
        {
            var now = clock.UtcNow;
            var booking = await session.GetBookingAsync(loaded.Id) ?? throw new InvalidOperationException("Booking vanished.");
            var recorded = booking.RecordStripePayment(evt.AmountPaid, evt.PaymentIntentId, evt.PaidAt, now) is not null;
            var superseded = new List<Booking>();
            var paidButUnavailable = false;

            if (booking.Status is BookingStatus.PendingPayment or BookingStatus.Expired)
            {
                var occupying = await session.GetOccupyingBookingsAsync(booking.Stay);
                var unavailable = await session.GetUnavailabilitiesAsync(booking.Stay);
                try
                {
                    BookingAvailabilityService.EnsureAvailable(booking.BoatId, booking.Stay, occupying, unavailable, now, booking.Id);
                    booking.Activate(now);
                    foreach (var standby in BookingAvailabilityService.StandbysSupersededBy(booking, occupying))
                    {
                        standby.Supersede(now);
                        session.Save(standby);
                        superseded.Add(standby);
                    }
                }
                catch (AvailabilityConflictException)
                {
                    if (booking.Status == BookingStatus.PendingPayment)
                    {
                        booking.Expire(now);
                    }

                    paidButUnavailable = true;
                }
            }

            if (recorded || booking.Status == BookingStatus.Active || paidButUnavailable)
            {
                session.Save(booking);
            }

            return (Booking: booking, Superseded: superseded, PaidButUnavailable: paidButUnavailable);
        }, ct);

        if (outcome.PaidButUnavailable)
        {
            logger.LogError(
                "{Marker}: booking {Reference} was paid ({Amount} AUD, {PaymentIntent}) after its hold expired and the dates were taken. Refund in the Stripe dashboard.",
                PaidButExpiredLogMessage, outcome.Booking.Reference.Value, evt.AmountPaid, evt.PaymentIntentId);
            return;
        }

        if (outcome.Booking.Status == BookingStatus.Active)
        {
            await notifications.SendBookingConfirmedAsync(outcome.Booking, ct);
            await notifications.SendStaffNewOnlineBookingAsync(outcome.Booking, ct);
        }

        foreach (var standby in outcome.Superseded)
        {
            await notifications.SendStaffStandbySupersededAsync(standby, ct);
        }
    }

    private async Task HandlePaymentAsync(Booking booking, CheckoutSessionCompleted evt, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payment = booking.RecordStripePayment(evt.AmountPaid, evt.PaymentIntentId, evt.PaidAt, clock.UtcNow);
            if (payment is null)
            {
                // Replay: the payment is already recorded. Make sure the receipt went out.
                payment = booking.Payments.First(p => p.StripePaymentIntentId == evt.PaymentIntentId);
                await notifications.SendPaymentReceivedAsync(booking, payment, ct);
                return;
            }

            try
            {
                await bookings.SaveAsync(booking, ct);
                logger.LogInformation("Payment {Amount} recorded on {Reference}", evt.AmountPaid, booking.Reference.Value);
                await notifications.SendPaymentReceivedAsync(booking, payment, ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 5)
            {
                booking = await bookings.GetAsync(booking.Id, ct) ?? throw new InvalidOperationException("Booking vanished.");
            }
        }
    }
}
