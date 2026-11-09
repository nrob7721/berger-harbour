using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Application.Bookings.Validation;
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

/// <summary>Staff bookings: timeline, search, the booking dialog, manual payments and payment links (admin <c>BookingsController</c>).</summary>
public sealed class StaffBookingService
{
    private readonly IBookingRepository bookings;
    private readonly IBoatRepository boats;
    private readonly ICustomerRepository customerRepository;
    private readonly IBoatUnavailabilityRepository unavailabilities;
    private readonly ReferenceDataLoader referenceData;
    private readonly IBookingRequestValidationStrategy strategy;
    private readonly IBoatScheduleLock scheduleLock;
    private readonly IBookingReferenceGenerator references;
    private readonly BookingAvailabilityService availability;
    private readonly PricingService pricing;
    private readonly PaymentScheduleService schedule;
    private readonly FrequentFloaterService frequentFloaters;
    private readonly StaffBookingWarnings warnings;
    private readonly CustomerRegistry customers;
    private readonly BookingNotifier notifier;
    private readonly PaymentLinkTokens links;
    private readonly IClock clock;

    public StaffBookingService(
        IBookingRepository bookings, IBoatRepository boats, ICustomerRepository customerRepository, IBoatUnavailabilityRepository unavailabilities,
        ReferenceDataLoader referenceData, IBookingRequestValidationStrategy strategy, IBoatScheduleLock scheduleLock,
        IBookingReferenceGenerator references, BookingAvailabilityService availability, PricingService pricing, PaymentScheduleService schedule,
        FrequentFloaterService frequentFloaters, StaffBookingWarnings warnings, CustomerRegistry customers, BookingNotifier notifier,
        PaymentLinkTokens links, IClock clock)
    {
        this.bookings = bookings;
        this.boats = boats;
        this.customerRepository = customerRepository;
        this.unavailabilities = unavailabilities;
        this.referenceData = referenceData;
        this.strategy = strategy;
        this.scheduleLock = scheduleLock;
        this.references = references;
        this.availability = availability;
        this.pricing = pricing;
        this.schedule = schedule;
        this.frequentFloaters = frequentFloaters;
        this.warnings = warnings;
        this.customers = customers;
        this.notifier = notifier;
        this.links = links;
        this.clock = clock;
    }

    /// <summary>Timeline data for <c>[from, to)</c>: every boat, its bookings, unavailabilities and the blocked periods.</summary>
    public async Task<TimelineResponse> GetTimelineAsync(DateOnly? from, DateOnly? to, bool includeInactive, CancellationToken ct = default)
    {
        if (from is not { } start || to is not { } end)
        {
            throw new DomainValidationException(from is null ? "from" : "to", "Enter both dates.");
        }

        var range = StayRange.Create(start, end, "to");
        var reference = await referenceData.LoadAsync(ct);
        var allBoats = Boat.InListOrder(await boats.ListAsync(ct)).ToList();
        var inRange = (await bookings.ListOverlappingAsync(start, end, ct))
            .Where(b => includeInactive || b.Status is BookingStatus.Active or BookingStatus.PendingPayment)
            .OrderBy(b => b.StartDate)
            .ToList();
        var names = await CustomerNamesAsync(ct);
        return new TimelineResponse(
            allBoats.Select(b => new TimelineBoat(b.Id.Value, b.Name, b.Slug, b.Berth, b.IsActive)).ToList(),
            inRange.Select(b => new TimelineBooking(b.Id.Value, b.BoatId.Value, b.Reference?.Value, NameOf(b, names), b.Status, b.IsStandby, b.PaymentStatus,
                b.PeriodType, b.StartDate, b.EndDate, StaffTimes.Format(b.CheckInTime), StaffTimes.Format(b.CheckOutTime))).ToList(),
            (await unavailabilities.ListInRangeAsync(start, end, ct))
                .Select(u => new TimelineUnavailability(u.Id.Value, u.BoatId.Value, u.Nights.FirstNight, u.Nights.LastNight, u.Comments)).ToList(),
            reference.BlockedPeriods.Where(p => p.Nights.Overlaps(range)).OrderBy(p => p.Nights.FirstNight)
                .Select(p => new TimelineBlockedPeriod(p.Id.Value, p.Name, p.Nights.FirstNight, p.Nights.LastNight)).ToList());
    }

    /// <summary>Bookings matching a name, email or reference (case-insensitive), including online snapshots.</summary>
    public async Task<IReadOnlyList<BookingSearchResult>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return [];
        }

        var all = await bookings.ListAllAsync(ct);
        var byId = (await customerRepository.ListAsync(ct)).ToDictionary(c => c.Id);
        var boatNames = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return all
            .Select(b => (Booking: b, Customer: b.CustomerId is { } id && byId.TryGetValue(id, out var c) ? c : null))
            .Where(x => Matches(term, x.Booking, x.Customer))
            .OrderByDescending(x => x.Booking.StartDate)
            .Take(50)
            .Select(x => new BookingSearchResult(x.Booking.Id.Value, x.Booking.Reference?.Value,
                x.Customer?.FullName ?? x.Booking.CustomerDetails?.FullName ?? string.Empty,
                x.Customer?.Email.Value ?? x.Booking.CustomerDetails?.Email.Value ?? string.Empty,
                boatNames.GetValueOrDefault(x.Booking.BoatId, string.Empty), x.Booking.StartDate, x.Booking.EndDate, x.Booking.Status,
                x.Booking.IsStandby))
            .ToList();
    }

    public async Task<AdminBookingDetail> GetAsync(string id, CancellationToken ct = default) =>
        await DetailAsync(await LoadAsync(id, ct), ct);

    /// <summary>Rate-table prices for pre-filling and Recalculate from rates. Saves nothing.</summary>
    public async Task<AdminQuoteResponse> QuoteAsync(AdminQuoteRequest request, CancellationToken ct = default)
    {
        var boat = await boats.GetAsync(new BoatId(request.BoatId ?? string.Empty), ct)
            ?? throw new DomainValidationException("boatId", "Choose a boat.");
        var reference = await referenceData.LoadAsync(ct);
        var addonPrices = (request.AddonIds ?? []).Distinct()
            .Select(id => new AddonPriceDto(id, reference.Addon(new AddonId(id)) is { } a ? pricing.AddonUnitPrice(a, request.PeriodType) : null))
            .ToList();
        return new AdminQuoteResponse(pricing.HirePrice(boat, request.PeriodType, request.StartDate, reference.Seasons), addonPrices);
    }

    /// <summary>
    /// A staff booking: Active immediately with its reference. The customer is matched or created by email. Email #1 is sent
    /// unless it is a stand-by, and overlapping stand-bys are superseded.
    /// </summary>
    public async Task<BookingSaveResponse> CreateAsync(CreateStaffBookingRequest request, CancellationToken ct = default)
    {
        var reference = await referenceData.LoadAsync(ct);
        var errors = new ValidationErrors();
        var boat = await BookableBoatAsync(request.BoatId, null, errors, ct);
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfAdults, request.NumberOfChildren,
            request.FirstName, request.LastName, request.Email, request.Mobile, request.SecondaryMobile, request.Address, [], false, null, false, null);
        var input = Validate(bookingRequest, boat, reference, request.CheckInTime, request.CheckOutTime, request.HirePrice, request.AddonLines, [], errors);
        var contact = ContactFrom(request.FirstName, request.LastName, request.Email, request.Mobile, request.SecondaryMobile, request.Address,
            request.CustomerComments, errors);
        errors.ThrowIfAny();

        var details = new StaffBookingDetails(boat!.Id, request.PeriodType, input.Stay, request.NumberOfAdults, request.NumberOfChildren, input.CheckIn,
            input.CheckOut, request.IsStandby, request.BookingComments, input.HirePrice, schedule.ScheduleFor(input.Stay, reference.BlockedPeriods), input.Lines);
        await PreCheckAvailabilityAsync(boat.Id, input.Stay, null, ct);

        var customer = await customers.SaveFromStaffAsync(null, contact!, ct);
        var prefix = BookingReference.PrefixFor(customer.LastName, input.Stay.Start);
        var bookingReference = BookingReference.Create(prefix, await references.NextSuffixAsync(prefix, ct));
        var id = BookingId.New();
        var now = clock.UtcNow;
        var (booking, superseded) = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var created = Booking.CreateByStaff(id, bookingReference, customer.Id, details, boat.EffectiveDeposit(reference.Settings), links.HashFor(id), now);
            var overlapping = await session.ListOverlappingBookingsAsync(created.Stay);
            availability.EnsureAvailable(created.Stay, overlapping, await session.ListOverlappingUnavailabilitiesAsync(created.Stay), now, created.Id);
            var supersededHere = Supersede(created, overlapping, session, now);
            session.SaveBooking(created);
            return (created, supersededHere);
        });

        await AfterSaveAsync(booking, superseded, ct);
        return new BookingSaveResponse(await DetailAsync(booking, ct), await WarningsAsync(boat, booking, reference, ct));
    }

    /// <summary>Saves every field of the booking dialog, including the full add-on line list and the customer's details.</summary>
    public async Task<BookingSaveResponse> UpdateAsync(string id, UpdateStaffBookingRequest request, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        if (booking.Status == BookingStatus.PendingPayment)
        {
            throw new BusinessRuleException("This booking is waiting for the customer's online payment and can't be edited yet.");
        }

        var reference = await referenceData.LoadAsync(ct);
        var errors = new ValidationErrors();
        var boat = await BookableBoatAsync(request.BoatId, booking.BoatId, errors, ct);
        var hasCustomer = booking.CustomerId is not null;
        var snapshot = booking.CustomerDetails;
        var bookingRequest = new BookingRequest(request.PeriodType, request.StartDate, request.EndDate, request.NumberOfAdults, request.NumberOfChildren,
            hasCustomer ? request.FirstName : snapshot?.FirstName, hasCustomer ? request.LastName : snapshot?.LastName,
            hasCustomer ? request.Email : snapshot?.Email.Value, hasCustomer ? request.Mobile : snapshot?.MobileNumber.Value,
            hasCustomer ? request.SecondaryMobile : null, hasCustomer ? request.Address : null, [], false, null, false, null);
        var input = Validate(bookingRequest, boat, reference, request.CheckInTime, request.CheckOutTime, request.HirePrice, request.AddonLines,
            booking.AddonLines, errors);
        var contact = hasCustomer
            ? ContactFrom(request.FirstName, request.LastName, request.Email, request.Mobile, request.SecondaryMobile, request.Address,
                request.CustomerComments, errors)
            : null;
        if (booking.Status is not (BookingStatus.Active or BookingStatus.Cancelled) && request.Status != booking.Status)
        {
            errors.Add("status", $"A {booking.Status} booking's status can't be changed.");
        }

        errors.ThrowIfAny();
        var scheduleType = input.Stay == booking.Stay ? booking.PaymentSchedule : schedule.ScheduleFor(input.Stay, reference.BlockedPeriods);
        var details = new StaffBookingDetails(boat!.Id, request.PeriodType, input.Stay, request.NumberOfAdults, request.NumberOfChildren, input.CheckIn,
            input.CheckOut, request.IsStandby, request.BookingComments, input.HirePrice, scheduleType, input.Lines);
        var now = clock.UtcNow;
        var (saved, superseded) = await scheduleLock.RunExclusiveAsync(boat.Id, async session =>
        {
            var current = await session.GetBookingAsync(booking.Id) ?? throw new NotFoundException("We couldn't find that booking.");
            current.EnsureVersion(request.Version);
            current.UpdateByStaff(details, now);
            if (current.Status is BookingStatus.Active or BookingStatus.Cancelled)
            {
                current.ChangeStatusByStaff(request.Status, now);
            }

            IReadOnlyList<Booking> supersededHere = [];
            if (current.Status == BookingStatus.Active)
            {
                var overlapping = await session.ListOverlappingBookingsAsync(current.Stay);
                availability.EnsureAvailable(current.Stay, overlapping, await session.ListOverlappingUnavailabilitiesAsync(current.Stay), now, current.Id);
                supersededHere = Supersede(current, overlapping, session, now);
            }

            session.SaveBooking(current);
            return (current, supersededHere);
        });

        if (contact is not null)
        {
            var customer = await customers.SaveFromStaffAsync(saved.CustomerId, contact, ct);
            if (customer.Id != saved.CustomerId)
            {
                saved.AttachCustomer(customer.Id, clock.UtcNow);
                await bookings.SaveAsync(saved, ct);
            }
        }

        await AfterSaveAsync(saved, superseded, ct);
        return new BookingSaveResponse(await DetailAsync(saved, ct), await WarningsAsync(boat, saved, reference, ct));
    }

    /// <summary>Records a manual payment and sends #5.</summary>
    public async Task<AdminBookingDetail> AddManualPaymentAsync(string id, ManualPaymentRequest request, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        booking.EnsureVersion(request.Version);
        var now = clock.UtcNow;
        var paidAt = request.Date == Sydney.DateOf(now) ? now : Sydney.InstantOf(request.Date, new TimeOnly(12, 0));
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note?.Length > 500)
        {
            throw new DomainValidationException("note", "The note must be 500 characters or fewer.");
        }

        var payment = new Payment(Guid.NewGuid().ToString("N"), request.Amount, PaymentMethod.Manual, null, paidAt, note, BookingCreator.SystemUser);
        booking.RecordPayment(payment, now);
        await bookings.SaveAsync(booking, ct);
        await notifier.SendPaymentReceivedAsync(booking, payment, ct);
        return await DetailAsync(booking, ct);
    }

    /// <summary>Sends #4 now for the amount due now. Manual sends are not deduplicated.</summary>
    public async Task SendPaymentLinkAsync(string id, CancellationToken ct = default)
    {
        var booking = await LoadAsync(id, ct);
        if (!booking.IsNotifiable)
        {
            throw new BusinessRuleException("Payment links can only be sent for Active bookings that aren't stand-bys.");
        }

        if (booking.AmountOwing <= 0m)
        {
            throw new BusinessRuleException(PaymentLinkService.NothingOwing);
        }

        var now = clock.UtcNow;
        var amount = schedule.AmountDueNow(booking, now);
        var due = schedule.Milestones(booking)
            .Where(m => m.Kind != MilestoneKind.Deposit && m.ShortfallFor(booking.AmountPaid) > 0)
            .OrderBy(m => m.DueAt)
            .FirstOrDefault();
        if (!await notifier.SendPaymentDueAsync(booking, amount, due?.DueDate, null, ct))
        {
            throw new BusinessRuleException("The payment link email couldn't be sent. Check the customer's email address and the email templates.");
        }
    }

    private static bool Matches(string term, Booking booking, Customer? customer)
    {
        bool Has(string? value) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        return Has(booking.Reference?.Value) || Has(customer?.FullName) || Has(customer?.Email.Value) || Has(booking.CustomerDetails?.FullName)
            || Has(booking.CustomerDetails?.Email.Value);
    }

    private static string NameOf(Booking booking, IReadOnlyDictionary<CustomerId, string> names) =>
        booking.CustomerId is { } id && names.TryGetValue(id, out var name) ? name : booking.CustomerDetails?.FullName ?? string.Empty;

    private IReadOnlyList<Booking> Supersede(Booking booking, IReadOnlyList<Booking> overlapping, IBoatScheduleSession session, DateTimeOffset now)
    {
        var superseded = availability.StandbysSupersededBy(booking, overlapping);
        foreach (var standby in superseded)
        {
            standby.Supersede(now);
            session.SaveBooking(standby);
        }

        return superseded;
    }

    private static StaffContact? ContactFrom(
        string? firstName, string? lastName, string? email, string? mobile, string? secondaryMobile, string? address, string? comments,
        ValidationErrors errors)
    {
        var e = errors.Capture(() => EmailAddress.Create(email));
        var m = errors.Capture(() => MobileNumber.CreateOptional(mobile, "mobile"));
        var s = errors.Capture(() => MobileNumber.CreateOptional(secondaryMobile, "secondaryMobile"));
        var a = errors.Capture(() => Customer.NormaliseAddress(address));
        if (comments?.Trim().Length > Customer.MaxCommentsLength)
        {
            errors.Add("customerComments", "Comments must be 5000 characters or fewer.");
        }

        return e is null || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)
            ? null
            : new StaffContact(firstName.Trim(), lastName.Trim(), e, m, s, a, comments);
    }

    private async Task<Booking> LoadAsync(string id, CancellationToken ct) =>
        await bookings.GetAsync(new BookingId(id), ct) ?? throw new NotFoundException("We couldn't find that booking.");

    private async Task<Boat?> BookableBoatAsync(string? boatId, BoatId? currentBoatId, ValidationErrors errors, CancellationToken ct)
    {
        var boat = string.IsNullOrWhiteSpace(boatId) ? null : await boats.GetAsync(new BoatId(boatId), ct);
        if (boat is null)
        {
            errors.Add("boatId", "Choose a boat.");
        }
        else if (!boat.IsActive && boat.Id != currentBoatId)
        {
            errors.Add("boatId", $"{boat.Name} is inactive and can't be booked.");
        }

        return boat;
    }

    private StaffInput Validate(
        BookingRequest request, Boat? boat, ReferenceData reference, string? checkIn, string? checkOut, decimal? hirePrice,
        IReadOnlyList<StaffAddonLineInput>? lineInputs, IReadOnlyList<AddonLine> existingLines, ValidationErrors errors)
    {
        var context = new BookingValidationContext(boat, reference.Settings, reference.BlockedPeriods, reference.Addons, Sydney.DateOf(clock.UtcNow),
            BookingValidationScope.Submit, null, new Dictionary<AddonId, int>());
        errors.AddRange(strategy.Validate(request, context));
        var inTime = errors.Capture(() => StaffTimes.Parse(checkIn, Sydney.StandardCheckIn, "checkInTime"));
        var outTime = errors.Capture(() => StaffTimes.Parse(checkOut, Sydney.StandardCheckOut, "checkOutTime"));
        if (hirePrice is not { } price || price < 0)
        {
            errors.Add("hirePrice", "Enter the hire price.");
        }

        var lines = new List<AddonLine>();
        foreach (var input in lineInputs ?? [])
        {
            var addon = reference.Addon(new AddonId(input.AddonId ?? string.Empty));
            if (addon is null)
            {
                errors.Add("addonLines", "An add-on line refers to an unknown add-on.");
                continue;
            }

            var existing = input.Id is null ? null : existingLines.FirstOrDefault(l => l.Id == input.Id);
            var line = errors.Capture(() => AddonLine.Create(existing?.Id, addon.Id, existing?.NameSnapshot ?? addon.Name, input.Quantity, input.UnitPrice));
            if (line is not null)
            {
                lines.Add(line);
            }
        }

        var stay = request.EndDate is { } end && end > request.StartDate ? StayRange.Create(request.StartDate, end) : default;
        return new StaffInput(stay, inTime, outTime, hirePrice ?? 0m, lines);
    }

    /// <summary>A cheap conflict check before anything is written; the exclusive section checks again.</summary>
    private async Task PreCheckAvailabilityAsync(BoatId boatId, StayRange stay, BookingId? self, CancellationToken ct)
    {
        var overlapping = (await bookings.ListOverlappingAsync(stay.Start, stay.End, ct)).Where(b => b.BoatId == boatId);
        var unavailable = (await unavailabilities.ListInRangeAsync(stay.Start, stay.End, ct)).Where(u => u.BoatId == boatId);
        availability.EnsureAvailable(stay, overlapping, unavailable, clock.UtcNow, self);
    }

    private async Task AfterSaveAsync(Booking booking, IReadOnlyList<Booking> superseded, CancellationToken ct)
    {
        if (await notifier.SendBookingConfirmedAsync(booking, ct))
        {
            await bookings.SaveAsync(booking, ct);
        }

        foreach (var standby in superseded)
        {
            await notifier.SendStandbySupersededAsync(standby, ct);
        }
    }

    private async Task<IReadOnlyList<string>> WarningsAsync(Boat boat, Booking booking, ReferenceData reference, CancellationToken ct)
    {
        var neighbours = await bookings.ListOverlappingAsync(booking.StartDate.AddDays(-1), booking.EndDate.AddDays(1), ct);
        var capacityBookings = new List<Booking>();
        foreach (var addonId in booking.AddonLines.Select(l => l.AddonId).Distinct())
        {
            if (reference.Addon(addonId) is { HasCapacity: true })
            {
                capacityBookings.AddRange(await bookings.ListWithAddonAsync(addonId, booking.StartDate, booking.EndDate, ct));
            }
        }

        return warnings.For(boat, booking, reference.Addons, neighbours, capacityBookings.DistinctBy(b => b.Id).ToList(), clock.UtcNow);
    }

    private async Task<IReadOnlyDictionary<CustomerId, string>> CustomerNamesAsync(CancellationToken ct) =>
        (await customerRepository.ListAsync(ct)).ToDictionary(c => c.Id, c => c.FullName);

    private async Task<AdminBookingDetail> DetailAsync(Booking b, CancellationToken ct)
    {
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } id ? await customerRepository.GetAsync(id, ct) : null;
        var now = clock.UtcNow;
        var frequent = customer is not null && b.ActivatedAt is { } anchor
            ? frequentFloaters.Evaluate(anchor, await bookings.ListByCustomerAsync(customer.Id, ct), b.Id)
            : FrequentFloaterResult.No;
        var milestones = schedule.Milestones(b);
        return new AdminBookingDetail(
            b.Id.Value, b.Reference?.Value, b.CreatedBy, b.IsStandby, b.Status, b.ActivatedAt, b.BoatId.Value, boat?.Name ?? string.Empty, b.PeriodType,
            b.StartDate, b.EndDate, b.NumberOfAdults, b.NumberOfChildren, StaffTimes.Format(b.CheckInTime), StaffTimes.Format(b.CheckOutTime),
            b.BookingComments, b.HirePrice, b.DepositAmount, b.PaymentSchedule,
            b.AddonLines.Select(l => new AddonLineDto(l.Id, l.AddonId.Value, l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            b.Payments.OrderBy(p => p.PaidAt).Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.PaidAt, p.Note, p.RecordedBy)).ToList(),
            b.TotalPrice, b.AmountPaid, b.AmountOwing, schedule.AmountDueNow(milestones, b.AmountPaid, b.AmountOwing, now), b.PaymentStatus,
            milestones.Select(m => new AdminScheduleItem(m.Kind, m.Label, m.RequiredCumulative, m.DueDate, m.ShortfallFor(b.AmountPaid))).ToList(),
            customer?.ToDto(),
            b.CustomerDetails is { } d ? new CustomerDetailsDto(d.FirstName, d.LastName, d.Email.Value, d.MobileNumber.Value) : null,
            new FrequentFloaterDto(frequent.IsFrequentFloater, frequent.OtherBookingCount),
            b.TermsAcceptedAt,
            b.RoomingWarningAcceptance is { } r ? new RoomingAcceptanceDto(r.AcceptedAt, r.WarningTextShown) : null,
            b.Status == BookingStatus.PendingPayment ? b.HoldExpiresAt : null,
            b.CreatedDate, b.ModifiedDate, b.Version);
    }

    private sealed record StaffInput(StayRange Stay, TimeOnly CheckIn, TimeOnly CheckOut, decimal HirePrice, IReadOnlyList<AddonLine> Lines);
}
