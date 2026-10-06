using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// The rules for customer bookings: period shape, lead time, open-until, blocked periods, guest limits, rooming
/// warning acceptance, group restriction answer, terms acceptance and allowed add-ons. Turnstile is verified in the
/// use case before this runs.
/// </summary>
public sealed class OnlineBookingValidationStrategy : IBookingRequestValidationStrategy
{
    public ValidationErrors Validate(BookingRequest request, BookingValidationContext context)
    {
        var errors = new ValidationErrors();
        var settings = context.Settings;
        var contact = $"{settings.ContactPhone} / {settings.ContactEmail}";

        // 1. The boat exists, is active and is a house boat.
        var boat = context.Boat;
        if (boat is null)
        {
            return errors.Add("slug", "That boat was not found.");
        }

        if (!boat.IsActive || boat.Type != BoatType.House)
        {
            return errors.Add("slug",
                $"This boat is currently unavailable for online booking. Please contact us on {contact}.");
        }

        // 2. Exactly one Midweek, Weekend or Week period of the right shape.
        Stay? stay = null;
        if (!PeriodTypes.Online.Contains(request.PeriodType))
        {
            errors.Add("periodType", $"Please call or email us to book this kind of stay: {contact}.");
        }
        else
        {
            stay = StayPeriodFactory.OnlineStayStartingOn(request.PeriodType, request.StartDate);
            if (stay is null || (request.EndDate is { } end && end != stay.EndDate))
            {
                errors.Add("startDate", request.PeriodType switch
                {
                    PeriodType.Midweek => "A mid-week stay runs Monday to Friday.",
                    PeriodType.Weekend => "A weekend stay runs Friday to Monday.",
                    _ => "A week runs Monday to Monday or Friday to Friday.",
                });
                stay = null;
            }
        }

        if (stay is not null)
        {
            // 3. Minimum lead time.
            if (!settings.SatisfiesLeadTime(stay, context.Today))
            {
                errors.Add("startDate",
                    $"Online bookings must start at least {settings.MinimumLeadTimeDays} days from today. " +
                    $"For sooner dates please contact us on {contact}.");
            }

            // 4. Every night inside the open window.
            if (!settings.IsOpenForOnlineBooking(stay))
            {
                errors.Add("startDate",
                    $"Online bookings are open until {settings.BookingsOpenUntil:dd/MM/yyyy}. " +
                    $"For later dates please contact us on {contact}.");
            }

            // 5. No blocked period.
            foreach (var blocked in BookingAvailabilityService.OverlappingBlockedPeriods(stay, context.BlockedPeriods))
            {
                errors.Add("startDate", $"Bookings over {blocked.Name} must be made by phone or email: {contact}.");
            }
        }

        // 6. Guest limits.
        if (request.NumberOfGuests < 1 || request.NumberOfGuests > boat.MaxNoOfGuests)
        {
            errors.Add("numberOfGuests", $"Choose between 1 and {boat.MaxNoOfGuests} guests.");
        }

        // 10. Add-ons: active, allowed for this boat, sensible quantities.
        ValidateAddons(request, context, boat, errors);

        if (context.Scope == BookingValidationScope.Quote)
        {
            return errors;
        }

        // 7. Rooming warning acceptance must match the text the server would show.
        if (boat.RequiresRoomingWarning(request.NumberOfGuests) && !errors.Has("numberOfGuests"))
        {
            var expected = boat.RoomingWarningText(request.NumberOfGuests);
            if (!request.RoomingWarningAccepted || !string.Equals(request.RoomingWarningText?.Trim(), expected, StringComparison.Ordinal))
            {
                errors.Add("roomingWarningAccepted", "Please read and accept the rooming arrangements for this boat.");
            }
        }

        // 8. Group restriction declared not applicable.
        if (request.GroupRestrictionApplies is not false)
        {
            errors.Add("groupRestrictionApplies",
                $"Groups of under-30s or all-male groups require prior approval — please contact us on {contact}.");
        }

        // 9. Terms accepted.
        if (!request.TermsAccepted)
        {
            errors.Add("termsAccepted", "Please accept the Hire Terms and Procedures.");
        }

        // 11. Contact details.
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("fullName", "Full name is required.");
        }
        else if (request.FullName.Trim().Length > 200)
        {
            errors.Add("fullName", "Full name must be 200 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("email", "Email is required.");
        }
        else if (!EmailAddress.IsValid(request.Email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        var mobileDigits = new string((request.Mobile ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(request.Mobile))
        {
            errors.Add("mobile", "Mobile number is required.");
        }
        else if (mobileDigits.Length is < 8 or > 15 || request.Mobile.Trim().Length > 30)
        {
            errors.Add("mobile", "Enter a valid mobile number.");
        }

        return errors;
    }

    private static void ValidateAddons(BookingRequest request, BookingValidationContext context, Boat boat,
        ValidationErrors errors)
    {
        var seen = new HashSet<string>();
        foreach (var requested in request.Addons)
        {
            var definition = context.AddonDefinitions.FirstOrDefault(a => a.Id.Value == requested.AddonId);
            if (definition is null || !definition.IsActive || !boat.AllowsAddon(definition.Id))
            {
                errors.Add("addons", "One of the selected add-ons is not available for this boat.");
                continue;
            }

            if (!seen.Add(requested.AddonId))
            {
                errors.Add("addons", $"{definition.Name} was selected more than once.");
            }

            if (requested.Quantity < 1 || requested.Quantity > 99)
            {
                errors.Add("addons", $"Choose a quantity between 1 and 99 for {definition.Name}.");
            }
            else if (!definition.QuantityApplies && requested.Quantity != 1)
            {
                errors.Add("addons", $"{definition.Name} can only be requested once.");
            }
        }
    }
}
