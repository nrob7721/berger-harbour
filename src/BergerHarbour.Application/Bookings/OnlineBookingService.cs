using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Application.Bookings;

/// <summary>Customer-facing use cases: boat info, availability, quote, booking with a hold, completion status.</summary>
public sealed class OnlineBookingService(
    IBoatRepository boats,
    IBookingRepository bookings,
    IBoatUnavailabilityRepository unavailabilities,
    ICustomerRepository customers,
    BookingCatalogueLoader catalogueLoader,
    IBookingRequestValidationStrategy validation,
    PaymentScheduleService paymentSchedule,
    CustomerService customerService,
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

    public async Task<PublicBoatDto> GetBoatAsync(string slug, CancellationToken ct = default)
    {
        var boat = await FindHouseBoatAsync(slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var settings = catalogue.Settings;
        var addons = boat.IsActive
            ? catalogue.Addons.Where(a => a.IsActive && boat.AllowsAddon(a.Id)).OrderBy(a => a.Name).Select(a =>
                new PublicAddonDto(a.Id.Value, a.Name, a.Description, a.PriceOnRequest, a.QuantityApplies,
                    a.Prices is { } p ? new AddonPricesDto(p.Midweek, p.Weekend, p.Week) : null)).ToList()
            : [];
        return new PublicBoatDto(boat.Slug, boat.Name, boat.MaxNoOfGuests, boat.NoOfBeds, boat.BeddingDescription,
            boat.SecurityBond, boat.IsActive, addons,
            new PublicBookingSettingsDto(settings.MinimumLeadTimeDays, settings.BookingsOpenUntil, settings.ContactPhone,
                settings.ContactEmail, settings.HireTermsUrl, clock.Today(), Format.Time(Stay.CheckInTime),
                Format.Time(Stay.CheckOutTime)));
    }

    public async Task<AvailabilityDto> GetAvailabilityAsync(string slug, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (from >= to)
        {
            throw RequestValidationException.For("to", "'to' must be after 'from'.");
        }

        if (to > from.AddMonths(MaxAvailabilitySpanMonths))
        {
            throw RequestValidationException.For("to", $"The range can be at most {MaxAvailabilitySpanMonths} months.");
        }

        var boat = await FindHouseBoatAsync(slug, ct);
        var now = clock.UtcNow;
        var bookingsTask = bookings.ListOverlappingAsync(from, to, [BookingStatus.Active, BookingStatus.PendingPayment], boat.Id, ct);
        var unavailableTask = unavailabilities.ListOverlappingAsync(from, to, boat.Id, ct);
        var catalogueTask = catalogueLoader.LoadAsync(ct);
        await Task.WhenAll(bookingsTask, unavailableTask, catalogueTask);

        var window = new Stay(from, to);
        var unavailable = bookingsTask.Result.Where(b => b.BlocksAvailability(now))
            .Select(b => new DateRangeDto(b.StartDate, b.EndDate))
            .Concat(unavailableTask.Result.Select(u => new DateRangeDto(u.Nights.FirstNight, u.Nights.LastNight.AddDays(1))))
            .OrderBy(r => r.Start)
            .ToList();
        var enquireOnly = catalogueTask.Result.BlockedPeriods.Where(p => window.Overlaps(p.Nights))
            .OrderBy(p => p.Nights.FirstNight)
            .Select(p => new EnquireOnlyDto(p.Name, p.Nights.FirstNight, p.Nights.LastNight))
            .ToList();
        return new AvailabilityDto(unavailable, enquireOnly);
    }

    public async Task<QuoteDto> QuoteAsync(string slug, QuoteRequest request, CancellationToken ct = default)
    {
        var boat = await FindHouseBoatAsync(slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, null, request.NumberOfGuests, null,
            null, null, request.Addons ?? []);
        validation.Validate(bookingRequest, Context(boat, catalogue, BookingValidationScope.Quote)).ThrowIfInvalid();
        return BuildQuote(boat, catalogue, bookingRequest);
    }

    public async Task<CreateOnlineBookingResult> CreateAsync(CreateOnlineBookingRequest request, string? remoteIp,
        CancellationToken ct = default)
    {
        if (!await turnstile.VerifyAsync(request.TurnstileToken, remoteIp, ct))
        {
            throw RequestValidationException.For("turnstileToken",
                "We could not verify you are human. Please try the check again.");
        }

        var boat = await FindHouseBoatAsync(request.Slug, ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, null, request.NumberOfGuests,
            request.FullName, request.Email, request.Mobile, request.Addons ?? [], request.RoomingWarningAccepted,
            request.RoomingWarningText, request.GroupRestrictionApplies, request.TermsAccepted);
        validation.Validate(bookingRequest, Context(boat, catalogue, BookingValidationScope.Submit)).ThrowIfInvalid();

        var quote = BuildQuote(boat, catalogue, bookingRequest);
        var stay = new Stay(quote.StartDate, quote.EndDate);
        var customer = await customerService.UpsertAsync(request.FullName!, request.Email!, request.Mobile, ct);
        var now = clock.UtcNow;
        var reference = await references.NextAsync(SydneyTime.ToLocal(now).Year, ct);
        var bookingId = BookingId.New();
        var holdExpiresAt = now + bookingOptions.Value.HoldDuration;
        var rooming = boat.RequiresRoomingWarning(request.NumberOfGuests)
            ? new RoomingWarningAcceptance(now, boat.RoomingWarningText(request.NumberOfGuests))
            : null;
        var requestedAddons = quote.Addons.Select(a => new RequestedAddon(new AddonId(a.AddonId), a.Name, a.Quantity, a.UnitPrice)).ToList();

        var hold = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var occupying = await session.GetOccupyingBookingsAsync(stay);
            var unavailable = await session.GetUnavailabilitiesAsync(stay);
            BookingAvailabilityService.EnsureAvailable(boat.Id, stay, occupying, unavailable, now);
            var booking = Booking.CreateOnlineHold(reference, customer.Id, boat.Id, request.PeriodType, stay,
                request.NumberOfGuests, quote.HirePrice, catalogue.Settings.DepositAmount, quote.PaymentSchedule,
                requestedAddons, holdExpiresAt, rooming, now, paymentLinks.HashFor(bookingId), now, bookingId);
            session.Save(booking);
            return booking;
        }, ct);

        CheckoutSessionResult checkout;
        try
        {
            checkout = await paymentGateway.CreateEmbeddedCheckoutSessionAsync(new CheckoutSessionRequest(
                hold.Id, hold.Reference.Value, CheckoutPurpose.Deposit, quote.AmountDueAtCheckout,
                $"{boat.Name} {Format.Date(stay.StartDate)} – {Format.Date(stay.EndDate)} ({hold.Reference.Value})",
                customer.Email.Value,
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
        var customer = await customers.GetAsync(booking.CustomerId, ct);
        return new BookingStatusDto(booking.Reference.Value, booking.Status, boat?.Name ?? string.Empty, booking.StartDate,
            booking.EndDate, customer?.Email.Value ?? string.Empty, booking.AmountPaid);
    }

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

        var addons = request.Addons.Select(r =>
        {
            var definition = catalogue.Addons.First(a => a.Id.Value == r.AddonId);
            var quantity = definition.NormaliseQuantity(r.Quantity);
            var unit = PricingService.AddonUnitPrice(definition, request.PeriodType);
            return new QuoteAddonDto(definition.Id.Value, definition.Name, quantity, unit, unit * quantity);
        }).ToList();

        var now = clock.UtcNow;
        var deposit = catalogue.Settings.DepositAmount;
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);
        var dueAtCheckout = paymentSchedule.OnlineCheckoutAmount(schedule, stay.StartDate, price.Price, deposit, now);
        var milestones = paymentSchedule.Milestones(schedule, stay.StartDate, price.Price, deposit, now);
        return new QuoteDto(boat.Name, request.PeriodType, stay.StartDate, stay.EndDate, Format.Time(Stay.CheckInTime),
            Format.Time(Stay.CheckOutTime), request.NumberOfGuests, price.Season.Name, price.Price, addons, deposit,
            dueAtCheckout, dueAtCheckout >= price.Price, schedule, MilestoneViews.Build(milestones, dueAtCheckout, true),
            boat.RequiresRoomingWarning(request.NumberOfGuests) ? boat.RoomingWarningText(request.NumberOfGuests) : null,
            boat.SecurityBond);
    }

    private BookingValidationContext Context(Boat boat, BookingCatalogue catalogue, BookingValidationScope scope) =>
        new(boat, catalogue.Settings, catalogue.BlockedPeriods, catalogue.Addons, clock.Today(), scope);

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
