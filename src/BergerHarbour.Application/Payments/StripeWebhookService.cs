using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Payments;

/// <summary>
/// Stripe events (§8.3), verified and parsed through the payment gateway port. Persistence completes before the
/// controller returns 2xx, so Stripe retries on failure; every step is idempotent.
/// </summary>
public sealed class StripeWebhookService
{
    private readonly IPaymentGateway gateway;
    private readonly IBookingRepository bookings;
    private readonly IBoatScheduleLock scheduleLock;
    private readonly BookingActivator activator;
    private readonly BookingNotifier notifier;
    private readonly IClock clock;
    private readonly ILogger<StripeWebhookService> logger;

    public StripeWebhookService(
        IPaymentGateway gateway, IBookingRepository bookings, IBoatScheduleLock scheduleLock, BookingActivator activator, BookingNotifier notifier,
        IClock clock, ILogger<StripeWebhookService> logger)
    {
        this.gateway = gateway;
        this.bookings = bookings;
        this.scheduleLock = scheduleLock;
        this.activator = activator;
        this.notifier = notifier;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Verifies and handles a raw webhook. Throws <see cref="InvalidWebhookException"/> on a bad signature.</summary>
    public async Task HandleAsync(string payload, string? signature, CancellationToken ct = default)
    {
        switch (gateway.ParseWebhookEvent(payload, signature))
        {
            case CheckoutSessionCompleted completed:
                await HandleCompletedAsync(completed, ct);
                break;
            case CheckoutSessionExpired expired:
                await HandleExpiredAsync(expired, ct);
                break;
        }
    }

    public async Task HandleCompletedAsync(CheckoutSessionCompleted e, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(e.BookingId, ct);
        if (booking is null)
        {
            logger.LogError("Stripe checkout {SessionId} completed for unknown booking {BookingId}.", e.SessionId, e.BookingId.Value);
            return;
        }

        var payment = new Payment(Guid.NewGuid().ToString("N"), e.Amount, PaymentMethod.Stripe, e.PaymentIntentId, e.PaidAt, null, BookingCreator.Customer);
        if (e.Purpose == CheckoutPurpose.Payment)
        {
            await RecordPaymentAsync(booking, payment, ct);
        }
        else
        {
            await HandleDepositAsync(booking, payment, ct);
        }
    }

    public async Task HandleExpiredAsync(CheckoutSessionExpired e, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var booking = await bookings.GetAsync(e.BookingId, ct);
            if (booking is not { Status: BookingStatus.PendingPayment } || booking.StripeCheckoutSessionId != e.SessionId)
            {
                return; // already paid, released or expired
            }

            booking.Expire(clock.UtcNow);
            try
            {
                await bookings.SaveAsync(booking, ct);
                return;
            }
            catch (ConcurrencyException) when (attempt < 3)
            {
            }
        }
    }

    private async Task RecordPaymentAsync(Booking booking, Payment payment, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!booking.RecordPayment(payment, clock.UtcNow))
            {
                return; // replay
            }

            try
            {
                await bookings.SaveAsync(booking, ct);
                break;
            }
            catch (ConcurrencyException) when (attempt < 3)
            {
                booking = await bookings.GetAsync(booking.Id, ct) ?? throw new NotFoundException("The booking disappeared.");
            }
        }

        await notifier.SendPaymentReceivedAsync(booking, payment, ct);
    }

    private async Task HandleDepositAsync(Booking booking, Payment payment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reference = await activator.ReferenceForAsync(booking, ct);
        var (activated, superseded, outcome) = await scheduleLock.RunExclusiveAsync(booking.BoatId, async session =>
        {
            var current = await session.GetBookingAsync(booking.Id) ?? throw new NotFoundException("The booking disappeared.");
            var wasActive = current.Status == BookingStatus.Active;
            var recorded = current.RecordPayment(payment, now);
            if (wasActive)
            {
                // A replay of the deposit that activated it, or a second deposit after staff confirmed the booking.
                if (recorded)
                {
                    session.SaveBooking(current);
                }

                return (current, (IReadOnlyList<Booking>)[], recorded ? DepositOutcome.AlreadyConfirmed : DepositOutcome.Activated);
            }

            current.AssignReference(reference);
            IReadOnlyList<Booking> supersededHere = [];
            if (current.Status is BookingStatus.PendingPayment or BookingStatus.Expired)
            {
                var attempt = await activator.TryActivateAsync(session, current, now);
                supersededHere = attempt.Superseded;
                if (!attempt.Activated)
                {
                    current.Expire(now);
                }
            }

            session.SaveBooking(current);
            return (current, supersededHere, current.Status == BookingStatus.Active ? DepositOutcome.Activated : DepositOutcome.PaidButExpired);
        });

        switch (outcome)
        {
            case DepositOutcome.AlreadyConfirmed:
                logger.LogError(
                    "Second deposit on booking {Reference} ({BookingId}): staff had already confirmed it while the webhook was delayed. " +
                    "Refund the payment in the Stripe dashboard.", activated.Reference?.Value, activated.Id.Value);
                return;
            case DepositOutcome.PaidButExpired:
                logger.LogError(
                    "Paid-but-expired booking {Reference} ({BookingId}): the deposit was paid after the hold lapsed and the dates were taken. " +
                    "Refund the payment in the Stripe dashboard.", activated.Reference?.Value, activated.Id.Value);
                return;
        }

        await activator.CompleteAsync(activated, ct);
        var sent = await notifier.SendBookingConfirmedAsync(activated, ct);
        sent |= await notifier.SendStaffNewOnlineBookingAsync(activated, ct);
        if (sent)
        {
            await bookings.SaveAsync(activated, ct);
        }

        foreach (var standby in superseded)
        {
            await notifier.SendStandbySupersededAsync(standby, ct);
        }
    }

    private enum DepositOutcome
    {
        Activated,
        PaidButExpired,
        AlreadyConfirmed,
    }
}
