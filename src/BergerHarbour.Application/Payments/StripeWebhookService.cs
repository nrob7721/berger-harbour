using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.BookingDrafts;
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
    private readonly IBookingDraftRepository drafts;
    private readonly IBoatScheduleLock scheduleLock;
    private readonly IBookingReferenceGenerator references;
    private readonly BookingAvailabilityService availability;
    private readonly CustomerRegistry customers;
    private readonly BookingNotifier notifier;
    private readonly IClock clock;
    private readonly ILogger<StripeWebhookService> logger;

    public StripeWebhookService(
        IPaymentGateway gateway, IBookingRepository bookings, IBookingDraftRepository drafts, IBoatScheduleLock scheduleLock,
        IBookingReferenceGenerator references, BookingAvailabilityService availability, CustomerRegistry customers, BookingNotifier notifier, IClock clock,
        ILogger<StripeWebhookService> logger)
    {
        this.gateway = gateway;
        this.bookings = bookings;
        this.drafts = drafts;
        this.scheduleLock = scheduleLock;
        this.references = references;
        this.availability = availability;
        this.customers = customers;
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
        var reference = booking.Reference;
        if (reference is null)
        {
            var details = booking.CustomerDetails ?? throw new InvalidOperationException("An online booking has no customer details.");
            var prefix = BookingReference.PrefixFor(details.LastName, booking.StartDate);
            reference = BookingReference.Create(prefix, await references.NextSuffixAsync(prefix, ct));
        }

        var (activated, superseded) = await scheduleLock.RunExclusiveAsync(booking.BoatId, async session =>
        {
            var current = await session.GetBookingAsync(booking.Id) ?? throw new NotFoundException("The booking disappeared.");
            current.RecordPayment(payment, now);
            current.AssignReference(reference);
            var supersededHere = new List<Booking>();
            if (current.Status is BookingStatus.PendingPayment or BookingStatus.Expired)
            {
                var overlapping = await session.ListOverlappingBookingsAsync(current.Stay);
                var unavailable = await session.ListOverlappingUnavailabilitiesAsync(current.Stay);
                if (availability.Check(current.Stay, overlapping, unavailable, now, current.Id).IsAvailable)
                {
                    current.ActivateFromDeposit(now);
                    foreach (var standby in availability.StandbysSupersededBy(current, overlapping))
                    {
                        standby.Supersede(now);
                        session.SaveBooking(standby);
                        supersededHere.Add(standby);
                    }
                }
                else
                {
                    current.Expire(now);
                }
            }

            session.SaveBooking(current);
            return (current, supersededHere);
        });

        if (activated.Status != BookingStatus.Active)
        {
            logger.LogError(
                "Paid-but-expired booking {Reference} ({BookingId}): the deposit was paid after the hold lapsed and the dates were taken. " +
                "Refund the payment in the Stripe dashboard.", activated.Reference?.Value, activated.Id.Value);
            return;
        }

        if (activated.CustomerId is null && activated.CreatedBy == BookingCreator.Customer)
        {
            var customer = await customers.UpsertFromOnlineAsync(activated.CustomerDetails!, ct);
            activated.AttachCustomer(customer.Id, clock.UtcNow);
            await bookings.SaveAsync(activated, ct);
        }

        if (activated.BookingDraftId is { } draftId && await drafts.GetAsync(draftId, ct) is { IsConverted: false } draft)
        {
            draft.MarkConverted(activated.Id, clock.UtcNow);
            await drafts.SaveAsync(draft, ct);
        }

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
}
