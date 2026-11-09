namespace BergerHarbour.Application.Boats.Dtos;

/// <summary>The booking settings the booking app needs. <c>Today</c> is today in Australia/Sydney.</summary>
public sealed record PublicBookingSettings(
    int MinimumLeadTimeDays,
    DateOnly BookingsOpenUntil,
    DateOnly Today,
    string? ContactPhone,
    string? ContactEmail,
    string HireTermsUrl);
