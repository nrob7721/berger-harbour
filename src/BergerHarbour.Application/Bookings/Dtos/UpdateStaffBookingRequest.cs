using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary><c>PUT /api/bookings/{id}</c> (admin), with the version edited.</summary>
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
    AddressDto? Address,
    IReadOnlyList<StaffAddonLineInput>? AddonLines,
    int Version);
