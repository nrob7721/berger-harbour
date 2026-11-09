using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Every stored field of a booking, used to rehydrate it from persistence.</summary>
public sealed record BookingState(
    BookingId Id,
    BookingReference? Reference,
    BookingCreator CreatedBy,
    bool IsStandby,
    BookingStatus Status,
    DateTimeOffset? ActivatedAt,
    CustomerId? CustomerId,
    CustomerDetails? CustomerDetails,
    BookingDraftId? BookingDraftId,
    BoatId BoatId,
    PeriodType PeriodType,
    StayRange Stay,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    string? BookingComments,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentScheduleType PaymentSchedule,
    IReadOnlyList<AddonLine> AddonLines,
    IReadOnlyList<Payment> Payments,
    DateTimeOffset? HoldExpiresAt,
    string? StripeCheckoutSessionId,
    RoomingWarningAcceptance? RoomingWarningAcceptance,
    DateTimeOffset? TermsAcceptedAt,
    string PaymentLinkTokenHash,
    IReadOnlyList<SentNotification> SentNotifications,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    int Version);
