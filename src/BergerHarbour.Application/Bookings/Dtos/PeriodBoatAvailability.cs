namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>One boat's availability and hire price for a stay.</summary>
public sealed record PeriodBoatAvailability(
    string Slug,
    bool Available,
    decimal? HirePrice);
