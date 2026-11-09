using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Catalogue;

public sealed record PublicBookingSettings(
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    DateOnly Today,
    string? ContactPhone,
    string? ContactEmail,
    string HireTermsUrl);

public sealed record PublicBoatSummary(string Slug, string Name, string? ImageUrl, int Berth, bool HasSpa, bool IsPetFriendly);

public sealed record PublicBoatsResponse(IReadOnlyList<PublicBoatSummary> Boats, PublicBookingSettings Settings);

public sealed record PublicAddonPrices(decimal? Midweek, decimal? Weekend, decimal? Week);

public sealed record PublicAddon(
    string Id,
    string Name,
    string? Description,
    bool PriceOnRequest,
    bool QuantityApplies,
    PublicAddonPrices Prices,
    bool HasCapacity,
    string? CapacityUnitLabel);

public sealed record PublicBoatDetails(
    string Slug,
    string Name,
    string? ImageUrl,
    int Berth,
    bool HasSpa,
    bool IsPetFriendly,
    int AdultBeds,
    int KidsBeds,
    string? BeddingDescription,
    decimal SecurityBond,
    decimal DepositAmount,
    bool IsActive,
    IReadOnlyList<PublicAddon> Addons);

public sealed record NightRangeDto(DateOnly FirstNight, DateOnly LastNight);
public sealed record EnquireOnlyRange(string Name, DateOnly FirstNight, DateOnly LastNight);
public sealed record StayRangeDto(DateOnly Start, DateOnly End);
public sealed record BoatAvailability(string Slug, IReadOnlyList<StayRangeDto> Unavailable);
public sealed record FleetAvailabilityResponse(IReadOnlyList<EnquireOnlyRange> EnquireOnly, IReadOnlyList<BoatAvailability> Boats);

public sealed record PeriodBoatAvailability(string Slug, bool Available, decimal? HirePrice);
public sealed record PeriodAvailabilityResponse(PeriodType PeriodType, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<PeriodBoatAvailability> Boats);

public sealed record AddonRemainingResponse(int Remaining);

public sealed record AddonRequestDto(string AddonId, int Quantity);

public sealed record CreateDraftRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    string Email,
    string Mobile,
    string? TurnstileToken);

public sealed record CreateDraftResponse(string DraftId);

public sealed record UpdateDraftRequest(
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    string Email,
    string Mobile,
    IReadOnlyList<AddonRequestDto>? Addons,
    DraftStep LastStepReached);

public sealed record QuoteRequest(
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    IReadOnlyList<AddonRequestDto>? Addons);

public sealed record QuoteAddonLine(string AddonId, string Name, int Quantity, decimal? UnitPrice, decimal? LineTotal);

public sealed record ScheduleItem(MilestoneKind Kind, string Label, decimal RequiredCumulative, decimal Amount, DateOnly DueDate, bool DueAtBooking);

public sealed record QuoteResponse(
    string BoatName,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string SeasonName,
    decimal HirePrice,
    IReadOnlyList<QuoteAddonLine> Addons,
    decimal TotalPrice,
    bool HasPriceOnRequestAddons,
    decimal DepositAmount,
    decimal AmountDueAtCheckout,
    IReadOnlyList<ScheduleItem> PaymentSchedule,
    decimal SecurityBond,
    string? RoomingWarning);

public sealed record CreateBookingRequest(
    string DraftId,
    string Slug,
    PeriodType PeriodType,
    DateOnly StartDate,
    int NumberOfAdults,
    int NumberOfChildren,
    string FirstName,
    string LastName,
    string Email,
    string Mobile,
    IReadOnlyList<AddonRequestDto>? Addons,
    bool RoomingWarningAccepted,
    string? RoomingWarningText,
    bool TermsAccepted);

public sealed record CreateBookingResponse(string BookingId, string ClientSecret, string SessionId);

public sealed record ReleaseBookingRequest(string DraftId);

public sealed record BookingStatusResponse(
    BookingStatus Status,
    string? Reference,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    string Email);

public sealed record PaymentLinkDetails(
    string Reference,
    string CustomerFirstName,
    string BoatName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    decimal AmountPaid,
    decimal AmountOwing,
    decimal AmountDueNow);

public sealed record CheckoutResponse(string ClientSecret, string SessionId);
