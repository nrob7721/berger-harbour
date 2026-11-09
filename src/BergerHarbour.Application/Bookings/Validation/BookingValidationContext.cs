using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Bookings.Validation;

/// <summary>
/// Everything the validation rules need, loaded by the calling application service before validating, so the
/// strategies stay pure.
/// </summary>
public sealed record BookingValidationContext(
    Boat? Boat,
    BusinessSettings Settings,
    IReadOnlyList<BlockedPeriod> BlockedPeriods,
    IReadOnlyList<AddonDefinition> Addons,
    DateOnly Today,
    BookingValidationScope Scope,
    BookingDraft? Draft,
    IReadOnlyDictionary<AddonId, int> RemainingCapacity);
