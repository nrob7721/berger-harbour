using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// The rules for customer bookings: period shape, lead time, open-until, blocked periods, adult/child limits,
/// rooming warning acceptance, terms acceptance, allowed add-ons, add-on capacity and a valid booking draft.
/// Turnstile was verified when the draft was created.
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

        // 2–5. Period shape, lead time, open-until and blocked periods.
        ValidateStay(request.PeriodType, request.StartDate, request.EndDate, settings, context.BlockedPeriods, context.Today,
            errors);

        // 6. Adults and children: at least one adult, and no more guests in total than the boat takes.
        if (request.NumberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "At least one adult must be on board.");
        }

        if (request.NumberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Number of children cannot be negative.");
        }
        else if (request.NumberOfAdults >= 1 && request.NumberOfGuests > boat.MaxNoOfGuests)
        {
            errors.Add("numberOfChildren", $"{boat.Name} takes at most {boat.MaxNoOfGuests} guests (adults and children).");
        }

        // 9. Add-ons: active, allowed for this boat, sensible quantities, within capacity.
        ValidateAddons(request, context, boat, errors);

        if (context.Scope == BookingValidationScope.Quote)
        {
            return errors;
        }

        // 7. Rooming warning acceptance must match the text the server would show.
        if (boat.RequiresRoomingWarning(request.NumberOfGuests) && !errors.Has("numberOfAdults") && !errors.Has("numberOfChildren"))
        {
            var expected = boat.RoomingWarningText(request.NumberOfGuests);
            if (!request.RoomingWarningAccepted || !string.Equals(request.RoomingWarningText?.Trim(), expected, StringComparison.Ordinal))
            {
                errors.Add("roomingWarningAccepted", "Please read and accept the rooming arrangements for this boat.");
            }
        }

        // 8. Terms accepted.
        if (!request.TermsAccepted)
        {
            errors.Add("termsAccepted", "Please accept the Hire Terms and Procedures.");
        }

        // 10. Contact details.
        ContactValidation.Validate(errors, request.FullName, request.Email, request.Mobile);

        // 11. A valid, unconverted booking draft for this boat.
        var draft = context.Draft;
        if (string.IsNullOrWhiteSpace(request.DraftId) || draft is null || draft.Id.Value != request.DraftId ||
            draft.BoatId != boat.Id)
        {
            errors.Add("draftId", "Your booking session has expired. Please start again.");
        }
        else if (draft.IsConverted)
        {
            errors.Add("draftId", "This booking has already been completed.");
        }

        return errors;
    }

    /// <summary>
    /// Rules 2–5 for an online stay: a Midweek, Weekend or Week period of the right shape, the minimum lead time, every
    /// night inside the open window, and no blocked period. Returns the stay when its shape is valid (it may still have
    /// broken rules 3–5; check <paramref name="errors"/>).
    /// </summary>
    public static Stay? ValidateStay(PeriodType periodType, DateOnly startDate, DateOnly? endDate, BusinessSettings settings,
        IReadOnlyList<BlockedPeriod> blockedPeriods, DateOnly today, ValidationErrors errors)
    {
        var contact = $"{settings.ContactPhone} / {settings.ContactEmail}";
        // 2. Exactly one Midweek, Weekend or Week period of the right shape.
        Stay? stay = null;
        if (!PeriodTypes.Online.Contains(periodType))
        {
            errors.Add("periodType", $"Please call or email us to book this kind of stay: {contact}.");
        }
        else
        {
            stay = StayPeriodFactory.OnlineStayStartingOn(periodType, startDate);
            if (stay is null || (endDate is { } end && end != stay.EndDate))
            {
                errors.Add("startDate", periodType switch
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
            if (!settings.SatisfiesLeadTime(stay, today))
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
            foreach (var blocked in BookingAvailabilityService.OverlappingBlockedPeriods(stay, blockedPeriods))
            {
                errors.Add("startDate", $"Bookings over {blocked.Name} must be made by phone or email: {contact}.");
            }
        }

        return stay;
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
            else if (definition.HasCapacity && context.AddonRemaining?.GetValueOrDefault(definition.Id) is { } remaining &&
                     requested.Quantity > remaining)
            {
                // A hard limit online, checked without a lock: a truly simultaneous race may oversell.
                errors.Add("addons", remaining == 0
                    ? $"{definition.Name} is fully booked for your dates."
                    : $"Only {remaining} {definition.CapacityUnitLabel ?? "of " + definition.Name} remain for your dates.");
            }
        }
    }
}
