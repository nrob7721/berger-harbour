using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings;

public sealed record TimelineBoat(string Id, string Name, string Slug, int Berth, bool IsActive);

public sealed record TimelineBooking(
    string Id,
    string BoatId,
    string? Reference,
    string CustomerName,
    BookingStatus Status,
    bool IsStandby,
    PaymentStatus PaymentStatus,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    string CheckInTime,
    string CheckOutTime);

public sealed record TimelineUnavailability(string Id, string BoatId, DateOnly FirstNight, DateOnly LastNight, string? Comments);
public sealed record TimelineBlockedPeriod(string Id, string Name, DateOnly FirstNight, DateOnly LastNight);

public sealed record TimelineResponse(
    IReadOnlyList<TimelineBoat> Boats,
    IReadOnlyList<TimelineBooking> Bookings,
    IReadOnlyList<TimelineUnavailability> Unavailabilities,
    IReadOnlyList<TimelineBlockedPeriod> BlockedPeriods);

public sealed record BookingSearchResult(
    string Id,
    string? Reference,
    string CustomerName,
    string Email,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    BookingStatus Status,
    bool IsStandby);

public sealed record AdminCustomer(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    string? Mobile,
    string? SecondaryMobile,
    string? Comments,
    DateTimeOffset CreatedDate,
    int Version);

public sealed record CustomerDetailsDto(string FirstName, string LastName, string Email, string MobileNumber);
public sealed record AddonLineDto(string Id, string AddonId, string Name, int Quantity, decimal? UnitPrice, decimal? LineTotal);
public sealed record PaymentDto(string Id, decimal Amount, PaymentMethod Method, DateTimeOffset PaidAt, string? Note, BookingCreator RecordedBy);
public sealed record AdminScheduleItem(MilestoneKind Kind, string Label, decimal RequiredCumulative, DateOnly DueDate, decimal Shortfall);
public sealed record FrequentFloaterDto(bool IsFrequentFloater, int OtherBookingCount);
public sealed record RoomingAcceptanceDto(DateTimeOffset AcceptedAt, string WarningTextShown);

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
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate,
    int Version);

public sealed record StaffAddonLineInput(string? Id, string AddonId, int Quantity, decimal? UnitPrice);

public sealed record CreateStaffBookingRequest(
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? CheckInTime,
    string? CheckOutTime,
    bool IsStandby,
    string? BookingComments,
    string? CustomerComments,
    decimal? HirePrice,
    string Email,
    string FirstName,
    string LastName,
    string? Mobile,
    string? SecondaryMobile,
    IReadOnlyList<StaffAddonLineInput>? AddonLines);

public sealed record UpdateStaffBookingRequest(
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string? CheckInTime,
    string? CheckOutTime,
    bool IsStandby,
    string? BookingComments,
    string? CustomerComments,
    decimal? HirePrice,
    BookingStatus Status,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Mobile,
    string? SecondaryMobile,
    IReadOnlyList<StaffAddonLineInput>? AddonLines,
    int Version);

public sealed record BookingSaveResponse(AdminBookingDetail Booking, IReadOnlyList<string> Warnings);

public sealed record AdminQuoteRequest(string BoatId, PeriodType PeriodType, DateOnly StartDate, IReadOnlyList<string>? AddonIds);
public sealed record AddonPriceDto(string AddonId, decimal? UnitPrice);
public sealed record AdminQuoteResponse(decimal? HirePrice, IReadOnlyList<AddonPriceDto> AddonPrices);

public sealed record ManualPaymentRequest(decimal Amount, DateOnly Date, string? Note, int Version);

public sealed record CustomerLookupResponse(AdminCustomer Customer, FrequentFloaterDto FrequentFloater);
public sealed record UpdateCustomerCommentsRequest(string? Comments, int Version);

public sealed record CustomerBookingItem(
    string Id,
    string? Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    bool IsStandby);
