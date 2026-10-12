using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Payments;

/// <summary>
/// Handles Stripe checkout events. Every step is idempotent so Stripe can redeliver: payments are keyed on the
/// PaymentIntent id and emails on the booking's sent-notification keys. Exceptions propagate so the webhook
/// returns non-2xx and Stripe retries. When a deposit activates an online booking, the Customer is created or updated
/// from the details entered online and the booking draft is marked converted; neither happens if it can't activate.
/// </summary>
public sealed class StripeWebhookHandler(
    IBookingRepository bookings,
    IBookingDraftRepository drafts,
    IBoatScheduleLock scheduleLock,
    CustomerService customerService,
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
            var active = await LinkCustomerAsync(outcome.Booking, ct);
            await ConvertDraftAsync(active, ct);
            await notifications.SendBookingConfirmedAsync(active, ct);
            await notifications.SendStaffNewOnlineBookingAsync(active, ct);
        }

        foreach (var standby in outcome.Superseded)
        {
            await notifications.SendStaffStandbySupersededAsync(standby, ct);
        }
    }

    /// <summary>
    /// Creates or updates the Customer from the booking's details and links it. Idempotent, so a webhook replay
    /// finishes the job if an earlier attempt failed after activation.
    /// </summary>
    private async Task<Booking> LinkCustomerAsync(Booking booking, CancellationToken ct)
    {
        if (booking.CustomerId is not null || booking.CustomerDetails is not { } details)
        {
            return booking;
        }

        var customer = await customerService.UpsertAsync(details.FullName, details.Email, details.MobileNumber, ct);
        for (var attempt = 1; ; attempt++)
        {
            booking.ChangeCustomer(customer.Id, clock.UtcNow);
            try
            {
                await bookings.SaveAsync(booking, ct);
                return booking;
            }
            catch (ConcurrencyConflictException) when (attempt < 5)
            {
                booking = await bookings.GetAsync(booking.Id, ct) ?? throw new InvalidOperationException("Booking vanished.");
                if (booking.CustomerId is not null)
                {
                    return booking;
                }
            }
        }
    }

    private async Task ConvertDraftAsync(Booking booking, CancellationToken ct)
    {
        if (booking.BookingDraftId is not { } draftId)
        {
            return;
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var draft = await drafts.GetAsync(draftId, ct);
            if (draft is null || draft.ConvertedBookingId == booking.Id)
            {
                return;
            }

            if (draft.IsConverted)
            {
                logger.LogWarning("Draft for {Reference} was already converted by another booking", booking.Reference.Value);
                return;
            }

            draft.MarkConverted(booking.Id, clock.UtcNow);
            try
            {
                await drafts.SaveAsync(draft, ct);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < 3)
            {
            }
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
