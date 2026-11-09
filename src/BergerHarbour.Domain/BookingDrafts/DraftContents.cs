using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>What the customer has entered in the booking app so far.</summary>
public sealed record DraftContents(
    BoatId BoatId,
    PeriodType PeriodType,
    StayRange Stay,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    EmailAddress Email,
    MobileNumber MobileNumber,
    IReadOnlyList<AddonRequest> AddonRequests,
    DraftStep LastStepReached);
