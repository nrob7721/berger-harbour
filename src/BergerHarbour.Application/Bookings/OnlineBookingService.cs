using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Bookings;

/// <summary>The customer booking flow: drafts, quotes, holds, release and status.</summary>
public sealed class OnlineBookingService(
    IBoatRepository boats,
    IBookingRepository bookings,
    IBookingDraftRepository drafts,
    IBookingRequestValidationStrategy validation,
    IBoatScheduleLock scheduleLock,
    IPaymentGateway payments,
    ITurnstileVerifier turnstile,
    ReferenceDataLoader referenceData,
    PaymentScheduleService schedule,
    PaymentLinkTokens tokens,
    IOptions<BookingOptions> bookingOptions,
    IOptions<AppUrlOptions> urls,
    IClock clock,
    ILogger<OnlineBookingService> logger)
{
    // ----- Drafts -----

    public async Task<CreateDraftResponse> CreateDraftAsync(CreateDraftRequest r, string? remoteIp, CancellationToken ct = default)
    {
        if (!await turnstile.VerifyAsync(r.TurnstileToken, remoteIp, ct))
            throw new DomainValidationException("turnstileToken", "Please complete the security check and try again.");
        var details = await DraftDetailsAsync(r.Slug, r.PeriodType, r.StartDate, r.NumberOfAdults, r.NumberOfChildren,
            r.FirstName, r.LastName, r.Email, r.Mobile, [], DraftStep.Details, ct);
        var draft = BookingDraft.Create(details, clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
        return new CreateDraftResponse(draft.Id);
    }

    public async Task UpdateDraftAsync(string draftId, UpdateDraftRequest r, CancellationToken ct = default)
    {
        var draft = await drafts.GetAsync(draftId, ct);
        if (draft is null || draft.IsConverted) throw new NotFoundException("Your booking session has expired. Please start again.");
        var details = await DraftDetailsAsync(r.Slug, r.PeriodType, r.StartDate, r.NumberOfAdults, r.NumberOfChildren,
            r.FirstName, r.LastName, r.Email, r.Mobile, r.Addons ?? [], r.LastStepReached, ct);
        draft.Update(details, clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
    }

    private async Task<DraftDetails> DraftDetailsAsync(string slug, PeriodType periodType, DateOnly startDate, int adults, int children,
        string firstName, string lastName, string email, string mobile, IReadOnlyList<AddonRequestDto> addons, DraftStep step, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var boat = await boats.GetBySlugAsync(slug ?? "", ct);
        if (boat is null || !boat.IsBookableHouseBoat) errors.Add("slug", "This boat isn't available for online booking.");
        var stay = errors.Capture(() => (StayRange?)StayPeriodFactory.ForOnlineStart(periodType, startDate));
        var guests = errors.Capture(() => new GuestNumbers(adults, children));
        var normalisedEmail = errors.Capture(() => EmailAddress.Require(email));
        var e164 = errors.Capture(() => MobileNumber.Require(mobile, "mobile"));
        errors.Capture(() => Text.Required(firstName, "firstName", "First name", 100));
        errors.Capture(() => Text.Required(lastName, "lastName", "Last name", 100));
        errors.ThrowIfAny();
        return new DraftDetails(boat!.Id, periodType, stay!.Value, guests!, firstName, lastName, normalisedEmail!, e164!,
            addons.Where(a => a.Quantity > 0).Select(a => new AddonRequest(a.AddonId, a.Quantity)).ToList(), step);
    }

    // ----- Quote -----

    public async Task<QuoteResponse> QuoteAsync(string slug, QuoteRequest r, CancellationToken ct = default)
    {
        var boat = await boats.GetBySlugAsync(slug, ct);
        if (boat is null || boat.Type != BoatType.House) throw new NotFoundException("We couldn't find that boat.");
        var reference = await referenceData.LoadAsync(ct);
        var result = await validation.ValidateAsync(new BookingRequest
        {
            Boat = boat, PeriodType = r.PeriodType, StartDate = r.StartDate, NumberOfAdults = r.NumberOfAdults,
            NumberOfChildren = r.NumberOfChildren, Addons = ToAddons(r.Addons), Reference = reference, Now = clock.UtcNow,
            Scope = ValidationScope.Quote,
        }, ct);
        result.ThrowIfInvalid();
        var stay = result.Stay!.Value;
        var guests = result.Guests!;
        var hire = HirePriceOrThrow(boat, reference, r.PeriodType, stay);
        var lines = PriceLines(reference, r.PeriodType, ToAddons(r.Addons));
        var deposit = boat.EffectiveDeposit(reference.Settings.DepositAmount);
        var total = hire + lines.Sum(l => l.LineTotal ?? 0);
        var scheduleType = PaymentScheduleService.DetermineSchedule(stay, reference.BlockedPeriods);
        var milestones = schedule.Milestones(scheduleType, stay.Start, total, deposit, clock.UtcNow);
        return new QuoteResponse(
            boat.Name, r.PeriodType, stay.Start, stay.End, guests.Adults, guests.Children,
            PricingService.SeasonFor(stay.Start, reference.Seasons).Name, hire,
            lines.Select(l => new QuoteAddonLine(l.AddonId, l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            total, lines.Any(l => l.UnitPrice is null), deposit, PaymentScheduleService.CheckoutAmount(deposit, hire),
            ScheduleView.Build(milestones), boat.SecurityBond,
            RoomingWarning.Applies(boat, guests.Total) ? RoomingWarning.Text(boat, guests.Total) : null);
    }

    private static decimal HirePriceOrThrow(Boat boat, ReferenceData reference, PeriodType periodType, StayRange stay) =>
        PricingService.HirePrice(boat, reference.Seasons, periodType, stay.Start)
        ?? throw new DomainValidationException("startDate", "This boat can't be booked online for these dates. Please call or email us.");

    private static IReadOnlyList<AddonQuantity> ToAddons(IReadOnlyList<AddonRequestDto>? addons) =>
        (addons ?? []).Select(a => new AddonQuantity(a.AddonId, a.Quantity)).ToList();

    /// <summary>Online add-on lines, priced on the server; prices sent by the browser are ignored.</summary>
    private static List<AddonLine> PriceLines(ReferenceData reference, PeriodType periodType, IReadOnlyList<AddonQuantity> addons) =>
        addons.GroupBy(a => a.AddonId)
            .Select(g => (Addon: reference.Addon(g.Key)!, Quantity: g.Sum(x => x.Quantity)))
            .OrderBy(x => x.Addon.SortOrder)
            .Select(x => AddonLine.Create(null, x.Addon.Id, x.Addon.Name, x.Quantity, PricingService.AddonUnitPrice(x.Addon, periodType)))
            .ToList();

    // ----- Hold + checkout -----

    public async Task<CreateBookingResponse> CreateBookingAsync(CreateBookingRequest r, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var boat = await boats.GetBySlugAsync(r.Slug ?? "", ct);
        var reference = await referenceData.LoadAsync(ct);
        var draft = string.IsNullOrWhiteSpace(r.DraftId) ? null : await drafts.GetAsync(r.DraftId, ct);
        var addons = ToAddons(r.Addons);
        var result = await validation.ValidateAsync(new BookingRequest
        {
            Boat = boat, PeriodType = r.PeriodType, StartDate = r.StartDate, NumberOfAdults = r.NumberOfAdults,
            NumberOfChildren = r.NumberOfChildren, Addons = addons, Reference = reference, Now = now, Scope = ValidationScope.Booking,
            Contact = new ContactInput(r.FirstName, r.LastName, r.Email, r.Mobile), Draft = draft, TermsAccepted = r.TermsAccepted,
            RoomingWarningAccepted = r.RoomingWarningAccepted, RoomingWarningText = r.RoomingWarningText,
        }, ct);
        result.ThrowIfInvalid();
        var stay = result.Stay!.Value;
        var guests = result.Guests!;
        var hire = HirePriceOrThrow(boat!, reference, r.PeriodType, stay);
        var lines = PriceLines(reference, r.PeriodType, addons);
        var deposit = boat!.EffectiveDeposit(reference.Settings.DepositAmount);
        var details = new CustomerDetails(r.FirstName.Trim(), r.LastName.Trim(), EmailAddress.Normalise(r.Email), MobileNumber.Require(r.Mobile, "mobile"));
        var rooming = RoomingWarning.Applies(boat, guests.Total) ? new RoomingWarningAcceptance(now, RoomingWarning.Text(boat, guests.Total)) : null;
        var bookingId = Ids.New();
        var options = bookingOptions.Value;

        var booking = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var existing = await session.GetPotentiallyOverlappingBookingsAsync(stay);
            var unavailable = await session.GetUnavailabilitiesAsync(stay);
            BookingAvailabilityService.EnsureAvailable(stay, existing, unavailable, now);
            var hold = Booking.CreateOnlineHold(boat.Id, r.PeriodType, stay, guests, details, draft!.Id, hire, deposit,
                PaymentScheduleService.DetermineSchedule(stay, reference.BlockedPeriods), lines, rooming, now,
                now + options.HoldDuration, bookingId, tokens.HashFor(bookingId), now);
            session.SaveBooking(hold);
            return hold;
        }, ct);

        draft!.Update(new DraftDetails(boat.Id, r.PeriodType, stay, guests, details.FirstName, details.LastName, details.Email,
            details.MobileNumber, addons.Select(a => new AddonRequest(a.AddonId, a.Quantity)).ToList(), DraftStep.Payment), now);
        await drafts.SaveAsync(draft, ct);

        CheckoutSession session;
        try
        {
            session = await payments.CreateEmbeddedCheckoutAsync(new CheckoutRequest(
                booking.Id, CheckoutPurpose.Deposit, PaymentScheduleService.CheckoutAmount(deposit, hire),
                $"Deposit – {boat.Name}, {Dates.Format(stay.Start)} to {Dates.Format(stay.End)}",
                details.Email, null,
                $"{urls.Value.BaseUrl}/?boat={Uri.EscapeDataString(boat.Slug)}&booking={booking.Id}&session_id={{CHECKOUT_SESSION_ID}}",
                now + options.CheckoutSessionLifetime), ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Creating the deposit checkout failed for booking {BookingId}; releasing the hold", booking.Id);
            booking.Expire();
            await bookings.SaveAsync(booking, ct);
            throw new ExternalServiceException("We couldn't start the payment. Please try again in a moment.", e);
        }

        booking.AttachCheckoutSession(session.SessionId);
        await bookings.SaveAsync(booking, ct);
        return new CreateBookingResponse(booking.Id, session.ClientSecret, session.SessionId);
    }

    /// <summary>Back from the Payment step: expire the Checkout session and the hold. Idempotent.</summary>
    public async Task ReleaseAsync(string bookingId, string draftId, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(bookingId, ct);
        if (booking is null || string.IsNullOrEmpty(draftId) || booking.BookingDraftId != draftId)
            throw new NotFoundException("We couldn't find that booking.");
        if (booking.Status == BookingStatus.Expired) return;
        if (booking.Status != BookingStatus.PendingPayment) throw new ConflictException("Your payment has already been completed.");
        if (booking.StripeCheckoutSessionId is { } sessionId)
        {
            var result = await payments.ExpireCheckoutSessionAsync(sessionId, ct);
            if (result == ExpireSessionResult.AlreadyCompleted) throw new ConflictException("Your payment has already been completed.");
        }
        try
        {
            booking.Expire();
            await bookings.SaveAsync(booking, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // The webhook changed it meanwhile.
            var latest = await bookings.GetAsync(bookingId, ct);
            if (latest?.Status == BookingStatus.Active) throw new ConflictException("Your payment has already been completed.");
            if (latest?.Status == BookingStatus.PendingPayment)
            {
                latest.Expire();
                await bookings.SaveAsync(latest, ct);
            }
        }
    }

    public async Task<BookingStatusResponse> GetStatusAsync(string bookingId, string? sessionId, CancellationToken ct = default)
    {
        var booking = await bookings.GetAsync(bookingId, ct);
        if (booking is null || string.IsNullOrEmpty(sessionId) || booking.StripeCheckoutSessionId != sessionId)
            throw new NotFoundException("We couldn't find that booking.");
        var boat = await boats.GetAsync(booking.BoatId, ct);
        return new BookingStatusResponse(booking.Status, booking.Status == BookingStatus.Active ? booking.Reference : null,
            boat?.Name ?? "", booking.StartDate, booking.EndDate, booking.CustomerDetails?.Email ?? "");
    }
}
