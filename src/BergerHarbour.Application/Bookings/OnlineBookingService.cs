using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Bookings;

/// <summary>
/// Customer-facing use cases: the fleet and boat info, fleet and period availability, add-on capacity, booking drafts, quote, booking with a hold,
/// completion status. No Customer record is created here; that happens when the deposit is paid.
/// </summary>
public sealed class OnlineBookingService(
    IBoatRepository boats,
    IBookingRepository bookings,
    IBoatUnavailabilityRepository unavailabilities,
    IBookingDraftRepository drafts,
    BookingCatalogueLoader catalogueLoader,
    IBookingRequestValidationStrategy validation,
    PaymentScheduleService paymentSchedule,
    AddonCapacityQueries capacity,
    IBookingReferenceGenerator references,
    IBoatScheduleLock scheduleLock,
    IPaymentGateway paymentGateway,
    ITurnstileVerifier turnstile,
    PaymentLinkTokens paymentLinks,
    IOptions<BookingOptions> bookingOptions,
    IClock clock,
    ILogger<OnlineBookingService> logger)
{
    public const int MaxAvailabilitySpanMonths = 15;

    /// <summary>Every active house boat, in display order, with the booking settings.</summary>
    public async Task<PublicFleetDto> GetFleetAsync(CancellationToken ct = default)
    {
        var fleetTask = ActiveHouseBoatsAsync(ct);
        var catalogueTask = catalogueLoader.LoadAsync(ct);
        await Task.WhenAll(fleetTask, catalogueTask);
        var settings = catalogueTask.Result.Settings;
        return new PublicFleetDto(
            fleetTask.Result.Select(b => new PublicFleetBoatDto(b.Slug, b.Name, b.ImageUrl, b.MaxNoOfGuests, b.HasSpa,
                b.IsPetFriendly)).ToList(),
            new PublicBookingSettingsDto(settings.MinimumLeadTimeDays, settings.BookingsOpenUntil, settings.ContactPhone,
                settings.ContactEmail, settings.HireTermsUrl, clock.Today(), Format.Time(Stay.CheckInTime),
                Format.Time(Stay.CheckInWindowEnd), Format.Time(Stay.CheckOutTime)));
    }

    /// <summary>One house boat, active or not. Inactive boats offer no add-ons.</summary>
    public async Task<PublicBoatDto> GetBoatAsync(string slug, CancellationToken ct = default)
    {
        var boat = await FindHouseBoatAsync(slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var addons = boat.IsActive
            ? AddonCatalogue.InDisplayOrder(catalogue.Addons.Where(a => a.IsActive && boat.AllowsAddon(a.Id))).Select(a =>
                new PublicAddonDto(a.Id.Value, a.Name, a.Description, a.PriceOnRequest, a.QuantityApplies,
                    a.Prices is { } p ? new AddonPricesDto(p.Midweek, p.Weekend, p.Week) : null, a.HasCapacity,
                    a.CapacityUnitLabel)).ToList()
            : [];
        return new PublicBoatDto(boat.Slug, boat.Name, boat.ImageUrl, boat.MaxNoOfGuests, boat.HasSpa, boat.IsPetFriendly,
            boat.AdultBeds, boat.KidsBeds, boat.BeddingDescription, boat.SecurityBond,
            boat.EffectiveDeposit(catalogue.Settings.DepositAmount), boat.IsActive, addons);
    }

    /// <summary>
    /// Each active house boat's taken ranges, and the blocked (enquire-only) periods, for [from, to). Never booking or
    /// customer data.
    /// </summary>
    public async Task<FleetAvailabilityDto> GetFleetAvailabilityAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (from >= to)
        {
            throw RequestValidationException.For("to", "'to' must be after 'from'.");
        }

        if (to > from.AddMonths(MaxAvailabilitySpanMonths))
        {
            throw RequestValidationException.For("to", $"The range can be at most {MaxAvailabilitySpanMonths} months.");
        }

        var occupancyTask = OccupancyAsync(from, to, ct);
        var catalogueTask = catalogueLoader.LoadAsync(ct);
        await Task.WhenAll(occupancyTask, catalogueTask);
        var (fleet, blocking, unavailable) = occupancyTask.Result;

        var window = new Stay(from, to);
        var enquireOnly = catalogueTask.Result.BlockedPeriods.Where(p => window.Overlaps(p.Nights))
            .OrderBy(p => p.Nights.FirstNight)
            .Select(p => new EnquireOnlyDto(p.Name, p.Nights.FirstNight, p.Nights.LastNight))
            .ToList();
        var boatsAvailability = fleet.Select(boat => new BoatAvailabilityDto(boat.Slug,
            blocking.Where(b => b.BoatId == boat.Id).Select(b => new DateRangeDto(b.StartDate, b.EndDate))
                .Concat(unavailable.Where(u => u.BoatId == boat.Id)
                    .Select(u => new DateRangeDto(u.Nights.FirstNight, u.Nights.LastNight.AddDays(1))))
                .OrderBy(r => r.Start)
                .ToList())).ToList();
        return new FleetAvailabilityDto(enquireOnly, boatsAvailability);
    }

    /// <summary>
    /// For one stay: whether each active house boat is free and its hire price. 400 when the stay breaks the period
    /// shape, lead time, open-until or blocked-period rules. Never booking or customer data.
    /// </summary>
    public async Task<PeriodAvailabilityDto> GetPeriodAvailabilityAsync(PeriodType periodType, DateOnly startDate,
        CancellationToken ct = default)
    {
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var errors = new ValidationErrors();
        var stay = OnlineBookingValidationStrategy.ValidateStay(periodType, startDate, null, catalogue.Settings,
            catalogue.BlockedPeriods, clock.Today(), errors);
        errors.ThrowIfInvalid();

        var (fleet, blocking, unavailable) = await OccupancyAsync(stay!.StartDate, stay.EndDate, ct);
        var now = clock.UtcNow;
        return new PeriodAvailabilityDto(fleet.Select(boat =>
        {
            var free = BookingAvailabilityService.ConflictingBookings(boat.Id, stay, blocking, now).Count == 0 &&
                       BookingAvailabilityService.ConflictingUnavailabilities(boat.Id, stay, unavailable).Count == 0;
            var price = boat.RateFor(PricingService.SeasonFor(stay.StartDate, catalogue.Seasons).Id, periodType);
            if (price is null)
            {
                logger.LogError("{Boat} has no {Period} rate for {Start}; it cannot be booked online", boat.Slug, periodType,
                    stay.StartDate);
            }

            return new BoatPeriodDto(boat.Slug, free && price is not null, price);
        }).ToList());
    }

    /// <summary>What is left of a capacity-limited add-on for the dates, across all boats. Nothing else is revealed.</summary>
    public async Task<PublicAddonAvailabilityDto> GetAddonAvailabilityAsync(string addonId, DateOnly startDate,
        DateOnly endDate, CancellationToken ct = default) =>
        new((await capacity.RemainingAsync(addonId, startDate, endDate, null, ct)).Remaining);

    // ---- Booking drafts -------------------------------------------------------------------------------------------

    /// <summary>
    /// "Your details" was completed: verifies Turnstile, validates the details and starts a draft so staff can follow
    /// up if the booking is not finished. Never creates a Customer.
    /// </summary>
    public async Task<BookingDraftCreatedDto> CreateDraftAsync(CreateBookingDraftRequest request, string? remoteIp,
        CancellationToken ct = default)
    {
        if (!await turnstile.VerifyAsync(request.TurnstileToken, remoteIp, ct))
        {
            throw RequestValidationException.For("turnstileToken",
                "We could not verify you are human. Please try the check again.");
        }

        var boat = await FindDraftBoatAsync(request.Slug, ct);
        var errors = new ValidationErrors();
        var stay = DraftStay(request.PeriodType, request.StartDate, errors);
        DraftGuests(request.NumberOfAdults, request.NumberOfChildren, errors);
        ContactValidation.Validate(errors, request.FullName, request.Email, request.Mobile).ThrowIfInvalid();

        var draft = BookingDraft.Create(boat.Id, request.PeriodType, stay!, request.NumberOfAdults, request.NumberOfChildren,
            request.FullName!, request.Email!, request.Mobile!, clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
        return new BookingDraftCreatedDto(draft.Id.Value);
    }

    /// <summary>
    /// The customer moved forward a step, possibly with another boat. 404 when the draft is unknown, converted or deleted.
    /// </summary>
    public async Task UpdateDraftAsync(string draftId, UpdateBookingDraftRequest request, CancellationToken ct = default)
    {
        var draft = await FindOpenDraftAsync(draftId, ct);
        var boat = await FindDraftBoatAsync(request.Slug, ct);
        var errors = new ValidationErrors();
        var stay = DraftStay(request.PeriodType, request.StartDate, errors);
        DraftGuests(request.NumberOfAdults, request.NumberOfChildren, errors);
        ContactValidation.Validate(errors, request.FullName, request.Email, request.Mobile);
        var addons = (request.Addons ?? []).Take(50).ToList();
        if (addons.Any(a => string.IsNullOrWhiteSpace(a.AddonId) || a.Quantity is < 1 or > 99))
        {
            errors.Add("addons", "Choose a quantity between 1 and 99.");
        }

        errors.ThrowIfInvalid();
        draft.Update(boat.Id, request.PeriodType, stay!, request.NumberOfAdults, request.NumberOfChildren,
            request.FullName!, request.Email!, request.Mobile!,
            addons.Select(a => new DraftAddonRequest(new AddonId(a.AddonId), a.Quantity)), request.LastStepReached,
            clock.UtcNow);
        await drafts.SaveAsync(draft, ct);
    }

    // ---- Quote and booking ----------------------------------------------------------------------------------------

    public async Task<QuoteDto> QuoteAsync(string slug, QuoteRequest request, CancellationToken ct = default)
    {
        var boat = await FindHouseBoatAsync(slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, null, request.NumberOfAdults,
            request.NumberOfChildren, null, null, null, request.Addons ?? []);
        var remaining = await RemainingCapacityAsync(catalogue, bookingRequest, ct);
        validation.Validate(bookingRequest, Context(boat, catalogue, BookingValidationScope.Quote, null, remaining)).ThrowIfInvalid();
        return BuildQuote(boat, catalogue, bookingRequest);
    }

    public async Task<CreateOnlineBookingResult> CreateAsync(CreateOnlineBookingRequest request, CancellationToken ct = default)
    {
        var boat = await FindHouseBoatAsync(request.Slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var draft = string.IsNullOrWhiteSpace(request.DraftId) ? null : await drafts.GetAsync(new BookingDraftId(request.DraftId), ct);
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, null, request.NumberOfAdults,
            request.NumberOfChildren, request.FullName, request.Email, request.Mobile, request.Addons ?? [],
            request.RoomingWarningAccepted, request.RoomingWarningText, request.TermsAccepted, request.DraftId);
        var remaining = await RemainingCapacityAsync(catalogue, bookingRequest, ct);
        validation.Validate(bookingRequest, Context(boat, catalogue, BookingValidationScope.Submit, draft, remaining)).ThrowIfInvalid();

        var quote = BuildQuote(boat, catalogue, bookingRequest);
        var stay = new Stay(quote.StartDate, quote.EndDate);
        var details = new BookingCustomerDetails(request.FullName!, request.Email!, request.Mobile!);
        var now = clock.UtcNow;
        var reference = await references.NextAsync(SydneyTime.ToLocal(now).Year, ct);
        var bookingId = BookingId.New();
        var holdExpiresAt = now + bookingOptions.Value.HoldDuration;
        var rooming = boat.RequiresRoomingWarning(bookingRequest.NumberOfGuests)
            ? new RoomingWarningAcceptance(now, boat.RoomingWarningText(bookingRequest.NumberOfGuests))
            : null;
        var requestedAddons = quote.Addons.Select(a => new RequestedAddon(new AddonId(a.AddonId), a.Name, a.Quantity, a.UnitPrice)).ToList();

        var hold = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var occupying = await session.GetOccupyingBookingsAsync(stay);
            var unavailable = await session.GetUnavailabilitiesAsync(stay);
            BookingAvailabilityService.EnsureAvailable(boat.Id, stay, occupying, unavailable, now);
            var booking = Booking.CreateOnlineHold(reference, details, draft!.Id, boat.Id, request.PeriodType, stay,
                request.NumberOfAdults, request.NumberOfChildren, quote.HirePrice, quote.DepositAmount, quote.PaymentSchedule,
                requestedAddons, holdExpiresAt, rooming, now, paymentLinks.HashFor(bookingId), now, bookingId);
            session.Save(booking);
            return booking;
        }, ct);

        await RecordPaymentStepAsync(draft!, boat, request, stay, ct);

        CheckoutSessionResult checkout;
        try
        {
            checkout = await paymentGateway.CreateEmbeddedCheckoutSessionAsync(new CheckoutSessionRequest(
                hold.Id, hold.Reference.Value, CheckoutPurpose.Deposit, quote.AmountDueAtCheckout,
                $"{boat.Name} {Format.Date(stay.StartDate)} – {Format.Date(stay.EndDate)} ({hold.Reference.Value})",
                details.Email,
                clock.UtcNow + bookingOptions.Value.CheckoutSessionLifetime + TimeSpan.FromMinutes(1)), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not create the checkout session for {Reference}; releasing the hold", hold.Reference.Value);
            hold.Expire(clock.UtcNow);
            await bookings.SaveAsync(hold, ct);
            throw;
        }

        hold.AttachCheckoutSession(checkout.SessionId, clock.UtcNow);
        await bookings.SaveAsync(hold, ct);
        logger.LogInformation("Hold {Reference} created for {Boat} {Stay}", hold.Reference.Value, boat.Slug, stay);
        return new CreateOnlineBookingResult(hold.Reference.Value, checkout.ClientSecret, checkout.SessionId,
            quote.AmountDueAtCheckout);
    }

    /// <summary>Returns the status only when the session id matches the booking's deposit checkout.</summary>
    public async Task<BookingStatusDto> GetStatusAsync(string reference, string? sessionId, CancellationToken ct = default)
    {
        if (!BookingReference.TryParse(reference, out var parsed) || string.IsNullOrWhiteSpace(sessionId))
        {
            throw new NotFoundException("Booking not found.");
        }

        var booking = await bookings.GetByReferenceAsync(parsed!, ct);
        if (booking is null || booking.StripeCheckoutSessionId != sessionId)
        {
            throw new NotFoundException("Booking not found.");
        }

        var boat = await boats.GetAsync(booking.BoatId, ct);
        return new BookingStatusDto(booking.Reference.Value, booking.Status, boat?.Name ?? string.Empty, booking.StartDate,
            booking.EndDate, booking.CustomerDetails?.Email ?? string.Empty, booking.AmountPaid);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private QuoteDto BuildQuote(Boat boat, BookingCatalogue catalogue, BookingRequest request)
    {
        var stay = StayPeriodFactory.OnlineStayStartingOn(request.PeriodType, request.StartDate)
                   ?? throw RequestValidationException.For("startDate", "Invalid period.");
        HirePriceQuote price;
        try
        {
            price = PricingService.QuoteHirePrice(boat, catalogue.Seasons, request.PeriodType, stay.StartDate);
        }
        catch (DomainValidationException ex)
        {
            logger.LogError("Missing rate when quoting {Boat}: {Message}", boat.Slug, ex.Message);
            throw RequestValidationException.For("startDate",
                $"We can't price these dates online. Please contact us on {catalogue.Settings.ContactPhone} / {catalogue.Settings.ContactEmail}.");
        }

        var addons = AddonCatalogue.InDisplayOrder(request.Addons.Select(r => catalogue.Addons.First(a => a.Id.Value == r.AddonId)))
            .Select(definition =>
            {
                var quantity = definition.NormaliseQuantity(request.Addons.First(r => r.AddonId == definition.Id.Value).Quantity);
                var unit = PricingService.AddonUnitPrice(definition, request.PeriodType);
                return new QuoteAddonDto(definition.Id.Value, definition.Name, quantity, unit, unit * quantity);
            }).ToList();

        var now = clock.UtcNow;
        var deposit = boat.EffectiveDeposit(catalogue.Settings.DepositAmount);
        var total = price.Price + addons.Sum(a => a.LineTotal ?? 0m);
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);
        var dueAtCheckout = PaymentScheduleService.OnlineCheckoutAmount(price.Price, deposit);
        var milestones = paymentSchedule.Milestones(schedule, stay.StartDate, total, deposit, now);
        return new QuoteDto(boat.Name, request.PeriodType, stay.StartDate, stay.EndDate, Format.Time(Stay.CheckInTime),
            Format.Time(Stay.CheckInWindowEnd), Format.Time(Stay.CheckOutTime),
            request.NumberOfAdults, request.NumberOfChildren, price.Season.Name, price.Price, addons, total, deposit,
            dueAtCheckout, schedule, MilestoneViews.Build(milestones, dueAtCheckout, true, now),
            boat.RequiresRoomingWarning(request.NumberOfGuests) ? boat.RoomingWarningText(request.NumberOfGuests) : null,
            boat.SecurityBond);
    }

    /// <summary>Capacity is checked without a lock: a truly simultaneous race may oversell, which is accepted.</summary>
    private async Task<IReadOnlyDictionary<AddonId, int>> RemainingCapacityAsync(BookingCatalogue catalogue,
        BookingRequest request, CancellationToken ct)
    {
        var stay = PeriodTypes.Online.Contains(request.PeriodType)
            ? StayPeriodFactory.OnlineStayStartingOn(request.PeriodType, request.StartDate)
            : null;
        if (stay is null)
        {
            return new Dictionary<AddonId, int>();
        }

        var requested = catalogue.Addons.Where(a => request.Addons.Any(r => r.AddonId == a.Id.Value));
        return await capacity.RemainingForAsync(requested, stay, null, ct);
    }

    /// <summary>The draft now holds what was submitted; a failure here must not stop the payment.</summary>
    private async Task RecordPaymentStepAsync(BookingDraft draft, Boat boat, CreateOnlineBookingRequest request, Stay stay,
        CancellationToken ct)
    {
        try
        {
            draft.Update(boat.Id, request.PeriodType, stay, request.NumberOfAdults, request.NumberOfChildren,
                request.FullName!, request.Email!, request.Mobile!,
                (request.Addons ?? []).Select(a => new DraftAddonRequest(new AddonId(a.AddonId), a.Quantity)),
                BookingStep.Payment, clock.UtcNow);
            await drafts.SaveAsync(draft, ct);
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException or DomainValidationException)
        {
            logger.LogWarning(ex, "Could not update booking draft {DraftId}", draft.Id.Value);
        }
    }

    private static Stay? DraftStay(PeriodType periodType, DateOnly startDate, ValidationErrors errors)
    {
        var stay = PeriodTypes.Online.Contains(periodType) ? StayPeriodFactory.OnlineStayStartingOn(periodType, startDate) : null;
        if (stay is null)
        {
            errors.Add("startDate", "Please choose your dates again.");
        }

        return stay;
    }

    private static void DraftGuests(int numberOfAdults, int numberOfChildren, ValidationErrors errors)
    {
        if (numberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "At least one adult must be on board.");
        }

        if (numberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Number of children cannot be negative.");
        }
    }

    /// <summary>A draft can only be for a boat that can be booked online.</summary>
    private async Task<Boat> FindDraftBoatAsync(string slug, CancellationToken ct)
    {
        var boat = await FindHouseBoatAsync(slug, ct);
        return boat.IsActive
            ? boat
            : throw RequestValidationException.For("slug", "This boat is currently unavailable for online booking.");
    }

    private async Task<BookingDraft> FindOpenDraftAsync(string draftId, CancellationToken ct)
    {
        var draft = string.IsNullOrWhiteSpace(draftId) || draftId.Length > 100
            ? null
            : await drafts.GetAsync(new BookingDraftId(draftId), ct);
        return draft is null || draft.IsConverted ? throw new NotFoundException("Booking draft not found.") : draft;
    }

    private BookingValidationContext Context(Boat boat, BookingCatalogue catalogue, BookingValidationScope scope,
        BookingDraft? draft, IReadOnlyDictionary<AddonId, int> remaining) =>
        new(boat, catalogue.Settings, catalogue.BlockedPeriods, catalogue.Addons, clock.Today(), scope, draft, remaining);

    private async Task<IReadOnlyList<Boat>> ActiveHouseBoatsAsync(CancellationToken ct) =>
        Boat.InDisplayOrder((await boats.ListAsync(ct)).Where(b => b.IsActive && b.Type == BoatType.House)).ToList();

    /// <summary>
    /// The active house boats, with every booking that blocks availability and every unavailability of any boat in
    /// [from, to).
    /// </summary>
    private async Task<(IReadOnlyList<Boat> Fleet, IReadOnlyList<Booking> Blocking, IReadOnlyList<BoatUnavailability> Unavailable)>
        OccupancyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fleetTask = ActiveHouseBoatsAsync(ct);
        var bookingsTask = bookings.ListOverlappingAsync(from, to, [BookingStatus.Active, BookingStatus.PendingPayment], null, ct);
        var unavailableTask = unavailabilities.ListOverlappingAsync(from, to, null, ct);
        await Task.WhenAll(fleetTask, bookingsTask, unavailableTask);
        var now = clock.UtcNow;
        return (fleetTask.Result, bookingsTask.Result.Where(b => b.BlocksAvailability(now)).ToList(), unavailableTask.Result);
    }

    private async Task<Boat> FindHouseBoatAsync(string slug, CancellationToken ct)
    {
        var boat = await boats.GetBySlugAsync((slug ?? string.Empty).Trim().ToLowerInvariant(), ct);
        if (boat is null || boat.Type != BoatType.House)
        {
            throw new NotFoundException("We couldn't find that boat.");
        }

        return boat;
    }
}
