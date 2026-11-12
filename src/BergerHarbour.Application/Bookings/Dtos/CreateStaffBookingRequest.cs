using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>POST /api/bookings</c> (admin).</summary>
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
    AddressDto? Address,
    IReadOnlyList<StaffAddonLineInput>? AddonLines);
