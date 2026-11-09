using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>What an online checkout holds: the validated request, priced on the server.</summary>
public sealed record OnlineHold(
    BoatId BoatId,
    BookingDraftId DraftId,
    PeriodType PeriodType,
    StayRange Stay,
    int NumberOfAdults,
    int NumberOfChildren,
    CustomerDetails CustomerDetails,
    IReadOnlyList<AddonLine> AddonLines,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentScheduleType PaymentSchedule,
    RoomingWarningAcceptance? RoomingWarningAcceptance,
    DateTimeOffset TermsAcceptedAt,
    DateTimeOffset HoldExpiresAt,
    string PaymentLinkTokenHash);
