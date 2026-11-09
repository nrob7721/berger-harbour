using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;
using BergerHarbour.Domain.Unavailabilities;

namespace BergerHarbour.Domain.Bookings;

public sealed record AvailabilityConflicts(IReadOnlyList<Booking> Bookings, IReadOnlyList<BoatUnavailability> Unavailabilities)
{
    public bool Any => Bookings.Count > 0 || Unavailabilities.Count > 0;

    public IReadOnlyList<string> Describe() =>
    [
        .. Bookings.Select(b => b.Reference ?? "a booking awaiting payment"),
        .. Unavailabilities.Select(u => $"unavailable {Dates.Format(u.Nights.FirstNight)}–{Dates.Format(u.Nights.LastNight)}"),
    ];
}

/// <summary>Overlap checks given already-loaded bookings, unavailabilities and blocked periods (decision 15).</summary>
public static class BookingAvailabilityService
{
    public const string TakenMessage = "Those dates are no longer available";

    /// <summary>
    /// What blocks a stay on one boat: blocking bookings (Active non-stand-by, unlapsed non-stand-by holds) and unavailabilities.
    /// Stand-bys ignore other stand-bys and are superseded rather than blocked.
    /// </summary>
    public static AvailabilityConflicts FindConflicts(StayRange stay, IEnumerable<Booking> boatBookings,
        IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now, string? excludeBookingId = null) =>
        new(
            boatBookings.Where(b => b.Id != excludeBookingId && b.BlocksAvailability(now) && b.Stay.Overlaps(stay)).ToList(),
            unavailabilities.Where(u => stay.Overlaps(u.Nights)).ToList());

    public static void EnsureAvailable(StayRange stay, IEnumerable<Booking> boatBookings,
        IEnumerable<BoatUnavailability> unavailabilities, DateTimeOffset now, string? excludeBookingId = null)
    {
        var conflicts = FindConflicts(stay, boatBookings, unavailabilities, now, excludeBookingId);
        if (conflicts.Any) throw new ConflictException(TakenMessage, conflicts.Describe());
    }

    public static BlockedPeriod? FirstBlockedPeriod(StayRange stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.OrderBy(p => p.Nights.FirstNight).FirstOrDefault(p => stay.Overlaps(p.Nights));

    /// <summary>Active stand-bys on the same boat that an Active non-stand-by booking overlaps.</summary>
    public static IReadOnlyList<Booking> StandbysToSupersede(Booking activeBooking, IEnumerable<Booking> boatBookings)
    {
        if (activeBooking.IsStandby || activeBooking.Status != BookingStatus.Active) return [];
        return boatBookings
            .Where(b => b.Id != activeBooking.Id && b.IsStandby && b.Status == BookingStatus.Active
                        && b.BoatId == activeBooking.BoatId && b.Stay.Overlaps(activeBooking.Stay))
            .ToList();
    }

    /// <summary>Unavailabilities may not overlap Active or PendingPayment non-stand-by bookings.</summary>
    public static void EnsureUnavailabilityAllowed(NightRange nights, IEnumerable<Booking> boatBookings, DateTimeOffset now)
    {
        var clashing = boatBookings
            .Where(b => !b.IsStandby && b.Status is BookingStatus.Active or BookingStatus.PendingPayment
                        && (b.Status == BookingStatus.Active || b.HoldExpiresAt > now) && b.Stay.Overlaps(nights))
            .ToList();
        if (clashing.Count == 0) return;
        var refs = clashing.Select(b => b.Reference ?? "a booking awaiting payment").ToList();
        throw new ConflictException($"This overlaps booking {string.Join(", ", refs)}.", refs);
    }
}

public static class PricingService
{
    /// <summary>The season whose range contains the start date, or Normal.</summary>
    public static Season SeasonFor(DateOnly startDate, IReadOnlyList<Season> seasons) =>
        seasons.FirstOrDefault(s => !s.IsDefault && s.Ranges.Any(r => r.Contains(startDate)))
        ?? seasons.FirstOrDefault(s => s.IsDefault)
        ?? throw new InvalidOperationException("The Normal season is missing.");

    /// <summary>The boat's rate for (season, period type); null for Custom or when the rate is missing.</summary>
    public static decimal? HirePrice(Boat boat, IReadOnlyList<Season> seasons, PeriodType periodType, DateOnly startDate) =>
        periodType == PeriodType.Custom ? null : boat.RateFor(SeasonFor(startDate, seasons).Id, periodType);

    /// <summary>The add-on's price for Midweek/Weekend/Week; null for price on request and for LongWeekend/Custom bookings.</summary>
    public static decimal? AddonUnitPrice(AddonDefinition addon, PeriodType periodType) => addon.PriceFor(periodType);
}

public sealed record PaymentScheduleOptions(
    TimeSpan BalanceDueBeforeHire,
    TimeSpan ExtendedFirstInstalmentBeforeHire,
    TimeSpan ExtendedFinalInstalmentBeforeHire,
    TimeSpan PaymentReminderBeforeDue)
{
    public static readonly PaymentScheduleOptions Default = new(
        TimeSpan.FromDays(30), TimeSpan.FromDays(120), TimeSpan.FromDays(90), TimeSpan.FromDays(7));
}

public enum MilestoneKind { Deposit, Balance, First, Final }

/// <summary>A required cumulative amount paid by an instant.</summary>
public sealed record Milestone(MilestoneKind Kind, decimal RequiredCumulative, DateTimeOffset DueAt, DateTimeOffset ReminderAt)
{
    public decimal Shortfall(decimal amountPaid) => Math.Max(0m, RequiredCumulative - amountPaid);
    /// <summary>Due at or before the booking was created.</summary>
    public bool WasDueAtCreation(DateTimeOffset createdDate) => DueAt <= createdDate;
}

public sealed class PaymentScheduleService(PaymentScheduleOptions options)
{
    public PaymentScheduleOptions Options => options;

    /// <summary>Extended when the stay overlaps a blocked period flagged for it (Christmas/New Year).</summary>
    public static PaymentScheduleType DetermineSchedule(StayRange stay, IEnumerable<BlockedPeriod> blockedPeriods) =>
        blockedPeriods.Any(p => p.UsesExtendedPaymentSchedule && stay.Overlaps(p.Nights)) ? PaymentScheduleType.Extended : PaymentScheduleType.Standard;

    /// <summary>Online checkout always charges min(deposit, hire price).</summary>
    public static decimal CheckoutAmount(decimal deposit, decimal hirePrice) => Math.Min(deposit, hirePrice);

    public IReadOnlyList<Milestone> Milestones(Booking b) =>
        Milestones(b.PaymentSchedule, b.StartDate, b.TotalPrice, b.DepositAmount, b.CreatedDate);

    public IReadOnlyList<Milestone> Milestones(PaymentScheduleType schedule, DateOnly startDate, decimal totalPrice, decimal deposit, DateTimeOffset createdDate)
    {
        var checkIn = SydneyTime.CheckInInstant(startDate);
        Milestone M(MilestoneKind kind, decimal required, DateTimeOffset due) => new(kind, Money.Round(required), due, due - options.PaymentReminderBeforeDue);
        var list = new List<Milestone> { M(MilestoneKind.Deposit, Math.Min(deposit, totalPrice), createdDate) };
        if (schedule == PaymentScheduleType.Standard)
        {
            list.Add(M(MilestoneKind.Balance, totalPrice, checkIn - options.BalanceDueBeforeHire));
        }
        else
        {
            list.Add(M(MilestoneKind.First, totalPrice * 0.5m, checkIn - options.ExtendedFirstInstalmentBeforeHire));
            list.Add(M(MilestoneKind.Final, totalPrice, checkIn - options.ExtendedFinalInstalmentBeforeHire));
        }
        return list;
    }

    /// <summary>The largest shortfall among milestones whose reminder window has opened; otherwise the amount owing.</summary>
    public decimal AmountDueNow(Booking b, DateTimeOffset now)
    {
        var paid = b.AmountPaid;
        var due = Milestones(b).Where(m => now >= m.ReminderAt).Select(m => m.Shortfall(paid)).DefaultIfEmpty(0m).Max();
        return due > 0 ? due : b.AmountOwing;
    }
}

/// <summary>Capacity shared by all boats and bookings, counted on the busiest night of the stay.</summary>
public static class AddonCapacityService
{
    public static int UsedOnNight(string addonId, DateOnly night, IEnumerable<Booking> bookings, DateTimeOffset now, string? excludeBookingId = null) =>
        bookings
            .Where(b => b.Id != excludeBookingId && b.BlocksAvailability(now) && b.Stay.IncludesNight(night))
            .SelectMany(b => b.AddonLines.Where(l => l.AddonId == addonId))
            .Sum(l => l.Quantity);

    public static int MaxUsed(string addonId, StayRange stay, IReadOnlyCollection<Booking> bookings, DateTimeOffset now, string? excludeBookingId = null) =>
        stay.EachNight().Select(n => UsedOnNight(addonId, n, bookings, now, excludeBookingId)).DefaultIfEmpty(0).Max();

    public static int Remaining(int capacity, string addonId, StayRange stay, IReadOnlyCollection<Booking> bookings, DateTimeOffset now, string? excludeBookingId = null) =>
        Math.Max(0, capacity - MaxUsed(addonId, stay, bookings, now, excludeBookingId));

    /// <summary>The first night on which adding <paramref name="quantity"/> exceeds the capacity, or null.</summary>
    public static DateOnly? FirstNightExceeded(int capacity, string addonId, StayRange stay, int quantity, IReadOnlyCollection<Booking> bookings,
        DateTimeOffset now, string? excludeBookingId = null) =>
        stay.EachNight().Cast<DateOnly?>().FirstOrDefault(n => UsedOnNight(addonId, n!.Value, bookings, now, excludeBookingId) + quantity > capacity);
}

public sealed record FrequentFloaterResult(bool IsFrequentFloater, int OtherBookingCount)
{
    public static readonly FrequentFloaterResult None = new(false, 0);
}

/// <summary>A booking is a Frequent Floater booking when the customer has another Active non-stand-by booking starting within a year either side of the anchor.</summary>
public static class FrequentFloaterService
{
    public static FrequentFloaterResult Evaluate(DateTimeOffset anchor, string? bookingId, IEnumerable<Booking> customerBookings)
    {
        var anchorDate = SydneyTime.DateOf(anchor);
        var from = anchorDate.AddYears(-1);
        var to = anchorDate.AddYears(1);
        var count = customerBookings.Count(b =>
            b.Id != bookingId && b.Status == BookingStatus.Active && !b.IsStandby && b.StartDate >= from && b.StartDate <= to);
        return new FrequentFloaterResult(count > 0, count);
    }

    public static string Notice(string customerName, FrequentFloaterResult r) =>
        !r.IsFrequentFloater ? ""
        : $"⭐ Frequent Floater: {customerName} has {r.OtherBookingCount} other booking{(r.OtherBookingCount == 1 ? "" : "s")} within a year of this one.";
}

public static class RoomingWarning
{
    public static bool Applies(Boat boat, int guests) => guests > boat.TotalBeds;

    public static string Text(Boat boat, int guests) =>
        $"This boat has {boat.AdultBeds ?? 0} adult beds and {boat.KidsBeds ?? 0} children's beds ({boat.BeddingDescription}). " +
        $"With {guests} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout.";
}
