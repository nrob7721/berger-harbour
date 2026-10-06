using BergerHarbour.Application.Common;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// Validates a booking request. PublicApi registers <see cref="OnlineBookingValidationStrategy"/>; AdminApi
/// registers <see cref="InternalBookingValidationStrategy"/>. Both are always followed by the availability
/// invariant, which is not part of either strategy.
/// </summary>
public interface IBookingRequestValidationStrategy
{
    ValidationErrors Validate(BookingRequest request, BookingValidationContext context);
}

public sealed record BookingAddonRequest(string AddonId, int Quantity);

/// <summary>The common shape of a booking request from either channel.</summary>
public sealed record BookingRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly? EndDate,
    int NumberOfGuests,
    string? FullName,
    string? Email,
    string? Mobile,
    IReadOnlyList<BookingAddonRequest> Addons,
    bool RoomingWarningAccepted = false,
    string? RoomingWarningText = null,
    bool? GroupRestrictionApplies = null,
    bool TermsAccepted = false);

public enum BookingValidationScope
{
    /// <summary>A price quote: skips terms, acceptance, group restriction and contact details.</summary>
    Quote,

    /// <summary>The booking is being submitted.</summary>
    Submit,
}

public sealed record BookingValidationContext(
    Boat? Boat,
    BusinessSettings Settings,
    IReadOnlyList<BlockedPeriod> BlockedPeriods,
    IReadOnlyList<AddonDefinition> AddonDefinitions,
    DateOnly Today,
    BookingValidationScope Scope = BookingValidationScope.Submit);
