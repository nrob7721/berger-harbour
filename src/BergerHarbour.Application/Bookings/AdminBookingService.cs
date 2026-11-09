using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings.Validation;
using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Notifications;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Application.Bookings;

/// <summary>Staff bookings: timeline, search, the booking dialog, manual payments and payment links.</summary>
public sealed class AdminBookingService(
    IBookingRepository bookings,
    IBoatRepository boats,
    ICustomerRepository customers,
    IBoatUnavailabilityRepository unavailabilities,
    IBookingRequestValidationStrategy validation,
    IBoatScheduleLock scheduleLock,
    IBookingReferenceGenerator references,
    CustomerRegistry customerRegistry,
    BookingNotifier notifier,
    ReferenceDataLoader referenceData,
    PaymentScheduleService schedule,
    PaymentLinkTokens tokens,
    IClock clock,
    ILogger<AdminBookingService> logger)
{
    private static readonly BookingStatus[] ActiveStatuses = [BookingStatus.Active, BookingStatus.PendingPayment];

    // ----- Timeline and search -----

    public async Task<TimelineResponse> GetTimelineAsync(DateOnly from, DateOnly to, bool includeInactive, CancellationToken ct = default)
    {
        if (to <= from || to > from.AddDays(120)) throw new DomainValidationException("to", "Choose a range of up to 120 days.");
        var allBoats = (await boats.ListAsync(ct)).Where(b => b.Type == BoatType.House).InDisplayOrder().ToList();
        var list = await bookings.ListOverlappingAsync(from, to, includeInactive ? BookingStatuses.All : ActiveStatuses, ct);
        var names = await CustomerNamesAsync(list, ct);
        var unavailable = await unavailabilities.ListInRangeAsync(from, to, ct);
        var reference = await referenceData.LoadAsync(ct);
        var window = new StayRange(from, to);
        return new TimelineResponse(
            allBoats.Select(b => new TimelineBoat(b.Id.Value, b.Name, b.Slug, b.Berth, b.IsActive)).ToList(),
            list.Select(b => new TimelineBooking(b.Id, b.BoatId.Value, b.Reference, names(b), b.Status, b.IsStandby, b.PaymentStatus,
                b.PeriodType, b.StartDate, b.EndDate, Dates.FormatTimeOfDay(b.CheckInTime), Dates.FormatTimeOfDay(b.CheckOutTime))).ToList(),
            unavailable.Select(u => new TimelineUnavailability(u.Id, u.BoatId.Value, u.Nights.FirstNight, u.Nights.LastNight, u.Comments)).ToList(),
            reference.BlockedPeriods.Where(p => window.Overlaps(p.Nights))
                .Select(p => new TimelineBlockedPeriod(p.Id, p.Name, p.Nights.FirstNight, p.Nights.LastNight)).ToList());
    }

    private async Task<Func<Booking, string>> CustomerNamesAsync(IReadOnlyCollection<Booking> list, CancellationToken ct)
    {
        var ids = list.Where(b => b.CustomerId is not null).Select(b => b.CustomerId!).Distinct().ToList();
        var map = (await customers.GetManyAsync(ids, ct)).ToDictionary(c => c.Id);
        return b => b.CustomerId is { } id && map.TryGetValue(id, out var c) ? c.FullName : b.SnapshotName;
    }

    /// <summary>By name, email or reference (case-insensitive), including the details customers entered online.</summary>
    public async Task<IReadOnlyList<BookingSearchResult>> SearchAsync(string? q, CancellationToken ct = default)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2) return [];
        bool Matches(string? s) => s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);

        var found = new Dictionary<string, Booking>();
        foreach (var b in await bookings.ListByReferencePrefixAsync(term.ToUpperInvariant(), ct)) found[b.Id] = b;
        var matchingCustomers = (await customers.ListAsync(ct))
            .Where(c => Matches(c.FullName) || Matches(c.Email) || Matches(c.FirstName) || Matches(c.LastName)).Select(c => c.Id).ToList();
        if (matchingCustomers.Count > 0)
            foreach (var b in await bookings.ListByCustomersAsync(matchingCustomers, ct)) found[b.Id] = b;
        foreach (var b in (await bookings.ListWithoutCustomerAsync(ct)).Where(b => Matches(b.SnapshotName) || Matches(b.CustomerDetails?.Email)))
            found[b.Id] = b;

        var list = found.Values.Where(b => b.Reference is not null || b.Status == BookingStatus.PendingPayment)
            .OrderByDescending(b => b.StartDate).Take(50).ToList();
        var names = await CustomerNamesAsync(list, ct);
        var customerMap = (await customers.GetManyAsync(list.Where(b => b.CustomerId is not null).Select(b => b.CustomerId!).Distinct().ToList(), ct))
            .ToDictionary(c => c.Id);
        var boatNames = (await boats.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return list.Select(b => new BookingSearchResult(b.Id, b.Reference, names(b),
            b.CustomerId is { } id && customerMap.TryGetValue(id, out var c) ? c.Email : b.CustomerDetails?.Email ?? "",
            boatNames.GetValueOrDefault(b.BoatId, ""), b.StartDate, b.EndDate, b.Status, b.IsStandby)).ToList();
    }

    // ----- Detail -----

    public async Task<AdminBookingDetail> GetAsync(string id, CancellationToken ct = default)
    {
        var b = await bookings.GetAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
        return await DetailAsync(b, ct);
    }

    private async Task<AdminBookingDetail> DetailAsync(Booking b, CancellationToken ct)
    {
        var boat = await boats.GetAsync(b.BoatId, ct);
        var customer = b.CustomerId is { } cid ? await customers.GetAsync(cid, ct) : null;
        var ff = FrequentFloaterResult.None;
        if (customer is not null)
            ff = FrequentFloaterService.Evaluate(b.ActivatedAt ?? clock.UtcNow, b.Id, await bookings.ListByCustomerAsync(customer.Id, ct));
        var paid = b.AmountPaid;
        var now = clock.UtcNow;
        return new AdminBookingDetail(
            b.Id, b.Reference, b.CreatedBy, b.IsStandby, b.Status, b.ActivatedAt, b.BoatId.Value, boat?.Name ?? "", b.PeriodType,
            b.StartDate, b.EndDate, b.Guests.Adults, b.Guests.Children, Dates.FormatTimeOfDay(b.CheckInTime), Dates.FormatTimeOfDay(b.CheckOutTime),
            b.BookingComments, b.HirePrice, b.DepositAmount, b.PaymentSchedule,
            b.AddonLines.Select(l => new AddonLineDto(l.Id, l.AddonId, l.NameSnapshot, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            b.Payments.OrderBy(p => p.PaidAt).Select(p => new PaymentDto(p.Id, p.Amount, p.Method, p.PaidAt, p.Note, p.RecordedBy)).ToList(),
            b.TotalPrice, paid, b.AmountOwing, schedule.AmountDueNow(b, now), b.PaymentStatus,
            schedule.Milestones(b).Select(m => new AdminScheduleItem(m.Kind, ScheduleLabel(m.Kind), m.RequiredCumulative,
                m.Kind == MilestoneKind.Deposit ? SydneyTime.DateOf(b.CreatedDate) : SydneyTime.DateOf(m.DueAt), m.Shortfall(paid))).ToList(),
            customer is null ? null : ToAdminCustomer(customer),
            b.CustomerDetails is { } d ? new CustomerDetailsDto(d.FirstName, d.LastName, d.Email, d.MobileNumber) : null,
            new FrequentFloaterDto(ff.IsFrequentFloater, ff.OtherBookingCount),
            b.TermsAcceptedAt,
            b.RoomingWarningAcceptance is { } r ? new RoomingAcceptanceDto(r.AcceptedAt, r.WarningTextShown) : null,
            b.HoldExpiresAt, b.CreatedDate, b.ModifiedDate, b.Version);
    }

    private static string ScheduleLabel(MilestoneKind k) => k switch
    {
        MilestoneKind.Deposit => "Deposit",
        MilestoneKind.Balance => "Balance (100%)",
        MilestoneKind.First => "First instalment (50%)",
        _ => "Final payment (100%)",
    };

    public static AdminCustomer ToAdminCustomer(Customer c) =>
        new(c.Id, c.FirstName, c.LastName, c.Email, c.MobileNumber, c.SecondaryMobileNumber, c.Address, c.Comments, c.CreatedDate, c.Version);

    // ----- Quote -----

    /// <summary>Prices for pre-filling the form and "Recalculate from rates". Saves nothing.</summary>
    public async Task<AdminQuoteResponse> QuoteAsync(AdminQuoteRequest r, CancellationToken ct = default)
    {
        var boat = await boats.GetAsync(new BoatId(r.BoatId), ct) ?? throw new NotFoundException("Boat not found.");
        var reference = await referenceData.LoadAsync(ct);
        var hire = PricingService.HirePrice(boat, reference.Seasons, r.PeriodType, r.StartDate);
        var prices = (r.AddonIds ?? []).Distinct()
            .Select(id => new AddonPriceDto(id, reference.Addon(id) is { } a ? PricingService.AddonUnitPrice(a, r.PeriodType) : null))
            .ToList();
        return new AdminQuoteResponse(hire, prices);
    }

    // ----- Create and update -----

    private static (TimeOnly CheckIn, TimeOnly CheckOut) ParseTimes(string? checkIn, string? checkOut, ValidationErrors errors)
    {
        TimeOnly Parse(string? s, TimeOnly fallback, string field)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (TimeOnly.TryParseExact(s.Trim(), "HH:mm", out var t)) return t;
            errors.Add(field, "Enter a time like 11:00.");
            return fallback;
        }
        return (Parse(checkIn, SydneyTime.StandardCheckIn, "checkInTime"), Parse(checkOut, SydneyTime.StandardCheckOut, "checkOutTime"));
    }

    /// <summary>The booking's complete add-on line list: new lines (no id) are added, lines with an id updated, lines left out deleted.</summary>
    private static List<AddonLine> BuildLines(IReadOnlyList<StaffAddonLineInput> input, IReadOnlyList<AddonLine> existing, ReferenceData reference, ValidationErrors errors)
    {
        var lines = new List<AddonLine>();
        foreach (var l in input)
        {
            var current = l.Id is null ? null : existing.FirstOrDefault(e => e.Id == l.Id);
            var addon = reference.Addon(l.AddonId);
            if (current is null && addon is null) { errors.Add("addonLines", "An add-on on this booking no longer exists."); continue; }
            var name = current is not null && current.AddonId == l.AddonId ? current.NameSnapshot : addon?.Name ?? current!.NameSnapshot;
            var line = errors.Capture(() => AddonLine.Create(current?.Id, l.AddonId, name, l.Quantity, l.UnitPrice));
            if (line is not null) lines.Add(line);
        }
        return lines;
    }

    public async Task<BookingSaveResponse> CreateAsync(CreateStaffBookingRequest r, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var reference = await referenceData.LoadAsync(ct);
        var boat = string.IsNullOrWhiteSpace(r.BoatId) ? null : await boats.GetAsync(new BoatId(r.BoatId), ct);
        var lineInputs = r.AddonLines ?? [];
        var contact = new ContactInput(r.FirstName, r.LastName, r.Email, r.Mobile, r.SecondaryMobile, r.Address);
        var result = await validation.ValidateAsync(new BookingRequest
        {
            Boat = boat, PeriodType = r.PeriodType, StartDate = r.StartDate, EndDate = r.EndDate, NumberOfAdults = r.NumberOfAdults,
            NumberOfChildren = r.NumberOfChildren, Addons = lineInputs.Select(l => new AddonQuantity(l.AddonId, l.Quantity)).ToList(),
            Reference = reference, Now = now, Contact = contact, CheckInTime = ParseTimesOrDefault(r.CheckInTime, SydneyTime.StandardCheckIn),
            CheckOutTime = ParseTimesOrDefault(r.CheckOutTime, SydneyTime.StandardCheckOut),
        }, ct);
        var errors = new ValidationErrors();
        errors.Merge(result.Errors);
        var (checkIn, checkOut) = ParseTimes(r.CheckInTime, r.CheckOutTime, errors);
        if (r.HirePrice is null) errors.Add("hirePrice", "Enter the hire price.");
        else if (r.HirePrice < 0) errors.Add("hirePrice", "Hire price can't be negative.");
        var lines = BuildLines(lineInputs, [], reference, errors);
        var customer = errors.Any ? null : await errors.CaptureAsync(() => customerRegistry.PrepareFromStaffAsync(contact, r.CustomerComments, ct));
        errors.ThrowIfAny();

        var stay = result.Stay!.Value;
        var bookingId = Ids.New();
        var superseded = new List<string>();
        var booking = await scheduleLock.RunExclusiveAsync(boat!.Id, async session =>
        {
            superseded.Clear();
            var others = await session.GetPotentiallyOverlappingBookingsAsync(stay);
            BookingAvailabilityService.EnsureAvailable(stay, others, await session.GetUnavailabilitiesAsync(stay), now);
            var prefix = BookingReference.Prefix(customer!.LastName, stay.Start);
            var bookingReference = BookingReference.Compose(prefix, await references.NextSuffixAsync(prefix, ct));
            var created = Booking.CreateByStaff(bookingId, bookingReference, boat.Id, r.PeriodType, stay, result.Guests!, checkIn, checkOut,
                r.IsStandby, customer.Id, r.BookingComments, r.HirePrice!.Value, boat.EffectiveDeposit(reference.Settings.DepositAmount),
                PaymentScheduleService.DetermineSchedule(stay, reference.BlockedPeriods), lines, tokens.HashFor(bookingId), now);
            superseded.AddRange(Supersede(created, others, session));
            session.SaveBooking(created);
            return created;
        }, ct);

        await LinkCustomerAsync(booking, customer!, ct);
        var warnings = result.Warnings.ToList();
        if (!booking.IsStandby) await TrySendAsync(booking, NotificationKeys.BookingConfirmed, () => notifier.SendBookingConfirmedAsync(booking, ct), warnings, ct);
        await NotifySupersededAsync(superseded, ct);
        return new BookingSaveResponse(await DetailAsync(booking, ct), warnings);
    }

    private static TimeOnly ParseTimesOrDefault(string? s, TimeOnly fallback) =>
        TimeOnly.TryParseExact(s?.Trim(), "HH:mm", out var t) ? t : fallback;

    public async Task<BookingSaveResponse> UpdateAsync(string id, UpdateStaffBookingRequest r, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var current = await bookings.GetAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
        current.EnsureVersion(r.Version);
        var reference = await referenceData.LoadAsync(ct);
        var boatId = new BoatId(r.BoatId ?? "");
        var boat = string.IsNullOrWhiteSpace(r.BoatId) ? null : await boats.GetAsync(boatId, ct);
        var lineInputs = r.AddonLines ?? [];
        var hasCustomer = current.CustomerId is not null;
        // Contact details, the address and customer comments are ignored for a booking without a Customer.
        var contact = hasCustomer
            ? new ContactInput(r.FirstName, r.LastName, r.Email, r.Mobile, r.SecondaryMobile, r.Address)
            : new ContactInput(current.CustomerDetails?.FirstName, current.CustomerDetails?.LastName, current.CustomerDetails?.Email, current.CustomerDetails?.MobileNumber);
        var result = await validation.ValidateAsync(new BookingRequest
        {
            Boat = boat, PeriodType = r.PeriodType, StartDate = r.StartDate, EndDate = r.EndDate, NumberOfAdults = r.NumberOfAdults,
            NumberOfChildren = r.NumberOfChildren, Addons = lineInputs.Select(l => new AddonQuantity(l.AddonId, l.Quantity)).ToList(),
            Reference = reference, Now = now, Contact = contact, ExcludeBookingId = id, AllowInactiveBoat = boatId == current.BoatId,
            CheckInTime = ParseTimesOrDefault(r.CheckInTime, SydneyTime.StandardCheckIn), CheckOutTime = ParseTimesOrDefault(r.CheckOutTime, SydneyTime.StandardCheckOut),
        }, ct);
        var errors = new ValidationErrors();
        errors.Merge(result.Errors);
        var (checkIn, checkOut) = ParseTimes(r.CheckInTime, r.CheckOutTime, errors);
        if (r.HirePrice is null) errors.Add("hirePrice", "Enter the hire price.");
        else if (r.HirePrice < 0) errors.Add("hirePrice", "Hire price can't be negative.");
        if (r.Status != current.Status && !(r.Status is BookingStatus.Active or BookingStatus.Cancelled && current.Status is BookingStatus.Active or BookingStatus.Cancelled))
            errors.Add("status", $"A {current.Status} booking's status can't be changed here.");
        var lines = BuildLines(lineInputs, current.AddonLines, reference, errors);
        Customer? customer = null;
        if (hasCustomer && !errors.Any)
            customer = await errors.CaptureAsync(() => customerRegistry.PrepareFromStaffAsync(contact, r.CustomerComments, ct));
        errors.ThrowIfAny();

        var stay = result.Stay!.Value;
        var superseded = new List<string>();
        var saved = await scheduleLock.RunExclusiveAsync(boatId, async session =>
        {
            superseded.Clear();
            var b = await session.GetBookingAsync(id) ?? throw new NotFoundException("Booking not found.");
            b.EnsureVersion(r.Version);
            b.UpdateByStaff(boatId, r.PeriodType, stay, result.Guests!, checkIn, checkOut, r.IsStandby, r.BookingComments,
                r.HirePrice!.Value, PaymentScheduleService.DetermineSchedule(stay, reference.BlockedPeriods));
            b.ReplaceAddonLines(lines);
            if (r.Status != b.Status)
            {
                if (r.Status == BookingStatus.Cancelled) b.Cancel();
                else b.Reactivate();
            }
            if (b.Status == BookingStatus.Active)
            {
                var others = await session.GetPotentiallyOverlappingBookingsAsync(stay);
                BookingAvailabilityService.EnsureAvailable(stay, others, await session.GetUnavailabilitiesAsync(stay), now, b.Id);
                superseded.AddRange(Supersede(b, others, session));
            }
            session.SaveBooking(b);
            return b;
        }, ct);

        if (customer is not null) await LinkCustomerAsync(saved, customer, ct);
        await NotifySupersededAsync(superseded, ct);
        var latest = await bookings.GetAsync(id, ct) ?? saved;
        return new BookingSaveResponse(await DetailAsync(latest, ct), result.Warnings);
    }

    private static IEnumerable<string> Supersede(Booking active, IReadOnlyList<Booking> others, IBoatScheduleSession session)
    {
        foreach (var standby in BookingAvailabilityService.StandbysToSupersede(active, others))
        {
            standby.Supersede();
            session.SaveBooking(standby);
            yield return standby.Id;
        }
    }

    private async Task LinkCustomerAsync(Booking booking, Customer prepared, CancellationToken ct)
    {
        var customer = await customerRegistry.CommitStaffChangesAsync(prepared, ct);
        if (booking.CustomerId != customer.Id)
        {
            booking.ChangeCustomer(customer.Id);
            await bookings.SaveAsync(booking, ct);
        }
    }

    private async Task NotifySupersededAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            try { await StripeWebhookService.NotifySupersededAsync(id, notifier, bookings, clock, ct); }
            catch (Exception e) { logger.LogError(e, "Sending the superseded stand-by alert for {BookingId} failed", id); }
        }
    }

    /// <summary>The booking is already saved, so an email failure becomes a warning instead of an error.</summary>
    private async Task TrySendAsync(Booking booking, string key, Func<Task> send, List<string> warnings, CancellationToken ct)
    {
        try
        {
            await send();
            booking.MarkNotificationSent(key, clock.UtcNow);
            await bookings.SaveAsync(booking, ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Sending {Key} for booking {BookingId} failed", key, booking.Id);
            warnings.Add("The booking was saved, but the confirmation email couldn't be sent.");
        }
    }

    // ----- Payments -----

    public async Task<AdminBookingDetail> AddManualPaymentAsync(string id, ManualPaymentRequest r, CancellationToken ct = default)
    {
        var b = await bookings.GetAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
        b.EnsureVersion(r.Version);
        var payment = new Payment(Ids.New(), r.Amount, PaymentMethod.Manual, null, SydneyTime.ToInstant(r.Date, new TimeOnly(12, 0)), r.Note, BookingCreator.SystemUser);
        b.RecordPayment(payment);
        await bookings.SaveAsync(b, ct);
        if (b.Status == BookingStatus.Active && !b.IsStandby)
        {
            try
            {
                await notifier.SendPaymentReceivedAsync(b, ct);
                b.MarkNotificationSent(NotificationKeys.PaymentReceived(payment.Id), clock.UtcNow);
                await bookings.SaveAsync(b, ct);
            }
            catch (Exception e) { logger.LogError(e, "Sending the payment receipt for {BookingId} failed", id); }
        }
        return await DetailAsync(b, ct);
    }

    /// <summary>Sends email #4 now for the amount due now. Not deduplicated.</summary>
    public async Task SendPaymentLinkAsync(string id, CancellationToken ct = default)
    {
        var b = await bookings.GetAsync(id, ct) ?? throw new NotFoundException("Booking not found.");
        if (b.Status != BookingStatus.Active) throw new ConflictException("Payment links can only be sent for Active bookings.");
        if (b.IsStandby) throw new ConflictException("Stand-by bookings don't get emails.");
        var now = clock.UtcNow;
        var due = schedule.AmountDueNow(b, now);
        if (due <= 0) throw new DomainValidationException("amount", "Nothing is owing on this booking.");
        var paid = b.AmountPaid;
        var next = schedule.Milestones(b).Where(m => m.Shortfall(paid) > 0).OrderBy(m => m.DueAt).FirstOrDefault();
        var dueDate = next is null || next.DueAt < now ? SydneyTime.Today(now) : SydneyTime.DateOf(next.DueAt);
        await notifier.SendPaymentDueAsync(b, due, dueDate, ct);
    }
}
