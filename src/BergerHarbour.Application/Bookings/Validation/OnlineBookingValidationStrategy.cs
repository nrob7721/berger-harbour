using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>The online booking rules (§7.7). Registered in the public API.</summary>
public sealed class OnlineBookingValidationStrategy : IBookingRequestValidationStrategy
{
    public const string RoomingNotAccepted = "Please accept the rooming warning to continue.";
    public const string RoomingChanged = "The rooming warning has changed. Please review it and accept it again.";
    public const string TermsNotAccepted = "Please accept the Hire Terms and Procedures.";
    public const string DraftInvalid = "Your booking session has expired. Please start again.";

    private readonly OnlineStayPolicy stayPolicy = new();

    public ValidationErrors Validate(BookingRequest request, BookingValidationContext context)
    {
        var errors = new ValidationErrors();
        var boat = context.Boat;

        // 1. The boat exists, is active and is a house boat.
        if (boat is null || !boat.IsActive || boat.Type != BoatType.House)
        {
            return errors.Add("slug", "This boat isn't available for online booking.");
        }

        // 2–5. Period shape, lead time, open-until and blocked periods.
        var stay = stayPolicy.Check(request.PeriodType, request.StartDate, context.Settings, context.BlockedPeriods, context.Today);
        errors.AddRange(stay.Errors);

        // 6. Guests.
        var guests = request.NumberOfAdults + request.NumberOfChildren;
        if (request.NumberOfAdults < 1)
        {
            errors.Add("numberOfAdults", "At least 1 adult is required.");
        }

        if (request.NumberOfChildren < 0)
        {
            errors.Add("numberOfChildren", "Children can't be negative.");
        }

        if (guests > boat.Berth)
        {
            errors.Add("numberOfChildren", $"{boat.Name} sleeps up to {boat.Berth} guests.");
        }

        // 9. Add-ons.
        ValidateAddons(request, context, boat, errors);

        if (context.Scope == BookingValidationScope.Quote)
        {
            return errors;
        }

        // 7. Rooming warning acceptance, with the exact text the server would generate.
        var warning = boat.RoomingWarningFor(guests);
        if (warning is not null)
        {
            if (!request.RoomingWarningAccepted)
            {
                errors.Add("roomingWarningAccepted", RoomingNotAccepted);
            }
            else if (!string.Equals(request.RoomingWarningText?.Trim(), warning, StringComparison.Ordinal))
            {
                errors.Add("roomingWarningText", RoomingChanged);
            }
        }

        // 8. Terms.
        if (!request.TermsAccepted)
        {
            errors.Add("termsAccepted", TermsNotAccepted);
        }

        // 10. Contact details.
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors.Add("firstName", "First name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors.Add("lastName", "Last name is required.");
        }

        errors.Capture(() => EmailAddress.Create(request.Email));
        errors.Capture(() => MobileNumber.Create(request.Mobile));

        // 11. A valid, unconverted draft for this boat.
        var draft = context.Draft;
        if (request.DraftId is null || draft is null || draft.Id != request.DraftId || draft.IsConverted || draft.Contents.BoatId != boat.Id)
        {
            errors.Add("draftId", DraftInvalid);
        }

        return errors;
    }

    private static void ValidateAddons(BookingRequest request, BookingValidationContext context, Boat boat, ValidationErrors errors)
    {
        foreach (var group in request.Addons.GroupBy(a => a.AddonId))
        {
            if (group.Count() > 1)
            {
                errors.Add("addons", "Each add-on can only be requested once.");
                continue;
            }

            var requested = group.Single();
            var addon = context.Addons.FirstOrDefault(a => a.Id == requested.AddonId);
            if (addon is null || !addon.IsActive || !boat.AllowsAddon(addon.Id))
            {
                errors.Add("addons", $"An add-on you chose isn't offered on {boat.Name}.");
                continue;
            }

            if (requested.Quantity < 1)
            {
                errors.Add("addons", $"Choose a quantity of at least 1 for {addon.Name}.");
            }
            else if (!addon.QuantityApplies && requested.Quantity != 1)
            {
                errors.Add("addons", $"{addon.Name} can only be requested once.");
            }
            else if (addon.HasCapacity && context.RemainingCapacity.TryGetValue(addon.Id, out var remaining) && requested.Quantity > remaining)
            {
                var unit = string.IsNullOrWhiteSpace(addon.CapacityUnitLabel) ? addon.Name : addon.CapacityUnitLabel;
                errors.Add("addons", $"Only {remaining} {unit} remain for your dates.");
            }
        }
    }
}
