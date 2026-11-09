using BergerHarbour.Application.Common;
using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

public sealed record AddonQuantity(string AddonId, int Quantity);

public sealed record ContactInput(string? FirstName, string? LastName, string? Email, string? Mobile, string? SecondaryMobile = null, string? Address = null);

public enum ValidationScope
{
    /// <summary>A price quote: skips the draft, terms and rooming acceptance.</summary>
    Quote,
    /// <summary>Creating or updating a booking.</summary>
    Booking,
}

/// <summary>Everything a validation strategy needs about a booking request.</summary>
public sealed record BookingRequest
{
    public required Boat? Boat { get; init; }
    public required PeriodType PeriodType { get; init; }
    public required DateOnly StartDate { get; init; }
    /// <summary>Staff bookings send the end date; online stays derive it from the period type.</summary>
    public DateOnly? EndDate { get; init; }
    public required int NumberOfAdults { get; init; }
    public required int NumberOfChildren { get; init; }
    public IReadOnlyList<AddonQuantity> Addons { get; init; } = [];
    public required ReferenceData Reference { get; init; }
    public required DateTimeOffset Now { get; init; }
    public ValidationScope Scope { get; init; } = ValidationScope.Booking;
    public ContactInput? Contact { get; init; }

    // Online only
    public BookingDraft? Draft { get; init; }
    public bool TermsAccepted { get; init; }
    public bool RoomingWarningAccepted { get; init; }
    public string? RoomingWarningText { get; init; }

    // Staff only
    public string? ExcludeBookingId { get; init; }
    /// <summary>Editing a booking that stays on its (now inactive) boat is allowed.</summary>
    public bool AllowInactiveBoat { get; init; }
    public TimeOnly CheckInTime { get; init; } = SydneyTime.StandardCheckIn;
    public TimeOnly CheckOutTime { get; init; } = SydneyTime.StandardCheckOut;
}

public sealed record BookingValidationResult(
    IReadOnlyDictionary<string, string[]> Errors,
    IReadOnlyList<string> Warnings,
    StayRange? Stay,
    GuestNumbers? Guests)
{
    public bool IsValid => Errors.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid) throw new DomainValidationException(Errors);
    }
}

/// <summary>Validates a booking request. Always followed by the availability invariant, which is not part of any strategy.</summary>
public interface IBookingRequestValidationStrategy
{
    Task<BookingValidationResult> ValidateAsync(BookingRequest request, CancellationToken ct = default);
}

/// <summary>Rules 2–5 of §7.7 for an online stay: period shape, lead time, open-until, blocked periods.</summary>
public static class OnlineStayRules
{
    public static StayRange? Validate(PeriodType periodType, DateOnly startDate, ReferenceData reference, DateTimeOffset now, ValidationErrors errors)
    {
        var stay = errors.Capture(() => (StayRange?)StayPeriodFactory.ForOnlineStart(periodType, startDate));
        if (stay is not { } s) return null;
        var settings = reference.Settings;
        var earliest = settings.EarliestOnlineStart(SydneyTime.Today(now));
        if (s.Start < earliest)
            errors.Add("startDate", $"Online bookings must start at least {settings.MinimumLeadTimeDays} days from today. {ContactLine(settings)}");
        if (s.LastNight > settings.BookingsOpenUntil)
            errors.Add("startDate", $"Online bookings are open until {Dates.Format(settings.BookingsOpenUntil)}. {ContactLine(settings)}");
        if (BookingAvailabilityService.FirstBlockedPeriod(s, reference.BlockedPeriods) is { } blocked)
            errors.Add("startDate", BlockedMessage(blocked, settings));
        return s;
    }

    public static string BlockedMessage(BlockedPeriod blocked, BusinessSettings settings) =>
        $"Bookings over {blocked.Name} must be made by phone or email: {settings.ContactPhone} / {settings.ContactEmail}.";

    private static string ContactLine(BusinessSettings s) =>
        $"Please call {s.ContactPhone} or email {s.ContactEmail} to book.";
}

public sealed class OnlineBookingValidationStrategy(IBookingRepository bookings) : IBookingRequestValidationStrategy
{
    public async Task<BookingValidationResult> ValidateAsync(BookingRequest r, CancellationToken ct = default)
    {
        var errors = new ValidationErrors();
        var boat = r.Boat;

        // 1. The boat exists, is active and is a house boat.
        if (boat is null || !boat.IsBookableHouseBoat)
            errors.Add("slug", "This boat isn't available for online booking.");

        // 2–5. Period shape, lead time, open-until, blocked periods.
        if (!StayPeriodFactory.OnlinePeriodTypes.Contains(r.PeriodType))
            errors.Add("periodType", "Online bookings are for a mid-week, weekend or week stay. Please call or email us for other stays.");
        var stay = StayPeriodFactory.OnlinePeriodTypes.Contains(r.PeriodType)
            ? OnlineStayRules.Validate(r.PeriodType, r.StartDate, r.Reference, r.Now, errors)
            : null;

        // 6. Guests.
        var guests = errors.Capture(() => new GuestNumbers(r.NumberOfAdults, r.NumberOfChildren));
        if (guests is not null && boat is not null && guests.Total > boat.Berth)
            errors.Add("numberOfAdults", $"{boat.Name} sleeps up to {boat.Berth} guests.");

        // 7. Rooming warning acceptance.
        if (r.Scope == ValidationScope.Booking && guests is not null && boat is not null && RoomingWarning.Applies(boat, guests.Total))
        {
            var expected = RoomingWarning.Text(boat, guests.Total);
            if (!r.RoomingWarningAccepted || !string.Equals(r.RoomingWarningText?.Trim(), expected, StringComparison.Ordinal))
                errors.Add("roomingWarningAccepted", "Please read and accept the rooming warning.");
        }

        // 8. Terms.
        if (r.Scope == ValidationScope.Booking && !r.TermsAccepted)
            errors.Add("termsAccepted", "Please accept the Hire Terms and Procedures.");

        // 9. Add-ons.
        if (boat is not null)
            await ValidateAddonsAsync(r, boat, stay, errors, ct);

        // 10. Contact details.
        if (r.Scope == ValidationScope.Booking)
        {
            var c = r.Contact ?? new ContactInput(null, null, null, null);
            errors.Capture(() => Text.Required(c.FirstName, "firstName", "First name", 100));
            errors.Capture(() => Text.Required(c.LastName, "lastName", "Last name", 100));
            errors.Capture(() => EmailAddress.Require(c.Email));
            errors.Capture(() => MobileNumber.Require(c.Mobile, "mobile"));
        }

        // 11. A valid, unconverted draft for this boat.
        if (r.Scope == ValidationScope.Booking && (r.Draft is null || r.Draft.IsConverted || (boat is not null && r.Draft.BoatId != boat.Id)))
            errors.Add("draftId", "Your booking session has expired. Please start again.");

        return new BookingValidationResult(errors.ToDictionary(), [], stay, guests);
    }

    private async Task ValidateAddonsAsync(BookingRequest r, Boat boat, StayRange? stay, ValidationErrors errors, CancellationToken ct)
    {
        foreach (var group in r.Addons.GroupBy(a => a.AddonId))
        {
            var addon = r.Reference.Addon(group.Key);
            var quantity = group.Sum(a => a.Quantity);
            if (addon is null || !addon.IsActive || !boat.AllowedAddonIds.Contains(addon.Id))
            {
                errors.Add("addons", $"{addon?.Name ?? "An add-on you chose"} isn't offered on {boat.Name}.");
                continue;
            }
            if (quantity < 1) { errors.Add("addons", $"Choose at least 1 for {addon.Name}."); continue; }
            if (!addon.QuantityApplies && quantity != 1) { errors.Add("addons", $"{addon.Name} can only be requested once."); continue; }
            if (addon.Capacity is { } capacity && stay is { } s)
            {
                // Checked without a lock: a truly simultaneous race may oversell (accepted, §7.5).
                var related = await bookings.ListWithAddonAsync(addon.Id, s.Start, s.End, ct);
                var remaining = AddonCapacityService.Remaining(capacity, addon.Id, s, related, r.Now);
                if (quantity > remaining)
                    errors.Add("addons", CapacityMessage(addon, remaining));
            }
        }
    }

    public static string CapacityMessage(AddonDefinition addon, int remaining) =>
        string.IsNullOrWhiteSpace(addon.CapacityUnitLabel)
            ? $"Only {remaining} {addon.Name.ToLowerInvariant()} remain for your dates."
            : $"Only {remaining} {addon.CapacityUnitLabel} remain for your dates.";
}

public sealed class InternalBookingValidationStrategy(IBookingRepository bookings) : IBookingRequestValidationStrategy
{
    public async Task<BookingValidationResult> ValidateAsync(BookingRequest r, CancellationToken ct = default)
    {
        var errors = new ValidationErrors();
        var warnings = new List<string>();
        var boat = r.Boat;
        if (boat is null) errors.Add("boatId", "Choose a boat.");
        else if (!boat.IsActive && !r.AllowInactiveBoat) errors.Add("boatId", $"{boat.Name} is inactive and can't be booked.");

        StayRange? stay = null;
        if (r.EndDate is not { } end) errors.Add("endDate", "Enter the end date.");
        else stay = errors.Capture(() => (StayRange?)new StayRange(r.StartDate, end));

        var guests = errors.Capture(() => new GuestNumbers(r.NumberOfAdults, r.NumberOfChildren));

        // A valid customer: email, first name and last name are required; mobiles are optional but must be mobiles;
        // the address is optional, at most 500 characters. Applies on create and on every update.
        var c = r.Contact ?? new ContactInput(null, null, null, null);
        errors.Capture(() => EmailAddress.Require(c.Email));
        errors.Capture(() => Text.Required(c.FirstName, "firstName", "First name", 100));
        errors.Capture(() => Text.Required(c.LastName, "lastName", "Last name", 100));
        errors.Capture(() => MobileNumber.Optional(c.Mobile, "mobile"));
        errors.Capture(() => MobileNumber.Optional(c.SecondaryMobile, "secondaryMobile"));
        errors.Capture(() => Customer.NormaliseAddress(c.Address));

        foreach (var a in r.Addons)
        {
            if (r.Reference.Addon(a.AddonId) is null) errors.Add("addonLines", "An add-on on this booking no longer exists.");
            if (a.Quantity < 1) errors.Add("addonLines", "Quantity must be at least 1.");
        }

        if (boat is not null && guests is not null)
        {
            if (guests.Total > boat.Berth)
                warnings.Add($"{guests.Total} guests is more than {boat.Name}'s berth of {boat.Berth}.");
            else if (guests.Total > boat.TotalBeds)
                warnings.Add($"{guests.Total} guests is more than {boat.Name}'s {boat.TotalBeds} beds ({boat.AdultBeds ?? 0} adult + {boat.KidsBeds ?? 0} kids); some guests will share beds.");
        }

        if (stay is { } s && errors.Any is false)
        {
            warnings.AddRange(await CapacityWarningsAsync(r, s, ct));
            if (boat is not null) warnings.AddRange(await ChangeoverWarningsAsync(r, boat, s, ct));
        }

        return new BookingValidationResult(errors.ToDictionary(), warnings, stay, guests);
    }

    private async Task<IEnumerable<string>> CapacityWarningsAsync(BookingRequest r, StayRange stay, CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var group in r.Addons.GroupBy(a => a.AddonId))
        {
            var addon = r.Reference.Addon(group.Key);
            if (addon?.Capacity is not { } capacity) continue;
            var related = await bookings.ListWithAddonAsync(addon.Id, stay.Start, stay.End, ct);
            var night = AddonCapacityService.FirstNightExceeded(capacity, addon.Id, stay, group.Sum(a => a.Quantity), related, r.Now, r.ExcludeBookingId);
            if (night is { } n) result.Add($"This exceeds {addon.Name.ToLowerInvariant()} capacity on {Dates.Format(n)}.");
        }
        return result;
    }

    /// <summary>A custom time clashing with a same-day changeover on the boat.</summary>
    private async Task<IEnumerable<string>> ChangeoverWarningsAsync(BookingRequest r, Boat boat, StayRange stay, CancellationToken ct)
    {
        var nearby = await bookings.ListOverlappingAsync(stay.Start.AddDays(-1), stay.End.AddDays(1), BookingStatuses.Blocking, ct);
        var result = new List<string>();
        foreach (var b in nearby.Where(b => b.BoatId == boat.Id && b.Id != r.ExcludeBookingId && b.BlocksAvailability(r.Now)))
        {
            var label = b.Reference ?? "a booking awaiting payment";
            if (b.EndDate == stay.Start && r.CheckInTime < b.CheckOutTime)
                result.Add($"Check-in at {Dates.FormatTime(r.CheckInTime)} on {Dates.Format(stay.Start)} is before {label} checks out at {Dates.FormatTime(b.CheckOutTime)}.");
            if (b.StartDate == stay.End && r.CheckOutTime > b.CheckInTime)
                result.Add($"Check-out at {Dates.FormatTime(r.CheckOutTime)} on {Dates.Format(stay.End)} is after {label} checks in at {Dates.FormatTime(b.CheckInTime)}.");
        }
        return result;
    }
}
