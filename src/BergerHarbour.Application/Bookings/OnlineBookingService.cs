using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Bookings;

/// <summary>Online checkout: the hold, Back from Payment, and the completion status (public <c>BookingsController</c>).</summary>
public sealed class OnlineBookingService
{
    public const string AlreadyPaid = "Your payment has already been completed.";

    private readonly OnlineBookingPreparer preparer;
    private readonly IBookingRepository bookings;
    private readonly IBookingDraftRepository drafts;
    private readonly IBoatScheduleLock scheduleLock;
    private readonly BookingAvailabilityService availability;
    private readonly IPaymentGateway payments;
    private readonly PaymentLinkTokens links;
    private readonly BookingOptions options;
    private readonly IClock clock;
    private readonly ILogger<OnlineBookingService> logger;

    public OnlineBookingService(
        OnlineBookingPreparer preparer, IBookingRepository bookings, IBookingDraftRepository drafts, IBoatScheduleLock scheduleLock,
        BookingAvailabilityService availability, IPaymentGateway payments, PaymentLinkTokens links, IOptions<BookingOptions> options, IClock clock,
        ILogger<OnlineBookingService> logger)
    {
        this.preparer = preparer;
        this.bookings = bookings;
        this.drafts = drafts;
        this.scheduleLock = scheduleLock;
        this.availability = availability;
        this.payments = payments;
        this.links = links;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>
    /// Releases any hold from the same draft that is still PendingPayment, then validates, creates the new PendingPayment
    /// hold atomically (no customer, no reference yet), updates the draft and creates the deposit's Embedded Checkout
    /// session. The session closes before the hold lapses.
    /// </summary>
    public async Task<CreateBookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        var boat = await preparer.HouseBoatAsync(request.Slug, ct);
        var draft = string.IsNullOrWhiteSpace(request.DraftId) ? null : await drafts.GetAsync(new BookingDraftId(request.DraftId), ct);
        if (draft is { IsConverted: false })
        {
            await ReleaseDraftHoldsAsync(draft.Id, ct);
        }

        var bookingRequest = OnlineRequestMapper.ToRequest(request);
        var p = await preparer.PrepareAsync(boat, bookingRequest, BookingValidationScope.Submit, draft, ct);
        var now = clock.UtcNow;
        var details = new CustomerDetails(request.FirstName.Trim(), request.LastName.Trim(), EmailAddress.Create(request.Email),
            MobileNumber.Create(request.Mobile));
        var id = BookingId.New();
        var hold = new OnlineHold(boat.Id, draft!.Id, request.PeriodType, p.Stay, request.NumberOfAdults, request.NumberOfChildren, details,
            p.AddonLines, p.HirePrice, p.DepositAmount, p.PaymentSchedule,
            p.RoomingWarning is null ? null : new RoomingWarningAcceptance(now, p.RoomingWarning), now, now + options.HoldLifetime, links.HashFor(id));

        var booking = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var overlapping = await session.ListOverlappingBookingsAsync(p.Stay);
            var unavailable = await session.ListOverlappingUnavailabilitiesAsync(p.Stay);
            availability.EnsureAvailable(p.Stay, overlapping, unavailable, now);
            var created = Booking.CreateOnlineHold(id, hold, now);
            session.SaveBooking(created);
            return created;
        });

        draft.Update(draft.Contents with
        {
            BoatId = boat.Id,
            PeriodType = request.PeriodType,
            Stay = p.Stay,
            NumberOfAdults = request.NumberOfAdults,
            NumberOfChildren = request.NumberOfChildren,
            FirstName = details.FirstName,
            LastName = details.LastName,
            Email = details.Email,
            MobileNumber = details.MobileNumber,
            AddonRequests = bookingRequest.Addons.Select(a => new AddonRequest(a.AddonId, a.Quantity)).ToList(),
            LastStepReached = DraftStep.Payment,
        }, now);
        await drafts.SaveAsync(draft, ct);

        var checkoutExpiresAt = now + options.CheckoutSessionLifetime;
        CheckoutSession session;
        try
        {
            session = await payments.CreateCheckoutSessionAsync(new CheckoutSessionRequest(booking.Id, CheckoutPurpose.Deposit, p.CheckoutAmount,
                $"{boat.Name} deposit", $"Deposit for {boat.Name}, check-in {Notifications.DisplayFormat.Date(p.Stay.Start)}", details.Email.Value,
                $"{links.BaseUrl}/?boat={Uri.EscapeDataString(boat.Slug)}&booking={booking.Id.Value}&session_id={{CHECKOUT_SESSION_ID}}",
                checkoutExpiresAt), ct);
        }
        catch
        {
            booking.Expire(clock.UtcNow);
            await bookings.SaveAsync(booking, ct);
            throw;
        }

        booking.AttachCheckoutSession(session.SessionId);
        await bookings.SaveAsync(booking, ct);
        return new CreateBookingResponse(booking.Id.Value, session.ClientSecret, session.SessionId, checkoutExpiresAt);
    }

    /// <summary>
    /// Back from the Payment step: expires the Checkout session and the hold. Idempotent; 409 when already paid; 404 when
    /// the draft doesn't match.
    /// </summary>
    public async Task ReleaseAsync(string bookingId, ReleaseBookingRequest request, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(new BookingId(bookingId), ct);
        if (booking is null || booking.CreatedBy != BookingCreator.Customer || booking.BookingDraftId?.Value != request.DraftId)
        {
            throw new NotFoundException("We couldn't find that booking.");
        }

        await ReleaseHoldAsync(booking, ct);
    }

    /// <summary>The completion screen's status, only when <paramref name="sessionId"/> matches the booking.</summary>
    public async Task<BookingStatusResponse> GetStatusAsync(string bookingId, string? sessionId, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(new BookingId(bookingId), ct);
        if (booking is null || string.IsNullOrEmpty(sessionId) || booking.StripeCheckoutSessionId != sessionId)
        {
            throw new NotFoundException("We couldn't find that booking.");
        }

        var boat = await preparer.BoatByIdAsync(booking.BoatId, ct);
        return new BookingStatusResponse(booking.Status, booking.Reference?.Value, boat?.Name ?? string.Empty, booking.StartDate, booking.EndDate,
            booking.CustomerDetails?.Email.Value ?? string.Empty);
    }

    /// <summary>A customer never collides with their own earlier hold: every PendingPayment hold from the draft is released.</summary>
    private async Task ReleaseDraftHoldsAsync(BookingDraftId draftId, CancellationToken ct)
    {
        foreach (var hold in (await bookings.ListByStatusAsync(BookingStatus.PendingPayment, ct)).Where(b => b.BookingDraftId == draftId))
        {
            await ReleaseHoldAsync(hold, ct);
        }
    }

    /// <summary>Expires the hold's Checkout session and the hold. Already Expired: nothing to do. Paid: 409.</summary>
    private async Task ReleaseHoldAsync(Booking booking, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            switch (booking.Status)
            {
                case BookingStatus.Expired:
                    return;
                case BookingStatus.PendingPayment:
                    break;
                default:
                    throw new ConflictException(AlreadyPaid);
            }

            if (booking.StripeCheckoutSessionId is { } sessionId && !await payments.TryExpireCheckoutSessionAsync(sessionId, ct))
            {
                throw new ConflictException(AlreadyPaid);
            }

            booking.Expire(clock.UtcNow);
            try
            {
                await bookings.SaveAsync(booking, ct);
                logger.LogInformation("Released hold {BookingId} from the Payment step.", booking.Id.Value);
                return;
            }
            catch (ConcurrencyException) when (attempt < 3)
            {
                // The webhook changed it meanwhile: reload and decide again.
                booking = await bookings.GetAsync(booking.Id, ct) ?? throw new NotFoundException("We couldn't find that booking.");
            }
        }
    }
}
