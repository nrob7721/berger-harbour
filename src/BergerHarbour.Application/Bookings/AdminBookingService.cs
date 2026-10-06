using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Bookings;

/// <summary>Staff booking use cases for the admin Bookings page.</summary>
public sealed class AdminBookingService(
    IBookingRepository bookings,
    IBoatRepository boats,
    ICustomerRepository customers,
    IBoatUnavailabilityRepository unavailabilities,
    BookingCatalogueLoader catalogueLoader,
    IBookingRequestValidationStrategy validation,
    PaymentScheduleService paymentSchedule,
    CustomerService customerService,
    IBookingReferenceGenerator references,
    IBoatScheduleLock scheduleLock,
    NotificationService notifications,
    PaymentLinkTokens paymentLinks,
    IClock clock)
{
    private static readonly BookingStatus[] OccupyingStatuses = [BookingStatus.Active, BookingStatus.PendingPayment];

    // ---- Queries --------------------------------------------------------------------------------------------------

    public async Task<TimelineDto> GetTimelineAsync(DateOnly from, DateOnly to, bool includeInactive, CancellationToken ct = default)
    {
        if (from >= to || to > from.AddMonths(3))
        {
            throw RequestValidationException.For("to", "Choose a range of up to three months.");
        }

        var boatsTask = boats.ListAsync(ct);
        var bookingsTask = bookings.ListOverlappingAsync(from, to, includeInactive ? null : OccupyingStatuses, null, ct);
        var unavailableTask = unavailabilities.ListOverlappingAsync(from, to, null, ct);
        var catalogueTask = catalogueLoader.LoadAsync(ct);
        await Task.WhenAll(boatsTask, bookingsTask, unavailableTask, catalogueTask);

        var now = clock.UtcNow;
        var list = bookingsTask.Result
            .Where(b => includeInactive || b.Status == BookingStatus.Active || b.BlocksAvailability(now))
            .ToList();
        var names = await CustomerNamesAsync(list, ct);
        var window = new Stay(from, to);
        return new TimelineDto(from, to,
            boatsTask.Result.Where(b => b.Type == BoatType.House).OrderBy(b => b.Name)
                .Select(b => new TimelineBoatDto(b.Id.Value, b.Name, b.IsActive)).ToList(),
            list.OrderBy(b => b.StartDate).Select(b => new TimelineBookingDto(b.Id.Value, b.Reference.Value, b.BoatId.Value,
                b.StartDate, b.EndDate, b.PeriodType, b.Status, b.IsStandby, b.PaymentStatus,
                names.GetValueOrDefault(b.CustomerId, "?"))).ToList(),
            unavailableTask.Result.Select(u => new TimelineUnavailabilityDto(u.Id.Value, u.BoatId.Value, u.Nights.FirstNight,
                u.Nights.LastNight, u.Comments)).ToList(),
            catalogueTask.Result.BlockedPeriods.Where(p => window.Overlaps(p.Nights)).Select(p =>
                new TimelineBlockedPeriodDto(p.Id.Value, p.Name, p.Nights.FirstNight, p.Nights.LastNight)).ToList());
    }

    /// <summary>Search by customer name, email or booking reference.</summary>
    public async Task<IReadOnlyList<BookingSearchResultDto>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < 2)
        {
            return [];
        }

        var found = new Dictionary<BookingId, Booking>();
        if (BookingReference.TryParse(term, out var reference) &&
            await bookings.GetByReferenceAsync(reference!, ct) is { } byReference)
        {
            found[byReference.Id] = byReference;
        }

        var matchingCustomers = CustomerQueries.Filter(await customers.ListAsync(ct), term).Take(50).ToList();
        if (matchingCustomers.Count > 0)
        {
            foreach (var b in await bookings.ListByCustomerAsync(matchingCustomers.Select(c => c.Id).ToList(), ct))
            {
                found[b.Id] = b;
            }
        }

        var customerById = (await customers.GetManyAsync(found.Values.Select(b => b.CustomerId).Distinct(), ct))
            .ToDictionary(c => c.Id);
        var boatNames = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return found.Values.OrderByDescending(b => b.StartDate).Take(50).Select(b =>
        {
            var customer = customerById.GetValueOrDefault(b.CustomerId);
            return new BookingSearchResultDto(b.Id.Value, b.Reference.Value, boatNames.GetValueOrDefault(b.BoatId, "?"),
                b.StartDate, b.EndDate, b.Status, b.IsStandby, customer?.FullName ?? "?", customer?.Email.Value ?? string.Empty);
        }).ToList();
    }

    public async Task<BookingDetailDto> GetAsync(string id, CancellationToken ct = default) =>
        await ToDetailAsync(await LoadAsync(id, ct), ct);

    /// <summary>Pre-fills the booking dialog: the rate-table price and the default end date.</summary>
    public async Task<AdminQuoteDto> QuoteAsync(string boatId, PeriodType periodType, DateOnly startDate, CancellationToken ct = default)
    {
        var boat = await boats.GetAsync(new BoatId(boatId), ct) ?? throw new NotFoundException("Boat not found.");
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var end = StayPeriodFactory.DefaultEndDate(periodType, startDate);
        var schedule = end is { } e ? PaymentScheduleService.DetermineSchedule(new Stay(startDate, e), catalogue.BlockedPeriods) : (PaymentSchedule?)null;
        if (periodType == PeriodType.Custom)
        {
            return new AdminQuoteDto(null, null, end, schedule, "Enter the hire price for a custom stay.");
        }

        try
        {
            var quote = PricingService.QuoteHirePrice(boat, catalogue.Seasons, periodType, startDate);
            return new AdminQuoteDto(quote.Price, quote.Season.Name, end, schedule, null);
        }
        catch (DomainValidationException ex)
        {
            return new AdminQuoteDto(null, null, end, schedule, ex.Message);
        }
    }

    // ---- Create and update ----------------------------------------------------------------------------------------

    public async Task<BookingDetailDto> CreateAsync(AdminBookingRequest request, CancellationToken ct = default)
    {
        var boat = await boats.GetAsync(new BoatId(request.BoatId ?? string.Empty), ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        Validate(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfGuests, request.FullName,
            request.Email, boat, catalogue);
        EnsureBookable(boat);

        var stay = new Stay(request.StartDate, request.EndDate!.Value);
        var hirePrice = request.HirePrice ?? PriceFromRates(boat!, catalogue, request.PeriodType, request.StartDate);
        var customer = await customerService.UpsertAsync(request.FullName!, request.Email!, request.Phone, ct);
        var now = clock.UtcNow;
        var reference = await references.NextAsync(SydneyTime.ToLocal(now).Year, ct);
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);
        var id = BookingId.New();

        var (booking, superseded) = await scheduleLock.RunExclusiveAsync(boat!.Id, async session =>
        {
            var occupying = await session.GetOccupyingBookingsAsync(stay);
            BookingAvailabilityService.EnsureAvailable(boat.Id, stay, occupying, await session.GetUnavailabilitiesAsync(stay), now);
            var created = Booking.CreateByStaff(reference, customer.Id, boat.Id, request.PeriodType, stay,
                request.NumberOfGuests, request.IsStandby, request.Comments, hirePrice, catalogue.Settings.DepositAmount,
                schedule, paymentLinks.HashFor(id), now, id);
            session.Save(created);
            return (created, SupersedeStandbys(session, created, occupying, now));
        }, ct);

        await SendAfterActivationAsync(booking, superseded, ct);
        return await ToDetailAsync(booking, ct);
    }

    public async Task<BookingDetailDto> UpdateAsync(string id, AdminBookingUpdateRequest request, CancellationToken ct = default)
    {
        var existing = await LoadAsync(id, ct);
        existing.EnsureVersion(request.Version);
        var boat = await boats.GetAsync(new BoatId(request.BoatId ?? string.Empty), ct);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        Validate(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfGuests, request.FullName,
            request.Email, boat, catalogue);
        if (boat!.Id != existing.BoatId)
        {
            EnsureBookable(boat);
        }

        var stay = new Stay(request.StartDate, request.EndDate!.Value);
        var customer = await customerService.UpsertAsync(request.FullName!, request.Email!, request.Phone, ct);
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);

        if (!existing.IsEditableByStaff)
        {
            var unchanged = boat.Id == existing.BoatId && stay == existing.Stay && request.PeriodType == existing.PeriodType &&
                            request.NumberOfGuests == existing.NumberOfGuests && request.IsStandby == existing.IsStandby &&
                            request.HirePrice == existing.HirePrice && request.Status == existing.Status;
            if (!unchanged)
            {
                throw RequestValidationException.For("status",
                    $"A {existing.Status} booking can't be changed; only its comments and customer details can.");
            }

            existing.UpdateComments(request.Comments, clock.UtcNow);
            existing.ChangeCustomer(customer.Id, clock.UtcNow);
            await bookings.SaveAsync(existing, ct);
            return await ToDetailAsync(existing, ct);
        }

        var (booking, superseded) = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var now = clock.UtcNow;
            var b = await session.GetBookingAsync(existing.Id) ?? throw new NotFoundException("Booking not found.");
            b.EnsureVersion(request.Version);
            ApplyStatus(b, request.Status, now);
            b.ChangeDetails(boat.Id, request.PeriodType, stay, request.NumberOfGuests, request.IsStandby, request.Comments,
                request.HirePrice, schedule, now);
            b.ChangeCustomer(customer.Id, now);

            IReadOnlyList<Booking> superseded = [];
            if (b.Status == BookingStatus.Active)
            {
                var occupying = await session.GetOccupyingBookingsAsync(stay);
                BookingAvailabilityService.EnsureAvailable(boat.Id, stay, occupying,
                    await session.GetUnavailabilitiesAsync(stay), now, b.Id);
                superseded = SupersedeStandbys(session, b, occupying, now);
            }

            session.Save(b);
            return (b, superseded);
        }, ct);

        await SendAfterActivationAsync(booking, superseded, ct);
        return await ToDetailAsync(booking, ct);
    }

    public async Task<BookingDetailDto> RecalculatePriceAsync(string id, VersionRequest request, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        var boat = await boats.GetAsync(booking.BoatId, ct) ?? throw new NotFoundException("Boat not found.");
        var catalogue = await catalogueLoader.LoadAsync(ct);
        booking.SetHirePrice(PriceFromRates(boat, catalogue, booking.PeriodType, booking.StartDate), clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        return await ToDetailAsync(booking, ct);
    }

    // ---- Add-ons --------------------------------------------------------------------------------------------------

    public async Task<BookingDetailDto> AddAddonAsync(string id, AddAddonLineRequest request, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var addon = catalogue.Addons.FirstOrDefault(a => a.Id.Value == request.AddonId && a.IsActive)
                    ?? throw RequestValidationException.For("addonId", "Choose an active add-on.");
        booking.AddAddon(new RequestedAddon(addon.Id, addon.Name, addon.NormaliseQuantity(request.Quantity),
            PricingService.AddonUnitPrice(addon, booking.PeriodType)), clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        return await ToDetailAsync(booking, ct);
    }

    /// <summary>Staff confirm or decline add-ons and contact the customer by hand; no email is sent.</summary>
    public async Task<BookingDetailDto> UpdateAddonAsync(string id, string lineId, UpdateAddonLineRequest request,
        CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        booking.UpdateAddonLine(lineId, request.Quantity, request.UnitPrice, request.Status, clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        return await ToDetailAsync(booking, ct);
    }

    public async Task<BookingDetailDto> RemoveAddonAsync(string id, string lineId, int version, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(version);
        booking.RemoveAddonLine(lineId, clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        return await ToDetailAsync(booking, ct);
    }

    // ---- Payments -------------------------------------------------------------------------------------------------

    public async Task<BookingDetailDto> AddManualPaymentAsync(string id, ManualPaymentRequest request, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        var payment = booking.RecordManualPayment(request.Amount, SydneyTime.ToInstant(request.Date, new TimeOnly(12, 0)),
            request.Note, clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        await notifications.SendPaymentReceivedAsync(booking, payment, ct);
        return await ToDetailAsync(booking, ct);
    }

    /// <summary>Emails #4 immediately for the amount due now. Not deduplicated.</summary>
    public async Task<SendPaymentLinkResult> SendPaymentLinkAsync(string id, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        if (!booking.ReceivesCustomerEmails)
        {
            throw RequestValidationException.For("status", "Payment links can only be sent for Active, non-stand-by bookings.");
        }

        var due = paymentSchedule.AmountDueNow(booking, clock.UtcNow);
        if (due <= 0)
        {
            throw RequestValidationException.For("amountOwing", "Nothing is owing on this booking.");
        }

        await notifications.SendPaymentDueAsync(booking, null, null, ct);
        var customer = await customers.GetAsync(booking.CustomerId, ct);
        return new SendPaymentLinkResult(customer?.Email.Value ?? string.Empty, due);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private void Validate(PeriodType periodType, DateOnly start, DateOnly? end, int guests, string? fullName, string? email,
        Boat? boat, BookingCatalogue catalogue)
    {
        var request = new BookingRequest(periodType, start, end, guests, fullName, email, null, []);
        var errors = validation.Validate(request,
            new BookingValidationContext(boat, catalogue.Settings, catalogue.BlockedPeriods, catalogue.Addons, clock.Today()));
        if (boat is null)
        {
            errors.Add("boatId", "Choose a boat.");
        }

        errors.ThrowIfInvalid();
    }

    /// <summary>Inactive boats cannot be booked by staff either.</summary>
    private static void EnsureBookable(Boat? boat)
    {
        if (boat is null || !boat.IsActive)
        {
            throw RequestValidationException.For("boatId", "This boat is inactive and cannot be booked.");
        }
    }

    private static decimal PriceFromRates(Boat boat, BookingCatalogue catalogue, PeriodType periodType, DateOnly start)
    {
        if (periodType == PeriodType.Custom)
        {
            throw RequestValidationException.For("hirePrice", "Enter the hire price for a custom stay.");
        }

        try
        {
            return PricingService.QuoteHirePrice(boat, catalogue.Seasons, periodType, start).Price;
        }
        catch (DomainValidationException ex)
        {
            throw RequestValidationException.For("hirePrice", ex.Message);
        }
    }

    private static void ApplyStatus(Booking booking, BookingStatus requested, DateTimeOffset now)
    {
        if (requested == booking.Status)
        {
            return;
        }

        switch (requested)
        {
            case BookingStatus.Cancelled:
                booking.Cancel(now);
                break;
            case BookingStatus.Active when booking.Status == BookingStatus.Cancelled:
                booking.Reinstate(now);
                break;
            default:
                throw RequestValidationException.For("status", "Staff can only set a booking to Active or Cancelled.");
        }
    }

    private static IReadOnlyList<Booking> SupersedeStandbys(IBoatScheduleSession session, Booking booking,
        IEnumerable<Booking> occupying, DateTimeOffset now)
    {
        var superseded = BookingAvailabilityService.StandbysSupersededBy(booking, occupying);
        foreach (var standby in superseded)
        {
            standby.Supersede(now);
            session.Save(standby);
        }

        return superseded;
    }

    /// <summary>#1 for a non-stand-by Active booking (once), and #8 for each superseded stand-by.</summary>
    private async Task SendAfterActivationAsync(Booking booking, IReadOnlyList<Booking> superseded, CancellationToken ct)
    {
        await notifications.SendBookingConfirmedAsync(booking, ct);
        foreach (var standby in superseded)
        {
            await notifications.SendStaffStandbySupersededAsync(standby, ct);
        }
    }

    private async Task<Booking> LoadAsync(string id, CancellationToken ct) =>
        await bookings.GetAsync(new BookingId(id ?? string.Empty), ct) ?? throw new NotFoundException("Booking not found.");

    private async Task<Dictionary<CustomerId, string>> CustomerNamesAsync(IEnumerable<Booking> list, CancellationToken ct) =>
        (await customers.GetManyAsync(list.Select(b => b.CustomerId).Distinct(), ct)).ToDictionary(c => c.Id, c => c.FullName);

    private async Task<BookingDetailDto> ToDetailAsync(Booking b, CancellationToken ct)
    {
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = await customers.GetAsync(b.CustomerId, ct);
        var now = clock.UtcNow;
        var milestones = paymentSchedule.Milestones(b);
        return new BookingDetailDto(b.Id.Value, b.Version, b.Reference.Value, b.CreatedBy, b.IsStandby, b.Status,
            b.BoatId.Value, boat?.Name ?? "?", b.PeriodType, b.StartDate, b.EndDate, b.NumberOfGuests, b.Comments,
            b.HirePrice, b.DepositAmount, b.PaymentSchedule,
            customer is null ? new CustomerDto(b.CustomerId.Value, "?", string.Empty, null, b.CreatedDate) : CustomerQueries.ToDto(customer),
            b.AddonLines.Select(l => new AddonLineDto(l.Id, l.AddonId.Value, l.NameSnapshot, l.Quantity, l.UnitPrice, l.Status,
                l.EstimatedAmount)).ToList(),
            b.Payments.OrderBy(p => p.PaidAt).Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.StripePaymentIntentId,
                p.PaidAt, p.Note, p.RecordedBy)).ToList(),
            b.TotalPrice, b.AmountPaid, b.AmountOwing,
            b.Status == BookingStatus.Active ? paymentSchedule.AmountDueNow(b, now) : 0m,
            b.PaymentStatus, MilestoneViews.Build(milestones, 0m, false), b.HoldExpiresAt, b.CreatedDate, b.ModifiedDate,
            b.TermsAcceptedAt, b.RoomingWarningAcceptance?.AcceptedAt, b.RoomingWarningAcceptance?.WarningTextShown,
            b.GroupRestrictionDeclaredNotApplicable, b.SentNotifications);
    }
}
