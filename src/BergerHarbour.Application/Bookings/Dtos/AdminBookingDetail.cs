using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>GET /api/bookings/{id}</c> (admin): everything the booking dialog shows.</summary>
public sealed record AdminBookingDetail(
    string Id,
    string? Reference,
    BookingCreator CreatedBy,
    bool IsStandby,
    BookingStatus Status,
    DateTimeOffset? ActivatedAt,
    string BoatId,
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string CheckInTime,
    string CheckOutTime,
    string? BookingComments,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentScheduleType PaymentScheduleType,
    IReadOnlyList<AddonLineDto> AddonLines,
    IReadOnlyList<PaymentDto> Payments,
    decimal TotalPrice,
    decimal AmountPaid,
    decimal AmountOwing,
    decimal AmountDueNow,
    PaymentStatus PaymentStatus,
    IReadOnlyList<AdminScheduleItem> Schedule,
    AdminCustomer? Customer,
    CustomerDetailsDto? CustomerDetails,
    FrequentFloaterDto FrequentFloater,
    DateTimeOffset? TermsAcceptedAt,
    RoomingAcceptanceDto? RoomingWarningAcceptance,
    DateTimeOffset? HoldExpiresAt,
    decimal? CheckoutAmount,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    int Version);
