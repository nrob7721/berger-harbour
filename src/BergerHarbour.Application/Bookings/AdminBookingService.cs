using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Payments;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Application.Bookings;

/// <summary>Staff booking use cases for the admin Bookings and Customers pages.</summary>
public sealed class AdminBookingService(
    IBookingRepository bookings,
    IBoatRepository boats,
    ICustomerRepository customers,
    IBoatUnavailabilityRepository unavailabilities,
    BookingCatalogueLoader catalogueLoader,
    IBookingRequestValidationStrategy validation,
    PaymentScheduleService paymentSchedule,
    AddonCapacityQueries capacity,
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
            Boat.InDisplayOrder(boatsTask.Result.Where(b => b.Type == BoatType.House))
                .Select(b => new TimelineBoatDto(b.Id.Value, b.Name, b.MaxNoOfGuests, b.IsActive)).ToList(),
            list.OrderBy(b => b.StartDate).Select(b => new TimelineBookingDto(b.Id.Value, b.Reference.Value, b.BoatId.Value,
                b.StartDate, b.EndDate, b.CheckInTime, b.CheckOutTime, b.PeriodType, b.Status, b.IsStandby, b.PaymentStatus, NameOf(b, names))).ToList(),
            unavailableTask.Result.Select(u => new TimelineUnavailabilityDto(u.Id.Value, u.BoatId.Value, u.Nights.FirstNight,
                u.Nights.LastNight, u.Comments)).ToList(),
            catalogueTask.Result.BlockedPeriods.Where(p => window.Overlaps(p.Nights)).Select(p =>
                new TimelineBlockedPeriodDto(p.Id.Value, p.Name, p.Nights.FirstNight, p.Nights.LastNight)).ToList());
    }

    /// <summary>
    /// Search by customer name, email or booking reference, including the details entered on online bookings that
    /// have no customer record (holds that never became Active).
    /// </summary>
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

        foreach (var status in new[] { BookingStatus.PendingPayment, BookingStatus.Expired })
        {
            foreach (var b in (await bookings.ListByStatusAsync(status, ct)).Where(b => b.CustomerId is null && DetailsMatch(b, term)))
            {
                found[b.Id] = b;
            }
        }

        var customerById = (await customers.GetManyAsync(CustomerIds(found.Values), ct)).ToDictionary(c => c.Id);
        var boatNames = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return found.Values.OrderByDescending(b => b.StartDate).Take(50).Select(b =>
        {
            var customer = b.CustomerId is { } id ? customerById.GetValueOrDefault(id) : null;
            return new BookingSearchResultDto(b.Id.Value, b.Reference.Value, boatNames.GetValueOrDefault(b.BoatId, "?"),
                b.StartDate, b.EndDate, b.Status, b.IsStandby, customer?.FullName ?? b.CustomerDetails?.FullName ?? "?",
                customer?.Email.Value ?? b.CustomerDetails?.Email ?? string.Empty);
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
        var lines = ResolveAddonLines(request.Addons ?? [], request.PeriodType, catalogue);
        Validate(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfAdults, request.NumberOfChildren,
            request.FullName, request.Email, request.Phone, request.SecondaryPhone, boat, catalogue);
        EnsureBookable(boat);

        var stay = new Stay(request.StartDate, request.EndDate!.Value);
        var hirePrice = request.HirePrice ?? PriceFromRates(boat!, catalogue, request.PeriodType, request.StartDate);
        var customer = await customerService.UpsertFromStaffAsync(request.FullName!, request.Email!, request.Phone,
            request.SecondaryPhone, ct);
        var now = clock.UtcNow;
        var reference = await references.NextAsync(SydneyTime.ToLocal(now).Year, ct);
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);
        var id = BookingId.New();

        var (booking, superseded) = await scheduleLock.RunExclusiveAsync(boat!.Id, async session =>
        {
            var occupying = await session.GetOccupyingBookingsAsync(stay);
            BookingAvailabilityService.EnsureAvailable(boat.Id, stay, occupying, await session.GetUnavailabilitiesAsync(stay), now);
            var created = Booking.CreateByStaff(reference, customer.Id, boat.Id, request.PeriodType, stay,
                request.NumberOfAdults, request.NumberOfChildren, request.CheckInTime ?? Stay.CheckInTime,
                request.CheckOutTime ?? Stay.CheckOutTime, request.IsStandby, request.Comments, hirePrice,
                boat.EffectiveDeposit(catalogue.Settings.DepositAmount), schedule, paymentLinks.HashFor(id), now, id);
            foreach (var line in lines)
            {
                created.AddAddon(line, now);
            }

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
        Validate(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfAdults, request.NumberOfChildren,
            request.FullName, request.Email, request.Phone, request.SecondaryPhone, boat, catalogue);
        if (boat!.Id != existing.BoatId)
        {
            EnsureBookable(boat);
        }

        var stay = new Stay(request.StartDate, request.EndDate!.Value);
        var checkIn = request.CheckInTime ?? existing.CheckInTime;
        var checkOut = request.CheckOutTime ?? existing.CheckOutTime;
        var schedule = PaymentScheduleService.DetermineSchedule(stay, catalogue.BlockedPeriods);

        if (!existing.IsEditableByStaff)
        {
            var unchanged = boat.Id == existing.BoatId && stay == existing.Stay && request.PeriodType == existing.PeriodType &&
                            request.NumberOfAdults == existing.NumberOfAdults &&
                            request.NumberOfChildren == existing.NumberOfChildren && checkIn == existing.CheckInTime &&
                            checkOut == existing.CheckOutTime && request.IsStandby == existing.IsStandby &&
                            request.HirePrice == existing.HirePrice && request.Status == existing.Status;
            if (!unchanged)
            {
                throw RequestValidationException.For("status",
                    $"A {existing.Status} booking can't be changed; only its comments and customer details can.");
            }

            existing.UpdateComments(request.Comments, clock.UtcNow);
            if (existing.CustomerId is not null)
            {
                // Online holds that never became Active have no customer, and never create one.
                var linked = await customerService.UpsertFromStaffAsync(request.FullName!, request.Email!, request.Phone,
                    request.SecondaryPhone, ct);
                existing.ChangeCustomer(linked.Id, clock.UtcNow);
            }

            await bookings.SaveAsync(existing, ct);
            return await ToDetailAsync(existing, ct);
        }

        var customer = await customerService.UpsertFromStaffAsync(request.FullName!, request.Email!, request.Phone,
            request.SecondaryPhone, ct);
        var (booking, superseded) = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var now = clock.UtcNow;
            var b = await session.GetBookingAsync(existing.Id) ?? throw new NotFoundException("Booking not found.");
            b.EnsureVersion(request.Version);
            ApplyStatus(b, request.Status, now);
            b.ChangeDetails(boat.Id, request.PeriodType, stay, request.NumberOfAdults, request.NumberOfChildren, checkIn,
                checkOut, request.IsStandby, request.Comments, request.HirePrice, schedule, now);
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

    /// <summary>Staff set the quantity or price and contact the customer by hand; no email is sent.</summary>
    public async Task<BookingDetailDto> UpdateAddonAsync(string id, string lineId, UpdateAddonLineRequest request,
        CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        booking.UpdateAddonLine(lineId, request.Quantity, request.UnitPrice, clock.UtcNow);
        await bookings.SaveAsync(booking, ct);
        return await ToDetailAsync(booking, ct);
    }

    /// <summary>Staff decline an add-on by removing its line; no email is sent.</summary>
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
        return new SendPaymentLinkResult((await ContactAsync(booking, ct)).Email ?? string.Empty, due);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private void Validate(PeriodType periodType, DateOnly start, DateOnly? end, int adults, int children, string? fullName,
        string? email, string? phone, string? secondaryPhone, Boat? boat, BookingCatalogue catalogue)
    {
        var request = new BookingRequest(periodType, start, end, adults, children, fullName, email, phone, []);
        var errors = validation.Validate(request,
            new BookingValidationContext(boat, catalogue.Settings, catalogue.BlockedPeriods, catalogue.Addons, clock.Today()));
        if (boat is null)
        {
            errors.Add("boatId", "Choose a boat.");
        }

        if (!string.IsNullOrWhiteSpace(secondaryPhone) && !PhoneNumber.IsValid(secondaryPhone))
        {
            errors.Add("secondaryPhone", "Enter a valid phone number, including the country code for overseas numbers.");
        }

        errors.ThrowIfInvalid();
    }

    /// <summary>Lines for a new booking: active add-ons only; a null price uses the current price.</summary>
    private static List<RequestedAddon> ResolveAddonLines(IEnumerable<AdminAddonLineRequest> requested, PeriodType periodType,
        BookingCatalogue catalogue)
    {
        var lines = new List<RequestedAddon>();
        foreach (var r in requested)
        {
            var addon = catalogue.Addons.FirstOrDefault(a => a.Id.Value == r.AddonId && a.IsActive)
                        ?? throw RequestValidationException.For("addons", "One of the add-ons is no longer available.");
            lines.Add(new RequestedAddon(addon.Id, addon.Name, addon.NormaliseQuantity(r.Quantity),
                r.UnitPrice ?? PricingService.AddonUnitPrice(addon, periodType)));
        }

        return lines;
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

    private static IEnumerable<CustomerId> CustomerIds(IEnumerable<Booking> list) =>
        list.Where(b => b.CustomerId is not null).Select(b => b.CustomerId!.Value).Distinct();

    private async Task<Dictionary<CustomerId, string>> CustomerNamesAsync(IEnumerable<Booking> list, CancellationToken ct) =>
        (await customers.GetManyAsync(CustomerIds(list), ct)).ToDictionary(c => c.Id, c => c.FullName);

    private static string NameOf(Booking b, IReadOnlyDictionary<CustomerId, string> names) =>
        (b.CustomerId is { } id ? names.GetValueOrDefault(id) : null) ?? b.CustomerDetails?.FullName ?? "?";

    private static bool DetailsMatch(Booking b, string term) =>
        b.CustomerDetails is { } d && (d.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                       d.Email.Contains(term, StringComparison.OrdinalIgnoreCase));

    private sealed record Contact(string? FullName, string? Email, string? Phone, string? SecondaryPhone);

    private async Task<Contact> ContactAsync(Booking b, CancellationToken ct)
    {
        if (b.CustomerId is { } id && await customers.GetAsync(id, ct) is { } customer)
        {
            return new Contact(customer.FullName, customer.Email.Value, customer.MobileNumber, customer.SecondaryPhone);
        }

        return new Contact(b.CustomerDetails?.FullName, b.CustomerDetails?.Email, b.CustomerDetails?.MobileNumber, null);
    }

    private async Task<BookingDetailDto> ToDetailAsync(Booking b, CancellationToken ct)
    {
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } customerId ? await customers.GetAsync(customerId, ct) : null;
        var catalogue = await catalogueLoader.LoadAsync(ct);
        var now = clock.UtcNow;
        var milestones = paymentSchedule.Milestones(b);

        var lineAddons = b.AddonLines.Select(l => l.AddonId).Distinct()
            .Select(addonId => catalogue.Addons.FirstOrDefault(a => a.Id == addonId)).OfType<AddonDefinition>().ToList();
        var capacityBookings = new List<Booking>();
        foreach (var addon in lineAddons.Where(a => a.HasCapacity))
        {
            capacityBookings.AddRange(await capacity.BookingsWithAsync(addon.Id, b.Stay, ct));
        }

        capacityBookings = capacityBookings.DistinctBy(x => x.Id).ToList();
        IReadOnlyList<string> warnings = [];
        if (boat is not null && b.Status == BookingStatus.Active)
        {
            var around = await bookings.ListOverlappingAsync(b.StartDate.AddDays(-1), b.EndDate.AddDays(1), OccupyingStatuses,
                b.BoatId, ct);
            warnings = StaffBookingWarnings.For(boat, b.Stay, b.NumberOfAdults, b.NumberOfChildren, b.CheckInTime, b.CheckOutTime,
                lineAddons.Select(a => (a, b.QuantityOf(a.Id))), around, capacityBookings, now, b.Id);
        }

        return new BookingDetailDto(b.Id.Value, b.Version, b.Reference.Value, b.CreatedBy, b.IsStandby, b.Status,
            b.BoatId.Value, boat?.Name ?? "?", b.PeriodType, b.StartDate, b.EndDate, b.NumberOfAdults, b.NumberOfChildren,
            b.NumberOfGuests, b.CheckInTime, b.CheckOutTime, b.Comments, b.HirePrice, b.DepositAmount, b.PaymentSchedule,
            customer is null ? null : CustomerQueries.ToDto(customer),
            b.CustomerDetails is { } d ? new BookingCustomerDetailsDto(d.FullName, d.Email, d.MobileNumber) : null,
            AddonCatalogue.InDisplayOrder(lineAddons).Select(a => a.Id)
                .Concat(b.AddonLines.Select(l => l.AddonId)).Distinct()
                .SelectMany(addonId => b.AddonLines.Where(l => l.AddonId == addonId))
                .Select(l =>
                {
                    var addon = lineAddons.FirstOrDefault(a => a.Id == l.AddonId);
                    int? remaining = addon is { HasCapacity: true }
                        ? AddonCapacityService.Remaining(addon, b.Stay, capacityBookings, now, b.Id)
                        : null;
                    return new AddonLineDto(l.Id, l.AddonId.Value, l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal,
                        remaining, addon?.Capacity);
                }).ToList(),
            b.Payments.OrderBy(p => p.PaidAt).Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.StripePaymentIntentId,
                p.PaidAt, p.Note, p.RecordedBy)).ToList(),
            b.TotalPrice, b.AmountPaid, b.AmountOwing,
            b.Status == BookingStatus.Active ? paymentSchedule.AmountDueNow(b, now) : 0m,
            b.PaymentStatus, MilestoneViews.Build(milestones, 0m, false, now), b.HoldExpiresAt, b.CreatedDate, b.ModifiedDate,
            b.TermsAcceptedAt, b.RoomingWarningAcceptance?.AcceptedAt, b.RoomingWarningAcceptance?.WarningTextShown,
            b.SentNotifications, warnings);
    }
}
